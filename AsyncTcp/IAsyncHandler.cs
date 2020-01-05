using System.Threading.Tasks;

namespace AsyncTcp
{
    public interface IAsyncHandler
    {
        Task PeerConnected(AsyncPeer peer);
        Task PeerDisconnected(AsyncPeer peer);
        Task DataReceived(AsyncPeer peer, int dataType, byte[] data);
    }
}