using System.Threading.Tasks;

namespace AsyncTcp
{
    public interface IPeerHandler
    {
        Task PeerConnected(AsyncPeer peer);
        Task PeerDisconnected(AsyncPeer peer);
        Task<byte[]> GetMessageBytes(AsyncPeer peer, int type, object obj);
        Task HandleMessage(AsyncPeer peer, int type, int size, byte[] bytes);
    }
}