using System.Runtime.Serialization;

namespace AsyncTcpBytes
{
    public class Message
    {
        [IgnoreDataMember]
        public int MessageType { get; set; }
        [IgnoreDataMember]
        public bool HeaderOnly { get; set; }
    }
}