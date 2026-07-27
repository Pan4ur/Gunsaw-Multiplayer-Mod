internal readonly struct WorldPayloadPacket : INetworkPacket
{
    internal readonly PacketType PacketType; internal readonly byte[] Payload;

    internal WorldPayloadPacket(PacketType packetType, byte[] payload) { PacketType = packetType; Payload = payload ?? new byte[0]; }

    public PacketType Type => PacketType;

    public void Write(ref PacketWriter writer) => writer.WriteBytes(Payload);

    internal static WorldPayloadPacket Read(PacketType type, ref PacketReader reader) => new WorldPayloadPacket(type, reader.ReadRemainingBytes());
}
