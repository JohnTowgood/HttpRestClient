using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HttpRestClient.Crm.Models
{
    [DataContract]
    public class Function : Dictionary<string, object>
    {
        public Function() { }

        public Function(string actionName)
        {
            ActionName = actionName;
        }

        [IgnoreDataMember]
        public string ActionName { get; set; }


    }
}
