using System;
using Intent.IArchitect.Agent.Persistence.Model;
using Intent.SQLSchemaExtractor.ModelMapper;

namespace Intent.SQLSchemaExtractor.ExtensionMethods
{
	internal static class ExtensionMethods
	{
		public static bool IsEnum(this ElementPersistable element)
		{
			return DatabaseSchemaToModelMapper.EnumType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}
		
		public static bool IsFolder(this ElementPersistable element)
		{
			return DatabaseSchemaToModelMapper.FolderType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsIndex(this ElementPersistable element)
		{
			return DatabaseSchemaToModelMapper.IndexType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsClass(this ElementPersistable element)
		{
			return DatabaseSchemaToModelMapper.ClassType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsAssociation(this AssociationPersistable association)
		{
			return DatabaseSchemaToModelMapper.AssociationType.Name.Equals(association.AssociationType, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsAttribute(this ElementPersistable element)
		{
			return DatabaseSchemaToModelMapper.AttributeType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsRepository(this ElementPersistable element)
		{
			return DatabaseSchemaToModelMapper.RepositoryType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsStoredProcedure(this ElementPersistable element)
		{
			return DatabaseSchemaToModelMapper.StoredProcedureType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsStoredProcedureParameter(this ElementPersistable element)
		{
			return DatabaseSchemaToModelMapper.StoredProcedureParameterType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

        public static bool IsTrigger(this ElementPersistable element)
        {
            return DatabaseSchemaToModelMapper.TriggerType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
        }

    }
}
