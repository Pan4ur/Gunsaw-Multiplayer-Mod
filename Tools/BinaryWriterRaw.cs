using System.IO;
using System.Runtime.InteropServices;

internal static class BinaryWriterRaw
{
    [StructLayout(LayoutKind.Explicit)]
    private struct SingleBits
    {
        [FieldOffset(0)]
        internal float Single;

        [FieldOffset(0)]
        internal int Int32;
    }

    internal static void WriteSingle(BinaryWriter writer, float value)
    {
        var bits = new SingleBits { Single = value }.Int32;
        var stream = writer.BaseStream;
        stream.WriteByte((byte)bits);
        stream.WriteByte((byte)(bits >> 8));
        stream.WriteByte((byte)(bits >> 16));
        stream.WriteByte((byte)(bits >> 24));
    }
}
