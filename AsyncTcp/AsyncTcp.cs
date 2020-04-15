using System;
using System.Collections.Generic;

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
        internal const int TaskCleanupInterval = 100;

        internal static ISerializer Serializer;
        internal static int KeepAliveType;
        internal static int ErrorType;
        internal static bool UseCompression;

        internal static bool IsInitialized;
        
        private static IDictionary<int, byte[]> _headerBytes;

        // Throw Exception on Client and Server if we try to create with initializing
        public static void Initialize(
            ISerializer serializer,
            int keepAliveType = -1,
            int errorType = -2,
            bool useCompression = true)
        {
            Serializer = serializer;
            KeepAliveType = keepAliveType;
            ErrorType = errorType;
            UseCompression = useCompression;

            _headerBytes = new Dictionary<int, byte[]>();
            HeaderBytes(KeepAliveType);
            HeaderBytes(ErrorType);

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
}