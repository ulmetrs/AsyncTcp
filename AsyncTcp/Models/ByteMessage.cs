using System.Buffers;

namespace AsyncTcp
{
    public class ByteMessage
    {
        public bool ParsedHeader { get; set; }
        public int Type { get; set; }
        public int Length { get; set; }
        public bool Compressed { get; set; }
        public ReadOnlySequence<byte> Bytes { get; set; }
        public int DecompressedLength { get; set; }
    }
}