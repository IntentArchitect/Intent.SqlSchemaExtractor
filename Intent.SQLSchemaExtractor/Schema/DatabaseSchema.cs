using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Intent.SQLSchemaExtractor.Schema
{
	public class DatabaseSchema
	{
		public IEnumerable<TableSchema> Tables { get; internal set; }
		public IEnumerable<StoredProcedureSchema> StoredProcedures { get; internal set; }
		public IEnumerable<ViewSchema> Views { get; internal set; }

		internal DataSet ExecuteWithResults(string v)
		{
			throw new NotImplementedException();
		}
	}
}


