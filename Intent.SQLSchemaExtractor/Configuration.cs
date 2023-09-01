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
        public Configuration()
        {            
        }
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
