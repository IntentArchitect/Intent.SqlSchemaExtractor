using Microsoft.SqlServer.Management.Smo;
using System.Collections.Generic;

namespace Intent.SQLSchemaExtractor.Schema
{
	public class ForeignKeySchema
	{
		public IList<ForeignKeyColumnSchema> Columns { get; internal set; }
		public string ID { get; internal set; }
	}
}