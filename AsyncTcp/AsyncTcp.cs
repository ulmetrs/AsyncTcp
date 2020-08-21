using Microsoft.IO;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;

namespace AsyncTcp
{
    public static class AsyncTcp
    {
        internal const int BoolSize = 1;
        internal const int IntSize = 4;

        internal const int ZeroOffset = 0;
        internal const int TypeOffset = 0;
        internal const int LengthOffset = 4;
        internal const int CompressedOffset = 8;
        internal const int HeaderSize = 9;

        internal const int CompressionCuttoff = 860; // 1000, 1500 ???
        internal const int MinReceiveBufferSize = 512;
        internal const int KeepAliveDelay = 1000;
        internal const int KeepAliveInterval = 10;

        internal static readonly RecyclableMemoryStreamManager StreamManager = new RecyclableMemoryStreamManager();
        internal static AsyncTcpConfig Config;
        internal static bool IsInitialized;
        private static IDictionary<int, byte[]> _headerBytes;

        // Throw Exception on Client and Server if we try to create with initializing
        public static void Initialize(AsyncTcpConfig config)
        {
            if (config.StreamSerializer == null && config.ByteSerializer == null)
                throw new Exception("Must supply either a stream or byte serializer");

            Config = config;

            _headerBytes = new Dictionary<int, byte[]>();
            HeaderBytes(Config.KeepAliveType);
            HeaderBytes(Config.ErrorType);

            IsInitialized = true;
        }

        // Lazy Memoize our Zero Length Headers
        public static byte[] HeaderBytes(int type)
        {
            if (_headerBytes.TryGetValue(type, out var bytes))
            {
                return bytes;
            }
            _headerBytes[type] = new byte[HeaderSize];
            BitConverter.GetBytes(type).CopyTo(_headerBytes[type], ZeroOffset);
            return _headerBytes[type];
        }
    }

    public class AsyncTcpConfig
    {
        public IStreamSerializer StreamSerializer { get; set; }
        public IByteSerializer ByteSerializer { get; set; }
        public int KeepAliveType { get; set; } = -1;
        public int ErrorType { get; set; } = -2;
        public bool UseCompression { get; set; } = true;
        // There are many pipe options we can play with
        //var options = new PipeOptions(pauseWriterThreshold: 10, resumeWriterThreshold: 5);
        public PipeOptions PipeOptions { get; set; }
    }
}