namespace AsyncTcp
{
    public interface ISerializer
    {
        byte[] Serialize(int type, object obj);
        object Deserialize(int type, int size, byte[] bytes);
    }
}