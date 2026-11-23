internal readonly struct MissionFinishedPacket : IGamePacket
{
    public PacketType Type => PacketType.MissionFinished;
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer) { }

    internal static MissionFinishedPacket Read(ref PacketReader reader) => default(MissionFinishedPacket);
}