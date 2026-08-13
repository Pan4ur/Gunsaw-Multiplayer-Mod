internal readonly struct MissionFinishedPacket : INetworkPacket
{
    public PacketType Type => PacketType.MissionFinished;

    public void Write(ref PacketWriter writer) { }

    internal static MissionFinishedPacket Read(ref PacketReader reader)
        => default(MissionFinishedPacket);
}
