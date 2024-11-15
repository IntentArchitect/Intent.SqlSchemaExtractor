using System;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.Management.Smo;

namespace Intent.SQLSchemaExtractor.Extractors;

internal static class StoredProcExtractor
{
    public static StoredProcedureResultSet GetStoredProcedureResultSet(Database database, StoredProcedure storedProc)
    {
        DataTable dataTable;
        try
        {
            var describeResults = database.ExecuteWithResults(
                $"""
                 EXEC sp_describe_first_result_set 
                     @tsql = N'EXEC [{storedProc.Schema}].[{storedProc.Name}]',
                     @params = N'',
                     @browse_information_mode = 1
                 """);
            
            dataTable = describeResults.Tables.OfType<DataTable>().First();
        }
        catch (Exception ex)
        {
            Logging.LogWarning($"Failed to get stored procedure results for procedure {storedProc.Name}: {ex.Message}");
            return new StoredProcedureResultSet();
        }

        var keyGroupedSourceRows = dataTable.Rows.Cast<DataRow>()
            .GroupBy(BuildKey)
            .ToArray();

        var tableIdLookup = keyGroupedSourceRows.SelectMany(group =>
            {
                var tableIdResults = database.ExecuteWithResults($@"SELECT OBJECT_ID('{group.Key}') AS TableID;");
                if (tableIdResults.Tables.Count != 1)
                {
                    Logging.LogWarning($"'{group.Key}' returned more than one table: {string.Join(",", tableIdResults.Tables.Cast<Table>().Select(s => s.Name))}");
                    return null!;
                }

                var tableIdObject = tableIdResults.Tables[0].Rows[0]["TableID"];
                var tableId = tableIdObject is not DBNull
                    ? (int?)tableIdObject
                    : null;
                return group.Select(row => new { DataRow = row, TableId = tableId });
            })
            .Where(entry => entry is not null)
            .ToDictionary(key => key.DataRow, value => value.TableId);

        return new StoredProcedureResultSet(
            tableCount: keyGroupedSourceRows.Length,
            tableIds: keyGroupedSourceRows.Select(group => tableIdLookup[group.First()]).Where(p => p is not null).Cast<int>().ToArray(),
            columns: dataTable.Rows.Cast<DataRow>().Select(row => new ResultSetColumn(row, tableIdLookup[row])).ToArray());

        static string BuildKey(DataRow key)
        {
            var sb = new StringBuilder();
            var db = Key(key, "source_database");
            if (db is not null)
            {
                sb.Append(db);
            }

            var schema = Key(key, "source_schema");
            if (schema is not null)
            {
                if (sb.Length > 0)
                {
                    sb.Append('.');
                }

                sb.Append(schema);
            }

            var table = Key(key, "source_table");
            if (table is not null)
            {
                if (sb.Length > 0)
                {
                    sb.Append('.');
                }

                sb.Append(table);
            }

            return sb.ToString();
        }

        static string? Key(DataRow row, string key)
        {
            return row.Table.Columns.Contains(key) ? row[key].ToString() : null;
        }
    }
}

internal class StoredProcedureResultSet
{
    public StoredProcedureResultSet()
    {
        TableCount = 0;
        TableIds = [];
        Columns = [];
    }

    public StoredProcedureResultSet(int tableCount, int[] tableIds, ResultSetColumn[] columns)
    {
        TableCount = tableCount;
        TableIds = tableIds;
        Columns = columns;
    }

    public int TableCount { get; private set; }
    public int[] TableIds { get; private set; }
    public ResultSetColumn[] Columns { get; private set; }
}

internal class ResultSetColumn
{
    public ResultSetColumn(DataRow row, int? sourceTableId)
    {
        Ordinal = row.Field<int>("column_ordinal");
        Name = row.Field<string>("name")!;
        IsNullable = row.Field<bool>("is_nullable");
        SystemTypeId = row.Field<int>("system_type_id");
        SystemTypeName = row.Field<string>("system_type_name")!;
        SqlDataType = DataType.SqlToEnum(Sanitize(SystemTypeName));
        SourceTableId = sourceTableId;
    }

    public int Ordinal { get; private set; }
    public string Name { get; private set; }
    public bool IsNullable { get; private set; }
    public int SystemTypeId { get; private set; }
    public string SystemTypeName { get; private set; }
    public SqlDataType SqlDataType { get; private set; }
    public int? SourceTableId { get; private set; }


    private static readonly Regex SanitizeRegex = new Regex(@"(\([^\)]+\))$", RegexOptions.Compiled); 
    private static string Sanitize(string systemTypeName)
    {
        return SanitizeRegex.Replace(systemTypeName, string.Empty);
    }
}