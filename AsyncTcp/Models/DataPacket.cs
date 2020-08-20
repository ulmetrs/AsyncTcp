using System.Buffers;

namespace AsyncTcp
{
    public class DataPacket
    {
        public int Type { get; set; }
        public int Length { get; set; }
        public bool Compressed { get; set; }
        public ReadOnlySequence<byte> Data { get; set; }
    }
}