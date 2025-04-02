using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Intent.SQLSchemaExtractor.Models;
public class ExtractStatistics
{
    public int TableCount { get; set; } = 0;

    public int ForeignKeyCount { get; set; } = 0;

    public int IndexCount { get; set; } = 0;

    public int ViewCount { get; set; } = 0;

    public int StoredProcedureCount { get; set; } = 0;
}
