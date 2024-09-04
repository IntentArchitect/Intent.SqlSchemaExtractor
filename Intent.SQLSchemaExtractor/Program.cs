using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Intent.Modules.Common.Templates;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Intent.SQLSchemaExtractor;

public class Program
{
    private static string GetOptionName(string propertyName) => $"--{propertyName.ToKebabCase()}";

    public static void Main(string[] args)
    {
        //Backwards compatability
        if (args.Length == 2 && !args[0].StartsWith("--"))
        {
            var configFile = new ImportConfiguration
            {
                ConnectionString = args[0],
                PackageFileName = args[1]
            };
            Run(configFile);
            return;
        }

        var rootCommand = new RootCommand(
            "The Intent SQL Schema Extractor tool can be used to create / update ans Intent Architect Domain Package" +
            "  based on a  SQL Schema.")
        {
            new Option<FileInfo>(
                name: GetOptionName(nameof(ImportConfiguration.ConfigFile)),
                description: "Path to a JSON formatted file containing options to use for execution of " +
                             "this tool as an alternative to using command line options. The " +
                             $"{GetOptionName(nameof(ImportConfiguration.GenerateConfigFile))} option can be used to " +
                             "generate a file with all the possible fields populated with null."),
            new Option<bool>(
                name: GetOptionName(nameof(ImportConfiguration.GenerateConfigFile)),
                description: $"Scaffolds into the current working directory a \"config.json\" for use with the " +
                             $"{GetOptionName(nameof(ImportConfiguration.ConfigFile))} option."),
            new Option<string?>(
                name: GetOptionName(nameof(ImportConfiguration.ConnectionString)),
                description: "Connection string for connecting to the database to import the schema from. "),
            new Option<string?>(
                name: GetOptionName(nameof(ImportConfiguration.PackageFileName)),
                description: "The file name of the Intent Domain Package into which to synchronize the metadata."),
            new Option<string?>(
                name: GetOptionName(nameof(ImportConfiguration.SerializedConfig)),
                description: "JSON string representing a serialized configuration file."),
        };

        rootCommand.SetHandler(
            handle: (
                FileInfo? configFile,
                bool generateConfigFile,
                string? connectionString,
                string? packageFileName,
                string? serializedConfig
            ) =>
            {
                try
                {
                    var serializerOptions = new JsonSerializerOptions
                    {
                        Converters = { new JsonStringEnumConverter() },
                        WriteIndented = true
                    };

                    if (generateConfigFile)
                    {
                        var path = Path.Join(Environment.CurrentDirectory, "config.json");
                        Console.WriteLine($"Writing {path}...");
                        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new ImportConfiguration(), serializerOptions));
                        Console.WriteLine("Done.");
                        return;
                    }

                    ImportConfiguration config;
                    if (serializedConfig != null)
                    {
                        config = JsonSerializer.Deserialize<ImportConfiguration>(serializedConfig, serializerOptions)
                                 ?? throw new Exception($"Parsing of serialized-config returned null.");
                    }
                    else if (configFile != null)
                    {
                        config = JsonSerializer.Deserialize<ImportConfiguration>(File.ReadAllText(configFile.FullName), serializerOptions)
                                 ?? throw new Exception($"Parsing of \"{configFile.FullName}\" returned null.");
                    }
                    else
                    {
                        config = new ImportConfiguration();
                    }

                    if (connectionString != null)
                        config.ConnectionString = connectionString;
                    if (packageFileName != null)
                        config.PackageFileName = packageFileName;


                    if (string.IsNullOrEmpty(config.ConnectionString))
                        throw new Exception($"{GetOptionName(nameof(ImportConfiguration.ConnectionString))} is mandatory or a --config-file with a connection string.");

                    if (string.IsNullOrEmpty(config.PackageFileName))
                        throw new Exception($"{GetOptionName(nameof(ImportConfiguration.PackageFileName))} is mandatory or a --config-file with a package file name.");

                    Run(config);
                }
                catch (Exception exception)
                {
                    Logging.LogError($"{exception.Message}");
                    throw;
                }
            },
            symbols: Enumerable.Empty<IValueDescriptor>()
                .Concat(rootCommand.Options)
                .ToArray());

        Console.WriteLine($"{rootCommand.Name} version {Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");

        new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build()
            .Invoke(args);
    }

    public static void Run(ImportConfiguration config)
    {
        var connection = new SqlConnection(config.ConnectionString);
        connection.Open();
        var db = new Server(new ServerConnection(connection)).Databases[connection.Database];


        var package = new SqlSchemaExtractor(config, db).BuildPackageModel(config.PackageFileName!, new SchemaExtractorConfiguration
        {
            OnTableHandlers = new[]
            {
                RdbmsDecorator.ApplyTableDetails
            },
            OnViewHandlers = new[]
            {
                RdbmsDecorator.ApplyViewDetails
            },
            OnTableColumnHandlers = new[]
            {
                RdbmsDecorator.ApplyPrimaryKey,
                RdbmsDecorator.ApplyColumnDetails,
                RdbmsDecorator.ApplyTextConstraint,
                RdbmsDecorator.ApplyDecimalConstraint,
                RdbmsDecorator.ApplyDefaultConstraint,
                RdbmsDecorator.ApplyComputedValue
            },
            OnViewColumnHandlers = new[]
            {
                RdbmsDecorator.ApplyColumnDetails,
                RdbmsDecorator.ApplyTextConstraint,
                RdbmsDecorator.ApplyDecimalConstraint
            },
            OnIndexHandlers = new[]
            {
                RdbmsDecorator.ApplyIndex
            },
            OnStoredProcedureHandlers = new[]
            {
                RdbmsDecorator.ApplyStoredProcedureSettings
            }
        });
        package.Name = Path.GetFileNameWithoutExtension(package.Name);
        package.References.Add(new PackageReferenceModel
        {
            PackageId = "870ad967-cbd4-4ea9-b86d-9c3a5d55ea67",
            Name = "Intent.Common.Types",
            Module = "Intent.Common.Types",
            IsExternal = true
        });
        package.References.Add(new PackageReferenceModel
        {
            PackageId = "AF8F3810-745C-42A2-93C8-798860DC45B1",
            Name = "Intent.Metadata.RDBMS",
            Module = "Intent.Metadata.RDBMS",
            IsExternal = true
        });
        package.References.Add(new PackageReferenceModel
        {
            PackageId = "a9d2a398-04e4-4300-9fbb-768568c65f9e",
            Name = "Intent.EntityFrameworkCore",
            Module = "Intent.EntityFrameworkCore",
            IsExternal = true
        });
        package.References.Add(new PackageReferenceModel
        {
            PackageId = "5869084c-2a08-4e40-a5c9-ff26220470c8",
            Name = "Intent.EntityFrameworkCore.Repositories",
            Module = "Intent.EntityFrameworkCore.Repositories",
            IsExternal = true
        });
        if (config.SettingPersistence != SettingPersistence.None)
        {
            string connectionString = config.ConnectionString!;

            if (config.SettingPersistence == SettingPersistence.AllSanitisedConnectionString)
            {
                var builder = new SqlConnectionStringBuilder();
                builder.ConnectionString = connectionString;

                var addPassword = builder.Remove("Password");

                var sanitisedConnectionString = builder.ConnectionString;
                if (addPassword)
                {
                    sanitisedConnectionString = "Password=  ;" + sanitisedConnectionString;
                }

                connectionString = sanitisedConnectionString;
            }

            if (config.SettingPersistence == SettingPersistence.AllWithoutConnectionString)
            {
                package.RemoveMetadata("sql-import:connectionString");
            }
            else
            {
                package.AddMetadata("sql-import:connectionString", connectionString);
            }

            package.AddMetadata("sql-import:tableStereotypes", config.TableStereotypes.ToString());
            package.AddMetadata("sql-import:entityNameConvention", config.EntityNameConvention.ToString());
            package.AddMetadata("sql-import:schemaFilter", config.SchemaFilter.Any() ? string.Join(";", config.SchemaFilter) : "");
            package.AddMetadata("sql-import:tableViewFilterFilePath", config.TableViewFilterFilePath);
            package.AddMetadata("sql-import:typesToExport", config.TypesToExport.Any() ? string.Join(";", config.TypesToExport.Select(t => t.ToString())) : "");
            package.AddMetadata("sql-import:settingPersistence", config.SettingPersistence.ToString());
        }
        else
        {
            package.RemoveMetadata("sql-import:connectionString");
            package.RemoveMetadata("sql-import:tableStereotypes");
            package.RemoveMetadata("sql-import:entityNameConvention");
            package.RemoveMetadata("sql-import:schemaFilter");
            package.RemoveMetadata("sql-import:tableViewFilterFilePath");
            package.RemoveMetadata("sql-import:typesToExport");
            package.RemoveMetadata("sql-import:settingPersistence");
        }

        Console.WriteLine("Saving package...");
        package.Save();

        Console.WriteLine("Package saved successfully.");
        Console.WriteLine();
    }
}