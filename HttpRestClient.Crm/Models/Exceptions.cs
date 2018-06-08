using System.Runtime.Serialization;

namespace HttpRestClient.Crm.Models
{
    [DataContract]
    public class Exceptions
    {
        [DataMember]
        public Error error { get; set; }
    }

    [DataContract]
    public class Error
    {
        [DataMember]
        public string code { get; set; }

        [DataMember]
        public string message { get; set; }

        [DataMember]
        public Innererror innererror { get; set; }
    }

    [DataContract]
    public class Innererror
    {
        [DataMember]
        public string message { get; set; }

        [DataMember]
        public string type { get; set; }

        [DataMember]
        public string stacktrace { get; set; }
    }
}
