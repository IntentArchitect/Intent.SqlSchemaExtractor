using System;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Intent.IArchitect.CrossPlatform.IO;
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
                        OnTableHandlers = new []
                        {
                            RdbmsDecorator.ApplyTableDetails
                        },
                        OnViewHandlers = new []
                        {
                            RdbmsDecorator.ApplyViewDetails
                        },
                        OnTableColumnHandlers = new []
                        {
                            RdbmsDecorator.ApplyPrimaryKey,
                            RdbmsDecorator.ApplyColumnDetails,
                            RdbmsDecorator.ApplyTextConstraint,
                            RdbmsDecorator.ApplyDecimalConstraint,
                            RdbmsDecorator.ApplyDefaultConstraint,
                            RdbmsDecorator.ApplyComputedValue
                        },
                        OnViewColumnHandlers = new []
                        {
                            RdbmsDecorator.ApplyColumnDetails,
                            RdbmsDecorator.ApplyTextConstraint,
                            RdbmsDecorator.ApplyDecimalConstraint
                        },
                        OnIndexHandlers = new []
                        {
                            RdbmsDecorator.ApplyIndex
                        }
                    });
                    package.Name = Path.GetFileNameWithoutExtension(package.Name);
                    package.References.Add(new PackageReferenceModel()
                        { PackageId = "870ad967-cbd4-4ea9-b86d-9c3a5d55ea67", Name = "Intent.Common.Types", Module = "Intent.Common.Types", IsExternal = true });
                    package.References.Add(new PackageReferenceModel()
                        { PackageId = "AF8F3810-745C-42A2-93C8-798860DC45B1", Name = "Intent.Metadata.RDBMS", Module = "Intent.Metadata.RDBMS", IsExternal = true });
                    
                    Console.WriteLine("Saving package...");
                    package.Save();
                    
                    Console.WriteLine("Package saved successfully.");
                    Console.WriteLine();

                    // Console.WriteLine("Press any key to continue...");
                    // Console.WriteLine();
                    // Console.ReadKey();
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
