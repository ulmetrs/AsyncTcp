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
        private bool _serverRunning;

        public string HostName { get; private set; }

        public async Task Start(IPAddress address = null, int bindPort = 9050)
        {
            if (_serverRunning)
                throw new Exception("Cannot Start, Server is running");

            try
            {
                if (address == null)
                {
                    address = await Utils.GetIPAddress().ConfigureAwait(false);
                }

                _listener = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _listener.NoDelay = true;
                _listener.Bind(new IPEndPoint(address, bindPort));
                _listener.Listen(100);

                HostName = address.ToString();
            }
            catch
            {
                throw;
            }

            await LogMessageAsync(string.Format(HostnameMessage, HostName, address, bindPort), false).ConfigureAwait(false);

            _serverRunning = true;

            Socket socket;
            try
            {
                while (true)
                {
                    socket = await _listener.AcceptAsync().ConfigureAwait(false);
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                    await Task.Delay(AsyncTcp.KeepAliveDelay).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                await LogErrorAsync(e, "Accepted Loop Exception", true).ConfigureAwait(false);
            }

            await ShutDown().ConfigureAwait(false);

            await LogMessageAsync(string.Format("Finished AsyncHealth Task"), false).ConfigureAwait(false);
        }

        public Task ShutDown()
        {
            _serverRunning = false;

            // If we never connect listener.Shutdown throws an error, so try separately
            try { _listener.Shutdown(SocketShutdown.Both); } catch { }
            try { _listener.Close(); } catch { }

            return Task.CompletedTask;
        }
    }
}