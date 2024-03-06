using Microsoft.SqlServer.Management.Smo;
using System.Collections.Generic;

namespace Intent.SQLSchemaExtractor.Schema
{
	public class TableSchema
	{
		internal string Name;

		public string Schema { get; internal set; }
		public string ID { get; internal set; }
		public IEnumerable<IndexSchema> Indexes { get; internal set; }
		public IEnumerable<ForeignKeySchema> ForeignKeys { get; internal set; }
		public IEnumerable<ColumnSchema> Columns { get; internal set; }
	}
}