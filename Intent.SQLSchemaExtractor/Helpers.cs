using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Intent.SQLSchemaExtractor
{
    public static class Helpers
    {
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

        public static bool IsFolder(this ElementPersistable @class, SchemaExtratorConfiguration config)
        {
            return config.FolderType.Id.Equals(@class.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsClass(this ElementPersistable @class, SchemaExtratorConfiguration config)
        {
            return config.ClassType.Id.Equals(@class.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
