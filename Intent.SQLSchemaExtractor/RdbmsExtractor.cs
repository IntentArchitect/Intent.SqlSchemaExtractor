using System;
using System.Collections.Generic;
using System.Linq;
using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Microsoft.SqlServer.Management.Dmf;
using Microsoft.SqlServer.Management.Smo;

namespace Intent.SQLSchemaExtractor;

internal static class RdbmsExtractor
{
    public static void ApplyTextConstraint(Column column, ElementPersistable element)
    {
        if ((column.DataType.SqlDataType != SqlDataType.VarChar &&
             column.DataType.SqlDataType != SqlDataType.NVarChar &&
             column.DataType.SqlDataType != SqlDataType.Text &&
             column.DataType.SqlDataType != SqlDataType.NText) ||
            column.DataType.MaximumLength == 0)
        {
            return;
        }

        var stereotype = element.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.Text.DefinitionId, ster => InitTextConstraintStereotype(ster, column));
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Text.PropertyId.MaxLength).Value = column.DataType.MaximumLength.ToString("D");
    }

    public static void ApplyDecimalConstraint(Column column, ElementPersistable element)
    {
        if (column.DataType.SqlDataType != SqlDataType.Decimal)
        {
            return;
        }

        var stereotype = element.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.Numeric.DefinitionId, InitDecimalConstraintStereotype);
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Numeric.PropertyId.Precision).Value = column.DataType.NumericPrecision.ToString();
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Numeric.PropertyId.Scale).Value = column.DataType.NumericScale.ToString();
    }

    private static void InitTextConstraintStereotype(StereotypePersistable stereotype, Column column)
    {
        stereotype.Name = Constants.Stereotypes.Rdbms.Text.Name;
        stereotype.DefinitionPackageName = Constants.Stereotypes.Rdbms.Text.DefinitionPackageName;
        stereotype.DefinitionPackageId = Constants.Stereotypes.Rdbms.Text.DefinitionPackageId;
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Text.PropertyId.SqlDataType, prop =>
        {
            prop.Name = Constants.Stereotypes.Rdbms.Text.PropertyId.SqlDataTypeName;
            prop.Value = column.DataType.SqlDataType.ToString().ToUpper();
        });
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Text.PropertyId.MaxLength, prop =>
        {
            prop.Name = Constants.Stereotypes.Rdbms.Text.PropertyId.MaxLengthName;
            prop.Value = null;
        });
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Text.PropertyId.IsUnicode, prop =>
        {
            prop.Name = Constants.Stereotypes.Rdbms.Text.PropertyId.IsUnicodeName;
            prop.Value = "false";
        });
    }

    private static void InitDecimalConstraintStereotype(StereotypePersistable stereotype)
    {
        stereotype.Name = Constants.Stereotypes.Rdbms.Numeric.Name;
        stereotype.DefinitionPackageId = Constants.Stereotypes.Rdbms.Numeric.DefinitionPackageId;
        stereotype.DefinitionPackageName = Constants.Stereotypes.Rdbms.Numeric.DefinitionPackageName;
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Numeric.PropertyId.Precision, prop => prop.Name = Constants.Stereotypes.Rdbms.Numeric.PropertyId.PrecisionName);
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Numeric.PropertyId.Scale, prop => prop.Name = Constants.Stereotypes.Rdbms.Numeric.PropertyId.ScaleName);
    }

    private static StereotypePersistable GetOrCreateStereotype(this ElementPersistable element, 
        string stereotypeDefinitionId,
        Action<StereotypePersistable> initAction = null)
    {
        element.Stereotypes ??= new List<StereotypePersistable>();
        
        var stereotype = element.Stereotypes.SingleOrDefault(x => x.DefinitionId == stereotypeDefinitionId);
        if (stereotype == null)
        {
            if (initAction == null)
            {
                throw new InvalidOperandException($"Stereotype Definition Id [{stereotypeDefinitionId}] cannot be found and cannot be created without {nameof(initAction)} being specified");
            }
            stereotype = new StereotypePersistable
            {
                DefinitionId = stereotypeDefinitionId
            };
            initAction(stereotype);
            element.Stereotypes.Add(stereotype);
        }

        return stereotype;
    }

    private static StereotypePropertyPersistable GetOrCreateProperty(this StereotypePersistable stereotype, 
        string propertyId, 
        Action<StereotypePropertyPersistable> initAction = null)
    {
        stereotype.Properties ??= new List<StereotypePropertyPersistable>();

        var property = stereotype.Properties.SingleOrDefault(p => p.Id == propertyId);
        if (property == null)
        {
            if (initAction == null)
            {
                throw new InvalidOperationException($"Stereotype Definition Id [{stereotype.DefinitionId}] cannot create a property with Id [{propertyId}] without {nameof(initAction)} being specified");
            }
            property = new StereotypePropertyPersistable
            {
                Id = propertyId,
                IsActive = true
            };
            initAction(property);
            stereotype.Properties.Add(property);
        }

        return property;
    }
}