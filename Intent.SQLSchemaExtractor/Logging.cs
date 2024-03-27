using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Intent.SQLSchemaExtractor
{
	internal class Logging
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
}
