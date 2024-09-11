﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Intent.SQLSchemaExtractor.ExtensionMethods;
using Intent.SQLSchemaExtractor.ModelMapper;
using Microsoft.SqlServer.Management.Smo;
using Index = Microsoft.SqlServer.Management.Smo.Index;

namespace Intent.SQLSchemaExtractor.Extractors;

public class SqlServerSchemaExtractor
{
    private readonly Database _db;
    private readonly ImportConfiguration _config;
    private readonly List<string> _tablesToIgnore = ["sysdiagrams", "__MigrationHistory", "__EFMigrationsHistory"];
    private readonly List<string> _viewsToIgnore = [];
    private readonly HashSet<string> _tableViewsFilter;

    public string SchemaVersion => "2.0";

    public SqlServerSchemaExtractor(ImportConfiguration config, Database db)
    {
        _db = db;
        _config = config;
        _tableViewsFilter = new HashSet<string>(_config.GetFilteredTableViewList(), StringComparer.InvariantCultureIgnoreCase);
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
            ProcessUserDefinedTableTypes(modelSchemaHelper);
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

            foreach (Index tableIndex in table.Indexes)
            {
                if (tableIndex.IsClustered)
                {
                    continue;
                }

                foreach (var handler in eventManager.OnIndexHandlers)
                {
                    handler(_config, tableIndex, @class, databaseSchemaToModelMapper);
                }
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

        foreach (var table in filteredTables)
        {
            var @class = databaseSchemaToModelMapper.GetOrCreateClass(table);

            Console.WriteLine($"{table.Name} ({++tableNumber}/{tableCount})");

            foreach (var handler in eventManager.OnTableHandlers)
            {
                handler(_config, table, @class);
            }

            foreach (Column col in table.Columns)
            {
                var attribute = databaseSchemaToModelMapper.GetOrCreateAttribute(col, @class);

                var typeId = GetTypeId(col.DataType);
                attribute.TypeReference.TypeId = typeId;

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
        foreach (var view in filteredViews)
        {
            var @class = databaseSchemaToModelMapper.GetOrCreateClass(view);

            Console.WriteLine($"{view.Name} ({++viewNumber}/{viewsCount})");

            foreach (var handler in eventManager.OnViewHandlers)
            {
                handler(view, @class);
            }

            foreach (Column col in view.Columns)
            {
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

    private void ProcessUserDefinedTableTypes(DatabaseSchemaToModelMapper databaseSchemaToModelMapper)
    {
        Console.WriteLine();
        Console.WriteLine("User Defined Table Types");
        Console.WriteLine("=======================");
        Console.WriteLine();

        var filteredUserDefinedTableTypes = GetFilteredUserDefinedTableTypes();
        var userDefinedTableTypesCount = filteredUserDefinedTableTypes.Length;
        var userDefinedTableTypesNumber = 0;
        foreach (var userDefinedTableType in filteredUserDefinedTableTypes)
        {
            if (!_config.ExportSchema(userDefinedTableType.Schema))
            {
                continue;
            }

            Console.WriteLine($"{userDefinedTableType.Name} ({++userDefinedTableTypesNumber}/{userDefinedTableTypesCount})");

            var modelDataContract = databaseSchemaToModelMapper.GetOrCreateDataContract(userDefinedTableType);
            foreach (Column column in userDefinedTableType.Columns)
            {
                var attr = databaseSchemaToModelMapper.GetOrCreateAttribute(column, modelDataContract);
                attr.TypeReference.TypeId = GetTypeId(column.DataType);
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
        foreach (var storedProc in filteredStoredProcedures)
        {
            if (!_config.ExportSchema(storedProc.Schema))
            {
                continue;
            }

            Console.WriteLine($"{storedProc.Name} ({++storedProceduresNumber}/{storedProceduresCount})");

            var modelStoredProcedure = _config.StoredProcedureType == StoredProcedureType.StoredProcedureElement 
                ? databaseSchemaToModelMapper.GetOrCreateStoredProcedureElement(storedProc, _config.RepositoryElementId)
                : databaseSchemaToModelMapper.GetOrCreateStoredProcedureOperation(storedProc, _config.RepositoryElementId);
            
            var resultSet = StoredProcExtractor.GetStoredProcedureResultSet(_db, storedProc);
            if (resultSet.TableCount == 1)
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
            else if (resultSet.TableCount > 1)
            {
                var dataContract = databaseSchemaToModelMapper.GetOrCreateDataContractResponse(storedProc);
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

            return databaseSchemaToModelMapper.GetOrCreateDataContract(type).Id;
        }
    }
    
    private Table[]? _cachedFilteredTables;

    private Table[] GetFilteredTables()
    {
        return _cachedFilteredTables ??= _db.Tables.OfType<Table>()
            .Where(table => !_tablesToIgnore.Contains(table.Name) && _config.ExportSchema(table.Schema) && IncludeTableView(table.Name))
            .ToArray();
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
    
    private bool IncludeTableView(string tableOrViewName)
    {
        return _tableViewsFilter.Count == 0 || _tableViewsFilter.Contains(tableOrViewName);
    }

    private StoredProcedure[]? _cachedFilteredStoredProcedures;
    private StoredProcedure[] GetFilteredStoredProcedures()
    {
        return _cachedFilteredStoredProcedures ??= _db.StoredProcedures.OfType<StoredProcedure>()
            .Where(storedProc => storedProc.Schema is not "sys" && _config.ExportSchema(storedProc.Schema))
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
}