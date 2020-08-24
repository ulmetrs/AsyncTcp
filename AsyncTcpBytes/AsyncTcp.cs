using Microsoft.IO;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;

namespace AsyncTcpBytes
{
    public static class AsyncTcp
    {
        public static readonly RecyclableMemoryStreamManager StreamManager = new RecyclableMemoryStreamManager();

        internal const int KeepAliveDelay = 1000;
        internal static IPeerHandler PeerHandler;
        internal static int KeepAliveType;
        internal static int ErrorType;
        internal static int KeepAliveInterval;
        internal static int ReceiveBufferSize;
        internal static PipeOptions ReceivePipeOptions;

        private static IDictionary<int, byte[]> _headerBytes;
        private static IDictionary<int, Message> _headerMessages;

        internal static bool IsConfigured;

        // Throw Exception on Client and Server if we try to create with initializing
        public static void Configure(Config config)
        {
            if (config == null || config.PeerHandler == null)
                throw new Exception("Must supply a Peer Handler and a Message Pool Manager");

            PeerHandler = config.PeerHandler;
            ErrorType = config.ErrorType;
            KeepAliveType = config.KeepAliveType;
            KeepAliveInterval = config.KeepAliveInterval;
            ReceiveBufferSize = config.ReceiveBufferSize;
            ReceivePipeOptions = config.ReceivePipeOptions;

            _headerBytes = new Dictionary<int, byte[]>();
            _headerMessages = new Dictionary<int, Message>();

            IsConfigured = true;
        }

        // Lazy Memoize our Zero Size Header Bytes for Sending
        public static byte[] HeaderBytes(int type)
        {
            if (_headerBytes.TryGetValue(type, out var bytes))
            {
                return bytes;
            }
            _headerBytes[type] = new byte[8];
            BitConverter.GetBytes(type).CopyTo(_headerBytes[type], 0);
            return _headerBytes[type];
        }

        // Lazy Memoize our Zero Size Header Messages for Receiving
        public static Message HeaderMessage(int type)
        {
            if (_headerMessages.TryGetValue(type, out var message))
            {
                return message;
            }
            _headerMessages[type] = new Message() { MessageType = type, HeaderOnly = true };
            return _headerMessages[type];
        }
    }

    public class Config
    {
        public IPeerHandler PeerHandler { get; set; } // Supply Callback Handler
        public int ErrorType { get; set; } = -2;
        public int KeepAliveType { get; set; } = -1;
        public int KeepAliveInterval { get; set; } = 10000;
        public int ReceiveBufferSize { get; set; } = 512;
        public PipeOptions ReceivePipeOptions { get; set; } = PipeOptions.Default;
    }
}