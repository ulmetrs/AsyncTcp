using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
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
        public static byte[] UdpBuffer { get; set; } = new byte[512];

        // Must initialize
        internal static ILogger Logger;

        internal static IPeerHandler PeerHandler;

        internal static bool Initialized;

        private static IDictionary<int, byte[]> _headers;

        private static ConcurrentBag<byte[]> _headerBuffers;

        public static void Initialize(ILogger logger, IPeerHandler peerHandler)
        {
            Logger = logger;
            PeerHandler = peerHandler;
            Initialized = true;

            _headers = new Dictionary<int, byte[]>();
            _headerBuffers = new ConcurrentBag<byte[]>();
        }

        public static void Log(long peerId, string message)
        {
            Logger.LogInformation("{" + Thread.CurrentThread.ManagedThreadId + "}:(" + peerId + "):" + message);
        }

        // Lazy Memoize our Zero Size Header Bytes for Sending
        internal static byte[] Headers(int type)
        {
            if (_headers.TryGetValue(type, out var header))
            {
                return header;
            }
            _headers[type] = new byte[8];
            MemoryMarshal.Write(new Span<byte>(_headers[type], 0, 4), ref type);
            return _headers[type];
        }

        internal static byte[] GetHeaderBuffer()
        {
            if (_headerBuffers.TryTake(out var buffer))
            {
                return buffer;
            }
            return new byte[8];
        }

        internal static void ReturnHeaderBuffer(byte[] buffer)
        {
            _headerBuffers.Add(buffer);
        }
    }
}