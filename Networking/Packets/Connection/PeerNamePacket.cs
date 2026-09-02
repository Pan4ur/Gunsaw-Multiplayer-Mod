internal readonly struct PeerNamePacket : INetworkPacket
{
    internal readonly ushort PeerId;
    internal readonly string Name;

    internal PeerNamePacket(ushort peerId, string name)
    {
        PeerId = peerId;
        Name = name ?? "";
    }

    public PacketType Type => PacketType.PeerName;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt16(PeerId);
        writer.WriteBinaryString(Name);
    }

    internal static PeerNamePacket Read(ref PacketReader reader) => new PeerNamePacket(reader.ReadUInt16(), reader.ReadBinaryString());
}
