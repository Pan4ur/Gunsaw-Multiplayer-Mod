internal readonly struct HostPingPacket : INetworkPacket
{
    internal readonly ushort PeerId;
    internal readonly ushort PingMs;

    internal HostPingPacket(ushort peerId, ushort pingMs)
    {
        PeerId = peerId;
        PingMs = pingMs;
    }

    public PacketType Type => PacketType.HostPing;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt16(PeerId);
        writer.WriteUInt16(PingMs);
    }

    internal static HostPingPacket Read(ref PacketReader reader)
        => new(reader.ReadUInt16(), reader.ReadUInt16());
}
