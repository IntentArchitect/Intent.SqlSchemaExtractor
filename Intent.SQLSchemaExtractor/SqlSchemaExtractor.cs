using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Index = Microsoft.SqlServer.Management.Smo.Index;

namespace Intent.SQLSchemaExtractor;

public class SqlSchemaExtractor
{
    private readonly Database _db;
    private readonly ImportConfiguration _config;
    private readonly List<string> _tablesToIgnore = new() { "sysdiagrams", "__MigrationHistory", "__EFMigrationsHistory" };
    private readonly List<string> _viewsToIgnore = new() { };
    private readonly HashSet<string> _tableViewsFilter;

    public string SchemaVersion => "2.0";

    public SqlSchemaExtractor(ImportConfiguration config, Database db)
    {
        _db = db;
        _config = config;
        _tableViewsFilter = new HashSet<string>(_config.GetFilteredTableViewList(), StringComparer.InvariantCultureIgnoreCase);
    }

    private bool IncludeTableView(string tableOrViewName)
    {
        return _tableViewsFilter.Count == 0 || _tableViewsFilter.Contains(tableOrViewName);
    }

    public PackageModelPersistable BuildPackageModel(string packageNameOrPath, SchemaExtractorConfiguration config)
    {
        var (fullPackagePath, packageName) = GetPackageLocationAndName(packageNameOrPath);
        var package = ModelSchemaHelper.GetOrCreateDomainPackage(fullPackagePath, packageName);
        var modelSchemaHelper = new ModelSchemaHelper(_config, package, _db);
        var savedSchemaVersion = package.Metadata.FirstOrDefault(m => m.Key == "sql-import:schemaVersion")?.Value;
        if (savedSchemaVersion != SchemaVersion)
        {
            MigrateSchema(package, savedSchemaVersion);
        }

        package.IsExternalOld = false;

        ApplyStereotypes(package);
        if (_config.ExportTables())
        {
            ProcessTables(config, modelSchemaHelper);
            ProcessForeignKeys(modelSchemaHelper);
        }

        if (_config.ExportIndexes())
        {
            ProcessIndexes(config, modelSchemaHelper);
        }

        if (_config.ExportViews())
        {
            ProcessViews(config, modelSchemaHelper);
        }

        if (_config.ExportStoredProcedures())
        {
            ProcessStoredProcedures(config, modelSchemaHelper);
        }

        package.References ??= new List<PackageReferenceModel>();
        package.AddMetadata("sql-import:schemaVersion", SchemaVersion);

        return package;
    }

    private static void MigrateSchema(PackageModelPersistable package, string? oldFileVersion)
    {
    }

    private Table[]? _cachedFilteredTables;

    private Table[] GetFilteredTables()
    {
        return _cachedFilteredTables ??= _db.Tables.OfType<Table>()
            .Where(table => !_tablesToIgnore.Contains(table.Name) && _config.ExportSchema(table.Schema) && IncludeTableView(table.Name))
            .ToArray();
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


    private void ProcessIndexes(SchemaExtractorConfiguration config, ModelSchemaHelper modelSchemaHelper)
    {
        Console.WriteLine();
        Console.WriteLine("Indexes");
        Console.WriteLine("======");
        Console.WriteLine();

        var filteredTables = GetFilteredTables();
        foreach (var table in filteredTables)
        {
            var @class = modelSchemaHelper.GetClass(table);
            if (@class == null)
            {
                continue;
            }

            foreach (Index tableIndex in table.Indexes)
            {
                if (tableIndex.IsClustered)
                {
                    continue;
                }

                foreach (var handler in config.OnIndexHandlers)
                {
                    handler(_config, tableIndex, @class, modelSchemaHelper);
                }
            }
        }
    }

    private void ProcessTables(SchemaExtractorConfiguration config, ModelSchemaHelper modelSchemaHelper)
    {
        Console.WriteLine();
        Console.WriteLine("Tables");
        Console.WriteLine("======");
        Console.WriteLine();

        var filteredTables = GetFilteredTables();
        var tableCount = filteredTables.Length;
        var tableNumber = 0;

        foreach (var table in filteredTables)
        {
            var @class = modelSchemaHelper.GetOrCreateClass(table);

            Console.WriteLine($"{table.Name} ({++tableNumber}/{tableCount})");

            foreach (var handler in config.OnTableHandlers)
            {
                handler(_config, table, @class);
            }

            foreach (Column col in table.Columns)
            {
                var attribute = ModelSchemaHelper.GetOrCreateAttribute(col, @class);

                var typeId = GetTypeId(col.DataType);
                attribute.TypeReference.TypeId = typeId;

                foreach (var handler in config.OnTableColumnHandlers)
                {
                    handler(col, attribute);
                }
            }
        }
    }

    private void ProcessForeignKeys(ModelSchemaHelper modelSchemaHelper)
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
                var association = modelSchemaHelper.GetOrCreateAssociation(foreignKey);
            }
        }
    }

    private View[]? _cachedFilteredViews;

    private View[] GetFilteredViews()
    {
        return _cachedFilteredViews ??= _db.Views.OfType<View>()
            .Where(view => view.Schema is not "sys" and not "INFORMATION_SCHEMA" &&
                           !_viewsToIgnore.Contains(view.Name) &&
                           _config.ExportSchema(view.Schema) &&
                           IncludeTableView(view.Name))
            .ToArray();
    }

    private void ProcessViews(SchemaExtractorConfiguration config, ModelSchemaHelper modelSchemaHelper)
    {
        Console.WriteLine();
        Console.WriteLine("Views");
        Console.WriteLine("=====");
        Console.WriteLine();

        var filteredViews = GetFilteredViews();
        var viewsCount = filteredViews.Length;
        var viewNumber = 0;
        foreach (var view in filteredViews)
        {
            var @class = modelSchemaHelper.GetOrCreateClass(view);

            Console.WriteLine($"{view.Name} ({++viewNumber}/{viewsCount})");

            foreach (var handler in config.OnViewHandlers)
            {
                handler(view, @class);
            }

            foreach (Column col in view.Columns)
            {
                var attribute = ModelSchemaHelper.GetOrCreateAttribute(col, @class);

                var typeId = GetTypeId(col.DataType);
                attribute.TypeReference.TypeId = typeId;

                foreach (var handler in config.OnViewColumnHandlers)
                {
                    handler(col, attribute);
                }
            }
        }
    }

    private StoredProcedure[] GetFilteredStoredProcedures()
    {
        return _db.StoredProcedures.OfType<StoredProcedure>().Where(storedProc => storedProc.Schema is not "sys" && _config.ExportSchema(storedProc.Schema)).ToArray();
    }


    private void ProcessStoredProcedures(SchemaExtractorConfiguration config, ModelSchemaHelper modelSchemaHelper)
    {
        Console.WriteLine();
        Console.WriteLine("Stored Procedures");
        Console.WriteLine("=================");
        Console.WriteLine();

        var filteredStoredProcedures = GetFilteredStoredProcedures();
        var storedProceduresCount = filteredStoredProcedures.Length;
        var storedProceduresNumber = 0;
        foreach (var storedProc in filteredStoredProcedures)
        {
            if (!_config.ExportSchema(storedProc.Schema))
            {
                continue;
            }

            Console.WriteLine($"{storedProc.Name} ({++storedProceduresNumber}/{storedProceduresCount})");

            var modelStoredProcedure = modelSchemaHelper.GetOrCreateStoredProcedure(storedProc);
                
            var resultSet = StoredProcExtractor.GetStoredProcedureResultSet(_db, storedProc);
            if (resultSet.TableCount == 1)
            {
                var table = GetFilteredTables().FirstOrDefault(p => p.ID == resultSet.TableIds[0]);
                if (table is not null)
                {
                    var @class = modelSchemaHelper.GetOrCreateClass(table);
                    modelStoredProcedure.TypeReference = new TypeReferencePersistable
                    {
                        Id = Guid.NewGuid().ToString(),
                        IsNullable = false,
                        IsCollection = false,
                        Stereotypes = [],
                        GenericTypeParameters = [],
                        TypeId = @class.Id
                    };
                }
            }
            else if (resultSet.TableCount > 1)
            {
                var dataContract = modelSchemaHelper.GetOrCreateDataContractResponse(storedProc);
                modelStoredProcedure.TypeReference = new TypeReferencePersistable
                {
                    Id = Guid.NewGuid().ToString(),
                    IsNullable = false,
                    IsCollection = false,
                    Stereotypes = [],
                    GenericTypeParameters = [],
                    TypeId = dataContract.Id
                };
                
                foreach (var column in resultSet.Columns)
                {
                    var attribute = ModelSchemaHelper.GetOrCreateAttribute(column, dataContract);
            
                    var typeId = GetTypeId(column.SqlDataType);
                    attribute.TypeReference.TypeId = typeId;
                }
            }

            foreach (StoredProcedureParameter procParameter in storedProc.Parameters)
            {
                var param = modelSchemaHelper.GetOrCreateStoredProcedureParameter(procParameter, modelStoredProcedure);
                var typeId = GetTypeId(procParameter.DataType);
                param.TypeReference.TypeId = typeId;
            }

            foreach (var handler in config.OnStoredProcedureHandlers)
            {
                handler(storedProc, modelStoredProcedure);
            }
        }
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
                Logging.LogWarning($"Unsupported column type: {dataType.ToString()}");
                return null;
            case SqlDataType.Json:
            default:
                Logging.LogWarning($"Unknown column type: {dataType.ToString()}");
                return null;
        }
    }
}

public class SchemaExtractorConfiguration
{
    public IEnumerable<Action<ImportConfiguration, Table, ElementPersistable>> OnTableHandlers { get; set; } =
        new List<Action<ImportConfiguration, Table, ElementPersistable>>();

    public IEnumerable<Action<Column, ElementPersistable>> OnTableColumnHandlers { get; set; } = new List<Action<Column, ElementPersistable>>();

    public IEnumerable<Action<ImportConfiguration, Index, ElementPersistable, ModelSchemaHelper>> OnIndexHandlers { get; set; } =
        new List<Action<ImportConfiguration, Index, ElementPersistable, ModelSchemaHelper>>();

    public IEnumerable<Action<View, ElementPersistable>> OnViewHandlers { get; set; } = new List<Action<View, ElementPersistable>>();
    public IEnumerable<Action<Column, ElementPersistable>> OnViewColumnHandlers { get; set; } = new List<Action<Column, ElementPersistable>>();
    public IEnumerable<Action<StoredProcedure, ElementPersistable>> OnStoredProcedureHandlers { get; set; } = new List<Action<StoredProcedure, ElementPersistable>>();
}