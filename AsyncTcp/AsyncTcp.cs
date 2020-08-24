using Microsoft.IO;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;

namespace AsyncTcp
{
    public static class AsyncTcp
    {
        public static readonly RecyclableMemoryStreamManager StreamManager = new RecyclableMemoryStreamManager();

        internal const int CompressionCuttoff = 860; // 1000, 1500 ???
        internal const int MinReceiveBufferSize = 512;
        internal const int KeepAliveDelay = 1000;
        internal const int KeepAliveInterval = 10;
        internal static AsyncTcpConfig Config;
        internal static bool IsInitialized;

        private static IDictionary<int, byte[]> _headerBytes;
        private static IDictionary<int, ObjectMessage> _headerMessages;

        // Throw Exception on Client and Server if we try to create with initializing
        public static void Initialize(AsyncTcpConfig config)
        {
            if (config.StreamSerializer == null && config.ByteSerializer == null)
                throw new Exception("Must supply either a stream or byte serializer");

            Config = config;

            _headerBytes = new Dictionary<int, byte[]>();
            _headerMessages = new Dictionary<int, ObjectMessage>();
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
            _headerBytes[type] = new byte[9];
            BitConverter.GetBytes(type).CopyTo(_headerBytes[type], 0);
            return _headerBytes[type];
        }

        // Lazy Memoize our Zero Length Header Messages
        public static ObjectMessage HeaderMessages(int type)
        {
            if (_headerMessages.TryGetValue(type, out var data))
            {
                return data;
            }
            _headerMessages[type] = new ObjectMessage() { Type = type };
            return _headerMessages[type];
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