using System.Threading.Tasks;

namespace AsyncTcp
{
    public interface IAsyncHandler
    {
        Task PeerConnected(AsyncPeer peer);
        Task PeerDisconnected(AsyncPeer peer);
        Task MessageReceived(AsyncPeer peer, int type, object message);
    }
}