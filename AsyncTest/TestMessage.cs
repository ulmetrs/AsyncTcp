using AsyncTcpBytes;

namespace AsyncTest
{
    public class TestMessage : Message
    {
        public int index { get; set; }
        public byte[] data { get; set; }
    }
}