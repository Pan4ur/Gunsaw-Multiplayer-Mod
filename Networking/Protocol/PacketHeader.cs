using System;

internal readonly struct PacketHeader
{
    internal const int Size = 5;
    private const byte Magic0 = 0x47; // G
    private const byte Magic1 = 0x4D; // M
    private const byte Magic2 = 0x50; // P
    private const byte Magic3 = 0x31; // 1

    internal PacketType Type { get; }

    internal PacketHeader(PacketType type) => Type = type;

    internal static bool TryRead(byte[] data, out PacketHeader header)
    {
        if (data != null && data.Length >= Size && data[0] == Magic0 && data[1] == Magic1 &&
            data[2] == Magic2 && data[3] == Magic3)
        {
            header = new PacketHeader((PacketType)data[4]);
            return true;
        }

        header = default(PacketHeader);
        return false;
    }

    internal static byte[] Create(PacketType type)
    {
        return new[] { Magic0, Magic1, Magic2, Magic3, (byte)type };
    }

    internal static bool HasType(byte[] data, PacketType type)
    {
        PacketHeader header;
        return TryRead(data, out header) && header.Type == type;
    }
}
