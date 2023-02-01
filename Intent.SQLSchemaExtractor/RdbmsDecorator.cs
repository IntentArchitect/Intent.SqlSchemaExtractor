using System;
using System.Collections.Generic;
using System.Linq;
using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Microsoft.SqlServer.Management.Dmf;
using Microsoft.SqlServer.Management.Smo;

namespace Intent.SQLSchemaExtractor;

internal static class RdbmsDecorator
{
    public static void ApplyTableDetails(Table table, ElementPersistable element)
    {
        if (element.Name == table.Name && table.Schema == "dbo")
        {
            return;
        }
        
        var stereotype = element.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.Table.DefinitionId, InitTableStereotype);
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Table.PropertyId.Name).Value = table.Name;
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Table.PropertyId.Schema).Value = table.Schema;
        
        void InitTableStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.Table.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Table.PropertyId.Name, prop => prop.Name = Constants.Stereotypes.Rdbms.Table.PropertyId.NameName);
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Table.PropertyId.Schema, prop => prop.Name = Constants.Stereotypes.Rdbms.Table.PropertyId.SchemaName);
        }
    }
    
    public static void ApplyPrimaryKey(Column column, ElementPersistable element)
    {
        if (!column.InPrimaryKey)
        {
            return;
        }
        
        var stereotype = element.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.PrimaryKey.DefinitionId, InitPrimaryKeyStereotype);
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.PrimaryKey.PropertyId.Identity).Value = column.Identity.ToString().ToLower();
        
        void InitPrimaryKeyStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.PrimaryKey.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.PrimaryKey.PropertyId.Identity, prop => prop.Name = Constants.Stereotypes.Rdbms.PrimaryKey.PropertyId.IdentityName);
        }
    }

    public static void ApplyColumnDetails(Column column, ElementPersistable element)
    {
        var stereotype = element.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.Column.DefinitionId, InitColumnStereotype);
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Column.PropertyId.Name).Value = column.Name;
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Column.PropertyId.Type).Value = column.DataType.Name;
        
        void InitColumnStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.Column.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Column.PropertyId.Name, prop => prop.Name = Constants.Stereotypes.Rdbms.Column.PropertyId.NameName);
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Column.PropertyId.Type, prop => prop.Name = Constants.Stereotypes.Rdbms.Column.PropertyId.TypeName);
        }
    }
    
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

        element.RemoveStereotypeIfPresent(Constants.Stereotypes.Rdbms.Column.DefinitionId);

        var stereotype = element.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.TextConstraints.DefinitionId, ster => InitTextConstraintStereotype(ster, column));
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.MaxLength).Value = column.DataType.MaximumLength.ToString("D");
        
        void InitTextConstraintStereotype(StereotypePersistable stereotype, Column column)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.TextConstraints.Name;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.SqlDataType, prop =>
            {
                prop.Name = Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.SqlDataTypeName;
                prop.Value = column.DataType.SqlDataType.ToString().ToUpper();
            });
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.MaxLength, prop =>
            {
                prop.Name = Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.MaxLengthName;
                prop.Value = null;
            });
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.IsUnicode, prop =>
            {
                prop.Name = Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.IsUnicodeName;
                prop.Value = "false";
            });
        }
    }

    public static void ApplyDecimalConstraint(Column column, ElementPersistable element)
    {
        if (column.DataType.SqlDataType != SqlDataType.Decimal)
        {
            return;
        }
        
        element.RemoveStereotypeIfPresent(Constants.Stereotypes.Rdbms.Column.DefinitionId);

        var stereotype = element.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.DecimalConstraints.DefinitionId, InitDecimalConstraintStereotype);
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DecimalConstraints.PropertyId.Precision).Value = column.DataType.NumericPrecision.ToString();
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DecimalConstraints.PropertyId.Scale).Value = column.DataType.NumericScale.ToString();
        
        void InitDecimalConstraintStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.DecimalConstraints.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DecimalConstraints.PropertyId.Precision, prop => prop.Name = Constants.Stereotypes.Rdbms.DecimalConstraints.PropertyId.PrecisionName);
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DecimalConstraints.PropertyId.Scale, prop => prop.Name = Constants.Stereotypes.Rdbms.DecimalConstraints.PropertyId.ScaleName);
        }
    }

    public static void ApplyDefaultConstraint(Column column, ElementPersistable element)
    {
        if (column.DefaultConstraint == null)
        {
            return;
        }
        
        var stereotype = element.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.DefaultConstraint.DefinitionId, InitDefaultConstraintStereotype);
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DefaultConstraint.PropertyId.Value).Value = column.DefaultConstraint.Text;
        
        void InitDefaultConstraintStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.DefaultConstraint.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DefaultConstraint.PropertyId.Value, prop => prop.Name = Constants.Stereotypes.Rdbms.DefaultConstraint.PropertyId.ValueName);
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DefaultConstraint.PropertyId.TreatAsSqlExpression, prop =>
            {
                prop.Name = Constants.Stereotypes.Rdbms.DefaultConstraint.PropertyId.TreatAsSqlExpressionName;
                prop.Value = "false";
            });
        }
    }

    public static void ApplyComputedValue(Column column, ElementPersistable element)
    {
        if (!column.Computed)
        {
            return;
        }
        
        var stereotype = element.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.ComputedValue.DefinitionId, InitComputedValueStereotype);
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.ComputedValue.PropertyId.Sql).Value = column.ComputedText;
        
        void InitComputedValueStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.ComputedValue.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.ComputedValue.PropertyId.Sql, prop => prop.Name = Constants.Stereotypes.Rdbms.ComputedValue.PropertyId.SqlName);
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.ComputedValue.PropertyId.Stored, prop =>
            {
                prop.Name = Constants.Stereotypes.Rdbms.ComputedValue.PropertyId.StoredName;
                prop.Value = "false";
            });
        }
    }
}