using Microsoft.SqlServer.Management.Smo;
using System.Collections.Generic;

namespace Intent.SQLSchemaExtractor.Schema
{
	public class IndexSchema
	{
		public IndexKeyType IndexKeyType { get; internal set; }
		public IEnumerable<IndexColumnSchema> IndexedColumns { get; internal set; }
		public bool IsClustered { get; internal set; }
		public bool IsUnique { get; internal set; }
	}
}