using System;
using System.IO;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public interface IPeerHandler
    {
        Task PeerConnected(AsyncPeer peer);
        Task PeerDisconnected(AsyncPeer peer);

        Task HandleUDPPacket(AsyncPeer peer, ArraySegment<byte> buffer);

        Task UnpackMessage(AsyncPeer peer, int type, Stream unpackFromStream);
        Task PackMessage(AsyncPeer peer, int type, object payload, Stream packToStream);
        Task DisposeMessage(AsyncPeer peer, int type, object payload);
    }
}