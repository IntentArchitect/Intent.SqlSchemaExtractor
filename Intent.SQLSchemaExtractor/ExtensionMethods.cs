using Intent.IArchitect.Agent.Persistence.Model;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Intent.SQLSchemaExtractor
{
	internal static class ExtensionMethods
	{
		public static bool IsFolder(this ElementPersistable element)
		{
			return ModelSchemaHelper.FolderType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsIndex(this ElementPersistable element)
		{
			return ModelSchemaHelper.IndexType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsClass(this ElementPersistable element)
		{
			return ModelSchemaHelper.ClassType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsAssociation(this AssociationPersistable association)
		{
			return ModelSchemaHelper.AssociationType.Name.Equals(association.AssociationType, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsAttribute(this ElementPersistable element)
		{
			return ModelSchemaHelper.AttributeType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsRepository(this ElementPersistable element)
		{
			return ModelSchemaHelper.RepositoryType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsStoredProcedure(this ElementPersistable element)
		{
			return ModelSchemaHelper.StoredProcedureType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsStoredProcedureParameter(this ElementPersistable element)
		{
			return ModelSchemaHelper.StoredProcedureParameterType.Id.Equals(element.SpecializationTypeId, StringComparison.OrdinalIgnoreCase);
		}

	}
}
