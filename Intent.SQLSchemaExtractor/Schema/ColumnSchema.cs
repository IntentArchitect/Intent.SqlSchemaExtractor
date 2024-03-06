namespace Intent.SQLSchemaExtractor.Schema
{
	public class ColumnSchema
	{
		public string Name { get; internal set; }
		public bool Nullable { get; internal set; }
		public object ID { get; internal set; }
		public bool InPrimaryKey { get; internal set; }
		public object Identity { get; internal set; }
		public DataType DataType { get; internal set; }
		public DefaultConstraint DefaultConstraint { get; internal set; }
	}

	public class DefaultConstraint
	{ 
		public string Text { get; set; }
	}

	public class DataType
	{ 
		public int MaxLength { get; set; }
		public Microsoft.SqlServer.Management.Smo.SqlDataType SqlDataType { get; internal set; }
		public object NumericPrecision { get; internal set; }
		public object NumericScale { get; internal set; }
	}
}