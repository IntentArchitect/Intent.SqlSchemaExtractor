using Intent.IArchitect.Agent.Persistence;
using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Intent.Metadata.Models;
using Intent.Modules.Common.Templates;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.XEvent;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace Intent.SQLSchemaExtractor
{
	public class ModelSchemaHelper
	{
		private const string DomainMetadataId = "6ab29b31-27af-4f56-a67c-986d82097d63";
		private const string ColumnMappingSettingsId = "30f4278f-1d74-4e7e-bfdb-39c8e120f24c";

		internal static readonly SpecializationType DomainPackageType = new("Domain Package", "1a824508-4623-45d9-accc-f572091ade5a");
		internal static readonly SpecializationType RepositoryType = new("Repository", "96ffceb2-a70a-4b69-869b-0df436c470c3");
		internal static readonly SpecializationType FolderType = new("Folder", "4d95d53a-8855-4f35-aa82-e312643f5c5f");
		internal static readonly SpecializationType ClassType = new("Class", "04e12b51-ed12-42a3-9667-a6aa81bb6d10");
		internal static readonly SpecializationType AttributeType = new("Attribute", "0090fb93-483e-41af-a11d-5ad2dc796adf");
		internal static readonly SpecializationType IndexType = new("Index", "436e3afe-b4ef-481c-b803-0d1e7d263561");
		internal static readonly SpecializationType StoredProcedureType = new("Stored Procedure", "575edd35-9438-406d-b0a7-b99d6f29b560");
		internal static readonly SpecializationType AssociationType = new("Association", "eaf9ed4e-0b61-4ac1-ba88-09f912c12087");
		private static readonly SpecializationType IndexColumnType = new("Index Column", "c5ba925d-5c08-4809-a848-585a0cd4ddd3");
		internal static readonly SpecializationType StoredProcedureParameterType = new("Stored Procedure Parameter", "5823b192-eb03-47c8-90d8-5501c922e9a5");

		private readonly ImportConfiguration _config;
		private readonly PackageModelPersistable _package;
		private readonly Database _db;
		private Dictionary<string, ConflictingTable> _conflictingTableNames = new Dictionary<string, ConflictingTable>();

		private record ConflictingTable (Table Table, bool DifferentSchemas);

		public ModelSchemaHelper(ImportConfiguration config, PackageModelPersistable package, Database db)
        {			
			_config = config;
			_package = package;
			_db = db;
			var unqiueNames = new Dictionary<string, List<Table>>();
			foreach (Table table in _db.Tables)
			{
				string className = GetClassName(table);
				if (unqiueNames.ContainsKey(className))
				{
					var otherTables = unqiueNames[className];
					otherTables.Add(table);
				}
				else
				{
					unqiueNames.Add(className,new List<Table> { table });
				}
			}

			var conflicts = unqiueNames.Values.Where(v => v.Count > 1).ToList();
			foreach (var conflict in conflicts)
			{
				//Conflicting names are in different schemas
				if (conflict.Select(t => t.Schema).Distinct().Count() == conflict.Count)
				{
					foreach (var table in conflict)
					{
						_conflictingTableNames.Add(GetClassExternal(table), new ConflictingTable(table, true));
					}
				}
				else
				{
					var className = GetClassName(conflict[0]);
					Logging.LogWarning($"conflicting table names ({className}) from {string.Join(",", conflict.Select(t => $"[{t.Schema}].[{t.Name}]"))}");

					var usableNames = conflict.Select(t => NormalizeTableName(t.Name)).Distinct().Count() == conflict.Count;
					if (!usableNames)
					{
						throw new Exception($"Unable to uniquely resolve Entity names for {string.Join(",", conflict.Select(t => $"[{t.Schema}].[{t.Name}]"))}");
					}

					foreach (var table in conflict)
					{
						_conflictingTableNames.Add(GetClassExternal(table), new ConflictingTable(table, false));
					}
				}
			}

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

		public void UpdateStereoType(ElementPersistable element, string stereotypeDefinitionId, Dictionary<string, string> properties)
		{
			var stereotype = element.Stereotypes.Single(x => x.DefinitionId == stereotypeDefinitionId);
			foreach (var property in properties)
			{
				if (property.Value is null)
				{
					stereotype.GetOrCreateProperty(property.Key, p => { });
				}
				else
				{
					stereotype.GetOrCreateProperty(property.Key, p => { p.Name = property.Key; p.Value = property.Value; });
				}
			}
		}

		public ElementPersistable GetOrCreateAttribute(Column column, ElementPersistable @class)
		{
			var identity = GetIdentity(column, @class);

			var element = @class.ChildElements.SingleOrDefault(x => x.ExternalReference == identity.ExternalReference && x.IsAttribute());
			if (element is null)
			{
				element = @class.ChildElements.SingleOrDefault(x => x.Name == identity.Name && x.IsAttribute());
			}
			if (element is null)
			{
				@class.ChildElements.Add(element = new ElementPersistable
				{
					Id = Guid.NewGuid().ToString(),
					Name = identity.Name,
					SpecializationTypeId = AttributeType.Id,
					SpecializationType = AttributeType.Name,
					Stereotypes = new List<StereotypePersistable>(),
					TypeReference = new TypeReferencePersistable()
					{
						Id = Guid.NewGuid().ToString(),
						IsNullable = column.Nullable,
						IsCollection = false,
						Stereotypes = new List<StereotypePersistable>(),
						GenericTypeParameters = new List<TypeReferencePersistable>()
					},
					ExternalReference = identity.ExternalReference
				});
			}
			element.ExternalReference = identity.ExternalReference;
			return element;
		}

		public ElementPersistable GetOrCreateIndex(Microsoft.SqlServer.Management.Smo.Index index, ElementPersistable @class)
		{
			var identity = GetIdentity(index);

			var element = @class.ChildElements.SingleOrDefault(x => x.ExternalReference == identity.ExternalReference && x.IsIndex());
			if (element is null)
			{
				element = @class.ChildElements.SingleOrDefault(x => x.Name == identity.Name && x.IsIndex());
			}
			if (element is null)
			{
				var newIndex = element = new ElementPersistable
				{
					Id = Guid.NewGuid().ToString(),
					Name = identity.Name,
					SpecializationTypeId = IndexType.Id,
					SpecializationType = IndexType.Name,
					Stereotypes = new List<StereotypePersistable>(),
					IsMapped = true,
					Mapping = new MappingModelPersistable
					{
						ApplicationId = _config.ApplicationId,
						MappingSettingsId = ColumnMappingSettingsId,
						MetadataId = DomainMetadataId,
						AutoSyncTypeReference = false,
						Path = new List<MappedPathTargetPersistable> { new MappedPathTargetPersistable() { Id = @class.Id, Name = @class.Name, Type = ElementType.Element, Specialization = @class.SpecializationType } }
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
				stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.UseDefaultName, p => { p.Name = Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.UseDefaultNameName; p.Value = "false"; });
				stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.Unique, p => { p.Name = Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.UniqueName; p.Value = index.IsUnique.ToString().ToLower(); });
				stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.Filter, p => { p.Name = Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.FilterName; p.Value = "Default"; });
				stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.FilterCustomValue, p => { });
				stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.Settings.PropertyId.FillFactror, p => { });
			});

			return element;
		}

		public AssociationPersistable GetOrCreateAssociation(ForeignKey foreignKey)
		{
			var identity = GetIdentity(foreignKey);

			var table = foreignKey.Parent;
			var sourcePKs = GetPrimaryKeys(table);
			var @class = GetOrCreateClass(table);
			var sourceColumns = foreignKey.Columns.Cast<ForeignKeyColumn>().Select(x => GetColumn(table, x.Name)).ToList();
			var targetTable = GetTable(foreignKey.Columns[0].Parent.ReferencedTableSchema, foreignKey.Columns[0].Parent.ReferencedTable);
			var targetColumn = foreignKey.Columns[0].Name;

			var sourceClassId = @class.Id;
			var targetClass = _package.Classes.Single(x => x.ExternalReference == GetClassExternal(targetTable) && x.IsClass());
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


			var association = _package.Associations.FirstOrDefault(a => a.ExternalReference == identity.ExternalReference && a.IsAssociation());
			if (association is null)
			{
				var associations = _package.Associations.Where(a => 
					a.TargetEnd.TypeReference.TypeId == targetClass.Id &&
					a.SourceEnd.TypeReference.TypeId == sourceClassId &&
					a.IsAssociation()).ToList();
				if (associations.Any() && associations.Count() == 1)
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
				association = new AssociationPersistable()
				{
					Id = associationId,
					ExternalReference = identity.ExternalReference,
					AssociationTypeId = AssociationType.Id,
					AssociationType = AssociationType.Name,
					TargetEnd = new AssociationEndPersistable()
					{
						//Keep this the same as association Id
						Id = associationId,
						Name = targetName,
						TypeReference = new TypeReferencePersistable()
						{
							Id = Guid.NewGuid().ToString(),
							TypeId = targetClass.Id,
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
				bool manuallyRemodeled = false;
				if (!SameAssociationExistsWithReverseOwnership(_package.Associations, association, out var manuallyModelledAssociation))
				{
					_package.Associations.Add(association);
				}
				else
				{
					association = manuallyModelledAssociation;
					manuallyRemodeled = true;
					Console.Write($"Skipping - ");
				}
				Console.WriteLine($"{table.Name}: {sourceColumns[0].Name} " +
								  $"[{(association.SourceEnd.TypeReference.IsNullable ? "0" : "1")}..{(association.SourceEnd.TypeReference.IsCollection ? "*" : "1")}] " +
								  "--> " +
								  $"[{(association.TargetEnd.TypeReference.IsNullable ? "0" : "1")}..{(association.TargetEnd.TypeReference.IsCollection ? "*" : "1")}] " +
								  $"{targetTable.Name}: {targetColumn}");

				var attributeExternalRef = GetAttributeExternal(sourceColumns[0]);
				var attribute = @class.ChildElements
					.FirstOrDefault(p => p.IsAttribute() &&
										 p.ExternalReference == attributeExternalRef);

				if (attribute is not null && !manuallyRemodeled)
				{
					if (attribute.Metadata.All(p => p.Key != "fk-original-name"))
					{
						attribute.Metadata.Add(new GenericMetadataPersistable
						{
							Key = "fk-original-name",
							Value = GetDefaultFKName(association, targetClass.Name, GetAttributeName(sourceColumns[0], @class))
						}); ;
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
			association.ExternalReference = identity.ExternalReference;
			return association;
		}
		private string GetDefaultFKName(AssociationPersistable association, string targetClassName, string pkAttributeName)
		{
			return (association.TargetEnd.Name ?? targetClassName) + pkAttributeName;
		}

		public ElementPersistable GetOrCreateStoredProcedure(StoredProcedure storedProcedure)
		{
			var folder = GetOrCreateFolder(storedProcedure.Schema);
			AddSchemaStereotype(folder, storedProcedure.Schema);

			var identity = GetIdentity(storedProcedure);

			var repositories = _package.Classes.Where(c => c.SpecializationTypeId == RepositoryType.Id).ToList();
			var element = repositories.SelectMany(r => r.ChildElements).SingleOrDefault(x => x.ExternalReference == identity.ExternalReference && x.IsStoredProcedure());
			if (element is null)
			{
				element = repositories.SelectMany(r => r.ChildElements).SingleOrDefault(x => x.Name == identity.Name && x.IsStoredProcedure());
			}
			if (element is null)
			{
				var repository = GetOrCreateRepository(folder, "StoredProcedureRepository");

				repository.ChildElements.Add(element = new ElementPersistable
				{
					Id = Guid.NewGuid().ToString(),
					Name = NormalizeStoredProcName(storedProcedure.Name),
					SpecializationTypeId = StoredProcedureType.Id,
					SpecializationType = StoredProcedureType.Name,
					Stereotypes = new List<StereotypePersistable>(),
					TypeReference = null,
					ExternalReference = identity.Name
				});
			}
			element.ExternalReference = identity.ExternalReference;
			return element;
		}

		private ElementPersistable GetOrCreateRepository(ElementPersistable folder, string repositoryName)
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
					SpecializationType = RepositoryType.Name,
				};
				_package.AddElement(element);
			}
			return element;
		}

		public ElementPersistable GetOrCreateStoredProcedureParameter(StoredProcedureParameter parameter, ElementPersistable storedProcedure )
		{
			var identity = GetIdentity(parameter);

			var element = storedProcedure.ChildElements.FirstOrDefault(c => c.ExternalReference == identity.ExternalReference && c.SpecializationTypeId == StoredProcedureParameterType.Id);
			if (element is null)
			{
				element = storedProcedure.ChildElements.FirstOrDefault(c => c.Name == identity.Name && c.SpecializationTypeId == StoredProcedureParameterType.Id);
			}
			if (element is null)
			{
				storedProcedure.ChildElements.Add(element = new ElementPersistable
				{
					Id = Guid.NewGuid().ToString(),
					Name = identity.Name,
					SpecializationTypeId = StoredProcedureParameterType.Id,
					SpecializationType = StoredProcedureParameterType.Name,
					Stereotypes = new List<StereotypePersistable>(),
					TypeReference = new TypeReferencePersistable
					{
						Id = Guid.NewGuid().ToString(),
						IsNullable = false,
						IsCollection = false,
						Stereotypes = new List<StereotypePersistable>(),
						GenericTypeParameters = new List<TypeReferencePersistable>()
					},
					ExternalReference = identity.ExternalReference
				});
			}
			element.ExternalReference = identity.ExternalReference;

			return element;
		}

		private  ElementPersistable GetOrCreateFolder(string folderName)
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

		internal void CreateIndexColumn(IndexedColumn indexColumn, ElementPersistable modelIndex, ElementPersistable? attribute)
		{
			MappingModelPersistable mapping = null;
			if (attribute != null)
			{
				mapping = new MappingModelPersistable
				{
					MappingSettingsId = ColumnMappingSettingsId,
					MetadataId = DomainMetadataId,
					AutoSyncTypeReference = false,
					Path = new List<MappedPathTargetPersistable> { new MappedPathTargetPersistable() { Id = attribute.Id, Name = attribute.Name, Type = ElementType.Element, Specialization = attribute.SpecializationType } }
				};

			}
			var columnIndex = new ElementPersistable()
			{
				Id = Guid.NewGuid().ToString(),
				Name = indexColumn.Name,
				SpecializationTypeId = IndexColumnType.Id,
				SpecializationType = IndexColumnType.Name,
				IsMapped = mapping != null,
				Mapping = mapping,
				ParentFolderId = modelIndex.Id,
				PackageId = _package.Id,
				PackageName = _package.Name,
			};

			columnIndex.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.Index.IndexColumn.Settings.DefinitionId, (stereotype) =>
			{
				stereotype.AddedByDefault = true;
				stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
				stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
				stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.IndexColumn.Settings.PropertyId.Type, p => p.Value = indexColumn.IsIncluded ? "Included" : "Key");
				stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.IndexColumn.Settings.PropertyId.SortDirection, p => p.Value = indexColumn.Descending ? "Descending" : "Ascending");
			});
			modelIndex.ChildElements.Add(columnIndex);
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

		/// <summary>
		/// This is to catch associations which have been manually fixed and not overwrite them
		/// Associations with reverse ownership might have been recreated and the external ref has been lost
		/// </summary>
		private bool SameAssociationExistsWithReverseOwnership(IList<AssociationPersistable> associations, AssociationPersistable association, out AssociationPersistable? equivelantAssociation)
		{
			equivelantAssociation = associations.FirstOrDefault(a =>
				a.SourceEnd.TypeReference.TypeId == association.TargetEnd.TypeReference.TypeId &&
				a.TargetEnd.TypeReference.TypeId == association.SourceEnd.TypeReference.TypeId &&
				a.SourceEnd.TypeReference.IsNullable == association.TargetEnd.TypeReference.IsNullable &&
				a.SourceEnd.TypeReference.IsCollection == association.TargetEnd.TypeReference.IsCollection &&
				a.TargetEnd.TypeReference.IsNullable == association.SourceEnd.TypeReference.IsNullable &&
				a.TargetEnd.TypeReference.IsCollection == association.SourceEnd.TypeReference.IsCollection
				) ;
			return equivelantAssociation != null;
		}

		private ElementIdentity GetIdentity(View view)
		{
			return new ElementIdentity(
				GetClassExternal(view.Schema, view.Name),
				GetClassName(view.Name)
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
						NormalizeTableName(table.Name)
						);
				}
			}
			return new ElementIdentity(
				external,
				GetClassName(table.Name)
				);
		}

		private ElementIdentity GetIdentity(StoredProcedureParameter parameter)
		{
			return new ElementIdentity(
				GetExternal(parameter),
				GetName(parameter)
				);
		}

		private string GetExternal(StoredProcedureParameter parameter)
		{
			return parameter.Name.ToLower();
		}

		private string GetName(StoredProcedureParameter parameter)
		{
			return NormalizeStoredProcParameterName(parameter.Name);
		}

		private ElementIdentity GetIdentity(Column column, ElementPersistable @class)
		{
			return new ElementIdentity( 
				GetAttributeExternal(column),
				GetAttributeName(column, @class)
				);
		}

		private ElementIdentity GetIdentity(StoredProcedure storedProcedure)
		{
			return new ElementIdentity(
				GetStoredProcedureExternal(storedProcedure),
				GetStoredProcedureName(storedProcedure)
				);
		}

		private AssociationIdentity GetIdentity(ForeignKey foreignKey)
		{
			return new AssociationIdentity(
				GetForeignKeyExternal(foreignKey)
				);
		}

		private ElementIdentity GetIdentity(Microsoft.SqlServer.Management.Smo.Index index)
		{
			return new ElementIdentity(
				GetIndexExternal(index),
				GetIndexName(index)
				);
		}

		private string GetStoredProcedureName(StoredProcedure storedProcedure)
		{
			return NormalizeStoredProcName(storedProcedure.Name);
		}

		private string GetIndexName(Microsoft.SqlServer.Management.Smo.Index index)
		{
			return index.Name;
		}

		private string GetStoredProcedureExternal (StoredProcedure storedProcedure)
		{
			return $"[{storedProcedure.Schema}].[{storedProcedure.Name}]".ToLower();
		}

		private string GetForeignKeyExternal(ForeignKey foreignKey)
		{
			return $"[{foreignKey.Parent.Schema}].[{foreignKey.Parent.Name}].[{foreignKey.Name}]".ToLower();
		}

		private string GetIndexExternal(Microsoft.SqlServer.Management.Smo.Index index)
		{
			if (index.Parent is Table table)
			{
				return $"index:[{table.Schema}].[{table.Name}].[{index.Name}]".ToLower();
			}
			else if (index.Parent is View view)
			{
				return $"index:[{view.Schema}].[{view.Name}].[{index.Name}]".ToLower();
			}
			throw new Exception($"Unknown parent type : {index.Parent.ToString()}");
		}

		public string GetClassName(Table table)
		{
			return GetClassName(table.Name);
		}

		private string GetClassName(string name)
		{
			var convention = _config.EntityNameConvention switch
			{
				EntityNameConvention.SingularEntity => name.Singularize(false),
				EntityNameConvention.MatchTable => name,
				_ => name
			};
			return NormalizeTableName( convention );
		}

		private string GetClassExternal(Table table)
		{
			return GetClassExternal(table.Schema, table.Name);
		}

		private string GetClassExternal(string schema, string name)
		{
			return $"[{schema}].[{name}]".ToLower();
		}


		private string GetAttributeName(Column column, ElementPersistable @class)
		{
			if (column.Parent is Table table)
			{
				return DeDuplicate(NormalizeColumnName(column.Name, table.Name), @class.Name);
			}
			else if (column.Parent is View view)
			{
				return DeDuplicate(NormalizeColumnName(column.Name, view.Name), @class.Name);
			}
			throw new Exception($"Unknown parent type : {column.Parent.ToString()}");

		}

		private string GetAttributeExternal(Column column)
		{
			if (column.Parent is Table table)
			{
				return $"[{table.Schema}].[{table.Name}].[{column.Name}]".ToLower();
			}
			else if (column.Parent is View view)
			{
				return $"[{view.Schema}].[{view.Name}].[{column.Name}]".ToLower();
			}
			throw new Exception($"Unknown parent type : {column.Parent.ToString()}");
		}

		private static string NormalizeColumnName(string colName, string tableOrViewName)
		{
			var normalized = (colName != tableOrViewName) ? colName : colName + "Value";
			normalized = RemoveInvalidCSharpCharacter(normalized);

			normalized = normalized.RemovePrefix("col").RemovePrefix("pk");

			normalized = normalized[..1].ToUpper() + normalized[1..];

			if (normalized.EndsWith("ID"))
			{
				normalized = normalized.RemoveSuffix("ID");
				normalized += "Id";
			}

			return normalized;
		}

		private static string DeDuplicate(string propertyName, string className)
		{
			if (propertyName != className)
			{
				return propertyName;
			}

			return propertyName + "Property";
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
				;
		}

		private static string NormalizeTableName(string tableName)
		{
			var normalized = RemoveInvalidCSharpCharacter(tableName);

			normalized = normalized.RemovePrefix("tbl");

			normalized = normalized[..1].ToUpper() + normalized[1..];
			return normalized;
		}
		private static string NormalizeStoredProcName(string storeProcName)
		{
			var normalized = RemoveInvalidCSharpCharacter(storeProcName);
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
			var normalized = RemoveInvalidCSharpCharacter(storeProcName);
			normalized = normalized.RemovePrefix("prc")
				.RemovePrefix("Prc")
				.RemovePrefix("proc");

			normalized = normalized[..1].ToUpper() + normalized[1..];
			return normalized;
		}

	}
}
