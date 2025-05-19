using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Intent.Modules.Common.Templates;
using Intent.SQLSchemaExtractor.ExtensionMethods;
using Intent.SQLSchemaExtractor.Extractors;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Intent.SQLSchemaExtractor.ModelMapper;

public class DatabaseSchemaToModelMapper
{
    private const string DomainMetadataId = "6ab29b31-27af-4f56-a67c-986d82097d63";
    private const string ColumnMappingSettingsId = "30f4278f-1d74-4e7e-bfdb-39c8e120f24c";

    internal static readonly SpecializationType DomainPackageType = new("Domain Package", "1a824508-4623-45d9-accc-f572091ade5a");
    internal static readonly SpecializationType RepositoryType = new("Repository", "96ffceb2-a70a-4b69-869b-0df436c470c3");
    internal static readonly SpecializationType FolderType = new("Folder", "4d95d53a-8855-4f35-aa82-e312643f5c5f");
    internal static readonly SpecializationType ClassType = new("Class", "04e12b51-ed12-42a3-9667-a6aa81bb6d10");
    internal static readonly SpecializationType DataContract = new("Data Contract", "4464fabe-c59e-4d90-81fc-c9245bdd1afd");
    internal static readonly SpecializationType AttributeType = new("Attribute", "0090fb93-483e-41af-a11d-5ad2dc796adf");
    internal static readonly SpecializationType IndexType = new("Index", "436e3afe-b4ef-481c-b803-0d1e7d263561");
    internal static readonly SpecializationType AssociationType = new("Association", "eaf9ed4e-0b61-4ac1-ba88-09f912c12087");
    internal static readonly SpecializationType IndexColumnType = new("Index Column", "c5ba925d-5c08-4809-a848-585a0cd4ddd3");
    internal static readonly SpecializationType StoredProcedureType = new("Stored Procedure", "575edd35-9438-406d-b0a7-b99d6f29b560");
    internal static readonly SpecializationType StoredProcedureParameterType = new("Stored Procedure Parameter", "5823b192-eb03-47c8-90d8-5501c922e9a5");
    internal static readonly SpecializationType OperationType = new("Operation", "e030c97a-e066-40a7-8188-808c275df3cb");
    internal static readonly SpecializationType ParameterType = new("Parameter", "00208d20-469d-41cb-8501-768fd5eb796b");
    internal static readonly SpecializationType EnumType = new("Enum", "85fba0e9-9161-4c85-a603-a229ef312beb");
    internal static readonly SpecializationType TriggerType = new("Trigger", "5b7b5e77-e627-464b-a157-6d01f2042641");

    private readonly ImportConfiguration _config;
    private readonly PackageModelPersistable _package;
    private readonly Database _db;
    private Dictionary<string, ConflictingTable> _conflictingTableNames = new();

    // keep track of the table and table column names actually added into the designer, so can track duplicates
    private static List<string> _addedTableNames = [];
    private static List<string> _addedColumnNames = [];
    private static List<string> _addedProcedureNames = [];
    private static List<string> _addedViewNames = [];

    #region ConflictingTable Class

    /// <summary>
    ///Represents Tables which cause "Class" name conflicts in the Model
    ///These are fine and expected
    ///e.g. [dbo].[Customer] && [account].[Customer]
    ///Here the different SQL Table names resolve to the same identifier
    ///e.g. [dbo].[tbl_Bank] && [dbo].[tbl___Bank]
    ///These will work but will break convention
    ///e.g. [dbo].[Bank] && [dbo].[Banks]
    /// </summary>
    private class ConflictingTable
    {
        public ConflictingTable(Table table, bool differentSchemas, bool uniqueIdentifier = true)
        {
            Table = table;
            DifferentSchemas = differentSchemas;
            UniqueIdentifier = uniqueIdentifier;
        }

        internal Table Table { get; }
        internal bool DifferentSchemas { get; set; }
        internal bool UniqueIdentifier { get; set; }
    }

    #endregion

    public DatabaseSchemaToModelMapper(ImportConfiguration config, PackageModelPersistable package, Database db)
    {
        _config = config;
        _package = package;
        _db = db;
    }

    public static PackageModelPersistable GetOrCreateDomainPackage(string fullPackagePath, string packageName)
    {
        PackageModelPersistable package;
        if (File.Exists(fullPackagePath))
        {
            package = PackageModelPersistable.Load(fullPackagePath);
        }
        else
        {
            package = PackageModelPersistable.CreateEmpty(DomainPackageType.Id, DomainPackageType.Name, Guid.NewGuid().ToString(), packageName);
            package.AbsolutePath = fullPackagePath;
        }

        return package;
    }

    public ElementPersistable? GetEnum(string id)
    {
        return _package.Classes.SingleOrDefault(x => x.Id == id && x.IsEnum());
    }

    public ElementPersistable? GetClass(Table table)
    {
        return GetClass(GetIdentity(table));
    }

    public ElementPersistable GetOrCreateClass(View view)
    {
        return GetOrCreateClass(GetIdentity(view), view.Schema);
    }

    public ElementPersistable GetOrCreateClass(Table table)
    {
        return GetOrCreateClass(GetIdentity(table), table.Schema);
    }

    public void UpdateStereoType(ElementPersistable element, string stereotypeDefinitionId, Dictionary<string, string?> properties)
    {
        var stereotype = element.Stereotypes.Single(x => x.DefinitionId == stereotypeDefinitionId);
        foreach (var property in properties)
        {
            if (property.Value is null)
            {
                stereotype.GetOrCreateProperty(property.Key, _ => { });
            }
            else
            {
                stereotype.GetOrCreateProperty(property.Key, p =>
                {
                    p.Name = property.Key;
                    p.Value = property.Value;
                });
            }
        }
    }

    public ElementPersistable GetOrCreateAttribute(Column column, ElementPersistable @class)
    {
        return InternalGetOrCreateAttribute(GetIdentity(column, @class), column.Nullable, @class);
    }

    internal ElementPersistable GetOrCreateAttribute(ResultSetColumn column, ElementPersistable dataContract)
    {
        return InternalGetOrCreateAttribute(GetIdentity(column, dataContract), column.IsNullable, dataContract);
    }
    
    private static ElementPersistable InternalGetOrCreateAttribute(ElementIdentity elementIdentity, bool isNullable, ElementPersistable @class)
    {
        var element = @class.ChildElements.SingleOrDefault(x => x.ExternalReference == elementIdentity.ExternalReference && x.IsAttribute());
        if (element is null)
        {
            element = @class.ChildElements.SingleOrDefault(x => x.Name == elementIdentity.Name && x.IsAttribute());
        }

        if (element is null)
        {
            @class.ChildElements.Add(element = new ElementPersistable
            {
                Id = Guid.NewGuid().ToString(),
                Name = elementIdentity.Name.ToSanitized()?.ToPascalCase(),
                SpecializationTypeId = AttributeType.Id,
                SpecializationType = AttributeType.Name,
                Stereotypes = [],
                TypeReference = new TypeReferencePersistable
                {
                    Id = Guid.NewGuid().ToString(),
                    IsNullable = isNullable,
                    IsCollection = false,
                    Stereotypes = [],
                    GenericTypeParameters = []
                },
                ExternalReference = elementIdentity.ExternalReference
            });
        }

        element.ExternalReference = elementIdentity.ExternalReference;
        return element;
    }

    public ElementPersistable GetOrCreateTrigger(Trigger trigger, ElementPersistable @class)
    {
        return InternalGetOrCreateTrigger(GetIdentity(trigger), @class);
    }

    private ElementPersistable InternalGetOrCreateTrigger(ElementIdentity elementIdentity, ElementPersistable @class)
    {
        var element = _package.ChildElements.SingleOrDefault(x => x.ExternalReference == elementIdentity.ExternalReference && x.IsTrigger());
        if (element is null)
        {
            element = _package.ChildElements.SingleOrDefault(x => x.Name == elementIdentity.Name && x.IsTrigger());
        }

        if (element is null)
        {
            @class.ChildElements.Add(element = new ElementPersistable
            {
                Id = Guid.NewGuid().ToString(),
                Name = elementIdentity.Name,
                SpecializationTypeId = TriggerType.Id,
                SpecializationType = TriggerType.Name,
                Stereotypes = [],
                ExternalReference = elementIdentity.ExternalReference
            });
        }

        element.ExternalReference = elementIdentity.ExternalReference;
        return element;
    }

    public ElementPersistable GetOrCreateIndex(Microsoft.SqlServer.Management.Smo.Index index, ElementPersistable @class)
    {
        var identity = GetIdentity(index);

        var element = @class.ChildElements.SingleOrDefault(x => x.ExternalReference == identity.ExternalReference && x.IsIndex()) 
                      ?? @class.ChildElements.SingleOrDefault(x => x.Name == identity.Name && x.IsIndex());

        if (element is null)
        {
            var newIndex = element = new ElementPersistable
            {
                Id = Guid.NewGuid().ToString(),
                Name = identity.Name,
                SpecializationTypeId = IndexType.Id,
                SpecializationType = IndexType.Name,
                Stereotypes = [],
                IsMapped = true,
                Mapping = new MappingModelPersistable
                {
                    ApplicationId = _config.ApplicationId,
                    MappingSettingsId = ColumnMappingSettingsId,
                    MetadataId = DomainMetadataId,
                    AutoSyncTypeReference = false,
                    Path =
                    [
                        new MappedPathTargetPersistable
                        {
                            Id = @class.Id, Name = @class.Name, Type = ElementType.Element, Specialization = @class.SpecializationType
                        }
                    ]
                },
                ParentFolderId = @class.Id,
                PackageId = @class.PackageId,
                PackageName = @class.PackageName,
                ExternalReference = identity.ExternalReference,
                ChildElements = new List<ElementPersistable>()
            };
            @class.ChildElements.Add(newIndex);
        }

        element.ExternalReference = identity.ExternalReference;
        element.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.Index.Settings.DefinitionId, (stereotype) =>
        {
            stereotype.AddedByDefault = true;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.UseDefaultName, p =>
            {
                p.Name = Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.UseDefaultNameName;
                p.Value = "false";
            });
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.Unique, p =>
            {
                p.Name = Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.UniqueName;
                p.Value = index.IsUnique.ToString().ToLower();
            });
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.Filter, p =>
            {
                p.Name = Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.FilterName;
                p.Value = "Default";
            });
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.FilterCustomValue, _ => { });
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.FillFactror, _ => { });
        });

        return element;
    }

    public AssociationPersistable? GetOrCreateAssociation(ForeignKey foreignKey)
    {
        var identity = GetIdentity(foreignKey);

        var table = foreignKey.Parent;
        var sourcePKs = GetPrimaryKeys(table);
        var @class = GetOrCreateClass(table);
        var targetTable = GetTable(foreignKey.Columns[0].Parent.ReferencedTableSchema, foreignKey.Columns[0].Parent.ReferencedTable);

        if (targetTable is null)
        {
            Logging.LogWarning($@"Foreign Key ""{foreignKey.Name}"" is referencing Table ""{foreignKey.Columns[0].Parent.ReferencedTable}"" but could not find it.");
            return null;
        }
        
        var sourceClassId = @class.Id;
        var targetClass = _package.Classes.SingleOrDefault(x => x.ExternalReference == GetClassExternal(targetTable) && x.IsClass());
        if (targetClass is null)
        {
            Logging.LogWarning($@"Foreign Key ""{foreignKey.Name}"" is referencing Table ""{targetTable.Name}"" but couldn't find the Class representing that Table.");
            return null;
        }

        string targetName;
        var singularTableName = targetTable.Name.Singularize(false);
        var sourceColumnsNullable = foreignKey.Columns.Cast<ForeignKeyColumn>().Select(x => GetColumn(table, x.Name)).ToArray();
        if (sourceColumnsNullable.Any(x => x is null))
        {
            Logging.LogWarning($@"Foreign Key ""{foreignKey.Name}"" trouble resolving columns for table ""{table.Name}""");
            return null;
        }

        var sourceColumns = sourceColumnsNullable.Cast<Column>().ToList();
        var firstColumn = sourceColumns[0];

        //Id = TablesNameId e.g. Bank has BankId
        if (firstColumn.Name.IndexOf(singularTableName, StringComparison.Ordinal) == 0)
        {
            targetName = singularTableName;
        }
        else
        {
            targetName = firstColumn.Name.IndexOf(targetTable.Name, StringComparison.Ordinal) switch
            {
                -1 => firstColumn.Name.Replace("ID", "", StringComparison.Ordinal) + targetTable.Name, //Ordinal Case
                0 => singularTableName,
                _ => firstColumn.Name.Substring(0, firstColumn.Name.IndexOf(targetTable.Name, StringComparison.Ordinal) + targetTable.Name.Length)
            };
        }

        var association = _package.Associations.FirstOrDefault(a => a.ExternalReference == identity.ExternalReference && a.IsAssociation());
        if (association is null)
        {
            var associations = _package.Associations.Where(a =>
                a.TargetEnd.TypeReference.TypeId == targetClass.Id &&
                a.SourceEnd.TypeReference.TypeId == sourceClassId &&
                a.IsAssociation()).ToList();
            if (associations.Any() && associations.Count == 1)
            {
                association = associations.First();
            }
            else
            {
                association = _package.Associations.FirstOrDefault(a =>
                    a.TargetEnd.TypeReference.TypeId == targetClass.Id &&
                    a.TargetEnd.Name == targetName &&
                    a.SourceEnd.TypeReference.TypeId == sourceClassId &&
                    a.IsAssociation());
            }
        }

        if (association is null)
        {
            var associationId = Guid.NewGuid().ToString();
            association = new AssociationPersistable
            {
                Id = associationId,
                ExternalReference = identity.ExternalReference,
                AssociationTypeId = AssociationType.Id,
                AssociationType = AssociationType.Name,
                TargetEnd = new AssociationEndPersistable
                {
                    //Keep this the same as association Id
                    Id = associationId,
                    Name = targetName.Equals(table.Name.Singularize(false), StringComparison.InvariantCultureIgnoreCase) ? $"{targetName}Reference" : targetName,
                    TypeReference = new TypeReferencePersistable
                    {
                        Id = Guid.NewGuid().ToString(),
                        TypeId = targetClass.Id,
                        IsNavigable = true,
                        IsNullable = sourceColumns.Any(x => x.Nullable),
                        IsCollection = false
                    },
                    Stereotypes = []
                },
                SourceEnd = new AssociationEndPersistable
                {
                    Id = Guid.NewGuid().ToString(),
                    TypeReference = new TypeReferencePersistable
                    {
                        Id = Guid.NewGuid().ToString(),
                        TypeId = sourceClassId,
                        IsNavigable = false,
                        IsNullable = false,
                        IsCollection = !(sourcePKs.Length == sourceColumns.Count && sourceColumns.All(x => sourcePKs.Any(pk => pk == x.Name)))
                    },
                    Stereotypes = []
                }
            };
            var manuallyRemodeled = false;
            if (!SameAssociationExistsWithReverseOwnership(_package.Associations, association, out var manuallyModelledAssociation))
            {
                _package.Associations.Add(association);
            }
            else
            {
                association = manuallyModelledAssociation!;
                manuallyRemodeled = true;
                Console.Write("Skipping - ");
            }
            
            Console.WriteLine($"{table.Name}: {sourceColumns[0].Name} " +
                              $"[{(association.SourceEnd.TypeReference.IsNullable ? "0" : "1")}..{(association.SourceEnd.TypeReference.IsCollection ? "*" : "1")}] " +
                              "--> " +
                              $"[{(association.TargetEnd.TypeReference.IsNullable ? "0" : "1")}..{(association.TargetEnd.TypeReference.IsCollection ? "*" : "1")}] " +
                              $"{targetTable.Name}: {GetColumnNames(foreignKey)}");

            foreach (var sourceColumn in sourceColumns)
            {
                var attributeExternalRef = GetAttributeExternal(sourceColumn);
                var attribute = @class.ChildElements
                    .FirstOrDefault(p => p.IsAttribute() &&
                                         p.ExternalReference == attributeExternalRef);

                if (attribute is null || manuallyRemodeled)
                {
                    continue;
                }

                if (attribute.Metadata.All(p => p.Key != "fk-original-name"))
                {
                    attribute.Metadata.Add(new GenericMetadataPersistable
                    {
                        Key = "fk-original-name",
                        Value = GetDefaultFkName(association, targetClass.Name, GetAttributeName(sourceColumn, @class))
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

                attribute.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.ForeignKey.DefinitionId, stereotype =>
                    {
                        stereotype.Name = Constants.Stereotypes.Rdbms.ForeignKey.Name;
                        stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
                        stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
                        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.ForeignKey.PropertyId.Association,
                            prop => prop.Name = Constants.Stereotypes.Rdbms.ForeignKey.PropertyId.AssociationName);
                    })
                    .GetOrCreateProperty(Constants.Stereotypes.Rdbms.ForeignKey.PropertyId.Association)
                    .Value = association.TargetEnd.Id;
            }
        }

        association.ExternalReference = identity.ExternalReference;
        return association;
    }

    private static string GetColumnNames(ForeignKey foreignKey)
    {
        var result = new StringBuilder();
        for (var i = 0; i < foreignKey.Columns.Count; i++)
        {
            if (i != 0)
            {
                result.Append(", ");
            }

            result.Append(foreignKey.Columns[i].Name);
        }

        return result.ToString();
    }

    public ElementPersistable GetOrCreateStoredProcedureElement(StoredProcedure storedProcedure, string? storedProcedureElementId)
    {
        return InternalGetOrCreateStoredProcedureElement(storedProcedure, storedProcedureElementId, StoredProcedureType);
    }

    public ElementPersistable GetOrCreateStoredProcedureOperation(StoredProcedure storedProcedure, string? storedProcedureElementId)
    {
        return InternalGetOrCreateStoredProcedureElement(storedProcedure, storedProcedureElementId, OperationType);
    }
    
    private ElementPersistable InternalGetOrCreateStoredProcedureElement(StoredProcedure storedProcedure, string? storedProcedureElementId, SpecializationType storedProcSpecializationType)
    {
        var folder = GetOrCreateFolder(storedProcedure.Schema);
        AddSchemaStereotype(folder, storedProcedure.Schema);

        var identity = GetIdentity(storedProcedure);

        var repositories = _package.Classes.Where(c => c.SpecializationTypeId == RepositoryType.Id).ToList();
        var element = repositories.SelectMany(r => r.ChildElements).SingleOrDefault(x => x.ExternalReference == identity.ExternalReference && x.SpecializationTypeId == storedProcSpecializationType.Id);
        if (element is null)
        {
            element = repositories.SelectMany(r => r.ChildElements).SingleOrDefault(x => x.Name == identity.Name && x.SpecializationTypeId == storedProcSpecializationType.Id);
        }

        if (element is null)
        {
            var repository = string.IsNullOrEmpty(storedProcedureElementId) 
                ? GetOrCreateRepositoryByName(folder, "StoredProcedureRepository")
                : GetRepositoryById(storedProcedureElementId);

            repository.ChildElements.Add(element = new ElementPersistable
            {
                Id = Guid.NewGuid().ToString(),
                Name = identity.Name,
                SpecializationTypeId = storedProcSpecializationType.Id,
                SpecializationType = storedProcSpecializationType.Name,
                Stereotypes = [],
                TypeReference = null,
                ExternalReference = identity.ExternalReference
            });
        }

        element.ExternalReference = identity.ExternalReference;
        return element;
    }

    public ElementPersistable GetOrCreateStoredProcedureElementParameter(StoredProcedureParameter parameter, ElementPersistable storedProcedure)
    {
        return InternalGetOrCreateStoredProcedureElementParameter(parameter, storedProcedure, StoredProcedureParameterType);
    }

    public ElementPersistable GetOrCreateStoredProcedureOperationParameter(StoredProcedureParameter parameter, ElementPersistable storedProcedure)
    {
        return InternalGetOrCreateStoredProcedureElementParameter(parameter, storedProcedure, ParameterType);
    }

    private ElementPersistable InternalGetOrCreateStoredProcedureElementParameter(StoredProcedureParameter parameter, ElementPersistable storedProcedure,
        SpecializationType storedProcedureParameterType)
    {
        var identity = GetIdentity(parameter);

        var element = storedProcedure.ChildElements
                          .FirstOrDefault(c => c.ExternalReference == identity.ExternalReference && c.SpecializationTypeId == storedProcedureParameterType.Id) ??
                      storedProcedure.ChildElements
                          .FirstOrDefault(c => c.Name == identity.Name && c.SpecializationTypeId == storedProcedureParameterType.Id);

        if (element is null)
        {
            storedProcedure.ChildElements.Add(element = new ElementPersistable
            {
                Id = Guid.NewGuid().ToString(),
                Name = identity.Name,
                SpecializationTypeId = storedProcedureParameterType.Id,
                SpecializationType = storedProcedureParameterType.Name,
                Stereotypes = [],
                TypeReference = new TypeReferencePersistable
                {
                    Id = Guid.NewGuid().ToString(),
                    IsNullable = false,
                    IsCollection = false,
                    Stereotypes = [],
                    GenericTypeParameters = []
                },
                ExternalReference = identity.ExternalReference
            });
        }

        element.ExternalReference = identity.ExternalReference;

        return element;
    }

    public void CreateIndexColumn(IndexedColumn indexColumn, ElementPersistable modelIndex, ElementPersistable? attribute)
    {
        MappingModelPersistable? mapping = null;
        if (attribute != null)
        {
            mapping = new MappingModelPersistable
            {
                MappingSettingsId = ColumnMappingSettingsId,
                MetadataId = DomainMetadataId,
                AutoSyncTypeReference = false,
                Path =
                [
                    new MappedPathTargetPersistable
                    {
                        Id = attribute.Id, Name = attribute.Name, Type = ElementType.Element, Specialization = attribute.SpecializationType
                    }
                ]
            };
        }

        var columnIndex = new ElementPersistable
        {
            Id = Guid.NewGuid().ToString(),
            Name = indexColumn.Name,
            SpecializationTypeId = IndexColumnType.Id,
            SpecializationType = IndexColumnType.Name,
            IsMapped = mapping != null,
            Mapping = mapping,
            ParentFolderId = modelIndex.Id,
            PackageId = _package.Id,
            PackageName = _package.Name
        };

        columnIndex.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.Index.IndexColumn.Settings.DefinitionId, (stereotype) =>
        {
            stereotype.AddedByDefault = true;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.IndexColumn.Settings.PropertyId.Type, p => p.Value = indexColumn.IsIncluded ? "Included" : "Key");
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.IndexColumn.Settings.PropertyId.SortDirection,
                p => p.Value = indexColumn.Descending ? "Descending" : "Ascending");
        });
        modelIndex.ChildElements.Add(columnIndex);
    }

    private string GetClassName(Table table)
    {
        return GetClassName(table.Name);
    }

    private ElementPersistable GetOrCreateFolder(string folderName)
    {
        var normalizedFolderName = NormalizeSchemaName(folderName);
        var element = _package.Classes.SingleOrDefault(x => x.Name == normalizedFolderName && x.IsFolder());
        if (element is null)
        {
            _package.AddElement(element = new ElementPersistable
            {
                Id = Guid.NewGuid().ToString(),
                Name = normalizedFolderName,
                ParentFolderId = _package.Id,
                SpecializationTypeId = FolderType.Id,
                SpecializationType = FolderType.Name
            });
        }

        if (!element.IsFolder())
        {
            throw new Exception($"Element with Name {normalizedFolderName} is not a {FolderType.Name}");
        }

        return element;
    }

    private ElementPersistable? GetClass(ElementIdentity identity)
    {
        var element = _package.Classes.SingleOrDefault(x => x.ExternalReference == identity.ExternalReference && x.IsClass());
        if (element is null && !_conflictingTableNames.ContainsKey(identity.ExternalReference))
        {
            element = _package.Classes.SingleOrDefault(x => x.Name == identity.Name && x.IsClass());
        }

        return element;
    }

    private ElementPersistable GetOrCreateClass(ElementIdentity identity, string schema)
    {
        var folder = GetOrCreateFolder(schema);
        AddSchemaStereotype(folder, schema);
        var element = GetClass(identity);
        if (element is null)
        {
            _package.AddElement(element = new ElementPersistable
            {
                Id = Guid.NewGuid().ToString(),
                ParentFolderId = folder.Id,
                Name = identity.Name,
                SpecializationTypeId = ClassType.Id,
                SpecializationType = ClassType.Name,
                ExternalReference = identity.ExternalReference
            });
        }

        element.ExternalReference = identity.ExternalReference;
        return element;
    }

    public ElementPersistable GetOrCreateDataContract(UserDefinedTableType userDefinedTableType)
    {
        var identity = GetIdentity(userDefinedTableType);
        var dataContract = _package.Classes.FirstOrDefault(x => x.ExternalReference == identity.ExternalReference);
        if (dataContract is null)
        {
            var dataContractName = $"{userDefinedTableType.Name.ToPascalCase()}";
            var folder = GetOrCreateFolder(userDefinedTableType.Schema);
            _package.AddElement(dataContract = new ElementPersistable
            {
                Id = Guid.NewGuid().ToString(),
                ParentFolderId = folder.Id,
                Name = dataContractName,
                SpecializationTypeId = DataContract.Id,
                SpecializationType = DataContract.Name,
                ExternalReference = identity.ExternalReference
            });
        }
        
        return dataContract;
    }

    internal ElementPersistable GetOrCreateDataContractResponse(StoredProcedure storedProcedure, ElementPersistable storedProcElement)
    {
        var identity = $"{storedProcElement.ExternalReference}.Response";
        var dataContract = _package.Classes.FirstOrDefault(x => x.ExternalReference == identity);
        if (dataContract is null)
        {
            var dataContractName = $"{storedProcElement.Name.ToPascalCase()}Response";
            var folder = GetOrCreateFolder(storedProcedure.Schema);
            _package.AddElement(dataContract = new ElementPersistable
            {
                Id = Guid.NewGuid().ToString(),
                ParentFolderId = folder.Id,
                Name = dataContractName,
                SpecializationTypeId = DataContract.Id,
                SpecializationType = DataContract.Name,
                ExternalReference = identity
            });
        }
        
        return dataContract;
    }

    private static void AddSchemaStereotype(ElementPersistable folder, string schemaName)
    {
        folder.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.Schema.DefinitionId, stereotype =>
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.Schema.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Schema.PropertyId.Name,
                prop =>
                {
                    prop.Name = Constants.Stereotypes.Rdbms.Schema.PropertyId.NameName;
                    prop.Value = schemaName;
                });
        });
    }

    /// <summary>
    /// This is to catch associations which have been manually fixed and not overwrite them
    /// Associations with reverse ownership might have been recreated and the external ref has been lost
    /// </summary>
    private static bool SameAssociationExistsWithReverseOwnership(
        IList<AssociationPersistable> associations, 
        AssociationPersistable association,
        out AssociationPersistable? equivalentAssociation)
    {
        equivalentAssociation = associations.FirstOrDefault(a =>
            a.SourceEnd.TypeReference.TypeId == association.TargetEnd.TypeReference.TypeId &&
            a.TargetEnd.TypeReference.TypeId == association.SourceEnd.TypeReference.TypeId &&
            a.SourceEnd.TypeReference.IsNullable == association.TargetEnd.TypeReference.IsNullable &&
            a.SourceEnd.TypeReference.IsCollection == association.TargetEnd.TypeReference.IsCollection &&
            a.TargetEnd.TypeReference.IsNullable == association.SourceEnd.TypeReference.IsNullable &&
            a.TargetEnd.TypeReference.IsCollection == association.SourceEnd.TypeReference.IsCollection
        );
        return equivalentAssociation != null;
    }

    private ElementIdentity GetIdentity(View view)
    {
        return new ElementIdentity(
            GetClassExternal(view.Schema, view.Name),
            DeDuplicateView(GetClassName(view.Name), view.Schema)
        );
    }

    private ElementIdentity GetIdentity(Table table)
    {
        string external = GetClassExternal(table.Schema, table.Name);
        if (_conflictingTableNames.TryGetValue(external, out var conflict))
        {
            if (!conflict.DifferentSchemas)
            {
                return new ElementIdentity(
                    external,
                    DeDuplicateTable(NormalizeTableName(table.Name), table.Schema)
                );
            }
        }

        return new ElementIdentity(
            external,
            DeDuplicateTable(GetClassName(table.Name), table.Schema)
        );
    }

    private ElementIdentity GetIdentity(StoredProcedureParameter parameter)
    {
        return new ElementIdentity(
            GetExternal(parameter),
            GetName(parameter)
        );
    }

    private static string GetExternal(StoredProcedureParameter parameter)
    {
        return parameter.Name.ToLower();
    }

    private static string GetName(StoredProcedureParameter parameter)
    {
        return DeDuplicateStoredProcedureParameter(NormalizeStoredProcParameterName(parameter.Name), parameter.Parent.Name, parameter.Parent.Schema);
    }

    private static ElementIdentity GetIdentity(Column column, ElementPersistable @class)
    {
        return new ElementIdentity(
            GetAttributeExternal(column),
            GetAttributeName(column, @class)
        );
    }
    
    private static ElementIdentity GetIdentity(ResultSetColumn column, ElementPersistable @class)
    {
        return new ElementIdentity(
            GetAttributeExternal(column, @class),
            GetAttributeName(column, @class)
        );
    }

    private static ElementIdentity GetIdentity(StoredProcedure storedProcedure)
    {
        return new ElementIdentity(
            GetStoredProcedureExternal(storedProcedure),
            GetStoredProcedureName(storedProcedure)
        );
    }

    private static ElementIdentity GetIdentity(Trigger trigger)
    {
        return new ElementIdentity(
            GetTriggerExternal(trigger),
            GetTriggerName(trigger)
        );
    }

    private static ElementIdentity GetIdentity(UserDefinedTableType userDefinedTableType)
    {
        return new ElementIdentity(
            $"{userDefinedTableType.Schema}.{userDefinedTableType.Name}",
            DeDuplicateTable(NormalizeTableName(userDefinedTableType.Name), userDefinedTableType.Schema));
    }

    private static AssociationIdentity GetIdentity(ForeignKey foreignKey)
    {
        return new AssociationIdentity(
            GetForeignKeyExternal(foreignKey)
        );
    }

    private static ElementIdentity GetIdentity(Microsoft.SqlServer.Management.Smo.Index index)
    {
        return new ElementIdentity(
            GetIndexExternal(index),
            GetIndexName(index)
        );
    }

    private static string GetStoredProcedureName(StoredProcedure storedProcedure, bool isResponse = false)
    {
        return DeDuplicateStoredProcedure(NormalizeStoredProcName(storedProcedure.Name), storedProcedure.Schema);
    }

    private static string GetIndexName(Microsoft.SqlServer.Management.Smo.Index index)
    {
        return index.Name;
    }

    private static string GetStoredProcedureExternal(StoredProcedure storedProcedure)
    {
        return $"[{storedProcedure.Schema}].[{storedProcedure.Name}]".ToLower();
    }

    private static string GetTriggerExternal(Trigger trigger)
    {
        return trigger.Parent switch
        {
            Table table => $"trigger:[{table.Schema}].[{table.Name}].[{trigger.Name}]".ToLower(),
            View view => $"trigger:[{view.Schema}].[{view.Name}].[{trigger.Name}]".ToLower(),
            _ => throw new Exception($"Unknown parent type : {trigger.Parent}")
        };
    }

    private static string GetTriggerName(Trigger trigger)
    {
        return trigger.Name;
    }

    private static string GetForeignKeyExternal(ForeignKey foreignKey)
    {
        return $"[{foreignKey.Parent.Schema}].[{foreignKey.Parent.Name}].[{foreignKey.Name}]".ToLower();
    }

    private static string GetIndexExternal(Microsoft.SqlServer.Management.Smo.Index index)
    {
        return index.Parent switch
        {
            Table table => $"index:[{table.Schema}].[{table.Name}].[{index.Name}]".ToLower(),
            View view => $"index:[{view.Schema}].[{view.Name}].[{index.Name}]".ToLower(),
            _ => throw new Exception($"Unknown parent type : {index.Parent}")
        };
    }

    private string GetClassName(string name)
    {
        var convention = _config.EntityNameConvention switch
        {
            EntityNameConvention.SingularEntity => name.Singularize(false),
            EntityNameConvention.MatchTable => name,
            _ => name
        };
        return NormalizeTableName(convention);
    }

    private string GetClassExternal(Table table)
    {
        return GetClassExternal(table.Schema, table.Name);
    }

    private static string GetClassExternal(string schema, string name)
    {
        return $"[{schema}].[{name}]".ToLower();
    }

    private static string GetAttributeName(Column column, ElementPersistable @class)
    {
        return column.Parent switch
        {
            Table table => DeDuplicateColumn(NormalizeColumnName(column.Name, table.Name), @class.Name, GetSchema(column)),
            View view => DeDuplicateColumn(NormalizeColumnName(column.Name, view.Name), @class.Name, GetSchema(column)),
            UserDefinedTableType table => DeDuplicateColumn(NormalizeColumnName(column.Name, table.Name), @class.Name, GetSchema(column)),
            _ => throw new Exception($"Unknown parent type : {column.Parent}")
        };
    }
    
    private static string GetAttributeName(ResultSetColumn column, ElementPersistable @class)
    {
        return DeDuplicateColumn(NormalizeColumnName(column.Name, null), @class.Name, "");
    }

    private static string GetAttributeExternal(Column column)
    {
        return column.Parent switch
        {
            Table table => $"[{table.Schema}].[{table.Name}].[{column.Name}]".ToLower(),
            View view => $"[{view.Schema}].[{view.Name}].[{column.Name}]".ToLower(),
            UserDefinedTableType table => $"[{table.Schema}].[{table.Name}].[{column.Name}".ToLower(),
            _ => throw new Exception($"Unknown parent type : {column.Parent}")
        };
    }

    private static string GetSchema(Column column)
    {
        return column.Parent switch
        {
            Table table => table.Schema.ToLower(),
            View view => view.Schema.ToLower(),
            UserDefinedTableType table => table.Schema.ToLower(),
            _ => throw new Exception($"Unknown parent type : {column.Parent}")
        };
    }

    private static string GetAttributeExternal(ResultSetColumn column, ElementPersistable @class)
    {
        return $"[{@class.ExternalReference}].[{column.Name}]".ToLower();
    }

    private static string NormalizeColumnName(string colName, string? tableOrViewName)
    {
        var normalized = colName != tableOrViewName ? colName : colName + "Value";
        normalized = ToCSharpIdentifier(normalized, "db");

        normalized = normalized.RemovePrefix("col").RemovePrefix("pk");

        normalized = normalized[..1].ToUpper() + normalized[1..];

        if (normalized.EndsWith("ID"))
        {
            normalized = normalized.RemoveSuffix("ID");
            normalized += "Id";
        }

        return normalized;
    }

    private static string DeDuplicateTable(string className, string schema)
    {
        var counter = 0;
        var addedReference = $"[{schema}].[{className}]";
        bool duplicateFound = false;

        // keep track of the columns added
        while (_addedTableNames.Any(x => x == addedReference))
        {
            duplicateFound = true;
            counter++;

            addedReference = $"[{schema}].[{className}{counter}]";
        }

        if (duplicateFound)
        {
            className = $"{className}{counter}";
        }

        _addedTableNames.Add(addedReference);

        return className;
    }

    private static string DeDuplicateView(string className, string schema)
    {
        var counter = 0;
        var addedReference = $"[{schema}].[{className}]";
        bool duplicateFound = false;

        // keep track of the columns added
        while (_addedViewNames.Any(x => x == addedReference))
        {
            duplicateFound = true;
            counter++;

            addedReference = $"[{schema}].[{className}{counter}]";
        }

        if (duplicateFound)
        {
            className = $"{className}{counter}";
        }

        _addedViewNames.Add(addedReference);

        return className;
    }

    private static string DeDuplicateColumn(string propertyName, string className, string schema)
    {
        if (propertyName == className)
        {
            propertyName = propertyName + "Property";
        }

        var counter = 0;
        var addedReference = $"[{schema}].[{className}].[{propertyName}]";
        bool duplicateFound = false;

        // keep track of the columns added
        while (_addedColumnNames.Any(x => x == addedReference))
        {
            duplicateFound = true;
            counter++;

            addedReference = $"[{schema}].[{className}].[{propertyName}{counter}]";
        }

        if(duplicateFound)
        {
            propertyName = $"{propertyName}{counter}";
        }

        _addedColumnNames.Add(addedReference);

        return propertyName;
    }

    private static string DeDuplicateStoredProcedure(string procedureName, string schema)
    {
        var counter = 0;
        var addedReference = $"[{schema}].[{procedureName}]";
        bool duplicateFound = false;

        // keep track of the columns added
        while (_addedProcedureNames.Any(x => x == addedReference))
        {
            duplicateFound = true;
            counter++;

            addedReference = $"[{schema}].[{procedureName}{counter}]";
        }

        if (duplicateFound)
        {
            procedureName = $"{procedureName}{counter}";
        }

        _addedProcedureNames.Add(addedReference);

        return procedureName;
    }

    private static string DeDuplicateStoredProcedureParameter(string parameterName, string procedureName, string schema)
    {
        var counter = 0;
        var addedReference = $"[{schema}].[{procedureName}].[{parameterName}]";
        bool duplicateFound = false;

        // keep track of the columns added
        while (_addedProcedureNames.Any(x => x == addedReference))
        {
            duplicateFound = true;
            counter++;

            addedReference = $"[{schema}].[{procedureName}].[{parameterName}{counter}]]";
        }

        if (duplicateFound)
        {
            parameterName = $"{parameterName}{counter}";
        }

        _addedProcedureNames.Add(addedReference);

        return parameterName;
    }

    private static string RemoveInvalidCSharpCharacter(string value)
    {
        return value
                .Replace(" ", "")
                .Replace("@", "")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("#", "Hash")
                .Replace("%", "Percent")
                .Replace("$", "Dollar")
                .Replace("?", "Question")
                .Replace("!", "Exclamation")
                .Replace(".", "")
                .Replace("\"", "")
                .Replace("&", "And")
                .Replace(",", "")
                .Replace("\\", "")
                .Replace("/", "")
                .Replace("_", "")
                .Replace("-", "")
                .Replace("|", "")
            ;
    }

    // this list and the ToCSharpIdentifier method is comp[ied from Intent.Modules.Common.CSharp module
    private static readonly HashSet<string> ReservedWords = [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate","do",
        "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", 
        "interface","internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "using static", "virtual", "void", "volatile", "while" 
    ];

    public static string ToCSharpIdentifier(string identifier, string prefixValue = "Db")
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return string.Empty;
        }
        
    // https://docs.microsoft.com/dotnet/csharp/fundamentals/coding-style/identifier-names
    // - Identifiers must start with a letter, or _.
    // - Identifiers may contain Unicode letter characters, decimal digit characters,
    //   Unicode connecting characters, Unicode combining characters, or Unicode formatting
    //   characters. For more information on Unicode categories, see the Unicode Category
    //   Database. You can declare identifiers that match C# keywords by using the @ prefix
    //   on the identifier. The @ is not part of the identifier name. For example, @if
    //   declares an identifier named if. These verbatim identifiers are primarily for
    //   interoperability with identifiers declared in other languages.

    identifier = identifier
            .Replace("#", "Sharp")
            .Replace("&", "And");

        var asCharArray = identifier.ToCharArray();
        for (var i = 0; i < asCharArray.Length; i++)
        {
            // Underscore character is not allowed in this case
            if (asCharArray[i] == '_')
            {
                asCharArray[i] = ' ';
                continue;
            }

            switch (char.GetUnicodeCategory(asCharArray[i]))
            {
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.LetterNumber:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.Format:
                    break;
                case UnicodeCategory.ClosePunctuation:
                case UnicodeCategory.ConnectorPunctuation:
                case UnicodeCategory.Control:
                case UnicodeCategory.CurrencySymbol:
                case UnicodeCategory.DashPunctuation:
                case UnicodeCategory.EnclosingMark:
                case UnicodeCategory.FinalQuotePunctuation:
                case UnicodeCategory.InitialQuotePunctuation:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.MathSymbol:
                case UnicodeCategory.ModifierSymbol:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.OpenPunctuation:
                case UnicodeCategory.OtherNotAssigned:
                case UnicodeCategory.OtherNumber:
                case UnicodeCategory.OtherPunctuation:
                case UnicodeCategory.OtherSymbol:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.SpaceSeparator:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.Surrogate:
                    asCharArray[i] = ' ';
                    break;
                default:
                    asCharArray[i] = ' ';
                    break;
            }
        }

        identifier = new string(asCharArray);

        // Replace double spaces
        while (identifier.Contains("  "))
        {
            identifier = identifier.Replace("  ", " ");
        }

        identifier = string.Concat(identifier
            .Split(' ')
            .Where(element => !string.IsNullOrWhiteSpace(element))
            .Select((element, index) => index == 0
                ? element
                : element.ToPascalCase()));

        if (char.IsNumber(identifier[0]))
        {
            identifier = $"{prefixValue}{identifier}";
        }

        if (ReservedWords.Contains(identifier))
        {
            identifier = $"{prefixValue}{identifier}";
        }

        return identifier;
    }

    private static string NormalizeTableName(string tableName)
    {
        var normalized = tableName.RemovePrefix("tbl");
        normalized = ToCSharpIdentifier(normalized, "Db");
        normalized = normalized[..1].ToUpper() + normalized[1..];

        return normalized;
    }

    private static string NormalizeStoredProcName(string storeProcName)
    {
        var normalized = ToCSharpIdentifier(storeProcName);
        normalized = normalized.RemovePrefix("prc")
            .RemovePrefix("Prc")
            .RemovePrefix("proc");

        normalized = normalized[..1].ToUpper() + normalized[1..];
        return normalized;
    }

    private static string NormalizeSchemaName(string schemaName)
    {
        var normalized = schemaName;
        normalized = normalized[..1].ToUpper() + normalized[1..];
        return normalized;
    }

    private static Column? GetColumn(Table table, string columnName)
    {
        return table.Columns.Cast<Column>().FirstOrDefault(column => column.Name == columnName);
    }

    private Table? GetTable(string schema, string tableName)
    {
        return _db.Tables.Cast<Table>().FirstOrDefault(table => table.Schema == schema && table.Name == tableName);
    }

    private static string[] GetPrimaryKeys(Table table)
    {
        foreach (Microsoft.SqlServer.Management.Smo.Index tableIndex in table.Indexes)
        {
            if (tableIndex.IndexKeyType == IndexKeyType.DriPrimaryKey)
            {
                return tableIndex.IndexedColumns.Cast<IndexedColumn>().Select(x => x.Name).ToArray();
            }
        }

        return Array.Empty<string>();
    }

    private static string NormalizeStoredProcParameterName(string storeProcName)
    {
        var normalized = ToCSharpIdentifier(storeProcName, "db");
        normalized = normalized.RemovePrefix("prc")
            .RemovePrefix("Prc")
            .RemovePrefix("proc");
        return normalized;
    }

    private static string GetDefaultFkName(AssociationPersistable association, string targetClassName, string pkAttributeName)
    {
        return (association.TargetEnd.Name ?? targetClassName) + pkAttributeName;
    }
    
    private ElementPersistable GetRepositoryById(string repositoryElementId)
    {
        var element = _package.Classes.SingleOrDefault(x => x.Id == repositoryElementId && x.IsRepository());
        if (element == null)
        {
            throw new Exception($"Unable to resolve repository element id {repositoryElementId}");
        }
        return element;
    }
    
    private ElementPersistable GetOrCreateRepositoryByName(ElementPersistable folder, string repositoryName)
    {
        var element = _package.Classes.SingleOrDefault(x => x.Name == repositoryName && x.IsRepository());
        if (element == null)
        {
            element = new ElementPersistable
            {
                Id = Guid.NewGuid().ToString(),
                ParentFolderId = folder.Id,
                Name = repositoryName,
                SpecializationTypeId = RepositoryType.Id,
                SpecializationType = RepositoryType.Name
            };
            _package.AddElement(element);
        }

        return element;
    }
}