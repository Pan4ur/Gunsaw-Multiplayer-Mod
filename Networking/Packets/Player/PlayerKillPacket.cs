internal readonly struct PlayerKillPacket : INetworkPacket
{
    internal readonly ushort KillerId;

    internal PlayerKillPacket(ushort killerId)
    {
        KillerId = killerId;
    }

    public PacketType Type => PacketType.PlayerKill;
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt16(KillerId);
    }

    internal static PlayerKillPacket Read(ref PacketReader reader) => new(reader.ReadUInt16());
}