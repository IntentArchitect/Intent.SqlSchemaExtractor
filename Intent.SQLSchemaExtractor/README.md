# Intent SQL Schema Extractor

The Intent SQL Schema Extractor CLI tool can be used to export SQL Server database schema's into an Intent Architect Domain package.

This tool can be useful for creating Intent Architect Domain Packages based on existing databases definitions.

## Pre-requisites

Latest Long Term Support (LTS) version of [.NET](https://dotnet.microsoft.com/download).

## Installation

The tool is available as a [.NET Tool](https://docs.microsoft.com/dotnet/core/tools/global-tools) and can be installed with the following command:

```powershell
dotnet tool install Intent.SQLSchemaExtractor --global
```

> [!NOTE]
> If `dotnet tool install` fails with an error to the effect of `The required NuGet feed can't be accessed, perhaps because of an Internet connection problem.` and it shows a private NuGet feed URL, you can try add the `--ignore-failed-sources` command line option ([source](https://learn.microsoft.com/dotnet/core/tools/troubleshoot-usage-issues#nuget-feed-cant-be-accessed)).

You should see output to the effect of:

```text
You can invoke the tool using the following command: intent-sqlschema-extractor
Tool 'intent-sqlschema-extractor' (version 'x.x.x') was successfully installed.
```
