using System.Buffers;
using System.Threading.Tasks;

namespace AsyncTcpBytes
{
    public interface IMessage
    {
        // Get the assigned message type
        int GetMessageType();
        // Get the exact buffer size needed to pack the class data, does not include type/length just the clas data
        int GetPackedSize();
        // Pack the class data into the buffer provided starting at the offset (This API signature requires zero allocations, so thats why its used)
        Task Pack(byte[] buffer, int offset);
        // Read bytes from the sequence setting the class data
        Task Unpack(ref ReadOnlySequence<byte> unpackFromBuffer);
        // If set to true we let the client manage returning the message to the message pool
        bool ManualReturn();
    }
}