using System;
using System.Collections.Generic;
using System.Linq;
using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using Microsoft.SqlServer.Management.Dmf;

namespace Intent.SQLSchemaExtractor;

public static class PersistableExtensions
{
    public static StereotypePersistable GetOrCreateStereotype(this ElementPersistable element, 
        string stereotypeDefinitionId,
        Action<StereotypePersistable> initAction = null)
    {
        element.Stereotypes ??= new List<StereotypePersistable>();
        
        var stereotype = element.Stereotypes.SingleOrDefault(x => x.DefinitionId == stereotypeDefinitionId);
        if (stereotype == null)
        {
            if (initAction == null)
            {
                throw new InvalidOperandException($"Stereotype Definition Id [{stereotypeDefinitionId}] cannot be found and cannot be created without {nameof(initAction)} being specified");
            }
            stereotype = new StereotypePersistable
            {
                DefinitionId = stereotypeDefinitionId
            };
            initAction(stereotype);
            element.Stereotypes.Add(stereotype);
        }

        return stereotype;
    }
    
    public static StereotypePersistable GetOrCreateStereotype(this AssociationEndPersistable element, 
        string stereotypeDefinitionId,
        Action<StereotypePersistable> initAction = null)
    {
        element.Stereotypes ??= new List<StereotypePersistable>();
        
        var stereotype = element.Stereotypes.SingleOrDefault(x => x.DefinitionId == stereotypeDefinitionId);
        if (stereotype == null)
        {
            if (initAction == null)
            {
                throw new InvalidOperandException($"Stereotype Definition Id [{stereotypeDefinitionId}] cannot be found and cannot be created without {nameof(initAction)} being specified");
            }
            stereotype = new StereotypePersistable
            {
                DefinitionId = stereotypeDefinitionId
            };
            initAction(stereotype);
            element.Stereotypes.Add(stereotype);
        }

        return stereotype;
    }

    public static StereotypePropertyPersistable GetOrCreateProperty(this StereotypePersistable stereotype, 
        string propertyId, 
        Action<StereotypePropertyPersistable> initAction = null)
    {
        stereotype.Properties ??= new List<StereotypePropertyPersistable>();

        var property = stereotype.Properties.SingleOrDefault(p => p.Id == propertyId);
        if (property == null)
        {
            if (initAction == null)
            {
                throw new InvalidOperationException($"Stereotype Definition Id [{stereotype.DefinitionId}] cannot create a property with Id [{propertyId}] without {nameof(initAction)} being specified");
            }
            property = new StereotypePropertyPersistable
            {
                Id = propertyId,
                IsActive = true
            };
            initAction(property);
            stereotype.Properties.Add(property);
        }

        return property;
    }
}