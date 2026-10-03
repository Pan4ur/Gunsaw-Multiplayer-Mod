internal readonly struct PayloadPacket
{
    internal readonly PacketType PacketType;
    internal readonly ArraySegment<byte> Payload;

    internal PayloadPacket(PacketType type, ArraySegment<byte> payload)
    {
        PacketType = type;
        Payload = payload;
    }

    public PacketType Type => PacketType;
}