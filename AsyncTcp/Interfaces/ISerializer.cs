namespace AsyncTcp
{
    public interface ISerializer
    {
        byte[] Serialize(object obj);
        object Deserialize(int type, byte[] bytes);
    }
}