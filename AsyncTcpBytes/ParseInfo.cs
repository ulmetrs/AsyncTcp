using System.Buffers;

namespace AsyncTcpBytes
{
    public class ParseInfo
    {
        public bool HeaderParsed { get; set; }
        public int Type { get; set; }
        public int Size { get; set; }
        public ReadOnlySequence<byte> Buffer { get; set; }
    }
}