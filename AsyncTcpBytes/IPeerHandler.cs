using System.IO;
using System.Threading.Tasks;

namespace AsyncTcpBytes
{
    public interface IPeerHandler
    {
        Task PeerConnected(AsyncPeer peer);
        Task PeerDisconnected(AsyncPeer peer);
        Task PackMessage(AsyncPeer peer, int type, object payload, Stream packToStream);
        Task DisposeMessage(AsyncPeer peer, int type, object payload);
        Task UnpackMessage(AsyncPeer peer, int type, Stream unpackFromStream);
    }
}