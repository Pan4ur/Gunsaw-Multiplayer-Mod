internal readonly struct PongPacket : INetworkPacket
{
    internal readonly long Timestamp;

    internal PongPacket(long timestamp) => Timestamp = timestamp;

    public PacketType Type => PacketType.Pong;

    public void Write(ref PacketWriter writer) => writer.WriteInt64(Timestamp);

    internal static PongPacket Read(ref PacketReader reader) => new PongPacket(reader.ReadInt64());
}
