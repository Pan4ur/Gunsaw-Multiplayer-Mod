internal readonly struct TeamPacket : INetworkPacket
{
    internal readonly ushort PlayerId;
    internal readonly string Team;

    internal TeamPacket(ushort playerId, string team)
    {
        PlayerId = playerId;
        Team = team ?? "";
    }

    public PacketType Type => PacketType.Team;
    
    public DeliverySettings Settings => DeliverySettings.Default;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt16(PlayerId);
        writer.WriteBinaryString(Team);
    }

    internal static TeamPacket Read(ref PacketReader reader) => new (reader.ReadUInt16(), reader.ReadBinaryString());
}