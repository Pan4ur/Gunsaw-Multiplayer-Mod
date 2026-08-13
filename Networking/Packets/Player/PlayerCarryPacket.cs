internal readonly struct PlayerCarryPacket : INetworkPacket
{
    internal readonly bool Carrying;
    internal readonly ushort CarrierId, TargetId;

    internal PlayerCarryPacket(bool carrying, ushort carrierId, ushort targetId)
    {
        Carrying = carrying;
        CarrierId = carrierId;
        TargetId = targetId;
    }

    public PacketType Type => PacketType.PlayerCarry;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteBoolean(Carrying);
        writer.WriteUInt16(CarrierId);
        writer.WriteUInt16(TargetId);
    }

    internal static PlayerCarryPacket Read(ref PacketReader reader) =>
        new(reader.ReadBoolean(), reader.ReadUInt16(), reader.ReadUInt16());
}