using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HttpRestClient.Crm.Models
{
    [DataContract]
    public class Action : Dictionary<string, object>
    {
        public Action() { }

        public Action(string actionName)
        {
            ActionName = actionName;
        }

        [IgnoreDataMember]
        public string ActionName { get; set; }


    }
}
