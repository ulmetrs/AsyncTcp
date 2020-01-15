using System.IO;

namespace AsyncTcp
{
    public interface ISerializer
    {
        byte[] Serialize(object data);
        //void Serialize(object data, StreamWriter writer);
        T Deserialize<T>(byte[] data);
        //T Deserialize<T>(StreamReader reader);
    }
}