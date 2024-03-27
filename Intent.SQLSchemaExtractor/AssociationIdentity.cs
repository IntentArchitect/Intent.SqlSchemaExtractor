using Intent.IArchitect.Agent.Persistence.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
