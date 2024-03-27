using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Intent.Modules.Common.Templates;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.SqlServer.Management.Common;
using Index = Microsoft.SqlServer.Management.Smo.Index;

namespace Intent.SQLSchemaExtractor
{
    public class SqlSchemaExtractor
    {
        private readonly Database _db;
        private readonly ImportConfiguration _config;
        private ModelSchemaHelper _modelSchemaHelper;
        private List<string> _tablesToIgnore = new List<string> { "sysdiagrams", "__EFMigrationsHistory" };
		private List<string> _viewsToIgnore = new List<string> { "INFORMATION_SCHEMA" };

		public string SchemaVersion { get; } = "2.0";

        public SqlSchemaExtractor(ImportConfiguration config, Database db)
        {
            _db = db;
            _config = config;

		}

        public PackageModelPersistable BuildPackageModel(string packageNameOrPath, SchemaExtractorConfiguration config)
        {
            var (fullPackagePath, packageName) = GetPackageLocationAndName(packageNameOrPath);
            var package = ModelSchemaHelper.GetOrCreateDomainPackage(fullPackagePath, packageName);
			_modelSchemaHelper = new ModelSchemaHelper(_config, package, _db);
			var savedSchemaVersion = package.Metadata.FirstOrDefault(m => m.Key == "sql-import:schemaVersion")?.Value;
            if (savedSchemaVersion != SchemaVersion)
            {
                MigrateSchema(package, savedSchemaVersion);
            }
            package.IsExternalOld = false;

            ApplyStereotypes(config, package);
            if (_config.ExportTables())
            {
                ProcessTables(config, package);
                ProcessForeignKeys(config, package);
            }
            if (_config.ExportIndexes())
            {
				ProcessIndexes(config, package);
			}

			if (_config.ExportViews())
            {
                ProcessViews(config, package);
            }

            if (_config.ExportStoredProcedures())
            {
                ProcessStoredProcedures(config, package);
            }

            package.References ??= new List<PackageReferenceModel>();
			package.AddMetadata("sql-import:schemaVersion", SchemaVersion);

			return package;
        }

		private void MigrateSchema(PackageModelPersistable package, string? oldFileVersion)
		{
		}

        private Table[] GetFilteredTables()
        {
				return _db.Tables.OfType<Table>().Where(table => !_tablesToIgnore.Contains( table.Name) && _config.ExportSchema(table.Schema)).ToArray();
		}

		private static void ApplyStereotypes(SchemaExtractorConfiguration config, PackageModelPersistable package)
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


        private void ProcessIndexes(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Indexes");
            Console.WriteLine("======");
            Console.WriteLine();

            var filteredTables = GetFilteredTables();
            var tableCount = filteredTables.Length;
            foreach (Table table in filteredTables)
            {
                var @class = _modelSchemaHelper.GetClass(table);

				if (@class != null)
                {
                    foreach (Index tableIndex in table.Indexes)
                    {
                        if (tableIndex.IsClustered)
                        {
                            continue;
                        }

                        foreach (var handler in config.OnIndexHandlers)
                        {
                            handler(_config, tableIndex, @class, _modelSchemaHelper);
                        }
                    }
                }
			}
        }

        private void ProcessTables(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Tables");
            Console.WriteLine("======");
            Console.WriteLine();

			var filteredTables = GetFilteredTables();
			var tableCount = filteredTables.Length;
            var tableNumber = 0;

			foreach (Table table in filteredTables)
            {
                var @class = _modelSchemaHelper.GetOrCreateClass(table);

                Console.WriteLine($"{table.Name} ({++tableNumber}/{tableCount})");

                foreach (var handler in config.OnTableHandlers)
                {
                    handler(_config, table, @class);
                }

                foreach (Column col in table.Columns)
                {
                    var attribute = _modelSchemaHelper.GetOrCreateAttribute(col, @class);

                    var typeId = GetTypeId(col.DataType);
                    attribute.TypeReference.TypeId = typeId;

                    foreach (var handler in config.OnTableColumnHandlers)
                    {
                        handler(col, attribute);
                    }
                }
            }
        }

		private void ProcessForeignKeys(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Foreign Keys");
            Console.WriteLine("============");
            Console.WriteLine();

			var filteredTables = GetFilteredTables();
			foreach (Table table in filteredTables)
            {
                foreach (ForeignKey foreignKey in table.ForeignKeys)
                {
                    var association = _modelSchemaHelper.GetOrCreateAssociation(foreignKey);
				}
			}
		}
		private View[] GetFilteredViews()
		{
            return _db.Views.OfType<View>().Where(view => view.Schema is not "sys" && !_viewsToIgnore.Contains(view.Name) && _config.ExportSchema(view.Schema)).ToArray();
		}

		private void ProcessViews(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Views");
            Console.WriteLine("=====");
            Console.WriteLine();

            var filteredViews = GetFilteredViews();
            var viewsCount = filteredViews.Length;
            var viewNumber = 0;
            foreach (View view in filteredViews)
            {
                var @class = _modelSchemaHelper.GetOrCreateClass(view);

                Console.WriteLine($"{view.Name} ({++viewNumber}/{viewsCount})");

                foreach (var handler in config.OnViewHandlers)
                {
                    handler(view, @class);
                }

                foreach (Column col in view.Columns)
                {
                    var attribute = _modelSchemaHelper.GetOrCreateAttribute(col, @class);

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


		private void ProcessStoredProcedures(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Stored Procedures");
            Console.WriteLine("=================");
            Console.WriteLine();

            var filteredStoredProcs = GetFilteredStoredProcedures();
            var storedProcsCount = filteredStoredProcs.Length;
            var storedProcsNumber = 0;
            foreach (StoredProcedure storedProc in filteredStoredProcs)
            {
                if (!_config.ExportSchema(storedProc.Schema))
                {
                    continue;
                }

                Console.WriteLine($"{storedProc.Name} ({++storedProcsNumber}/{storedProcsCount})");

                var modelStoredProcedure = _modelSchemaHelper.GetOrCreateStoredProcedure(storedProc);

                var tableId = GetTableIdInResultSet(storedProc);
                if (tableId is not null)
                {
                    Console.WriteLine($"For Stored Procedure {modelStoredProcedure.ExternalReference} table type return types not currently supported");
                    /*
                    //Write out not supported warning
                    var @class = package.GetOrCreateClass(null, tableId, null);
                    repoStoredProc.TypeReference = new TypeReferencePersistable
                    {
                        Id = Guid.NewGuid().ToString(),
                        IsNullable = false,
                        IsCollection = false,
                        Stereotypes = new List<StereotypePersistable>(),
                        GenericTypeParameters = new List<TypeReferencePersistable>(),
                        TypeId = @class.Id
                    };*/
                }

                foreach (StoredProcedureParameter procParameter in storedProc.Parameters)
                {
                    var param = _modelSchemaHelper.GetOrCreateStoredProcedureParameter(procParameter, modelStoredProcedure);
                    var typeId = GetTypeId(procParameter.DataType);
                    param.TypeReference.TypeId = typeId;
                }

                foreach (var handler in config.OnStoredProcedureHandlers)
                {
                    handler(storedProc, modelStoredProcedure);
                }
            }
        }

        private string GetTableIdInResultSet(StoredProcedure storedProc)
        {
            DataSet describeResults = null;
            try
            {
                describeResults = _db.ExecuteWithResults($@"
EXEC sp_describe_first_result_set 
    @tsql = N'EXEC [{storedProc.Schema}].[{storedProc.Name}]',
    @params = N'',
    @browse_information_mode = 1");
            }
            catch
            {
                return null;
            }

            if (describeResults.Tables.Count == 0)
            {
                return null;
            }

            var dataTable = describeResults.Tables[0];

            string sourceSchema = null;
            string sourceTable = null;

            if (!dataTable.Columns.Contains("source_schema") || !dataTable.Columns.Contains("source_table"))
            {
                return null;
            }
            
            foreach (DataRow row in dataTable.Rows)
            {
                var schema = row["source_schema"].ToString();
                var table = row["source_table"].ToString();

                // If the source schema is already set and it's different from the current row's schema
                if (sourceSchema != null && sourceSchema != schema)
                {
                    return null;
                }

                // If the source table is already set and it's different from the current row's table
                if (sourceTable != null && sourceTable != table)
                {
                    return null;
                }

                sourceSchema = schema;
                sourceTable = table;
            }

            var tableIdResults = _db.ExecuteWithResults($@"SELECT OBJECT_ID('{sourceSchema}.{sourceTable}') AS TableID;");
            if (tableIdResults.Tables.Count != 1)
            {
                return null;
            }

            var tableId = tableIdResults.Tables[0].Rows[0]["TableID"].ToString();
            if (string.IsNullOrWhiteSpace(tableId))
            {
                return null;
            }
            return tableId;
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


        private static string GetTypeId(DataType dataType)
        {
            switch (dataType.SqlDataType)
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
					Logging.LogWarning($"Unsupported column type: {dataType.SqlDataType.ToString()}");
					return null;
				default:
					Logging.LogWarning($"Unknown column type: {dataType.SqlDataType.ToString()}");
                    return null;
            }
        }
    }

    public class SchemaExtractorConfiguration
    {
        public IEnumerable<Action<ImportConfiguration, Table, ElementPersistable>> OnTableHandlers { get; set; } =
            new List<Action<ImportConfiguration, Table, ElementPersistable>>();

        public IEnumerable<Action<Column, ElementPersistable>> OnTableColumnHandlers { get; set; } = new List<Action<Column, ElementPersistable>>();
        public IEnumerable<Action<ImportConfiguration, Index, ElementPersistable, ModelSchemaHelper>> OnIndexHandlers { get; set; } = new List<Action<ImportConfiguration, Index, ElementPersistable, ModelSchemaHelper>>();
        public IEnumerable<Action<View, ElementPersistable>> OnViewHandlers { get; set; } = new List<Action<View, ElementPersistable>>();
        public IEnumerable<Action<Column, ElementPersistable>> OnViewColumnHandlers { get; set; } = new List<Action<Column, ElementPersistable>>();
        public IEnumerable<Action<StoredProcedure, ElementPersistable>> OnStoredProcedureHandlers { get; set; } = new List<Action<StoredProcedure, ElementPersistable>>();
    }
}