using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace Intent.SQLSchemaExtractor;

public class ImportConfiguration
{
	public const string ConfigFile = nameof(ConfigFile);
	public const string GenerateConfigFile = nameof(GenerateConfigFile);
	public const string SerializedConfig = nameof(SerializedConfig);
	
	
	public string? ApplicationId { get; set; }
	
	public EntityNameConvention EntityNameConvention { get; set; } = EntityNameConvention.SingularEntity;
	public TableStereotype TableStereotype { get; set; } = TableStereotype.WhenDifferent;
	public HashSet<ExportType> TypesToExport { get; set; } = [ExportType.Table, ExportType.View, ExportType.StoredProcedure, ExportType.Index];
	public HashSet<string> SchemaFilter { get; set; } = [];
	public string TableViewFilterFilePath { get; set; }
	
	public StoredProcedureType StoredProcedureType { get; set; } = StoredProcedureType.Default;
	public string? RepositoryElementId { get; set; }
	public List<string> StoredProcNames { get; set; } = [];
	
	public string? ConnectionString { get; set; }
	public string? PackageFileName { get; set; }

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
		return TypesToExport.Contains(ExportType.Table);
	}

	internal bool ExportViews()
	{
		return TypesToExport.Contains(ExportType.View);
	}

	internal bool ExportIndexes()
	{
		return TypesToExport.Contains(ExportType.Index);
	}
		

	internal bool ExportStoredProcedures()
	{
		return TypesToExport.Contains(ExportType.StoredProcedure);
	}
}

public enum ExportType
{
	Table,
	View,
	StoredProcedure,
	Index
}

public enum TableStereotype
{
	Always,
	WhenDifferent
}

public enum EntityNameConvention
{
	MatchTable,
	SingularEntity
}

public enum StoredProcedureType
{
	Default,
	StoredProcedureElement,
	RepositoryOperation
}