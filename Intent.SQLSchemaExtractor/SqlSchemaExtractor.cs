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
                if (directoryName == null)
                {
                    throw new Exception($"Path.GetDirectoryName(\"{Assembly.GetExecutingAssembly().Location}\")");
                }

                var packageLocation = Path.Combine(directoryName, "Packages");
                fullPackagePath = Path.Combine(packageLocation, $"{packageNameOrPath}.pkg.config");
            }

            PackageModelPersistable package;
            if (File.Exists(fullPackagePath))
            {
                package = PackageModelPersistable.Load(fullPackagePath);
            }
            else
            {
                package = PackageModelPersistable.CreateEmpty(config.PackageType.Id, config.PackageType.Name, Guid.NewGuid().ToString(), packageName);
                package.AbsolutePath = fullPackagePath;
            }


            // Classes
            foreach (Table table in _db.Tables)
            {
                if (table.Name == "sysdiagrams")
                {
                    continue;
                }

                var folder = package.Classes.SingleOrDefault(x => x.Name == table.Schema && x.IsFolder(config));
                if (folder == null)
                {
                    package.AddElement(folder = new ElementPersistable()
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = table.Schema,
                        ParentFolderId = package.Id,
                        SpecializationTypeId = config.FolderType.Id,
                        SpecializationType = config.FolderType.Name
                    });
                }

                var @class = package.Classes.SingleOrDefault(x => x.ExternalReference == table.ID.ToString() && x.IsClass(config));
                if (@class == null)
                {
                    package.AddElement(@class = new ElementPersistable
                    {
                        Id = Guid.NewGuid().ToString(),
                        ParentFolderId = folder.Id,
                        Name = table.Name,
                        SpecializationTypeId = config.ClassType.Id,
                        SpecializationType = config.ClassType.Name,
                        ExternalReference = table.ID.ToString()
                    });
                }

                foreach (var handler in config.OnTableHandlers)
                {
                    handler(table, @class);
                }

                Console.WriteLine(table.Name);
                foreach (Column col in table.Columns)
                {
                    var normalizedColumnName = NormalizeColumnName(col.Name, table);
                    var attribute = @class.ChildElements.SingleOrDefault(x => x.ExternalReference == col.ID.ToString());
                    if (attribute == null)
                    {
                        @class.ChildElements.Add(attribute = new ElementPersistable()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = normalizedColumnName,
                            SpecializationTypeId = config.AttributeType.Id,
                            SpecializationType = config.AttributeType.Name,
                            Stereotypes = new List<StereotypePersistable>(),
                            TypeReference = new TypeReferencePersistable()
                            {
                                Id = Guid.NewGuid().ToString(),
                                IsNullable = col.Nullable,
                                IsCollection = false,
                                Stereotypes = new List<StereotypePersistable>(),
                                GenericTypeParameters = new List<TypeReferencePersistable>()
                            },
                            ExternalReference = col.ID.ToString()
                        });
                    }

                    foreach (var handler in config.OnColumnHandlers)
                    {
                        handler(col, attribute);
                    }

                    var typeId = GetTypeId(col.DataType);
                    attribute.TypeReference.TypeId = typeId;
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

            // Associations
            foreach (Table table in _db.Tables)
            {
                var sourcePKs = GetPrimaryKeys(table);
                foreach (ForeignKey foreignKey in table.ForeignKeys)
                {
                    var sourceColumns = foreignKey.Columns.Cast<ForeignKeyColumn>().Select(x => GetColumn(table, x.Name)).ToList();
                    var targetTable = GetTable(foreignKey.Columns[0].Parent.ReferencedTableSchema, foreignKey.Columns[0].Parent.ReferencedTable);
                    var targetColumn = foreignKey.Columns[0].Name;

                    var sourceClassId = package.Classes.Single(x => x.ExternalReference == table.ID.ToString() && x.IsClass(config)).Id;
                    var targetClassId = package.Classes.Single(x => x.ExternalReference == targetTable.ID.ToString() && x.IsClass(config)).Id;
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
                    if (association == null)
                    {
                        package.Associations.Add(association = new AssociationPersistable()
                        {
                            Id = Guid.NewGuid().ToString(),
                            ExternalReference = foreignKey.ID.ToString(),
                            AssociationType = "Association",
                            TargetEnd = new AssociationEndPersistable()
                            {
                                Id = Guid.NewGuid().ToString(),
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
                                    IsNullable = true,
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
                }
            }

            package.References ??= new List<PackageReferenceModel>();

            return package;
        }

        private static string NormalizeColumnName(string colName, Table table)
        {
            return (colName != table.Name) ? colName.Replace(" ", "") : colName + "Value";
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
                    return Constants.TypeDefinitions.CommonTypes.String;
                case SqlDataType.BigInt:
                case SqlDataType.Time:
                    return Constants.TypeDefinitions.CommonTypes.Long;
                case SqlDataType.Int:
                    return Constants.TypeDefinitions.CommonTypes.Int;
                // Don't know about a "long" type in SQL
                //case "long":
                //    return Constants.TypeDefinitions.CommonTypes.Long;
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
                case SqlDataType.VarBinary:
                case SqlDataType.VarBinaryMax:
                case SqlDataType.Timestamp:
                case SqlDataType.Image:
                    return Constants.TypeDefinitions.CommonTypes.Binary;
                case SqlDataType.DateTimeOffset:
                    return Constants.TypeDefinitions.CommonTypes.DatetimeOffset;
                default:
                    throw new ArgumentOutOfRangeException(nameof(dataType), dataType.SqlDataType.ToString());
            }
        }
    }

    public class SchemaExtractorConfiguration
    {
        public SpecializationType PackageType { get; set; } = new SpecializationType("Domain Package", "1a824508-4623-45d9-accc-f572091ade5a");
        public SpecializationType FolderType { get; set; } = new SpecializationType("Folder", "4d95d53a-8855-4f35-aa82-e312643f5c5f");
        public SpecializationType ClassType { get; set; } = new SpecializationType("Class", "04e12b51-ed12-42a3-9667-a6aa81bb6d10");
        public SpecializationType AttributeType { get; set; } = new SpecializationType("Attribute", "0090fb93-483e-41af-a11d-5ad2dc796adf");
        public IEnumerable<Action<Table, ElementPersistable>> OnTableHandlers { get; set; } = new List<Action<Table, ElementPersistable>>();
        public IEnumerable<Action<Column, ElementPersistable>> OnColumnHandlers { get; set; } = new List<Action<Column, ElementPersistable>>();
        public IEnumerable<Action<Index, ElementPersistable>> OnIndexHandlers { get; set; } = new List<Action<Index, ElementPersistable>>();
    }

    public class SpecializationType
    {
        public SpecializationType(string name, string id)
        {
            Name = name;
            Id = id;
        }
        public string Name { get; set; }
        public string Id { get; set; }
    }
}
