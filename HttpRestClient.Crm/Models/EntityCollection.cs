using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HttpRestClient.Crm.Models
{
    [DataContract]
    public class EntityCollection
    {
        [DataMember(Name = "value")]
        public List<Entity> Entities { get; set; }

        [IgnoreDataMember]
        public Exceptions Exceptions { get; set; }

        [IgnoreDataMember]
        public bool IsSuccessStatusCode { get; set; }
    }
    
}
