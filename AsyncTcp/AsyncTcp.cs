using Microsoft.IO;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;

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

        // Must initialize

        internal static IPeerHandler PeerHandler;

        internal static bool Initialized;

        private static IDictionary<int, byte[]> _headerBytes;

        public static void Initialize(IPeerHandler peerHandler)
        {
            PeerHandler = peerHandler;
            Initialized = true;

            _headerBytes = new Dictionary<int, byte[]>();
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