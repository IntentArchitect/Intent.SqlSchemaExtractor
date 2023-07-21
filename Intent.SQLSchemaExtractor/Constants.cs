namespace Intent.SQLSchemaExtractor;

public static class Constants
{
    public static class Specializations
    {
        public static readonly SpecializationType PackageType = new("Domain Package", "1a824508-4623-45d9-accc-f572091ade5a");
        public static readonly SpecializationType FolderType = new("Folder", "4d95d53a-8855-4f35-aa82-e312643f5c5f");
        public static readonly SpecializationType ClassType = new("Class", "04e12b51-ed12-42a3-9667-a6aa81bb6d10");
        public static readonly SpecializationType AttributeType = new("Attribute", "0090fb93-483e-41af-a11d-5ad2dc796adf");
    }
    
    public static class Packages
    {
        public static class Rdbms
        {
            public const string DefinitionPackageId = "AF8F3810-745C-42A2-93C8-798860DC45B1";
            public const string DefinitionPackageName = "Intent.Metadata.RDBMS";
        }
    }

    public static class TypeDefinitions
    {
        public static class CommonTypes
        {
            public const string String = "d384db9c-a279-45e1-801e-e4e8099625f2";
            public const string Byte = "A4E9102F-C1C8-4902-A417-CA418E1874D2";
            public const string Bool = "e6f92b09-b2c5-4536-8270-a4d9e5bbd930";
            public const string Binary = "013af2c5-3c32-4752-8f59-db5691050aef";
            public const string Short = "2ABF0FD3-CD56-4349-8838-D120ED268245";
            public const string Long = "33013006-E404-48C2-AC46-24EF5A5774FD";
            public const string Int = "fb0a362d-e9e2-40de-b6ff-5ce8167cbe74";
            public const string Decimal = "675c7b84-997a-44e0-82b9-cd724c07c9e6";
            public const string Datetime = "a4107c29-7851-4121-9416-cf1236908f1e";
            public const string Date = "1fbaa056-b666-4f25-b8fd-76fe3165acc8";
            public const string Guid = "6b649125-18ea-48fd-a6ba-0bfff0d8f488";
            public const string DatetimeOffset = "f1ba4df3-a5bc-427e-a591-4f6029f89bd7";
        }
    }

    public static class Stereotypes
    {
        public static class Rdbms
        {
            public static class Table
            {
                public const string DefinitionId = "dd205b32-b48b-4c77-98f5-faefb2c047ce";
                public const string Name = "Table";

                public static class PropertyId
                {
                    public const string Name = "2b3a9df7-65e1-4800-b919-bef1a6b8f5a9";
                    public const string NameName = "Name";
                    public const string Schema = "13e6101f-0e37-4eda-a6ae-ec48cd9f8f4b";
                    public const string SchemaName = "Schema";
                }
            }

            public static class View
            {
                public const string DefinitionId = "6dfa2c79-4b9a-4741-9201-95a9d7631b4d";
                public const string Name = "View";

                public static class PropertyId
                {
                    public const string Name = "cb845ecf-b4f6-46cb-986c-0bb111403445";
                    public const string NameName = "Name";
                    public const string Schema = "877751bd-961d-49cd-952e-6a8ffd6c8064";
                    public const string SchemaName = "Schema";
                }
            }

            public static class Column
            {
                public const string DefinitionId = "0b630b29-9513-4bbb-87fa-6cb3e6f65199";
                public const string Name = "Column";

                public static class PropertyId
                {
                    public const string Name = "30044760-ea18-480e-b840-e3cb7e7296d0";
                    public const string NameName = "Name";
                    public const string Type = "bc03d4fc-b9cc-4ac0-8869-b3e2cb18f1ef";
                    public const string TypeName = "Type";
                }
            }

            public static class PrimaryKey
            {
                public const string DefinitionId = "b99aac21-9ca4-467f-a3a6-046255a9eed6";
                public const string Name = "Primary Key";

                public static class PropertyId
                {
                    public const string Identity = "4c1e3f7e-61d4-460d-bd20-c2edbc0c0e2e";
                    public const string IdentityName = "Identity";
                }
            }

            public static class DefaultConstraint
            {
                public const string DefinitionId = "f21339bf-9ce6-4584-828f-de82089e3b72";
                public const string Name = "Default Constraint";

                public static class PropertyId
                {
                    public const string TreatAsSqlExpression = "c213fe57-1d8c-4e3b-9714-21237e526e24";
                    public const string TreatAsSqlExpressionName = "Treat as SQL Expression";
                    public const string Value = "0b03f735-394c-4c12-8f10-bd14f9ab2dd0";
                    public const string ValueName = "Value";
                }
            }

            public static class DecimalConstraints
            {
                public const string DefinitionId = "8775f4d0-7ffd-4678-a6a8-fd7e0c6fbc87";
                public const string Name = "Decimal Constraints";

                public static class PropertyId
                {
                    public const string Precision = "849d8f6f-573f-428a-bc46-8db2614c47c9";
                    public const string PrecisionName = "Precision";
                    public const string Scale = "ff13f032-826f-4ca3-9806-ea4f10b8d600";
                    public const string ScaleName = "Scale";
                }
            }

            public static class TextConstraints
            {
                public const string DefinitionId = "6347286E-A637-44D6-A5D7-D9BE5789CA7A";
                public const string Name = "Text Constraints";

                public static class PropertyId
                {
                    public const string SqlDataType = "1288cfcd-ee51-437e-9713-73b80118f026";
                    public const string SqlDataTypeName = "SQL Data Type";
                    public const string MaxLength = "A04CC24D-81FB-4EA2-A34A-B3C58E04DCFD";
                    public const string MaxLengthName = "MaxLength";
                    public const string IsUnicode = "67EC4CF4-7706-4B39-BC7C-DF539EE2B0AF";
                    public const string IsUnicodeName = "IsUnicode";
                }
            }

            public static class ComputedValue
            {
                public const string DefinitionId = "05321832-016e-49f4-acae-f2923a16b4aa";
                public const string Name = "Computed Value";

                public static class PropertyId
                {
                    public const string Sql = "be749386-4aa1-4f28-8cfc-77b60a144d5b";
                    public const string SqlName = "SQL";
                    public const string Stored = "baaf3703-322c-4917-a365-8c27fa44ad7a";
                    public const string StoredName = "Stored";
                }
            }

            public static class Index
            {
                public const string DefinitionId = "bbe43b90-c20d-4fdb-8a55-9037a5f6bd0b";
                public const string Name = "Index";

                public static class PropertyId
                {
                    public const string UniqueKey = "3427ad7b-e1a6-4d36-9e12-c5ada14a414b";
                    public const string UniqueKeyName = "UniqueKey";
                    public const string Order = "a8f903d5-e8b6-4d15-aedb-08c6b290b733";
                    public const string OrderName = "Order";
                    public const string IsUnique = "90002464-b824-40e2-9f73-32f306868897";
                    public const string IsUniqueName = "IsUnique";
                }
            }

            public static class ForeignKey
            {
                public const string DefinitionId = "793a5128-57a1-440b-a206-af5722b752a6";
                public const string Name = "Foreign Key";

                public static class PropertyId
                {
                    public const string Association = "42e4f9b5-f834-4e5f-86aa-d3a35c505076";
                    public const string AssociationName = "Association";
                }
            }
        }
    }
}

public sealed class SpecializationType
{
    public SpecializationType(string name, string id)
    {
        Name = name;
        Id = id;
    }

    public string Name { get; }
    public string Id { get; }
}