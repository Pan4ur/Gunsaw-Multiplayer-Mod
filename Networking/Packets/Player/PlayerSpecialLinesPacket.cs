internal readonly struct PlayerSpecialLinesPacket : INetworkPacket
{
    internal readonly PlayerSnapshotLineState Levitator, CrystalTongue;

    internal PlayerSpecialLinesPacket(PlayerSnapshotLineState levitator, PlayerSnapshotLineState crystalTongue)
    {
        Levitator = levitator;
        CrystalTongue = crystalTongue;
    }

    public PacketType Type => PacketType.PlayerSpecialLines;

    public void Write(ref PacketWriter writer)
    {
        PlayerSnapshotPacket.WriteLine(ref writer, Levitator);
        PlayerSnapshotPacket.WriteLine(ref writer, CrystalTongue);
    }

    internal static PlayerSpecialLinesPacket Read(ref PacketReader reader) => new(PlayerSnapshotPacket.ReadLine(ref reader), PlayerSnapshotPacket.ReadLine(ref reader));
}