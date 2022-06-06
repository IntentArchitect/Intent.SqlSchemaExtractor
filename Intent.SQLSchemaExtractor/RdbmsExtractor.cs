using System.Collections.Generic;
using System.Linq;
using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Intent.SQLSchemaExtractor
{
    internal class RdbmsExtractor
    {
        public static void ApplyTextConstraint(Column column, ElementPersistable element)
        {
            if ((column.DataType.SqlDataType != SqlDataType.VarChar &&
                 column.DataType.SqlDataType != SqlDataType.NVarChar) ||
                column.DataType.MaximumLength == 0)
            {
                return;
            }

            element.Stereotypes ??= new List<StereotypePersistable>();

            var stereotype = element.Stereotypes.SingleOrDefault(x => x.DefinitionId == Constants.Stereotypes.Rdbms.Text.DefinitionId);
            if (stereotype == null)
            {
                stereotype = new StereotypePersistable
                {
                    DefinitionId = Constants.Stereotypes.Rdbms.Text.DefinitionId,
                    Name = Constants.Stereotypes.Rdbms.Text.Name,
                    Comment = null,
                    AddedByDefault = false,
                    DefinitionPackageName = Constants.Stereotypes.Rdbms.Text.DefinitionPackageName,
                    DefinitionPackageId = Constants.Stereotypes.Rdbms.Text.DefinitionPackageId,
                    Properties = new List<StereotypePropertyPersistable>
                    {
                        new()
                        {
                            Id = Constants.Stereotypes.Rdbms.Text.PropertyId.SqlDataType,
                            Name = Constants.Stereotypes.Rdbms.Text.PropertyId.SqlDataTypeName,
                            Value = "DEFAULT",
                            IsActive = true
                        },
                        new()
                        {
                            Id = Constants.Stereotypes.Rdbms.Text.PropertyId.MaxLength,
                            Name = Constants.Stereotypes.Rdbms.Text.PropertyId.MaxLengthName,
                            Value = null,
                            IsActive = true
                        },
                        new()
                        {
                            Id = Constants.Stereotypes.Rdbms.Text.PropertyId.IsUnicode,
                            Name = Constants.Stereotypes.Rdbms.Text.PropertyId.IsUnicodeName,
                            Value = "false",
                            IsActive = true
                        },
                    }
                };
                element.Stereotypes.Add(stereotype);
            }

            stereotype.Properties ??= new List<StereotypePropertyPersistable>();
            var property = stereotype.Properties.SingleOrDefault(x => x.Id == Constants.Stereotypes.Rdbms.Text.PropertyId.MaxLength);
            if (property == null)
            {
                property = new()
                {
                    Id = Constants.Stereotypes.Rdbms.Text.PropertyId.MaxLength,
                    Name = Constants.Stereotypes.Rdbms.Text.PropertyId.MaxLengthName,
                    Value = "false",
                    IsActive = true
                };
                stereotype.Properties.Add(property);
            }

            property.IsActive = true;
            property.Name = Constants.Stereotypes.Rdbms.Text.PropertyId.MaxLengthName;
            property.Value = column.DataType.MaximumLength.ToString("D");
        }
    }
}
