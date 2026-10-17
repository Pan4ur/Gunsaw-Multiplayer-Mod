using System;

internal readonly struct PacketHeader
{
    internal const int Size = 5;
    private const byte Magic0 = 0x47; // G
    private const byte Magic1 = 0x4D; // M
    private const byte Magic2 = 0x50; // P
    private const byte Magic3 = 0x31; // 1
    private const byte CompressedFlag = 0x80;

    internal PacketType Type { get; }
    internal bool Compressed { get; }

    internal PacketHeader(PacketType type, bool compressed = false)
    {
        Type = type;
        Compressed = compressed;
    }
    
    internal static bool TryRead(byte[] data, int offset, out PacketHeader header)
    {
        if (data != null && offset >= 0 && offset <= data.Length - Size &&
            data[offset] == Magic0 && data[offset + 1] == Magic1 &&
            data[offset + 2] == Magic2 && data[offset + 3] == Magic3)
        {
            var type = data[offset + 4];
            header = new PacketHeader((PacketType)(type & ~CompressedFlag), (type & CompressedFlag) != 0);
            return true;
        }

        header = default;
        return false;
    }

    internal static void Write(ref PacketWriter writer, PacketType type, bool compressed = false)
    {
        writer.WriteByte(Magic0);
        writer.WriteByte(Magic1);
        writer.WriteByte(Magic2);
        writer.WriteByte(Magic3);
        writer.WriteByte((byte)((byte)type | (compressed ? CompressedFlag : 0)));
    }

    internal static void Write(byte[] data, int offset, PacketType type)
    {
        data[offset] = Magic0;
        data[offset + 1] = Magic1;
        data[offset + 2] = Magic2;
        data[offset + 3] = Magic3;
        data[offset + 4] = (byte)type;
    }
}