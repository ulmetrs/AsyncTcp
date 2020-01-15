using System;
using System.Collections.Generic;
using System.Reflection;

namespace AsyncTcp
{
    public static class AsyncTcp
    {
        internal const int ZeroOffset = 0;
        internal const int IntSize = 4;
        internal const int TypeOffset = 0;
        internal const int LengthOffset = 4;
        internal const int HeaderSize = 8;

        internal static IDictionary<int, byte[]> HeaderBytes;
        internal static bool UseCompression;
        internal static bool Initialized;

        private static ISerializer _serializer;
        private static IDictionary<int, MethodInfo> _methods;

        // Throw Exception on Client and Server if we try to create with initializing
        public static void Initialize(
            ISerializer serializer,
            IDictionary<int, Type> typeMap,
            bool useCompression = true)
        {
            _serializer = serializer;
            _methods = new Dictionary<int, MethodInfo>();
            HeaderBytes = new Dictionary<int, byte[]>();
            HeaderBytes[0] = new byte[8];

            // Use reflection to get the deserialize method info of the provided serializer
            var method = serializer.GetType().GetMethod("Deserialize");

            foreach (var kv in typeMap)
            {
                if (kv.Key == 0)
                    throw new Exception("Type '0' Reserved for Keep-Alive Packet");

                _methods[kv.Key] = method.MakeGenericMethod(new Type[] { kv.Value });
                var bytes = new byte[HeaderSize];
                BitConverter.GetBytes(kv.Key).CopyTo(bytes, 0);
                HeaderBytes[kv.Key] = bytes;
            }
            UseCompression = useCompression;
            Initialized = true;
        }

        public static byte[] Serialize(object data)
        {
            return _serializer.Serialize(data);
        }

        public static object Deserialize(int type, byte[] bytes)
        {
            return _methods[type].Invoke(_serializer, new object[] { bytes });
        }
    }
}