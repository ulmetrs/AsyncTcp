namespace AsyncTcp
{
    public class BytePacket
    {
        public int Type { get; set; }
        public bool Compressed { get; set; }
        public byte[] Bytes { get; set; }
    }
}