using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Intent.SQLSchemaExtractor
{
    public class Configuration
    {
        public EntityNameConvention EntityNameConvention { get; set; } = EntityNameConvention.SingularEntity;
        public TableStereotypes TableStereotypes { get; set; } = TableStereotypes.WhenDifferent;

        public HashSet<ExportTypes> TypesToExport { get; set; } = new HashSet<ExportTypes> { ExportTypes.Table, ExportTypes.View, ExportTypes.StoredProcedure };

        public HashSet<string> SchemaFilter { get; set; } = new HashSet<string>();

        public Configuration()
        {            
        }

        internal bool ExportSchema(string schema)
        {
            if (!SchemaFilter.Any()) return true;

            return SchemaFilter.Contains(schema);
        }

        internal bool ExportTables()
        {
            return TypesToExport.Contains(ExportTypes.Table);
        }

        internal bool ExportViews()
        {
            return TypesToExport.Contains(ExportTypes.View);
        }

        internal bool ExportStoredProcedures()
        {
            return TypesToExport.Contains(ExportTypes.StoredProcedure);
        }
    }

    public enum ExportTypes
    {
        Table,
        View,
        StoredProcedure
    }

    public enum TableStereotypes
    {
        Always,
        WhenDifferent,
    }

    public enum EntityNameConvention
    {
        MatchTable,
        SingularEntity,
    }
}
