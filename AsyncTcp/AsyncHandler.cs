using System.Threading.Tasks;

namespace AsyncTcp
{
    public interface AsyncHandler
    {
        Task PeerConnected(AsyncPeer peer);
        Task PeerDisconnected(AsyncPeer peer);
        Task DataReceived(AsyncPeer peer, int dataType, int dataSize, byte[] data);
    }
}