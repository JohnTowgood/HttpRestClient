using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace HttpRestClient.Crm.Models
{
    public class EntityReference
    {
        public EntityReference() { }

        public EntityReference(string entityName, Guid id)
        {
            LogicalName = entityName;
            Id = id;
        }

        [IgnoreDataMember]
        public string LogicalName { get; set; }

        [IgnoreDataMember]
        public Guid Id { get; set; }

    }
}
