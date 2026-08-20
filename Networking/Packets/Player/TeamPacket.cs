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

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt16(PlayerId);
        writer.WriteBinaryString(Team);
    }

    internal static TeamPacket Read(ref PacketReader reader) =>
        new TeamPacket(reader.ReadUInt16(), reader.ReadBinaryString());
}