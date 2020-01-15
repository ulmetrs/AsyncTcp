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

        internal static ISerializer Serializer;
        internal static bool UseCompression;
        internal static bool IsInitialized;
        
        private static IDictionary<int, byte[]> _headerBytes;

        // Throw Exception on Client and Server if we try to create with initializing
        public static void Initialize(
            ISerializer serializer,
            bool useCompression = true)
        {
            Serializer = serializer;
            UseCompression = useCompression;

            _headerBytes = new Dictionary<int, byte[]>();
            _headerBytes[0] = new byte[8];

            IsInitialized = true;

            /*
             _methods = new Dictionary<int, MethodInfo>();
            // Use reflection to get the deserialize method info of the provided serializer
            var method = serializer.GetType().GetMethod("Deserialize");

            foreach (var kv in typeMap)
            {
                if (kv.Key == 0)
                    throw new Exception("Type '0' Reserved for Keep-Alive Packet");

                if (kv.Value != null)
                    _methods[kv.Key] = method.MakeGenericMethod(new Type[] { kv.Value });

                var bytes = new byte[HeaderSize];
                BitConverter.GetBytes(kv.Key).CopyTo(bytes, 0);
                HeaderBytes[kv.Key] = bytes;
            }
            */
        }

        /*
        public static byte[] Serialize(object data)
        {
            return _serializer.Serialize(data);
        }

        public static object Deserialize(int type, byte[] bytes)
        {
            //return _methods[type].Invoke(_serializer, new object[] { bytes });
            return _serializer.Deserialize(type, bytes);
        }
        */

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