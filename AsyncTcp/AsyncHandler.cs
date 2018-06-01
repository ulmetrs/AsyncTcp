namespace AsyncTcp
{
    public interface AsyncHandler
    {
        void DataReceived(AsyncPeer peer, DataPacket packet);
        void PeerConnected(AsyncPeer peer);
        void PeerDisconnected(AsyncPeer peer);
    }

    public class DataPacket
    {
        public int dataType;
        public int dataSize;
        public byte[] data;
    }
}