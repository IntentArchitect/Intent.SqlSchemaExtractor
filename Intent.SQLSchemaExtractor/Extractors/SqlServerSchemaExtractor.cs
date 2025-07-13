using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Intent.SQLSchemaExtractor.ExtensionMethods;
using Intent.SQLSchemaExtractor.ModelMapper;
using Intent.SQLSchemaExtractor.Models;
using Microsoft.SqlServer.Management.Smo;

namespace Intent.SQLSchemaExtractor.Extractors;

public class SqlServerSchemaExtractor
{
    private readonly Database _db;
    private readonly Server _server;
    private readonly ImportConfiguration _config;
    private readonly List<string> _tablesToIgnore = ["sysdiagrams", "__MigrationHistory", "__EFMigrationsHistory"];
    private readonly List<string> _viewsToIgnore = [];
    private readonly ExtractStatistics _extractStats = new ExtractStatistics();

    public string SchemaVersion => "2.0";

    public ExtractStatistics Statistics => _extractStats;

    public SqlServerSchemaExtractor(ImportConfiguration config, Database db, Server server)
    {
        _db = db;
        _server = server;
        _config = config;
    }

    public PackageModelPersistable BuildPackageModel(string packageNameOrPath, SchemaExtractorEventManager eventManager)
    {
        var (fullPackagePath, packageName) = GetPackageLocationAndName(packageNameOrPath);
        var package = DatabaseSchemaToModelMapper.GetOrCreateDomainPackage(fullPackagePath, packageName);
        var modelSchemaHelper = new DatabaseSchemaToModelMapper(_config, package, _db);
        var savedSchemaVersion = package.Metadata.FirstOrDefault(m => m.Key == "sql-import:schemaVersion")?.Value;
        if (savedSchemaVersion != SchemaVersion)
        {
            MigrateSchema(package, savedSchemaVersion);
        }

        package.IsExternalOld = false;

        ApplyStereotypes(package);
        if (_config.ExportTables())
        {
            ProcessTables(eventManager, modelSchemaHelper);
            ProcessForeignKeys(modelSchemaHelper);
        }

        if (_config.ExportIndexes())
        {
            ProcessIndexes(eventManager, modelSchemaHelper);
        }

        if (_config.ExportViews())
        {
            ProcessViews(eventManager, modelSchemaHelper);
        }

        if (_config.ExportStoredProcedures())
        {
            ProcessStoredProcedures(eventManager, modelSchemaHelper);
        }

        package.References ??= [];
        package.AddMetadata("sql-import:schemaVersion", SchemaVersion);

        return package;
    }

    private static void MigrateSchema(PackageModelPersistable package, string? oldFileVersion)
    {
    }

    private static void ApplyStereotypes(PackageModelPersistable package)
    {
        if (package.Stereotypes.Any(p => p.DefinitionId == Constants.Stereotypes.Rdbms.RelationalDatabase.DefinitionId))
        {
            return;
        }

        package.Stereotypes.Add(new StereotypePersistable
        {
            Name = Constants.Stereotypes.Rdbms.RelationalDatabase.Name,
            DefinitionId = Constants.Stereotypes.Rdbms.RelationalDatabase.DefinitionId,
            AddedByDefault = false,
            DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName,
            DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId
        });
    }


    private void ProcessIndexes(SchemaExtractorEventManager eventManager, DatabaseSchemaToModelMapper databaseSchemaToModelMapper)
    {
        Console.WriteLine();
        Console.WriteLine("Indexes");
        Console.WriteLine("======");
        Console.WriteLine();

        var filteredTables = GetFilteredTables();
        foreach (var table in filteredTables)
        {
            var @class = databaseSchemaToModelMapper.GetClass(table);
            if (@class == null)
            {
                continue;
            }

            foreach (Microsoft.SqlServer.Management.Smo.Index tableIndex in table.Indexes)
            {
                if (tableIndex.IsClustered)
                {
                    continue;
                }

                foreach (var handler in eventManager.OnIndexHandlers)
                {
                    handler(_config, tableIndex, @class, databaseSchemaToModelMapper);
                }

                _extractStats.IndexCount++;
            }
        }
    }

    private void ProcessTables(SchemaExtractorEventManager eventManager, DatabaseSchemaToModelMapper databaseSchemaToModelMapper)
    {
        Console.WriteLine();
        Console.WriteLine("Tables");
        Console.WriteLine("======");
        Console.WriteLine();

        var filteredTables = GetFilteredTables();
        var tableCount = filteredTables.Length;
        var tableNumber = 0;
        _extractStats.TableCount = tableCount;

        OutputMissingIncludedObjects(filteredTables.Select(t => t.Name).ToList(), OutputItemType.Tables);
        OutputMissingExcludedColumns(filteredTables);

        foreach (var table in filteredTables)
        {
            var @class = databaseSchemaToModelMapper.GetOrCreateClass(table);

            Console.WriteLine($"{table.Name} ({++tableNumber}/{tableCount})");

            foreach (var handler in eventManager.OnTableHandlers)
            {
                handler(_config, table, @class);
            }

            foreach(Trigger trig in table.Triggers)
            {
                _ = databaseSchemaToModelMapper.GetOrCreateTrigger(trig, @class);
            }
            
            foreach (Column col in table.Columns)
            {
                if (!_config.ExportTableColumn(table.Schema, table.Name, col.Name))
                {
                    continue;
                }
                var attribute = databaseSchemaToModelMapper.GetOrCreateAttribute(col, @class);

                var typeId = GetTypeId(col.DataType);

                // Developers would like to have Enums defined on Attributes where the underlying SQL type
                // is a numeric type like int, bit, etc. so don't overwrite in those cases since the importer
                // itself cannot introduce Enums (yet).
                if (attribute.TypeReference.TypeId is null ||
                    databaseSchemaToModelMapper.GetEnum(attribute.TypeReference.TypeId) is null ||
                    (col.DataType.SqlDataType != SqlDataType.Int &&
                     col.DataType.SqlDataType != SqlDataType.SmallInt &&
                     col.DataType.SqlDataType != SqlDataType.Bit))
                {
                    attribute.TypeReference.TypeId = typeId;
                }

                foreach (var handler in eventManager.OnTableColumnHandlers)
                {
                    handler(col, attribute);
                }
            }
        }
    }

    private void ProcessForeignKeys(DatabaseSchemaToModelMapper databaseSchemaToModelMapper)
    {
        Console.WriteLine();
        Console.WriteLine("Foreign Keys");
        Console.WriteLine("============");
        Console.WriteLine();

        var filteredTables = GetFilteredTables();
        foreach (var table in filteredTables)
        {
            foreach (ForeignKey foreignKey in table.ForeignKeys)
            {
                databaseSchemaToModelMapper.GetOrCreateAssociation(foreignKey);
                _extractStats.ForeignKeyCount++;
            }
        }
    }

    private void ProcessViews(SchemaExtractorEventManager eventManager, DatabaseSchemaToModelMapper databaseSchemaToModelMapper)
    {
        Console.WriteLine();
        Console.WriteLine("Views");
        Console.WriteLine("=====");
        Console.WriteLine();

        var filteredViews = GetFilteredViews();
        var viewsCount = filteredViews.Length;
        var viewNumber = 0;
        _extractStats.ViewCount = viewsCount;

        OutputMissingIncludedObjects(filteredViews.Select(t => t.Name).ToList(), OutputItemType.Views);
        OutputMissingExcludedColumns(filteredViews);

        foreach (var view in filteredViews)
        {
            var @class = databaseSchemaToModelMapper.GetOrCreateClass(view);

            Console.WriteLine($"{view.Name} ({++viewNumber}/{viewsCount})");

            foreach (var handler in eventManager.OnViewHandlers)
            {
                handler(view, @class);
            }

            foreach (Trigger trig in view.Triggers)
            {
                _ = databaseSchemaToModelMapper.GetOrCreateTrigger(trig, @class);
            }

            foreach (Column col in view.Columns)
            {
                if (!_config.ExportViewColumn(view.Schema, view.Name, col.Name))
                {
                    continue;
                }
                var attribute = databaseSchemaToModelMapper.GetOrCreateAttribute(col, @class);

                var typeId = GetTypeId(col.DataType);
                attribute.TypeReference.TypeId = typeId;

                foreach (var handler in eventManager.OnViewColumnHandlers)
                {
                    handler(col, attribute);
                }
            }
        }
    }

    private void ProcessStoredProcedures(SchemaExtractorEventManager eventManager, DatabaseSchemaToModelMapper databaseSchemaToModelMapper)
    {
        Console.WriteLine();
        Console.WriteLine("Stored Procedures");
        Console.WriteLine("=================");
        Console.WriteLine();

        var filteredStoredProcedures = GetFilteredStoredProcedures();
        var storedProceduresCount = filteredStoredProcedures.Length;
        var storedProceduresNumber = 0;
        _extractStats.StoredProcedureCount = storedProceduresCount;

        OutputMissingIncludedObjects(filteredStoredProcedures.Select(t => t.Name).ToList(), OutputItemType.StoredProcedures);

        foreach (var storedProc in filteredStoredProcedures)
        {
            if (!_config.ExportStoredProcedure(storedProc.Schema, storedProc.Name))
            {
                continue;
            }

            if (_config.StoredProcNames?.Count > 0)
            {
                var storedProcLookup = new HashSet<string>(_config.StoredProcNames, StringComparer.OrdinalIgnoreCase);
                if (!storedProcLookup.Contains(storedProc.Name) && !storedProcLookup.Contains($"{storedProc.Schema}.{storedProc.Name}"))
                {
                    continue;
                }
            }

            Console.WriteLine($"{storedProc.Name} ({++storedProceduresNumber}/{storedProceduresCount})");

            var modelStoredProcedure = _config.StoredProcedureType == StoredProcedureType.StoredProcedureElement 
                ? databaseSchemaToModelMapper.GetOrCreateStoredProcedureElement(storedProc, _config.RepositoryElementId)
                : databaseSchemaToModelMapper.GetOrCreateStoredProcedureOperation(storedProc, _config.RepositoryElementId);
            
            var resultSet = StoredProcExtractor.GetStoredProcedureResultSet(_db, storedProc);
            // This code tries to match the result to a Class-Table when only one Table is detected
            // in the output. I'm disabling this for now since the StoredProc import would not
            // export Class-Tables and thus results in empty Classes. We could adjust the ImportSettings
            // to cater for this, but I think in general people would want DataContracts instead of Classes.
            // So I'm keeping this here just in case it becomes a requirement.
            /*if (resultSet.TableCount == 1)
            {
                var table = GetFilteredTables().FirstOrDefault(p => p.ID == resultSet.TableIds[0]);
                if (table is not null)
                {
                    var @class = databaseSchemaToModelMapper.GetOrCreateClass(table);
                    modelStoredProcedure.TypeReference = new TypeReferencePersistable
                    {
                        Id = Guid.NewGuid().ToString(),
                        IsNullable = false,
                        IsCollection = true,
                        Stereotypes = [],
                        GenericTypeParameters = [],
                        TypeId = @class.Id
                    };
                }
            }
            else if (resultSet.TableCount > 1)*/
            if (resultSet.TableCount > 0)
            {
                var dataContract = databaseSchemaToModelMapper.GetOrCreateDataContractResponse(storedProc, modelStoredProcedure);
                modelStoredProcedure.TypeReference = new TypeReferencePersistable
                {
                    Id = Guid.NewGuid().ToString(),
                    IsNullable = false,
                    IsCollection = true,
                    Stereotypes = [],
                    GenericTypeParameters = [],
                    TypeId = dataContract.Id
                };
                
                foreach (var column in resultSet.Columns)
                {
                    var attribute = databaseSchemaToModelMapper.GetOrCreateAttribute(column, dataContract);
            
                    var typeId = GetTypeId(column.SqlDataType);
                    attribute.TypeReference.TypeId = typeId;
                }
            }

            foreach (StoredProcedureParameter procParameter in storedProc.Parameters)
            {
                var param = _config.StoredProcedureType == StoredProcedureType.StoredProcedureElement 
                    ? databaseSchemaToModelMapper.GetOrCreateStoredProcedureElementParameter(procParameter, modelStoredProcedure)
                    : databaseSchemaToModelMapper.GetOrCreateStoredProcedureOperationParameter(procParameter, modelStoredProcedure);
                
                var typeId = procParameter.DataType.SqlDataType is SqlDataType.UserDefinedTableType
                    ? GetDataContractTypeId(storedProc, procParameter)
                    : GetTypeId(procParameter.DataType);
                param.TypeReference.TypeId = typeId;
                if (procParameter.DataType.SqlDataType is SqlDataType.UserDefinedTableType)
                {
                    param.TypeReference.IsCollection = true;
                }
            }

            foreach (var handler in eventManager.OnStoredProcedureHandlers)
            {
                handler(storedProc, modelStoredProcedure);
            }
        }

        return;

        string? GetDataContractTypeId(StoredProcedure storedProc, StoredProcedureParameter procParameter)
        {
            var type = GetFilteredUserDefinedTableTypes().FirstOrDefault(p => p.Schema == storedProc.Schema && p.Name == procParameter.DataType.Name);
            if (type is null)
            {
                Logging.LogWarning($"UserDefined Table {storedProc.Schema}.{procParameter.DataType.Name} not found");
                return null;
            }

            // Create the Data Contract on demand
            var dataContract = databaseSchemaToModelMapper.GetOrCreateDataContract(type);
            if (dataContract.ChildElements.Count == 0 && type.Columns.Count > 0)
            {
                foreach (Column column in type.Columns)
                {
                    var attr = databaseSchemaToModelMapper.GetOrCreateAttribute(column, dataContract);
                    attr.TypeReference.TypeId = GetTypeId(column.DataType);
                }
            }

            return dataContract.Id;
        }
    }

    private Table[]? _cachedFilteredTables;

    private Table[] GetFilteredTables()
    {
        // storing this so we can know if we need to call GetDependantTables or not 
        var firstTimeExecuting = _cachedFilteredTables is null;

        _cachedFilteredTables ??= _db.Tables.OfType<Table>()
            .Where(table => !_tablesToIgnore.Contains(table.Name) && _config.ExportTable(table.Schema, table.Name))
            .ToArray();

        // if this is the first time running
        if (firstTimeExecuting)
        {
            _cachedFilteredTables = [.. _cachedFilteredTables, .. GetDependantTables()];
        }

        return _cachedFilteredTables;
    }

    private Table[] GetDependantTables() 
    {
        if(!_config.IncludeDependantTables())
        {
            return [];
        }

        var dependencyWalker = new DependencyWalker(_server);
        var dependencyTree = dependencyWalker.DiscoverDependencies(_cachedFilteredTables?.Select(t => t.Urn).ToArray(), DependencyType.Children);

        // traverse through the dependencies
        HashSet<Table> dependentTables = [];
        TraverseDependencyTree(dependencyTree.FirstChild, dependentTables);

        return [.. dependentTables];
    }

    private void TraverseDependencyTree(DependencyTreeNode node, HashSet<Table> dependentTables)
    {
        if (node == null)
            return;

        // Only collect table dependencies
        if (node.Urn.Type == "Table")
        {
            var tableName = node.Urn.GetAttribute("Name");
            var tableSchema = node.Urn.GetAttribute("Schema");

            var table = _db.Tables.OfType<Table>().FirstOrDefault(t => t.Schema == tableSchema && t.Name == tableName);
            if(table != null && _config.ExportDependantTable(tableSchema, tableName) && !_cachedFilteredTables.Contains(table))
            {
                dependentTables.Add(table);
            }
        }

        // get children
        TraverseDependencyTree(node.FirstChild, dependentTables);

        if (node.NumberOfSiblings != 0)
        {
            var currentNode = node.NextSibling;
            while (currentNode != null)
            {
                TraverseDependencyTree(currentNode, dependentTables);
                currentNode = currentNode.NextSibling;
            }
        }
    }

    private void OutputMissingIncludedObjects(List<string> foundItems, OutputItemType outputType)
    {
        List<string> includedItems = GetIncludedObjects(outputType);
        var missingItems = includedItems.Except(foundItems).ToList();

        if (missingItems.Count != 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Explicitly included {outputType} not found:");
        }

        foreach (var table in missingItems)
        {
            Console.WriteLine($"* {table}");
        }

        if (missingItems.Count != 0)
        {
            Console.WriteLine("");
            Console.ResetColor();
        }
    }

    private List<string> GetIncludedObjects(OutputItemType outputType) => outputType switch
    {
        OutputItemType.Tables => _config.GetImportFilterSettings().IncludeTables.Select(table => table.Name).ToList(),
        OutputItemType.Views => _config.GetImportFilterSettings().IncludeViews.Select(view => view.Name).ToList(),
        OutputItemType.StoredProcedures => _config.GetImportFilterSettings().IncludeStoredProcedures.Select(storedProc => storedProc).ToList(),
        _ => throw new NotSupportedException()
    };

    private void OutputMissingExcludedColumns(Table[] filteredTables)
    {
        var filteredItems = filteredTables
            .ToDictionary(k => $"{k.Name} ({k.Schema})", v => v.Columns.Cast<Column>().Select(c => c.Name).ToHashSet());

        OutputMissingExcludedColumns(filteredItems, OutputItemType.Tables);
    }

    private void OutputMissingExcludedColumns(View[] filteredViews)
    {
        var filteredItems = filteredViews
            .ToDictionary(k => $"{k.Name} ({k.Schema})", v => v.Columns.Cast<Column>().Select(c => c.Name).ToHashSet());

        OutputMissingExcludedColumns(filteredItems, OutputItemType.Views);
    }

    private void OutputMissingExcludedColumns(Dictionary<string, HashSet<string>> foundItems, OutputItemType outputType)
    {
        var excludedItems = GetExcludedItemColumns(outputType);

        var missingItems = excludedItems
            .Where(e => foundItems.ContainsKey(e.Key))
            .Select(a => new
            {
                Table = a.Key,
                MissingColumns = a.Value.Except(foundItems[a.Key]).ToList()
            })
            .Where(a => a.MissingColumns.Count != 0) 
            .ToDictionary(a => a.Table, a => a.MissingColumns);

        if (missingItems.Count != 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Explicitly excluded Columns not Found:");
        }

        foreach (var record in missingItems)
        {
            Console.WriteLine($"* {record.Key}");
            foreach(var column in record.Value)
            {
                Console.WriteLine($"  - {column}");
            }
        }

        if (missingItems.Count != 0)
        {
            Console.WriteLine("");
            Console.ResetColor();
        }
    }

    private Dictionary<string, HashSet<string>> GetExcludedItemColumns(OutputItemType outputType) => outputType switch
    {
        OutputItemType.Tables => _config.GetImportFilterSettings().IncludeTables.Distinct().ToDictionary(t => t.Name, t => t.ExcludeColumns),
        OutputItemType.Views => _config.GetImportFilterSettings().IncludeViews.Distinct().ToDictionary(t => t.Name, t => t.ExcludeColumns),
        _ => throw new NotSupportedException()
    };

    private View[]? _cachedFilteredViews;

    private View[] GetFilteredViews()
    {
        return _cachedFilteredViews ??= _db.Views.OfType<View>()
            .Where(view => view.Schema is not "sys" and not "INFORMATION_SCHEMA" &&
                           !_viewsToIgnore.Contains(view.Name) && _config.ExportView(view.Schema, view.Name))
            .ToArray();
    }

    private StoredProcedure[]? _cachedFilteredStoredProcedures;
    private StoredProcedure[] GetFilteredStoredProcedures()
    {
        return _cachedFilteredStoredProcedures ??= _db.StoredProcedures.OfType<StoredProcedure>()
            .Where(storedProc => storedProc.Schema is not "sys" && 
                                 _config.ExportStoredProcedure(storedProc.Schema, storedProc.Name))
            .ToArray();
    }
    
    private UserDefinedTableType[]? _cachedFilteredUserDefinedTableTypes;

    private UserDefinedTableType[] GetFilteredUserDefinedTableTypes()
    {
        return _cachedFilteredUserDefinedTableTypes ??= _db.UserDefinedTableTypes.OfType<UserDefinedTableType>()
            .Where(type => type.Schema is not "sys" && _config.ExportSchema(type.Schema))
            .ToArray();
    }

    private static (string fullPackagePath, string packageName) GetPackageLocationAndName(string packageNameOrPath)
    {
        string fullPackagePath;
        string packageName;
        if (packageNameOrPath.EndsWith(".pkg.config", StringComparison.OrdinalIgnoreCase))
        {
            fullPackagePath = packageNameOrPath;
            var fileName = Path.GetFileName(packageNameOrPath);
            packageName = fileName.Substring(0, fileName.Length - ".pkg.config".Length);
        }
        else
        {
            packageName = packageNameOrPath;

            var directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (directoryName is null)
            {
                throw new Exception($"Path.GetDirectoryName(\"{Assembly.GetExecutingAssembly().Location}\") is null");
            }

            var packageLocation = Path.Combine(directoryName, "Packages");
            fullPackagePath = Path.Combine(packageLocation, $"{packageNameOrPath}.pkg.config");
        }

        return (fullPackagePath, packageName);
    }

    private static string? GetTypeId(DataType dataType)
    {
        return GetTypeId(dataType.SqlDataType);
    }

    private static string? GetTypeId(SqlDataType dataType)
    {
        switch (dataType)
        {
            case SqlDataType.Char:
            case SqlDataType.VarChar:
            case SqlDataType.VarCharMax:
            case SqlDataType.NVarChar:
            case SqlDataType.NVarCharMax:
            case SqlDataType.SysName:
            case SqlDataType.Xml:
            case SqlDataType.Text:
            case SqlDataType.NText:
                return Constants.TypeDefinitions.CommonTypes.String;
            case SqlDataType.Time:
                return Constants.TypeDefinitions.CommonTypes.TimeSpan;
            case SqlDataType.BigInt:
                return Constants.TypeDefinitions.CommonTypes.Long;
            case SqlDataType.Int:
                return Constants.TypeDefinitions.CommonTypes.Int;
            case SqlDataType.SmallInt:
                return Constants.TypeDefinitions.CommonTypes.Short;
            case SqlDataType.NChar:
            case SqlDataType.TinyInt:
                return Constants.TypeDefinitions.CommonTypes.Byte;
            case SqlDataType.SmallMoney:
            case SqlDataType.Money:
            case SqlDataType.Decimal:
            case SqlDataType.Numeric:
            case SqlDataType.Float:
            case SqlDataType.Real:
                return Constants.TypeDefinitions.CommonTypes.Decimal;
            case SqlDataType.Bit:
                return Constants.TypeDefinitions.CommonTypes.Bool;
            case SqlDataType.Date:
                return Constants.TypeDefinitions.CommonTypes.Date;
            case SqlDataType.DateTime:
            case SqlDataType.DateTime2:
            case SqlDataType.SmallDateTime:
                return Constants.TypeDefinitions.CommonTypes.Datetime;
            case SqlDataType.UniqueIdentifier:
                return Constants.TypeDefinitions.CommonTypes.Guid;
            case SqlDataType.Binary:
            case SqlDataType.VarBinary:
            case SqlDataType.VarBinaryMax:
            case SqlDataType.Timestamp:
            case SqlDataType.Image:
                return Constants.TypeDefinitions.CommonTypes.Binary;
            case SqlDataType.DateTimeOffset:
                return Constants.TypeDefinitions.CommonTypes.DatetimeOffset;
            case SqlDataType.None:
            case SqlDataType.UserDefinedDataType:
            case SqlDataType.UserDefinedType:
            case SqlDataType.Variant:
            case SqlDataType.UserDefinedTableType:
            case SqlDataType.Geometry:
            case SqlDataType.Geography:
            case SqlDataType.HierarchyId:
            case SqlDataType.Json:
                Logging.LogWarning($"Unsupported column type: {dataType.ToString()}");
                return null;
            default:
                Logging.LogWarning($"Unknown column type: {dataType.ToString()}");
                return null;
        }
    }

    private enum OutputItemType
    {
        Tables,
        Views,
        StoredProcedures
    }
}