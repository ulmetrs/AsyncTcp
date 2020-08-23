using System.Collections.Concurrent;

namespace AsyncTcpBytes
{
    // Example MessagePoolManager, DO NOT USE YOU MUST IMPLEMENT YOUR OWN OR NONE AT ALL
    public class MessagePoolManager : IMessagePoolManager
    {
        private readonly ConcurrentDictionary<int, IMessagePool<IMessage>> _pools;

        public MessagePoolManager()
        {
            _pools = new ConcurrentDictionary<int, IMessagePool<IMessage>>();
        }

        public IMessage Get(int messageType)
        {
            IMessagePool<IMessage> pool;
            switch (messageType)
            {
                case 1:
                    pool = _pools.GetOrAdd(messageType, new MessagePool<IMessage>(GenerateMessageType1));
                    break;
                default:
                    pool = _pools.GetOrAdd(messageType, new MessagePool<IMessage>(GenerateMessageTypeDefault));
                    break;
            }
            return pool.Get();
        }

        private IMessage GenerateMessageType1()
        {
            return new HeaderMessage(1);
        }

        private IMessage GenerateMessageTypeDefault()
        {
            return new HeaderMessage(0);
        }

        public void Return(IMessage message)
        {
            int messageType = message.GetMessageType();
            IMessagePool<IMessage> pool;
            switch (message.GetMessageType())
            {
                case 1:
                    pool = _pools.GetOrAdd(messageType, new MessagePool<IMessage>(GenerateMessageType1));
                    break;
                default:
                    pool = _pools.GetOrAdd(messageType, new MessagePool<IMessage>(GenerateMessageTypeDefault));
                    break;
            }
            pool.Return(message);
        }
    }
}