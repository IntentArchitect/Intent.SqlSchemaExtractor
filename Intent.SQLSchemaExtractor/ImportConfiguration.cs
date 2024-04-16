using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace Intent.SQLSchemaExtractor
{
    public class ImportConfiguration
    {
        public EntityNameConvention EntityNameConvention { get; set; } = EntityNameConvention.SingularEntity;
        public TableStereotypes TableStereotypes { get; set; } = TableStereotypes.WhenDifferent;

        public HashSet<ExportTypes> TypesToExport { get; set; } = new HashSet<ExportTypes> { ExportTypes.Table, ExportTypes.View, ExportTypes.StoredProcedure, ExportTypes.Index };

        public HashSet<string> SchemaFilter { get; set; } = new HashSet<string>();
        public string TableViewFilterFilePath { get; set; }

        public string? ConnectionString { get; set; }
        public string? PackageFileName { get; set; }
		public string? ApplicationId { get; set; }

		public SettingPersistence SettingPersistence { get; set; } = SettingPersistence.None;

		public ImportConfiguration()
        {            
        }

        internal IReadOnlyList<string> GetFilteredTableViewList()
        {
	        if (string.IsNullOrWhiteSpace(TableViewFilterFilePath))
	        {
		        return ImmutableList<string>.Empty;
	        }

	        var tables = File.ReadAllLines(TableViewFilterFilePath, Encoding.UTF8).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToList();
	        return tables;
        }

        internal bool ExportSchema(string schema)
        {
	        return SchemaFilter.Count == 0 || SchemaFilter.Contains(schema);
        }

        internal bool ExportTables()
        {
            return TypesToExport.Contains(ExportTypes.Table);
        }

        internal bool ExportViews()
        {
            return TypesToExport.Contains(ExportTypes.View);
        }

		internal bool ExportIndexes()
		{
			return TypesToExport.Contains(ExportTypes.Index);
		}
		

		internal bool ExportStoredProcedures()
        {
            return TypesToExport.Contains(ExportTypes.StoredProcedure);
        }

        public static void ConfigFile() { }
        public static void GenerateConfigFile() { }
		public static void SerializedConfig() { }
	}

	public enum ExportTypes
    {
        Table,
        View,
        StoredProcedure,
        Index
    }

    public enum TableStereotypes
    {
        Always,
        WhenDifferent,
    }

    public enum EntityNameConvention
    {
        MatchTable,
        SingularEntity,
    }

	public enum SettingPersistence
	{
		None,
		AllSanitisedConnectionString,
		AllWithoutConnectionString,
		All,
	}

}
