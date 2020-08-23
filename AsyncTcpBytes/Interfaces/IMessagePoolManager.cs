namespace AsyncTcpBytes
{
    public interface IMessagePoolManager
    {
        IMessage Get(int messageType);
        void Return(IMessage message);
    }
}