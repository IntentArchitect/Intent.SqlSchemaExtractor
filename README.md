# Sample code for extracting a SQL Server schema into an Intent Architect package

This repository contains a sample C# application which connects to a SQL Server, analyzes its schema and then generates a package containing with the extracted "domain" including classes, their attributes and their associations to each other.

The intent of this working sample code is to give a starting point for developers wanting to write a program to import data into Intent Architect.

NOTE: While this code has been found to work against a test schema of ours, it is intended merely as a sample so may not work for all scenarios.

## Pre-requisites

Ensure you have at least .NET Core 3.1 installed.

## Running the code

From inside the `./Intent.SQLSchemaExtractor` folder use the `dotnet run` command.

When prompted to `Enter database connection:`, enter a [SQL server connection string](https://www.connectionstrings.com/sql-server/) for the database you want to connect to and from which the schema will be extracted and press return.

When prompted to `Enter output Intent Package:` enter a name for the Intent Architect package, such as `MyDomain` and press enter.

The program will log each _class_ and _relationship_ it creates and should finally say `Package saved successfully`.

## Using the generated package in Intent Architect

If you don't already have an Intent Architect application, create one which includes the domain designer. The quickest way to do this is to use the `Clean Architecture .NET Core 3.1` [application template](https://intentarchitect.com/docs/articles/references/application-templates/application-templates.html).

- Click on the `Domain` designer option in the pane on the left.
- Click the `Add existing package...` button in the top toolbar.
- Browse to the `./Intent.SQLSchemaExtractor/bin/Debug/netcoreapp3.1/Packages` folder within this checked out repository.
- Select the `.pkg.config` file (EG: `MyDomain.pkg.config`).

The package will now be loaded, but when you expand any of the classes you will see that all of them have a type of `<not found>`, to fix this:

- Right-click on the `References` node.
- Click the `Add package reference...` option.
- Check the `Intent.Common.Types` package.
- Click `OK`.

The types should now be correct.
