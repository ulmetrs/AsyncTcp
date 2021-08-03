using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncClient
    {
        private static readonly IPEndPoint _blankEndpoint = new IPEndPoint(IPAddress.Any, 0); // This is just used in type validation on ReceiveFromAsync, this should match any type

        private Socket _udpSocket;
        private AsyncPeer _peer;
        private bool _alive;

        public AsyncClient()
        {
            if (!AsyncTcp.Initialized)
                throw new Exception("AsyncTcp must be initialized before creating a client");
        }

        public async Task Start(IPAddress address, int bindPort = 9050)
        {
            if (_alive)
                throw new Exception("Cannot start, client is running");

            if (address == null)
                throw new Exception("Specify server address to use");

            if (address.AddressFamily != AddressFamily.InterNetwork)
                throw new Exception("Address family must be of type Internetwork for UDP support");

            _udpSocket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            var tcpSocket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await tcpSocket.ConnectAsync(new IPEndPoint(address, bindPort)).ConfigureAwait(false);

            _alive = true;

            var receiveTask = ProcessReceiveUDP();

            try
            {
                _peer = new AsyncPeer(tcpSocket);

                await _peer.Process().ConfigureAwait(false);
            }
            catch
            { }

            ShutDown();

            await receiveTask.ConfigureAwait(false);
        }

        private async Task ProcessReceiveUDP()
        {
            try
            {
                while (_alive)
                {
                    var result = await _udpSocket
                        .ReceiveFromAsync(AsyncTcp.UdpBuffer, SocketFlags.None, _blankEndpoint)
                        .ConfigureAwait(false);

                    await AsyncTcp.PeerHandler
                        .HandleUDPPacket(_peer, new ArraySegment<byte>(AsyncTcp.UdpBuffer, 0, result.ReceivedBytes))
                        .ConfigureAwait(false);
                }
            }
            catch { }
        }

        public async Task SendUDPPacket(ArraySegment<byte> buffer)
        {
            try
            {
                await _udpSocket
                    .SendToAsync(buffer, SocketFlags.None, _peer.EndPoint)
                    .ConfigureAwait(false);
            }
            catch { }
        }

        public Task Send(int type, object payload = null)
        {
            return _peer.Send(type, payload);
        }

        public void ShutDown()
        {
            _alive = false;

            try { _udpSocket.Dispose(); } catch { }
            _peer.ShutDown();
        }
    }
}