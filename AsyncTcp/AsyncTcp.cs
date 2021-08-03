using Microsoft.Extensions.Logging;
using Microsoft.IO;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;

namespace AsyncTcp
{
    public static class AsyncTcp
    {
        // Free to set
        public static int ErrorType { get; set; } = -2;
        public static int KeepAliveType { get; set; } = -1;
        public static int KeepAliveInterval { get; set; } = 10;
        public static StreamPipeReaderOptions ReceivePipeOptions { get; set; } = new StreamPipeReaderOptions();
        public static RecyclableMemoryStreamManager StreamManager { get; set; } = new RecyclableMemoryStreamManager();
        public static byte[] UdpBuffer { get; set; } = new byte[512];

        // Must initialize
        internal static ILogger Logger;

        internal static IPeerHandler PeerHandler;

        internal static bool Initialized;

        private static IDictionary<int, byte[]> _headerBytes;

        public static void Initialize(ILogger logger, IPeerHandler peerHandler)
        {
            Logger = logger;
            PeerHandler = peerHandler;
            Initialized = true;

            _headerBytes = new Dictionary<int, byte[]>();
        }

        public static void Log(long peerId, string message)
        {
            Logger.LogInformation("{" + Thread.CurrentThread.ManagedThreadId + "}:(" + peerId + "):" + message);
        }

        // Lazy Memoize our Zero Size Header Bytes for Sending
        internal static byte[] HeaderBytes(int type)
        {
            if (_headerBytes.TryGetValue(type, out var bytes))
            {
                return bytes;
            }
            _headerBytes[type] = new byte[8];
            BitConverter.GetBytes(type).CopyTo(_headerBytes[type], 0);
            return _headerBytes[type];
        }
    }
}