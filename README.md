# Sample code for extracting a SQL Server schema into an Intent Architect package

This repository contains a sample C# application which connects to a SQL Server, analyzes its schema and then generates a package containing with the extracted "domain" including classes, their attributes and their associations to each other.

The intent of this working sample code is to give a starting point for developers wanting to write a program to import data into Intent Architect.

NOTE: While this code has been found to work against a variety of schemas already of other Intent Architect users, it has not been exhaustively tested or checked to ensure it covers all SQL Server schemas. Please create a pull request with desired changes or [log an issue](https://github.com/IntentArchitect/Support/issues).

## Pre-requisites

- [.NET SDK](https://dotnet.microsoft.com/download/visual-studio-sdks) (6.0 or greater)
- [Connection string](https://www.connectionstrings.com/sql-server/) for your SQL Server database.
- [Intent Architect](https://intentarchitect.com/#/downloads) (4.0.0 or greater)

## Running the code

### Have an output package created in Intent Architect

The extractor will need the path of an Intent Architect package as the output in which to place extracted Intent metadata based on the database schema.

If you don't already have an Intent Architect application, create one which includes the domain designer. The quickest way to do this is to use the `Clean Architecture .NET` [application template](https://docs.intentarchitect.com/articles/application-templates/about-application-templates/about-application-templates.html).

For the application into which you want the metadata to be extracted, open the Domain designer, click in the `Location` text box, press `Ctrl+A` to select all the text and then `Ctrl+C` to copy the path to your clipboard.

![Screenshot of Intent Architect showing the package location selected](./images/package-path.png)

#### Use Git (or similar SCM)

We advise this Intent Architect application and package exists in a Git (or similar SCM) repository and ensuring any changes for it have been so that the working directory is clean before doing the extraction. This will allow reverting of any changes in the event they're undesirable or you wish to re-run it again "from scratch" after making changes to the extractor's source code.

### Run the tool

From inside the `./Intent.SQLSchemaExtractor` folder use the `dotnet run` command (or alternatively open the `.sln` with your IDE and start it).

### Options

|Option                                   |Description|
|-----------------------------------------|-----------|
|`--config-file <config-file>`            |Path to a [JSON formatted file](#configuration-file) containing options to use for execution of this tool. The `--generate-config-file` option can be used to generate a file with all the possible fields populated with null.|
|`--generate-config-file`                 |Scaffolds into the current working directory a "config.json" for use with the `--config-file` option.|
|`--connection-string <source-json-file>` |(optional)a [SQL server connection string](https://www.connectionstrings.com/sql-server/) for the database you want to connect to and from which the schema will be extracted. Note this will override the equivelant setting in the config file if it is specified there.|
|`--package-file-name <source-json-file>` |(optional)The path and name to the where you would like the Domain package created / updated. Note this will override the equivelant setting in the config file if it is specified there.|
|`--version`                              |Show version information|
|`-?`, `-h`, `--help`                     |Show help and usage information|

### Configuration file

The `--config-file` option expects the name of a file containing configuration options to be used as an alternative to adding them as CLI options. A template for the configuration file can be generated using the `--generate-config-file` option. The content of the generated template is as follows:

```json
{
  "EntityNameConvention": "SingularEntity",
  "TableStereotypes": "WhenDifferent",
  "TypesToExport": [
    "Table",
    "View",
    "StoredProcedure"
  ],
  "SchemaFilter": [],
  "ConnectionString": "Server=.;Initial Catalog=Test;Integrated Security=true;MultipleActiveResultSets=True;Encrypt=False;",
  "PackageFileName": "bob"
}
```

|JSon Setting                             |Type|Description|
|-----------------------------------------|-----------|
|EntityNameConvention                     |string  |The options are : `SingularEntity`(default),`MatchTable`. `SingularEntity` creates the domain entities as the singularization of the table name. `MatchTable` imports the entity with the same name as the Sql Table. |
|TableStereotypes                         |string  |The options are : `WhenDifferent`(default),`Always`. `WhenDifferent` only applies `Table` stereotypes, if the SQL table name does not match the plural version of the `Entity`. This is in line with default Intent Architect behviours. `Always` table stereotypes are applied to all entities.|
|TypesToExport                            |string[]|List of SQL Types to export. The valid options are `Table`, `View`, `StoredProcedure`. e.g. ["Table"] to only export tables. |
|SchemaFilter                             |string[]|List of schema's to export. If the list is empty, all schemas are exported. e.g. ["dbo", "admin"]|
|ConnectionString                         |string  |A [SQL server connection string](https://www.connectionstrings.com/sql-server/) for the database you want to connect to and from which the schema will be extracted.|
|PackageFileName                          |string  |The path and name to the where you would like the Domain package created / updated.|

As the program executes it will write to the console each _class_ and _relationship_ it creates and should finally say `Package saved successfully`.
