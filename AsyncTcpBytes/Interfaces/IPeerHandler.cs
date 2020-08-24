using System.IO;
using System.Threading.Tasks;

namespace AsyncTcpBytes
{
    public interface IPeerHandler
    {
        Task PeerConnected(AsyncPeer peer);
        Task PeerDisconnected(AsyncPeer peer);
        Task PackMessage(AsyncPeer peer, Message message, Stream packToStream);
        Task DisposeMessage(AsyncPeer peer, Message message);
        Task UnpackMessage(AsyncPeer peer, int messageType, Stream unpackFromStream);
    }
}