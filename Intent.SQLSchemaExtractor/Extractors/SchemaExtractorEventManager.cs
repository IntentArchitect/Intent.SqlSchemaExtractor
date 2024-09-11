using System;
using System.Collections.Generic;
using Intent.IArchitect.Agent.Persistence.Model;
using Intent.SQLSchemaExtractor.ModelMapper;
using Microsoft.SqlServer.Management.Smo;
using Index = Microsoft.SqlServer.Management.Smo.Index;

namespace Intent.SQLSchemaExtractor.Extractors;

public class SchemaExtractorEventManager
{
    public IEnumerable<Action<ImportConfiguration, Table, ElementPersistable>> OnTableHandlers { get; set; } =
        new List<Action<ImportConfiguration, Table, ElementPersistable>>();

    public IEnumerable<Action<Column, ElementPersistable>> OnTableColumnHandlers { get; set; } = new List<Action<Column, ElementPersistable>>();

    public IEnumerable<Action<ImportConfiguration, Index, ElementPersistable, DatabaseSchemaToModelMapper>> OnIndexHandlers { get; set; } =
        new List<Action<ImportConfiguration, Index, ElementPersistable, DatabaseSchemaToModelMapper>>();

    public IEnumerable<Action<View, ElementPersistable>> OnViewHandlers { get; set; } = new List<Action<View, ElementPersistable>>();
    public IEnumerable<Action<Column, ElementPersistable>> OnViewColumnHandlers { get; set; } = new List<Action<Column, ElementPersistable>>();
    public IEnumerable<Action<StoredProcedure, ElementPersistable>> OnStoredProcedureHandlers { get; set; } = new List<Action<StoredProcedure, ElementPersistable>>();
}