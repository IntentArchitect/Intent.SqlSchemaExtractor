using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Index = Microsoft.SqlServer.Management.Smo.Index;

namespace Intent.SQLSchemaExtractor
{
    public class SqlSchemaExtractor
    {
        private readonly Database _db;

        public SqlSchemaExtractor(Database db)
        {
            _db = db;
        }

        public PackageModelPersistable BuildPackageModel(string packageNameOrPath, SchemaExtractorConfiguration config)
        {
            var (fullPackagePath, packageName) = GetPackageLocationAndName(packageNameOrPath);
            var package = ElementHelper.GetOrCreateDomainPackage(fullPackagePath, packageName);

            ProcessTables(config, package);
            ProcessForeignKeys(config, package);
            ProcessViews(config, package);
            ProcessStoredProcedures(config, package);

            package.References ??= new List<PackageReferenceModel>();

            return package;
        }

        private void ProcessTables(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Tables");
            Console.WriteLine("======");
            Console.WriteLine();
            
            // Classes
            foreach (Table table in _db.Tables)
            {
                if (table.Name == "sysdiagrams")
                {
                    continue;
                }

                var folder = package.GetOrCreateFolder(table.Schema);
                var @class = package.GetOrCreateClass(folder.Id, table.ID.ToString(), table.Name);

                foreach (var handler in config.OnTableHandlers)
                {
                    handler(table, @class);
                }

                Console.WriteLine(table.Name);
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

        private void ProcessForeignKeys(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Foreign Keys");
            Console.WriteLine("============");
            Console.WriteLine();
            
            // Associations
            foreach (Table table in _db.Tables)
            {
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

                    switch (sourceColumns[0].Name.IndexOf(targetTable.Name, StringComparison.Ordinal))
                    {
                        case -1:
                            targetName = sourceColumns[0].Name.Replace("ID", "") + targetTable.Name;
                            break;
                        case 0:
                            break;
                        default:
                            targetName = sourceColumns[0].Name.Substring(0, sourceColumns[0].Name.IndexOf(targetTable.Name, StringComparison.Ordinal) + targetTable.Name.Length);
                            break;
                    }

                    var association = package.Associations.SingleOrDefault(x => x.ExternalReference == foreignKey.ID.ToString());
                    if (association is null)
                    {
                        var associationId = Guid.NewGuid().ToString();
                        package.Associations.Add(association = new AssociationPersistable()
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
                        });
                    }

                    Console.WriteLine($"{table.Name}: {sourceColumns[0].Name} " +
                                      $"[{(association.SourceEnd.TypeReference.IsNullable ? "0" : "1")}..{(association.SourceEnd.TypeReference.IsCollection ? "*" : "1")}] " +
                                      "--> " +
                                      $"[{(association.TargetEnd.TypeReference.IsNullable ? "0" : "1")}..{(association.TargetEnd.TypeReference.IsCollection ? "*" : "1")}] " +
                                      $"{targetTable.Name}: {targetColumn}");

                    var attribute = @class.ChildElements
                        .FirstOrDefault(p => p.SpecializationType == "Attribute" &&
                                             p.ExternalReference == sourceColumns[0].ID.ToString());
                    if (attribute is not null)
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
                }
            }
        }
        
        private void ProcessViews(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Views");
            Console.WriteLine("=====");
            Console.WriteLine();
            
            foreach (View view in _db.Views)
            {
                if (view.Schema is "sys" or "INFORMATION_SCHEMA")
                {
                    continue;
                }
                
                var folder = package.GetOrCreateFolder(view.Schema);
                var @class = package.GetOrCreateClass(folder.Id, view.ID.ToString(), view.Name);

                Console.WriteLine(view.Name);
                
                foreach (var handler in config.OnViewHandlers)
                {
                    handler(view, @class);
                }
                
                foreach (Column col in view.Columns)
                {
                    var attribute = @class.GetOrCreateAttribute(view.Name, col.ID.ToString(), col.Name, col.Nullable);

                    foreach (var handler in config.OnViewColumnHandlers)
                    {
                        handler(col, attribute);
                    }

                    var typeId = GetTypeId(col.DataType);
                    attribute.TypeReference.TypeId = typeId;
                }
            }
        }
        
        private void ProcessStoredProcedures(SchemaExtractorConfiguration config, PackageModelPersistable package)
        {
            Console.WriteLine();
            Console.WriteLine("Stored Procedures");
            Console.WriteLine("=================");
            Console.WriteLine();

            foreach (StoredProcedure storedProc in _db.StoredProcedures)
            {
                if (storedProc.Schema is "sys")
                {
                    continue;
                }
                
                var folder = package.GetOrCreateFolder(storedProc.Schema);
                var repository = package.GetOrCreateRepository(folder.Id, storedProc.Schema, $"StoredProcedureRepository");
                var repoStoredProc = repository.GetOrCreateStoredProcedure(storedProc.ID.ToString(), storedProc.Name);
                // We're not setting the return type since its not simple to extract that from a SQL query

                foreach (StoredProcedureParameter procParameter in storedProc.Parameters)
                {
                    var param = repoStoredProc.GetOrCreateStoredProcedureParameter(procParameter.ID.ToString(), procParameter.Name);
                    var typeId = GetTypeId(procParameter.DataType);
                    param.TypeReference.TypeId = typeId;
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
        public IEnumerable<Action<Table, ElementPersistable>> OnTableHandlers { get; set; } = new List<Action<Table, ElementPersistable>>();
        public IEnumerable<Action<Column, ElementPersistable>> OnTableColumnHandlers { get; set; } = new List<Action<Column, ElementPersistable>>();
        public IEnumerable<Action<Index, ElementPersistable>> OnIndexHandlers { get; set; } = new List<Action<Index, ElementPersistable>>();
        public IEnumerable<Action<View, ElementPersistable>> OnViewHandlers { get; set; } = new List<Action<View, ElementPersistable>>();
        public IEnumerable<Action<Column, ElementPersistable>> OnViewColumnHandlers { get; set; } = new List<Action<Column, ElementPersistable>>();
    }
}
