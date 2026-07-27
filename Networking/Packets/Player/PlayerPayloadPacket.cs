internal readonly struct PlayerPayloadPacket : INetworkPacket
{
    internal readonly PacketType PacketType; internal readonly byte[] Payload;

    internal PlayerPayloadPacket(PacketType packetType, byte[] payload) { PacketType = packetType; Payload = payload ?? new byte[0]; }

    public PacketType Type => PacketType;

    public void Write(ref PacketWriter writer) => writer.WriteBytes(Payload);

    internal static PlayerPayloadPacket Read(PacketType type, ref PacketReader reader) => new PlayerPayloadPacket(type, reader.ReadRemainingBytes());
}
