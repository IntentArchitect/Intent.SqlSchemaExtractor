namespace Intent.SQLSchemaExtractor
{
	internal class AssociationIdentity
	{
		public AssociationIdentity(string externalReference)
		{
			ExternalReference = externalReference;
		}

		public string ExternalReference { get; }
	}
}
