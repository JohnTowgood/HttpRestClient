using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HttpRestClient.Crm.Models
{
    [DataContract]
    public class Entity : Dictionary<string, object>
    {
        public Entity() { }

        public Entity(string entityName)
        {
            LogicalName = entityName;
        }

        public Entity(string entityName, Guid id)
        {
            LogicalName = entityName;
            Id = id;
        }

        public Entity(string entityName, AlternateKeys keys)
        {
            LogicalName = entityName;
            AlternateKeys = keys;
        }

        [IgnoreDataMember]
        public AlternateKeys AlternateKeys { get; set; }

        [IgnoreDataMember]
        public string LogicalName { get; set; }

        [IgnoreDataMember]
        public Guid Id {
            get {
                if (!string.IsNullOrEmpty(LogicalName) && ContainsKey($"{LogicalName}id"))
                {
                     return Get<Guid>($"{LogicalName}id");
                }
                else
                {
                    return Guid.Empty;
                }
            }

            set {
                if (!string.IsNullOrEmpty(LogicalName))
                {
                    this[$"{LogicalName}id"] = value;
                }
            }
        }

        public T Get<T>(string field)
        {
            if (ContainsKey(field))
            {
                var obj = this[field];
                if (obj != null)
                {
                    Type returnType = typeof(T);

                    if (returnType == typeof(string))
                    {
                        return (T)Convert.ChangeType(obj.ToString(), returnType, null);
                    }

                    if (returnType == typeof(decimal))
                    {
                        return (T)Convert.ChangeType((decimal)obj, returnType, null);
                    }

                    if (returnType == typeof(Guid))
                    {
                        if (Guid.TryParse(obj.ToString(), out Guid result))
                        {
                            return (T)Convert.ChangeType(result, returnType, null);
                        }
                    }

                    if (returnType == typeof(DateTime))
                    {
                        return (T)Convert.ChangeType(obj, returnType, null);
                    }

                    if (returnType == typeof(int))
                    {
                        return (T)Convert.ChangeType(obj, returnType, null);
                    }

                    if (returnType == typeof(bool))
                    {
                        return (T)Convert.ChangeType(obj, returnType, null);
                    }
                }
            }

            return default(T);
        }

        [IgnoreDataMember]
        public Exceptions Exceptions { get; set; }

        [IgnoreDataMember]
        public bool IsSuccessStatusCode { get; set; }
    }
}
