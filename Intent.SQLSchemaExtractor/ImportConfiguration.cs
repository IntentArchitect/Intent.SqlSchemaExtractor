using Json.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

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
	public string ImportFilterFilePath { get; set; }
	
	public StoredProcedureType StoredProcedureType { get; set; } = StoredProcedureType.Default;
	public string? RepositoryElementId { get; set; }
	public List<string> StoredProcNames { get; set; } = [];
	
	public string? ConnectionString { get; set; }
	public string? PackageFileName { get; set; }

	private ImportFilterSettings? _importFilterSettings;
	internal ImportFilterSettings GetImportFilterSettings()
	{
		if (_importFilterSettings is not null)
		{
			return _importFilterSettings;
		}
		
		if (string.IsNullOrWhiteSpace(ImportFilterFilePath))
		{
			return new ImportFilterSettings();
		}

		var jsonContent = File.ReadAllText(ImportFilterFilePath);
		_importFilterSettings =
			JsonConvert.DeserializeObject<ImportFilterSettings>(
				jsonContent,
				new JsonSerializerSettings
				{
					ContractResolver = new DefaultContractResolver
					{
						NamingStrategy = new SnakeCaseNamingStrategy()
					}
				})
			?? throw new Exception("Import filter settings are not valid.");

		return _importFilterSettings;
	}

	internal bool ExportSchema(string schema)
	{
		var settings = GetImportFilterSettings();
		return settings.Schemas.Count == 0 || settings.Schemas.Contains(schema);
	}

    internal bool ExportTable(string schema, string tableName)
	{
		var settings = GetImportFilterSettings();
		if (!ExportSchema(schema))
		{
			return false;
		}
		
		if (settings.ExcludeTables.Contains(tableName) || settings.ExcludeTables.Contains($"{schema}.{tableName}"))
		{
			return false;
		}
		
		return settings.IncludeTables.Count == 0 || 
		       settings.IncludeTables.Any(x => x.Name == tableName || x.Name == $"{schema}.{tableName}");
	}

	internal bool ExportView(string schema, string viewName)
	{
		var settings = GetImportFilterSettings();
		if (!ExportSchema(schema))
		{
			return false;
		}
		
		if (settings.ExcludeViews.Contains(viewName) || settings.ExcludeViews.Contains($"{schema}.{viewName}"))
		{
			return false;
		}
		
		return settings.IncludeViews.Count == 0 || 
		       settings.IncludeViews.Any(x => x.Name == viewName || x.Name == $"{schema}.{viewName}");
	}

	internal bool ExportStoredProcedure(string schema, string storedProcedureName)
	{
		var settings = GetImportFilterSettings();
		if (!ExportSchema(schema))
		{
			return false;
		}
		
		if (settings.ExcludeStoredProcedures.Contains(storedProcedureName) || settings.ExcludeStoredProcedures.Contains($"{schema}.{storedProcedureName}"))
		{
			return false;
		}

		return settings.IncludeStoredProcedures.Count == 0 ||
		       settings.IncludeStoredProcedures.Contains(storedProcedureName) ||
		       settings.IncludeStoredProcedures.Contains($"{schema}.{storedProcedureName}");
	}
	
	internal bool ExportDependantTable(string schema, string tableName)
	{
		if (!ExportSchema(schema))
		{
			return false;
		}

		var settings = GetImportFilterSettings();
		if (settings.ExcludeTables.Contains(tableName) || settings.ExcludeTables.Contains($"{schema}.{tableName}"))
		{
			return false;
		}

		return true;
	}
	
	internal bool ExportTableColumn(string schema, string tableName, string colName)
	{
		var filterSettings = GetImportFilterSettings();
		var table = filterSettings.IncludeTables.FirstOrDefault(x => x.Name == tableName || x.Name == $"{schema}.{tableName}");
		return table?.ExcludeColumns.Contains(colName) != true && filterSettings.ExcludedTableColumns.Contains(colName) != true;
	}

	internal bool ExportViewColumn(string schema, string viewName, string colName)
	{
		var filterSettings = GetImportFilterSettings();
		var view = filterSettings.IncludeViews.FirstOrDefault(x => x.Name == viewName || x.Name == $"{schema}.{viewName}");
		return view?.ExcludeColumns.Contains(colName) != true && filterSettings.ExcludedViewColumns.Contains(colName) != true;
	}
	
	internal bool IncludeDependantTables()
	{
		var settings = GetImportFilterSettings();

		return settings.IncludeDependantTables;
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

	internal bool ValidateFilterFile()
	{
        if (string.IsNullOrWhiteSpace(ImportFilterFilePath))
        {
			return true;
        }

        var jsonContent = File.ReadAllText(ImportFilterFilePath);
		var jsonSchema = JsonSchema.FromFile("Resources/filter-file-schema.json");

        var options = new EvaluationOptions
        {
			AddAnnotationForUnknownKeywords = true,
			OutputFormat = OutputFormat.List
        };

        var result = jsonSchema.Evaluate(JsonNode.Parse(jsonContent), options);
        if (!result.IsValid)
        {
			Console.ForegroundColor = ConsoleColor.Red;
            Logging.LogError("The Import Filter File failed schema validation");
			Console.WriteLine("");
            foreach (var detail in result.Details.Where(d => d.HasErrors))
			{
                Console.WriteLine($"Error at path: {detail.EvaluationPath}");
                Console.WriteLine($"Instance location: {detail.InstanceLocation}");
                foreach (var error in detail?.Errors)
                {
                    Console.WriteLine($"  - {error.Key}: {error.Value}");
                }
            }
            Console.WriteLine(".");

			Console.ResetColor();
            return false;
        }

		return true;
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

// IMPORTANT! If anything changes here, you need to update the SQL Server Import Module too!
class ImportFilterSettings
{
	[JsonProperty("schemas")]
	public HashSet<string> Schemas { get; set; } = new();

    [JsonProperty("include_tables")]
	public List<ImportFilterTable> IncludeTables { get; set; } = new();

    [JsonProperty("include_dependant_tables")]
	public bool IncludeDependantTables = false;

    [JsonProperty("include_views")]
	public List<ImportFilterTable> IncludeViews { get; set; } = new();
	
	[JsonProperty("exclude_tables")]
	public List<string> ExcludeTables { get; set; } = new();
	
	[JsonProperty("exclude_views")]
	public List<string> ExcludeViews { get; set; } = new();
	
	[JsonProperty("include_stored_procedures")]
	public List<string> IncludeStoredProcedures { get; set; } = new();
	
	[JsonProperty("exclude_stored_procedures")]
	public List<string> ExcludeStoredProcedures { get; set; } = new();

    [JsonProperty("exclude_table_columns")]
    public HashSet<string> ExcludedTableColumns { get; set; } = new();

    [JsonProperty("exclude_view_columns")]
    public HashSet<string> ExcludedViewColumns { get; set; } = new();
}

class ImportFilterTable
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("exclude_columns")]
	public HashSet<string> ExcludeColumns { get; set; } = new();
}
