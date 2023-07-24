﻿using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;

namespace Intent.SQLSchemaExtractor
{
    public static class ElementHelper
    {
        private static readonly SpecializationType DomainPackageType = new("Domain Package", "1a824508-4623-45d9-accc-f572091ade5a");
        private static readonly SpecializationType FolderType = new("Folder", "4d95d53a-8855-4f35-aa82-e312643f5c5f");
        private static readonly SpecializationType ClassType = new("Class", "04e12b51-ed12-42a3-9667-a6aa81bb6d10");
        private static readonly SpecializationType AttributeType = new("Attribute", "0090fb93-483e-41af-a11d-5ad2dc796adf");
        private static readonly SpecializationType RepositoryType = new("Repository", "96ffceb2-a70a-4b69-869b-0df436c470c3");
        private static readonly SpecializationType StoredProcedureType = new("Stored Procedure", "575edd35-9438-406d-b0a7-b99d6f29b560");
        private static readonly SpecializationType StoredProcedureParameterType = new("Stored Procedure Parameter", "5823b192-eb03-47c8-90d8-5501c922e9a5");
        
        public static void AddOrUpdateStereotype(
            this ElementPersistable element,
            StereotypePersistable requiredStereotype,
            Func<StereotypePropertyPersistable, StereotypePropertyPersistable, bool> additionalEqualityPredicate = null)
        {
            if (element.Stereotypes == null)
            {
                element.Stereotypes = new List<StereotypePersistable>();
            }

            AddOrUpdate(element.Stereotypes, requiredStereotype, additionalEqualityPredicate);
        }

        public static void AddOrUpdateStereotypes(
            this ElementPersistable element,
            IEnumerable<StereotypePersistable> requiredStereotypes,
            Func<StereotypePropertyPersistable, StereotypePropertyPersistable, bool> additionalEqualityPredicate = null)
        {
            foreach (var required in requiredStereotypes)
            {
                element.AddOrUpdateStereotype(required);
            }
        }

        public static void AddOrUpdateStereotype(
            this AssociationEndPersistable associationEnd,
            StereotypePersistable requiredStereotype,
            Func<StereotypePropertyPersistable, StereotypePropertyPersistable, bool> additionalEqualityPredicate = null)
        {
            if (associationEnd.Stereotypes == null)
            {
                associationEnd.Stereotypes = new List<StereotypePersistable>();
            }

            AddOrUpdate(associationEnd.Stereotypes, requiredStereotype, additionalEqualityPredicate);
        }

        private static void AddOrUpdate(
            ICollection<StereotypePersistable> existingStereotypes,
            StereotypePersistable requiredStereotype,
            Func<StereotypePropertyPersistable, StereotypePropertyPersistable, bool> additionalEqualityPredicate)
        {
            if (additionalEqualityPredicate == null)
            {
                additionalEqualityPredicate = (x, y) => true;
            }

            var targetStereotype = existingStereotypes
                .SingleOrDefault(x =>
                    x.DefinitionId == requiredStereotype.DefinitionId &&
                    x.Properties
                        .Any(y => requiredStereotype.Properties
                            .Any(z => additionalEqualityPredicate(y, z))));

            if (targetStereotype == null)
            {
                existingStereotypes.Add(targetStereotype = new StereotypePersistable
                {
                    DefinitionId = requiredStereotype.DefinitionId,
                    Properties = new List<StereotypePropertyPersistable>()
                });
            }

            foreach (var requiredProperty in requiredStereotype.Properties)
            {
                var targetProperty = targetStereotype.Properties.SingleOrDefault(x => x.Id == requiredProperty.Id);
                if (targetProperty == null)
                {
                    targetStereotype.Properties.Add(targetProperty = new StereotypePropertyPersistable
                    {
                        Id = requiredProperty.Id
                    });
                }

                targetProperty.Value = requiredProperty.Value;
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
        
        public static ElementPersistable GetOrCreateFolder(this PackageModelPersistable package, string folderName)
        {
            var normalizedFolderName = NormalizeSchemaName(folderName);
            var element = package.Classes.SingleOrDefault(x => x.Name == normalizedFolderName && x.IsFolder());
            if (element is null)
            {
                package.AddElement(element = new ElementPersistable
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = normalizedFolderName,
                    ParentFolderId = package.Id,
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
        
        public static ElementPersistable GetOrCreateClass(this PackageModelPersistable package, string parentId, string externalReference, string className)
        {
            var element = package.Classes.SingleOrDefault(x => x.ExternalReference == externalReference && x.IsClass());
            if (element is null)
            {
                package.AddElement(element = new ElementPersistable
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentFolderId = parentId,
                    Name = NormalizeTableName(className),
                    SpecializationTypeId = ClassType.Id,
                    SpecializationType = ClassType.Name,
                    ExternalReference = externalReference
                });
            }
            if (!element.IsClass())
            {
                throw new Exception($"Element with External Reference {externalReference} is not a {ClassType.Name}");
            }

            return element;
        }

        public static ElementPersistable GetOrCreateAttribute(this ElementPersistable @class, string tableOrViewName, string externalReference, string attributeName, bool nullable)
        {
            var element = @class.ChildElements.SingleOrDefault(x => x.ExternalReference == externalReference);
            if (element is null)
            {
                @class.ChildElements.Add(element = new ElementPersistable
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = DeDuplicate(NormalizeColumnName(attributeName, tableOrViewName), @class.Name),
                    SpecializationTypeId = AttributeType.Id,
                    SpecializationType = AttributeType.Name,
                    Stereotypes = new List<StereotypePersistable>(),
                    TypeReference = new TypeReferencePersistable()
                    {
                        Id = Guid.NewGuid().ToString(),
                        IsNullable = nullable,
                        IsCollection = false,
                        Stereotypes = new List<StereotypePersistable>(),
                        GenericTypeParameters = new List<TypeReferencePersistable>()
                    },
                    ExternalReference = externalReference
                });
            }
            if (!element.IsAttribute())
            {
                throw new Exception($"Element with External Reference {externalReference} is not an {AttributeType.Name}");
            }

            return element;
        }
        
        public static ElementPersistable GetOrCreateRepository(this PackageModelPersistable package, string parentId, string externalReference, string repositoryName)
        {
            var element = package.Classes.SingleOrDefault(x => x.ExternalReference == externalReference && x.IsRepository());
            if (element is null)
            {
                package.AddElement(element = new ElementPersistable
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentFolderId = parentId,
                    Name = NormalizeTableName(repositoryName),
                    SpecializationTypeId = RepositoryType.Id,
                    SpecializationType = RepositoryType.Name,
                    ExternalReference = externalReference
                });
            }
            if (!element.IsRepository())
            {
                throw new Exception($"Element with External Reference {externalReference} is not a {RepositoryType.Name}");
            }

            return element;
        }

        public static ElementPersistable GetOrCreateStoredProcedure(this ElementPersistable repository, string externalReference, string storeProcedureName)
        {
            var element = repository.ChildElements.SingleOrDefault(x => x.ExternalReference == externalReference);
            if (element is null)
            {
                repository.ChildElements.Add(element = new ElementPersistable
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = NormalizeStoredProcName(storeProcedureName),
                    SpecializationTypeId = StoredProcedureType.Id,
                    SpecializationType = StoredProcedureType.Name,
                    Stereotypes = new List<StereotypePersistable>(),
                    TypeReference = null,
                    ExternalReference = externalReference
                });
            }
            if (!element.IsStoredProcedure())
            {
                throw new Exception($"Element with External Reference {externalReference} is not a {StoredProcedureType.Name}");
            }

            return element;
        }
        
        public static ElementPersistable GetOrCreateStoredProcedureParameter(this ElementPersistable storedProcedure, string externalReference, string parameterName)
        {
            var element = storedProcedure.ChildElements.SingleOrDefault(x => x.ExternalReference == externalReference);
            if (element is null)
            {
                storedProcedure.ChildElements.Add(element = new ElementPersistable
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = NormalizeStoredProcParameterName(parameterName),
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
                    ExternalReference = externalReference
                });
            }
            if (!element.IsStoredProcedureParameter())
            {
                throw new Exception($"Element with External Reference {externalReference} is not a {StoredProcedureParameterType.Name}");
            }

            return element;
        }

        public static bool IsFolder(this ElementPersistable element)
        {
            return FolderType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsClass(this ElementPersistable element)
        {
            return ClassType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
        }
        
        public static bool IsAttribute(this ElementPersistable element)
        {
            return AttributeType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsRepository(this ElementPersistable element)
        {
            return RepositoryType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
        }
        
        public static bool IsStoredProcedure(this ElementPersistable element)
        {
            return StoredProcedureType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
        }
        
        public static bool IsStoredProcedureParameter(this ElementPersistable element)
        {
            return StoredProcedureParameterType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
        }
        
        // C# can't have the property name and class name be the same
        private static string DeDuplicate(string propertyName, string className)
        {
            if (propertyName != className)
            {
                return propertyName;
            }

            return propertyName + "Property";
        }
        
        private static string NormalizeSchemaName(string schemaName)
        {
            var normalized = schemaName;
            normalized = normalized[..1].ToUpper() + normalized[1..];
            return normalized;
        }

        private static string NormalizeTableName(string tableName)
        {
            var normalized = tableName;
            normalized = normalized
                .Replace("(", "_")
                .Replace(")", "_")
                .Replace("#", "Hash")
                .Replace("%", "Percent")
                .Replace("$", "Dollar")
                .Replace("?", "Question")
                .Replace("!", "Exclamation")
                .Replace(".", "_");
            normalized = normalized[..1].ToUpper() + normalized[1..];
            return normalized;
        }
        
        private static string NormalizeColumnName(string colName, string tableOrViewName)
        {
            var normalized = (colName != tableOrViewName) ? colName.Replace(" ", "") : colName + "Value";
            normalized = normalized
                .Replace("(", "_")
                .Replace(")", "_")
                .Replace("#", "Hash")
                .Replace("%", "Percent")
                .Replace("$", "Dollar")
                .Replace("?", "Question")
                .Replace("!", "Exclamation")
                .Replace(".", "_");
            normalized = normalized[..1].ToUpper() + normalized[1..];
            return normalized;
        }
        
        private static string NormalizeStoredProcName(string storeProcName)
        {
            var normalized = storeProcName;
            normalized = normalized
                .Replace(" ", "")
                .Replace("(", "_")
                .Replace(")", "_")
                .Replace("#", "Hash")
                .Replace("%", "Percent")
                .Replace("$", "Dollar")
                .Replace("?", "Question")
                .Replace("!", "Exclamation")
                .Replace(".", "_");
            return normalized;
        }
        
        private static string NormalizeStoredProcParameterName(string storeProcName)
        {
            var normalized = storeProcName;
            normalized = normalized
                .Replace(" ", "")
                .Replace("@", "")
                .Replace("(", "_")
                .Replace(")", "_")
                .Replace("#", "Hash")
                .Replace("%", "Percent")
                .Replace("$", "Dollar")
                .Replace("?", "Question")
                .Replace("!", "Exclamation")
                .Replace(".", "_");
            return normalized;
        }
    }
}