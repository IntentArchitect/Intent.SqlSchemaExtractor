namespace Intent.SQLSchemaExtractor
{
    public class Constants
    {
        public class TypeDefinitions
        {
            public class CommonTypes
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
                public const string New = "";
            }
        }

        public class Stereotypes
        {
            public class Rdbms
            {
                public class Table
                {
                    public const string DefinitionId = "dd205b32-b48b-4c77-98f5-faefb2c047ce";

                    public class PropertyId
                    {
                        public const string Name = "2b3a9df7-65e1-4800-b919-bef1a6b8f5a9";
                        public const string Schema = "13e6101f-0e37-4eda-a6ae-ec48cd9f8f4b";
                    }
                }

                public class PrimaryKey
                {
                    public const string DefinitionId = "b99aac21-9ca4-467f-a3a6-046255a9eed6";

                    public class PropertyId
                    {
                        public const string Identity = "4c1e3f7e-61d4-460d-bd20-c2edbc0c0e2e";
                    }
                }
                public class DefaultConstraint
                {
                    public const string DefinitionId = "f21339bf-9ce6-4584-828f-de82089e3b72";

                    public class PropertyId
                    {
                        public const string Name = "a1a18b91-6ad7-4828-9d2c-d77e40638e4d";
                        public const string Value = "0b03f735-394c-4c12-8f10-bd14f9ab2dd0";
                    }
                }

                public class Index
                {
                    public const string DefinitionId = "bbe43b90-c20d-4fdb-8a55-9037a5f6bd0b";

                    public class PropertyId
                    {
                        public const string UniqueKey = "3427ad7b-e1a6-4d36-9e12-c5ada14a414b";
                        public const string Order = "a8f903d5-e8b6-4d15-aedb-08c6b290b733";
                        public const string IsUnique = "90002464-b824-40e2-9f73-32f306868897";
                    }
                }

                public class ForeignKey
                {
                    public const string DefinitionId = "dfe17723-99ee-4554-9be3-f4c90dd48078";

                    public class PropertyId
                    {
                        public const string ColumnName = "41c9145c-7ac7-4c60-acda-10544bfaa6c0";

                    }
                }

                public class Numeric
                {
                    public const string DefinitionId = "8565814D-EF70-40EE-A0D3-577AB7B1254C";

                    public class PropertyId
                    {
                        public const string Precision = "3BB6C479-049E-4258-87D3-7068129B7B57";
                        public const string Scale = "CFA3180C-71F6-465B-B4BC-3238FF3648BA";

                    }
                }

                public class Text
                {
                    public const string DefinitionId = "6347286E-A637-44D6-A5D7-D9BE5789CA7A";
                    public const string Name = "Text Constraints";
                    public const string DefinitionPackageId = "AF8F3810-745C-42A2-93C8-798860DC45B1";
                    public const string DefinitionPackageName = "Intent.Metadata.RDBMS";
                    

                    public class PropertyId
                    {
                        public const string SqlDataType = "1288cfcd-ee51-437e-9713-73b80118f026";
                        public const string SqlDataTypeName = "SQL Data Type";
                        public const string MaxLength = "A04CC24D-81FB-4EA2-A34A-B3C58E04DCFD";
                        public const string MaxLengthName = "MaxLength";
                        public const string IsUnicode = "67EC4CF4-7706-4B39-BC7C-DF539EE2B0AF";
                        public const string IsUnicodeName = "IsUnicode";

                    }
                }
                public class New
                {
                    public const string DefinitionId = "";

                    public class PropertyId
                    {
                        public const string NewProp = "";

                    }
                }
            }
        }
    }
}