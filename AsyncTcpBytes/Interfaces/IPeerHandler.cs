using System.Buffers;
using System.Threading.Tasks;

namespace AsyncTcpBytes
{
    public interface IPeerHandler
    {
        Task PeerConnected(AsyncPeer peer);
        Task PeerDisconnected(AsyncPeer peer);
        Task MessageReceived(AsyncPeer peer, IMessage message);
    }
}