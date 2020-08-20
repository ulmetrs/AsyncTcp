namespace AsyncTcp
{
    public interface IByteSerializer
    {
        byte[] Serialize(object obj);
        object Deserialize(int type, byte[] bytes);
    }
}