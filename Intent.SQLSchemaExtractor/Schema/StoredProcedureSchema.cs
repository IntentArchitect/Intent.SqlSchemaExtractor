using System.Collections.Generic;

namespace Intent.SQLSchemaExtractor.Schema
{
	public class StoredProcedureSchema
	{
		public string Name { get; internal set; }
		public IList<object> Parameters { get; internal set; }
	}
}