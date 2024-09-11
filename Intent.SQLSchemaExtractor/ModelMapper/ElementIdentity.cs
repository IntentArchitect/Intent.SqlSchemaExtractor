namespace Intent.SQLSchemaExtractor.ModelMapper;

internal class ElementIdentity
{
	public ElementIdentity(string externalReference, string name)
	{
		ExternalReference = externalReference;
		Name = name;
	}

	public string ExternalReference { get; }
	public string Name { get; }
}