using System;
using System.Linq;
using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Intent.Modules.Common.Templates;
using Microsoft.SqlServer.Management.Smo;
using Index = Microsoft.SqlServer.Management.Smo.Index;

namespace Intent.SQLSchemaExtractor;

internal static class RdbmsDecorator
{
    public static void ApplyTableDetails(ImportConfiguration config, Table table, ElementPersistable @class)
    {

        if (!RequiresTableStereoType(config, table, @class))
        {
            return;
        }

        var stereotype = @class.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.Table.DefinitionId, InitTableStereotype);
        //For now always set this incase generated table names don't match the generated names due to things like pluralization
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Table.PropertyId.Name).Value = table.Name;

        static void InitTableStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.Table.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Table.PropertyId.Name, _ => { });
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Table.PropertyId.Schema, _ => { });
        }
    }

    private static bool RequiresTableStereoType(ImportConfiguration config, Table table, ElementPersistable @class)
    {
        if (config.TableStereotypes == TableStereotypes.Always)
        {
            return true;
        }
        else if (config.TableStereotypes == TableStereotypes.WhenDifferent)
        {
            switch (config.EntityNameConvention) 
            {
                case EntityNameConvention.MatchTable: return @class.Name != table.Name;
                case EntityNameConvention.SingularEntity: return @class.Name.Pluralize() != table.Name;
            }
        }
        return false;
    }

    public static void ApplyViewDetails(View view, ElementPersistable @class)
    {
        var stereotype = @class.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.View.DefinitionId, InitTableStereotype);
        if (view.Name == @class.Name)
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.View.PropertyId.Name).Value = view.Name;

        static void InitTableStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.View.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.View.PropertyId.Name, _ => { });
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.View.PropertyId.Schema, _ => { });
        }
    }

    public static void ApplyPrimaryKey(Column column, ElementPersistable attribute)
    {
        if (!column.InPrimaryKey)
        {
            return;
        }

        var stereotype = attribute.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.PrimaryKey.DefinitionId, InitPrimaryKeyStereotype);
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.PrimaryKey.PropertyId.Identity).Value = column.Identity.ToString().ToLower();

        static void InitPrimaryKeyStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.PrimaryKey.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.PrimaryKey.PropertyId.Identity, prop => prop.Name = Constants.Stereotypes.Rdbms.PrimaryKey.PropertyId.IdentityName);
        }
    }

    public static void ApplyColumnDetails(Column column, ElementPersistable attribute)
    {
        if (column.Name == attribute.Name &&
            IsImplicitColumnType(column, attribute))
        {
            return;
        }

        var stereotype = attribute.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.Column.DefinitionId, InitColumnStereotype);

        if (column.Name != attribute.Name)
        {
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Column.PropertyId.Name).Value = column.Name;
        }

        if (!IsImplicitColumnType(column, attribute))
        {
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Column.PropertyId.Type).Value = column.DataType.SqlDataType switch
            {
                SqlDataType.VarBinaryMax => "varbinary(max)",
                SqlDataType.VarCharMax => "varchar(max)",
                SqlDataType.NVarCharMax => "nvarchar(max)",
                SqlDataType.VarChar or SqlDataType.NVarChar or SqlDataType.VarBinary when column.DataType.MaximumLength > 0 =>
                    $"{column.DataType.Name}({column.DataType.MaximumLength})",
                _ => column.DataType.Name
            };
        }

        static void InitColumnStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.Column.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Column.PropertyId.Name, _ => { });
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Column.PropertyId.Type, _ => { });
        }

        static bool IsImplicitColumnType(Column column, ElementPersistable attribute)
        {
            return attribute.TypeReference.TypeId switch
            {
                Constants.TypeDefinitions.CommonTypes.String => true, // Column types for strings are handled by "Text Constraints" stereotype
                Constants.TypeDefinitions.CommonTypes.Byte when column.DataType.SqlDataType == SqlDataType.TinyInt => true,
                Constants.TypeDefinitions.CommonTypes.Bool when column.DataType.SqlDataType == SqlDataType.Bit => true,
                Constants.TypeDefinitions.CommonTypes.Binary when column.DataType.SqlDataType == SqlDataType.VarBinary => true,
                Constants.TypeDefinitions.CommonTypes.Short when column.DataType.SqlDataType == SqlDataType.SmallInt => true,
                Constants.TypeDefinitions.CommonTypes.Long when column.DataType.SqlDataType == SqlDataType.BigInt => true,
                Constants.TypeDefinitions.CommonTypes.Int when column.DataType.SqlDataType == SqlDataType.Int => true,
                Constants.TypeDefinitions.CommonTypes.Decimal when column.DataType.SqlDataType == SqlDataType.Decimal => true,
                Constants.TypeDefinitions.CommonTypes.Datetime when column.DataType.SqlDataType == SqlDataType.DateTime2 => true,
                Constants.TypeDefinitions.CommonTypes.Date when column.DataType.SqlDataType == SqlDataType.Date => true,
                Constants.TypeDefinitions.CommonTypes.Guid when column.DataType.SqlDataType == SqlDataType.UniqueIdentifier => true,
                Constants.TypeDefinitions.CommonTypes.DatetimeOffset when column.DataType.SqlDataType == SqlDataType.DateTimeOffset => true,
                _ => false
            };
        }
    }

    public static void ApplyTextConstraint(Column column, ElementPersistable attribute)
    {
        if (column.DataType.SqlDataType != SqlDataType.VarChar &&
             column.DataType.SqlDataType != SqlDataType.NVarChar &&
             column.DataType.SqlDataType != SqlDataType.VarCharMax &&
             column.DataType.SqlDataType != SqlDataType.NVarCharMax &&
             column.DataType.SqlDataType != SqlDataType.Text &&
             column.DataType.SqlDataType != SqlDataType.NText)
        {
            return;
        }

        if (column.DataType.MaximumLength == 0)
        {
            return;
        }


        var stereotype = attribute.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.TextConstraints.DefinitionId, ster => InitTextConstraintStereotype(ster, column));
        if (column.DataType.MaximumLength != -1)
        {
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.MaxLength).Value = column.DataType.MaximumLength.ToString("D");
        }

        static void InitTextConstraintStereotype(StereotypePersistable stereotype, Column column)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.TextConstraints.Name;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.SqlDataType, prop =>
            {
                string value = column.DataType.SqlDataType.ToString().ToUpper();
                if (value.EndsWith("MAX")) { value = value.Substring(0, value.Length - 3); }                              
                prop.Name = Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.SqlDataTypeName;
                prop.Value = value;
            });
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.MaxLength, prop =>
            {
                prop.Name = Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.MaxLengthName;
                prop.Value = null;
            });
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.IsUnicode, prop =>
            {
                prop.Name = Constants.Stereotypes.Rdbms.TextConstraints.PropertyId.IsUnicodeName;
                prop.Value = column.DataType.SqlDataType == SqlDataType.NVarChar || column.DataType.SqlDataType == SqlDataType.NVarCharMax ? "true" : "false";
            });
        }
    }

    public static void ApplyDecimalConstraint(Column column, ElementPersistable attribute)
    {
        if (column.DataType.SqlDataType != SqlDataType.Decimal)
        {
            return;
        }

        var stereotype = attribute.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.DecimalConstraints.DefinitionId, InitDecimalConstraintStereotype);
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DecimalConstraints.PropertyId.Precision).Value = column.DataType.NumericPrecision.ToString();
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DecimalConstraints.PropertyId.Scale).Value = column.DataType.NumericScale.ToString();

        static void InitDecimalConstraintStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.DecimalConstraints.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DecimalConstraints.PropertyId.Precision, prop => prop.Name = Constants.Stereotypes.Rdbms.DecimalConstraints.PropertyId.PrecisionName);
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DecimalConstraints.PropertyId.Scale, prop => prop.Name = Constants.Stereotypes.Rdbms.DecimalConstraints.PropertyId.ScaleName);
        }
    }

    public static void ApplyDefaultConstraint(Column column, ElementPersistable attribute)
    {
        if (column.DefaultConstraint == null)
        {
            return;
        }

        var stereotype = attribute.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.DefaultConstraint.DefinitionId, InitDefaultConstraintStereotype);
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DefaultConstraint.PropertyId.Value).Value = $@"""{column.DefaultConstraint.Text}""";

        static void InitDefaultConstraintStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.DefaultConstraint.Name;
            stereotype.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DefaultConstraint.PropertyId.Value, prop => prop.Name = Constants.Stereotypes.Rdbms.DefaultConstraint.PropertyId.ValueName);
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.DefaultConstraint.PropertyId.TreatAsSqlExpression, prop =>
            {
                prop.Name = Constants.Stereotypes.Rdbms.DefaultConstraint.PropertyId.TreatAsSqlExpressionName;
                prop.Value = "true";
            });
        }
    }

    public static void ApplyComputedValue(Column column, ElementPersistable attribute)
    {
        if (!column.Computed)
        {
            return;
        }

        var stereotype = attribute.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.ComputedValue.DefinitionId, InitComputedValueStereotype);
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.ComputedValue.PropertyId.Sql).Value = column.ComputedText;
        stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.ComputedValue.PropertyId.Stored).Value = column.IsPersisted ? "true" : "false";


        static void InitComputedValueStereotype(StereotypePersistable stereotype)
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

    public static void ApplyIndex(Index index, ElementPersistable @class)
    {
        var indexKeyName = $"IX_{string.Join("_", index.IndexedColumns.Cast<IndexedColumn>().Select(s => s.Name))}";
        foreach (var attribute in @class.ChildElements.Where(p => p.SpecializationType == "Attribute"))
        {
            var columnSqlName = attribute.TryGetStereotypeProperty(
                Constants.Stereotypes.Rdbms.Column.DefinitionId,
                Constants.Stereotypes.Rdbms.Column.PropertyId.Name,
                out var value)
                ? value
                : attribute.Name;

            var indexCount = 0;
            foreach (IndexedColumn indexedColumn in index.IndexedColumns)
            {
                indexCount++;
                if (indexedColumn.Name == columnSqlName || indexedColumn.Name == attribute.Name)
                {
                    var stereotype = attribute.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.Index.DefinitionId, ster =>
                    {
                        ster.Name = Constants.Stereotypes.Rdbms.Index.Name;
                        ster.DefinitionPackageId = Constants.Packages.Rdbms.DefinitionPackageId;
                        ster.DefinitionPackageName = Constants.Packages.Rdbms.DefinitionPackageName;
                    });
                    stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.PropertyId.UniqueKey,
                        prop => prop.Name = Constants.Stereotypes.Rdbms.Index.PropertyId.UniqueKeyName).Value = indexKeyName;
                    stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.PropertyId.Order,
                        prop => prop.Name = Constants.Stereotypes.Rdbms.Index.PropertyId.OrderName).Value = indexCount.ToString();
                    stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.Index.PropertyId.IsUnique,
                        prop => prop.Name = Constants.Stereotypes.Rdbms.Index.PropertyId.IsUniqueName).Value = index.IsUnique.ToString().ToLower();
                }
            }
        }
    }

    public static void ApplyStoredProcedureSettings(StoredProcedure sqlStoredProc, ElementPersistable elementStoredProc)
    {
        var stereotype = elementStoredProc.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.StoredProcedure.DefinitionId, InitStoredProcStereotype);
        if (sqlStoredProc.Name != elementStoredProc.Name)
        {
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.StoredProcedure.PropertyId.NameInSchema).Value = sqlStoredProc.Name;
        }

        for (var paramIndex = 0; paramIndex < sqlStoredProc.Parameters.Count; paramIndex++)
        {
            var elementParam = elementStoredProc.ChildElements[paramIndex];
            var sqlProcParam = sqlStoredProc.Parameters[paramIndex];
            var paramStereotype = elementParam.GetOrCreateStereotype(Constants.Stereotypes.Rdbms.StoredProcedureParameter.DefinitionId, InitStoredProcParamStereotype);
            paramStereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.StoredProcedureParameter.PropertyId.IsOutputParam).Value = sqlProcParam.IsOutputParameter.ToString().ToLower();
        }

        static void InitStoredProcStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.StoredProcedure.Name;
            stereotype.DefinitionPackageId = Constants.Packages.EntityFrameworkCoreRepository.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.EntityFrameworkCoreRepository.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.StoredProcedure.PropertyId.NameInSchema, prop => prop.Name = Constants.Stereotypes.Rdbms.StoredProcedure.PropertyId.NameInSchemaName);
        }
        
        static void InitStoredProcParamStereotype(StereotypePersistable stereotype)
        {
            stereotype.Name = Constants.Stereotypes.Rdbms.StoredProcedureParameter.Name;
            stereotype.DefinitionPackageId = Constants.Packages.EntityFrameworkCoreRepository.DefinitionPackageId;
            stereotype.DefinitionPackageName = Constants.Packages.EntityFrameworkCoreRepository.DefinitionPackageName;
            stereotype.GetOrCreateProperty(Constants.Stereotypes.Rdbms.StoredProcedureParameter.PropertyId.IsOutputParam, prop => prop.Name = Constants.Stereotypes.Rdbms.StoredProcedureParameter.PropertyId.IsOutputParamName);
        }
    }
}