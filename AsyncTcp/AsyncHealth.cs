using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using static AsyncTcp.Logging;
using static AsyncTcp.Values;

namespace AsyncTcp
{
    // Health Check Server that just accepts connections and closes them
    // AWS Network LB Requires this behaviour to register targets
    public class AsyncHealth
    {
        private Socket _listener;

        public async Task Start(IPAddress address = null, int bindPort = 9050)
        {
            if (address == null)
            {
                address = await Utils.GetIPAddress().ConfigureAwait(false);
            }

            _listener = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _listener.NoDelay = true;
            _listener.Bind(new IPEndPoint(address, bindPort));
            _listener.Listen(100);

            await LogMessageAsync(string.Format(HostnameMessage, address.ToString(), address, bindPort), true).ConfigureAwait(false);

            try
            {
                Socket socket;
                while ((socket = await _listener.AcceptAsync().ConfigureAwait(false)) != null)
                {
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                await LogErrorAsync(e, "Accepted Loop Exception", true).ConfigureAwait(false);
            }

            ShutDown();
        }

        public void ShutDown()
        {
            try
            {
                _listener.Shutdown(SocketShutdown.Both);
                _listener.Close();
            }
            catch
            { }
        }
    }
}