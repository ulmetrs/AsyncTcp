using System.IO;

namespace AsyncTcp
{
    public interface IStreamSerializer
    {
        void Serialize(Stream outputStream, object val);
        object Deserialize(int type, Stream inputStream);
    }
}