internal readonly struct NpcPayloadPacket : INetworkPacket
{
    internal readonly PacketType PacketType; internal readonly byte[] Payload;

    internal NpcPayloadPacket(PacketType packetType, byte[] payload) { PacketType = packetType; Payload = payload ?? new byte[0]; }

    public PacketType Type => PacketType;

    public void Write(ref PacketWriter writer) => writer.WriteBytes(Payload);

    internal static NpcPayloadPacket Read(PacketType type, ref PacketReader reader) => new NpcPayloadPacket(type, reader.ReadRemainingBytes());
}
