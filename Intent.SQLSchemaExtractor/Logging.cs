using System;

namespace Intent.SQLSchemaExtractor;

internal static class Logging
{
	public static void LogWarning(string message)
	{
		Console.WriteLine("Warning: " + message);
	}

	public static void LogError(string message)
	{
		Console.WriteLine("Error: " + message);
	}
}