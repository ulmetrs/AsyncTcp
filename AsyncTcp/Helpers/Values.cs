namespace AsyncTcp
{
    public static class Values
    {
        public const int ByteOffsetSize = 8;
        public static byte[] KABytes { get; } = new byte[8];

        // String Error Messages
        public const string ParseReceiveErrorMessage = "Data Received Error: {0}";
        public const string SendErrorMessage = "Send Error: {0}";
        public const string PeerRemovedErrorMessage = "Peer Disconnected Error: {0}";
        public const string PeerConnectedErrorMessage = "Peer Connected Error: {0}";

        // String Messages
        public const string HostnameMessage = "\tHostname : {0}\tIP : {1}\tPort : {2}";

        // Tracers
        public const string SimpleTracerMessageFormatter = "{0}: {1}";
        public const string SimpleErrorTracerFormatter = "{0}:\nError: {1}\n\n{2}\n\n";
        public const string SimpleErrorTracerMessageFormatter = "{0}: {1}\n\nError: {2}\n\n{3}\n\n";
    }
}
