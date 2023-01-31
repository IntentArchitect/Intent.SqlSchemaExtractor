using System;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Intent.SQLSchemaExtractor
{
    public class Program
    {
        public static void Main(string[] args)
        {
            while (true)
            {
                string connectionString;
                if (args.Length < 1)
                {
                    Console.WriteLine("Enter database connection:");
                    connectionString = Console.ReadLine();
                }
                else
                {
                    connectionString = args[0];
                    Console.WriteLine("Connection: " + connectionString);
                }

                try
                {
                    var connection = new SqlConnection(connectionString);
                    connection.Open();
                    Database db = new Server(new ServerConnection(connection)).Databases[connection.Database];


                    string destinationPackage;
                    if (args.Length < 1)
                    {
                        Console.WriteLine("Enter output Intent Package:");
                        destinationPackage = Console.ReadLine();
                    }
                    else
                    {
                        destinationPackage = args[1];
                        Console.WriteLine("Intent Package: " + destinationPackage);
                    }
                    Console.WriteLine("Extracting tables...");
                    var package = new SqlSchemaExtractor(db).BuildPackageModel(destinationPackage, new SchemaExtractorConfiguration()
                    {
                        PackageType = new SpecializationType("Domain Package", "1a824508-4623-45d9-accc-f572091ade5a"),
                        FolderType = new SpecializationType("Folder", "4d95d53a-8855-4f35-aa82-e312643f5c5f"),
                        ClassType = new SpecializationType("Class", "04e12b51-ed12-42a3-9667-a6aa81bb6d10"),
                        AttributeType = new SpecializationType("Attribute", "0090fb93-483e-41af-a11d-5ad2dc796adf"),
                        OnColumnHandlers = new []
                        {
                            RdbmsExtractor.ApplyTextConstraint
                        }
                    });

                    Console.WriteLine("Saving package...");
                    package.Save();
                    Console.WriteLine("Package saved successfully.");
                    Console.WriteLine();

                    Console.WriteLine("Press any key to continue...");
                    Console.WriteLine();
                    Console.ReadKey();
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine("An error occurred: ");
                    Console.WriteLine(e.GetBaseException().Message);
                    Console.WriteLine(e.GetBaseException());
                    Console.WriteLine();

                    Console.WriteLine("Press any key to continue...");
                    Console.WriteLine();
                    Console.ReadKey();
                    args = Array.Empty<string>();
                }
            }
        }
    }
}
