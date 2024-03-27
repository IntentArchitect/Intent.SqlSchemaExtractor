using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Intent.SQLSchemaExtractor
{
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
}
