namespace AsyncTcp
{
    public interface AsyncHandler
    {
        void DataReceived(AsyncPeer peer, int dataType, int dataSize, byte[] data);
        void PeerConnected(AsyncPeer peer);
        void PeerDisconnected(AsyncPeer peer);
    }
}