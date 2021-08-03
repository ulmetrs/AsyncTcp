using System.IO;
using System.Runtime.CompilerServices;

namespace AsyncTest
{
    public struct Waypoint
    {
        public ushort MatchIndex { get; set; }
        public ushort WaypointIndex { get; set; }
        public bool FlipY { get; set; }
        public float X { get; set; }
        public float Y { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(BinaryWriter writer)
        {
            writer.Write(MatchIndex);
            writer.Write(WaypointIndex);
            writer.Write(FlipY);
            writer.Write(X);
            writer.Write(Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Read(BinaryReader reader)
        {
            MatchIndex = reader.ReadUInt16();
            WaypointIndex = reader.ReadUInt16();
            FlipY = reader.ReadBoolean();
            X = reader.ReadSingle();
            Y = reader.ReadSingle();
        }
    }
}
