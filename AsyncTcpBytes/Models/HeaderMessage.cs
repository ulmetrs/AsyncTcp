using System.Buffers;
using System.Threading.Tasks;

namespace AsyncTcpBytes
{
    public class HeaderMessage : IMessage
    {
        private int _messageType;
        public HeaderMessage(int messageType)
        {
            _messageType = messageType;
        }
        public int GetMessageType()
        {
            return _messageType;
        }
        public int GetPackedSize()
        {
            return 0;
        }
        public Task Pack(byte[] buffer, int offset)
        {
            return Task.CompletedTask;
        }
        public Task Unpack(ref ReadOnlySequence<byte> unpackFromBuffer)
        {
            return Task.CompletedTask;
        }
    }
}