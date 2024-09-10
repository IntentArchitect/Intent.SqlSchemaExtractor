﻿using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using System.Linq;

namespace Intent.SQLSchemaExtractor;

internal static class PackageExtensions
{
	internal static void AddMetadata(this PackageModelPersistable package, string key, string value)
	{
		var existing =  package.Metadata.SingleOrDefault(x => x.Key == key);
		if (existing != null)
		{
			existing.Value = value;
		}
		else
		{
			package.Metadata.Add(new GenericMetadataPersistable { Key = key, Value = value });
		}
	}

	internal static void RemoveMetadata(this PackageModelPersistable package, string key)
	{
		if (package.Metadata.Any(x => x.Key == key))
		{
			package.Metadata.Remove(package.Metadata.Single(x => x.Key == key));
		}
	}
}