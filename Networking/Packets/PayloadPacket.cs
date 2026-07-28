internal readonly struct PayloadPacket : INetworkPacket
{
    internal readonly PacketType PacketType;
    internal readonly byte[] Payload;

    internal PayloadPacket(PacketType packetType, byte[] payload)
    {
        PacketType = packetType;
        Payload = payload ?? new byte[0];
    }

    public PacketType Type => PacketType;

    public void Write(ref PacketWriter writer) => writer.WriteBytes(Payload);

    internal static PayloadPacket Read(PacketType type, ref PacketReader reader)
        => new PayloadPacket(type, reader.ReadRemainingBytes());
}
