using System;
using System.Collections.Concurrent;

namespace AsyncTcpBytes
{
    // Simple MessagePool implementation, Write your own for setting pool bounds, other features
    public class MessagePool<IMessage> : IMessagePool<IMessage>
    {
        private readonly Func<IMessage> _messageGenerator;
        private readonly ConcurrentBag<IMessage> _messages;

        public MessagePool(Func<IMessage> messageGenerator)
        {
            _messageGenerator = messageGenerator ?? throw new ArgumentNullException(nameof(messageGenerator));
            _messages = new ConcurrentBag<IMessage>();
        }

        public IMessage Get() => _messages.TryTake(out IMessage item) ? item : _messageGenerator();

        public void Return(IMessage message) => _messages.Add(message);
    }
}