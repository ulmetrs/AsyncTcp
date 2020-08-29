using System.Threading.Tasks;

namespace AsyncTcp
{
    public interface IPeerHandler
    {
        Task PeerConnected(AsyncPeer peer);
        Task PeerDisconnected(AsyncPeer peer);
        Task MessageReceived(AsyncPeer peer, int type, object message);
    }
}