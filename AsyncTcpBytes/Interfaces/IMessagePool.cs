namespace AsyncTcpBytes
{
    public interface IMessagePool<IMessage>
    {
        IMessage Get();
        void Return(IMessage message);
    }
}