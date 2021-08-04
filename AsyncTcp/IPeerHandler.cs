using System;
using System.Buffers;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public interface IPeerHandler
    {
        Task PeerConnected(AsyncPeer peer);
        Task PeerDisconnected(AsyncPeer peer);
        Task ReceiveUnreliable(AsyncPeer peer, ReadOnlyMemory<byte> buffer);
        Task Receive(AsyncPeer peer, int type, ReadOnlySequence<byte> buffer);
    }
}