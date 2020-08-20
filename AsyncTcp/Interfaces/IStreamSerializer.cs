using System.IO;

namespace AsyncTcp
{
    public interface IStreamSerializer
    {
        void Serialize(object obj, Stream stream);
        object Deserialize(int type, Stream stream);
    }
}