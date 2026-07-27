internal readonly struct PingPacket : INetworkPacket
{
    internal readonly long Timestamp;

    internal PingPacket(long timestamp) => Timestamp = timestamp;

    public PacketType Type => PacketType.Ping;

    public void Write(ref PacketWriter writer) => writer.WriteInt64(Timestamp);

    internal static PingPacket Read(ref PacketReader reader) => new PingPacket(reader.ReadInt64());
}
