﻿using Intent.IArchitect.Agent.Persistence.Model;
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

        public SqlSchemaExtractor(ImportConfiguration config, Database db)
        {
            _db = db;
            _config = config;
        }

        public PackageModelPersistable BuildPackageModel(string packageNameOrPath, SchemaExtractorConfiguration config)
        {
            var (fullPackagePath, packageName) = GetPackageLocationAndName(packageNameOrPath);
            var package = ElementHelper.GetOrCreateDomainPackage(fullPackagePath, packageName);
            package.IsExternalOld = false;

            ApplyStereotypes(config, package);
            if (_config.ExportTables())
            {
                ProcessTables(config, package);
                ProcessForeignKeys(config, package);
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

            return package;
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

        private void ProcessTables(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Tables");
            Console.WriteLine("======");
            Console.WriteLine();

            var filteredTables = _db.Tables.OfType<Table>().Where(table => table.Name is not "sysdiagrams").ToArray();
            var tableCount = filteredTables.Length;
            var tableNumber = 0;
            foreach (Table table in filteredTables)
            {
                if (!_config.ExportSchema(table.Schema))
                {
                    continue;
                }

                Console.WriteLine($"{table.Name} ({++tableNumber}/{tableCount})");

                var folder = package.GetOrCreateFolder(table.Schema);
                AddSchemaStereotype(folder, table.Schema);
                var @class = package.GetOrCreateClass(folder.Id, table.ID.ToString(), GetEntityName(table.Name));

                foreach (var handler in config.OnTableHandlers)
                {
                    handler(_config, table, @class);
                }

                foreach (Column col in table.Columns)
                {
                    var attribute = @class.GetOrCreateAttribute(table.Name, col.ID.ToString(), col.Name, col.Nullable);

                    var typeId = GetTypeId(col.DataType);
                    attribute.TypeReference.TypeId = typeId;

                    foreach (var handler in config.OnTableColumnHandlers)
                    {
                        handler(col, attribute);
                    }
                }

                foreach (Index tableIndex in table.Indexes)
                {
                    if (tableIndex.IsClustered)
                    {
                        continue;
                    }

                    foreach (var handler in config.OnIndexHandlers)
                    {
                        handler(tableIndex, @class);
                    }
                }
            }
        }

        private string GetEntityName(string name)
        {
            return _config.EntityNameConvention switch
            {
                EntityNameConvention.SingularEntity => name.Singularize(false),
                EntityNameConvention.MatchTable => name,
                _ => name
            };
        }

        private void AddSchemaStereotype(ElementPersistable folder, string schemaName)
        {
            folder.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.Schema.DefinitionId, ster =>
            {
                ster.Name = Constants.Stereotypes.Rdbms.Schema.Name;
                ster.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
                ster.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
                ster.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Schema.PropertyId.Name,
                    prop =>
                    {
                        prop.Name = Constants.Stereotypes.Rdbms.Schema.PropertyId.NameName;
                        prop.Value = schemaName;
                    });
            });
        }

        private void ProcessForeignKeys(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Foreign Keys");
            Console.WriteLine("============");
            Console.WriteLine();

            var filteredTables = _db.Tables.OfType<Table>().Where(table => table.Name != "sysdiagrams").ToArray();
            foreach (Table table in filteredTables)
            {
                if (!_config.ExportSchema(table.Schema))
                {
                    continue;
                }

                var @class = package.Classes.SingleOrDefault(x => x.ExternalReference == table.ID.ToString() && x.IsClass());
                var sourcePKs = GetPrimaryKeys(table);
                foreach (ForeignKey foreignKey in table.ForeignKeys)
                {
                    var sourceColumns = foreignKey.Columns.Cast<ForeignKeyColumn>().Select(x => GetColumn(table, x.Name)).ToList();
                    var targetTable = GetTable(foreignKey.Columns[0].Parent.ReferencedTableSchema, foreignKey.Columns[0].Parent.ReferencedTable);
                    var targetColumn = foreignKey.Columns[0].Name;

                    var sourceClassId = package.Classes.Single(x => x.ExternalReference == table.ID.ToString() && x.IsClass()).Id;
                    var targetClassId = package.Classes.Single(x => x.ExternalReference == targetTable.ID.ToString() && x.IsClass()).Id;
                    string targetName = null;

                    var singularTableName = targetTable.Name.Singularize(false);
                    if (sourceColumns[0].Name.IndexOf(singularTableName, StringComparison.Ordinal) == 0)
                    {
                        targetName = singularTableName;
                    }
                    else
                    {
                        switch (sourceColumns[0].Name.IndexOf(targetTable.Name, StringComparison.Ordinal))
                        {
                            case -1:

                                //Ordinal Case
                                targetName = sourceColumns[0].Name.Replace("ID", "", StringComparison.Ordinal) + targetTable.Name;
                                break;
                            case 0:
                                targetName = singularTableName;
                                break;
                            default:
                                targetName = sourceColumns[0].Name
                                    .Substring(0, sourceColumns[0].Name.IndexOf(targetTable.Name, StringComparison.Ordinal) + targetTable.Name.Length);
                                break;
                        }
                    }

                    bool skip = false;
                    var association = package.Associations.SingleOrDefault(x => x.ExternalReference == foreignKey.ID.ToString());
                    if (association is null)
                    {
						var associationId = Guid.NewGuid().ToString();
                        association = new AssociationPersistable()
                        {
                            Id = associationId,
                            ExternalReference = foreignKey.ID.ToString(),
                            AssociationType = "Association",
                            TargetEnd = new AssociationEndPersistable()
                            {
                                //Keep this the same as association Id
                                Id = associationId,
                                Name = targetName,
                                TypeReference = new TypeReferencePersistable()
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    TypeId = targetClassId,
                                    IsNavigable = true,
                                    IsNullable = sourceColumns.Any(x => x.Nullable),
                                    IsCollection = false,
                                },
                                Stereotypes = new List<StereotypePersistable>(),
                            },
                            SourceEnd = new AssociationEndPersistable
                            {
                                Id = Guid.NewGuid().ToString(),
                                TypeReference = new TypeReferencePersistable()
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    TypeId = sourceClassId,
                                    IsNavigable = false,
                                    IsNullable = false,
                                    IsCollection = !(sourcePKs.Length == sourceColumns.Count && sourceColumns.All(x => sourcePKs.Any(pk => pk == x.Name))),
                                },
                                Stereotypes = new List<StereotypePersistable>()
                            }
                        };
                        if (!SameAssociationExistsWithReverseOwnership(package.Associations, association))
                        {
                            package.Associations.Add(association);
                        }
                        else
						{
                            skip = true;
							Console.Write($"Skipping - ");
						}

					}

					Console.WriteLine($"{table.Name}: {sourceColumns[0].Name} " +
                                      $"[{(association.SourceEnd.TypeReference.IsNullable ? "0" : "1")}..{(association.SourceEnd.TypeReference.IsCollection ? "*" : "1")}] " +
                                      "--> " +
                                      $"[{(association.TargetEnd.TypeReference.IsNullable ? "0" : "1")}..{(association.TargetEnd.TypeReference.IsCollection ? "*" : "1")}] " +
                                      $"{targetTable.Name}: {targetColumn}");

                    var attribute = @class.ChildElements
                        .FirstOrDefault(p => p.SpecializationType == "Attribute" &&
                                             p.ExternalReference == sourceColumns[0].ID.ToString());
                    if (attribute is not null && !skip)
                    {
                        if (attribute.Metadata.All(p => p.Key != "fk-original-name"))
                        {
                            attribute.Metadata.Add(new GenericMetadataPersistable
                            {
                                Key = "fk-original-name",
                                Value = attribute.Name
                            });
                        }

                        if (attribute.Metadata.All(p => p.Key != "association"))
                        {
                            attribute.Metadata.Add(new GenericMetadataPersistable
                            {
                                Key = "association",
                                Value = association.Id
                            });
                        }

                        attribute.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.ForeignKey.DefinitionId, ster =>
                            {
                                ster.Name = Constants.Stereotypes.Rdbms.ForeignKey.Name;
                                ster.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
                                ster.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
                                ster.GetOrCreateProperty(Constants.Stereotypes.Rdbms.ForeignKey.PropertyId.Association,
                                    prop => prop.Name = Constants.Stereotypes.Rdbms.ForeignKey.PropertyId.AssociationName);
                            })
                            .GetOrCreateProperty(Constants.Stereotypes.Rdbms.ForeignKey.PropertyId.Association)
                            .Value = association.TargetEnd.Id;
					}
					if (attribute is not null && HasDefaultForeignKeyIndex(attribute, out var index))
					{
						attribute.Stereotypes.Remove(index);
					}
				}
			}
        }

        /// <summary>
        /// This is to catch associations which have been manually fixed and not overwrite them
        /// Associations with reverse ownership might have been recreated and the external ref has been lost
        /// </summary>
		private bool SameAssociationExistsWithReverseOwnership(IList<AssociationPersistable> associations, AssociationPersistable association)
		{
            return associations.FirstOrDefault(a => 
                a.SourceEnd.TypeReference.TypeId == association.TargetEnd.TypeReference.TypeId &&
				a.TargetEnd.TypeReference.TypeId == association.SourceEnd.TypeReference.TypeId &&
				a.SourceEnd.TypeReference.IsNullable == association.TargetEnd.TypeReference.IsNullable &&
				a.SourceEnd.TypeReference.IsCollection == association.TargetEnd.TypeReference.IsCollection &&
				a.TargetEnd.TypeReference.IsNullable == association.SourceEnd.TypeReference.IsNullable &&
				a.TargetEnd.TypeReference.IsCollection == association.SourceEnd.TypeReference.IsCollection
				) != null;
		}

		private bool HasDefaultForeignKeyIndex(ElementPersistable attribute, out StereotypePersistable? foundIndex)
        {
			foundIndex = attribute.Stereotypes.FirstOrDefault(s =>
                s.DefinitionId == Constants.Stereotypes.Rdbms.Index.DefinitionId &&
                s.Properties.FirstOrDefault(p => p.Name == "UniqueKey")?.Value == $"IX_{attribute.Name}");
            return foundIndex != null;
		}

		private void ProcessViews(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Views");
            Console.WriteLine("=====");
            Console.WriteLine();

            var filteredViews = _db.Views.OfType<View>().Where(view => view.Schema is not "sys" and not "INFORMATION_SCHEMA").ToArray();
            var viewsCount = filteredViews.Length;
            var viewNumber = 0;
            foreach (View view in filteredViews)
            {
                if (!_config.ExportSchema(view.Schema))
                {
                    continue;
                }

                var folder = package.GetOrCreateFolder(view.Schema);
                AddSchemaStereotype(folder, view.Schema);
                var @class = package.GetOrCreateClass(folder.Id, view.ID.ToString(), view.Name);

                Console.WriteLine($"{view.Name} ({++viewNumber}/{viewsCount})");

                foreach (var handler in config.OnViewHandlers)
                {
                    handler(view, @class);
                }

                foreach (Column col in view.Columns)
                {
                    var attribute = @class.GetOrCreateAttribute(view.Name, col.ID.ToString(), col.Name, col.Nullable);

                    var typeId = GetTypeId(col.DataType);
                    attribute.TypeReference.TypeId = typeId;

                    foreach (var handler in config.OnViewColumnHandlers)
                    {
                        handler(col, attribute);
                    }
                }
            }
        }

        private void ProcessStoredProcedures(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Stored Procedures");
            Console.WriteLine("=================");
            Console.WriteLine();

            var filteredStoredProcs = _db.StoredProcedures.OfType<StoredProcedure>().Where(storedProc => storedProc.Schema is not "sys").ToArray();
            var storedProcsCount = filteredStoredProcs.Length;
            var storedProcsNumber = 0;
            foreach (StoredProcedure storedProc in filteredStoredProcs)
            {
                if (!_config.ExportSchema(storedProc.Schema))
                {
                    continue;
                }

                Console.WriteLine($"{storedProc.Name} ({++storedProcsNumber}/{storedProcsCount})");

                var folder = package.GetOrCreateFolder(storedProc.Schema);
                AddSchemaStereotype(folder, storedProc.Schema);
                var repository = package.GetOrCreateRepository(folder.Id, storedProc.Schema, $"StoredProcedureRepository");
                var repoStoredProc = repository.GetOrCreateStoredProcedure(storedProc.ID.ToString(), storedProc.Name);

                var tableId = GetTableIdInResultSet(storedProc);
                if (tableId is not null)
                {
                    var @class = package.GetOrCreateClass(null, tableId, null);
                    repoStoredProc.TypeReference = new TypeReferencePersistable
                    {
                        Id = Guid.NewGuid().ToString(),
                        IsNullable = false,
                        IsCollection = false,
                        Stereotypes = new List<StereotypePersistable>(),
                        GenericTypeParameters = new List<TypeReferencePersistable>(),
                        TypeId = @class.Id
                    };
                }

                foreach (StoredProcedureParameter procParameter in storedProc.Parameters)
                {
                    var param = repoStoredProc.GetOrCreateStoredProcedureParameter(procParameter.ID.ToString(), procParameter.Name);
                    var typeId = GetTypeId(procParameter.DataType);
                    param.TypeReference.TypeId = typeId;
                }

                foreach (var handler in config.OnStoredProcedureHandlers)
                {
                    handler(storedProc, repoStoredProc);
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
    @tsql = N'EXEC {storedProc.Name}',
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

        private static string[] GetPrimaryKeys(Table table)
        {
            foreach (Index tableIndex in table.Indexes)
            {
                if (tableIndex.IndexKeyType == IndexKeyType.DriPrimaryKey)
                {
                    return tableIndex.IndexedColumns.Cast<IndexedColumn>().Select(x => x.Name).ToArray();
                }
            }

            return Array.Empty<string>();
        }

        private static Column GetColumn(Table table, string columnName)
        {
            foreach (Column column in table.Columns)
            {
                if (column.Name == columnName)
                {
                    return column;
                }
            }

            return null;
        }

        private Table GetTable(string schema, string tableName)
        {
            foreach (Table table in _db.Tables)
            {
                if (table.Schema == schema && table.Name == tableName)
                {
                    return table;
                }
            }

            return null;
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
                case SqlDataType.HierarchyId:
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
                    Console.WriteLine($"WARNING: Unsupported column type: {dataType.SqlDataType.ToString()}");
                    return null;
                default:
                    Console.WriteLine($"WARNING: Unknown column type: {dataType.SqlDataType.ToString()}");
                    return null;
            }
        }
    }

    public class SchemaExtractorConfiguration
    {
        public IEnumerable<Action<ImportConfiguration, Table, ElementPersistable>> OnTableHandlers { get; set; } =
            new List<Action<ImportConfiguration, Table, ElementPersistable>>();

        public IEnumerable<Action<Column, ElementPersistable>> OnTableColumnHandlers { get; set; } = new List<Action<Column, ElementPersistable>>();
        public IEnumerable<Action<Index, ElementPersistable>> OnIndexHandlers { get; set; } = new List<Action<Index, ElementPersistable>>();
        public IEnumerable<Action<View, ElementPersistable>> OnViewHandlers { get; set; } = new List<Action<View, ElementPersistable>>();
        public IEnumerable<Action<Column, ElementPersistable>> OnViewColumnHandlers { get; set; } = new List<Action<Column, ElementPersistable>>();
        public IEnumerable<Action<StoredProcedure, ElementPersistable>> OnStoredProcedureHandlers { get; set; } = new List<Action<StoredProcedure, ElementPersistable>>();
    }
}