internal readonly struct PlayerCarryPacket : INetworkPacket
{
    internal readonly bool Carrying;
    internal readonly ushort CarrierId, TargetId;
    internal readonly bool HasReleaseVelocity;
    internal readonly float ReleaseVelocityX, ReleaseVelocityY;

    internal PlayerCarryPacket(bool carrying, ushort carrierId, ushort targetId, float releaseVelocityX = 0f, float releaseVelocityY = 0f, bool hasReleaseVelocity = false)
    {
        Carrying = carrying;
        CarrierId = carrierId;
        TargetId = targetId;
        HasReleaseVelocity = hasReleaseVelocity;
        ReleaseVelocityX = releaseVelocityX;
        ReleaseVelocityY = releaseVelocityY;
    }

    public PacketType Type => PacketType.PlayerCarry;
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteBoolean(Carrying);
        writer.WriteUInt16(CarrierId);
        writer.WriteUInt16(TargetId);
        writer.WriteBoolean(HasReleaseVelocity);
        if (HasReleaseVelocity)
        {
            writer.WriteSingle(ReleaseVelocityX);
            writer.WriteSingle(ReleaseVelocityY);
        }
    }

    internal static PlayerCarryPacket Read(ref PacketReader reader)
    {
        var carrying = reader.ReadBoolean();
        var carrier = reader.ReadUInt16();
        var target = reader.ReadUInt16();
        var hasVelocity = reader.ReadBoolean();
        var velocityX = hasVelocity ? reader.ReadSingle() : 0f;
        var velocityY = hasVelocity ? reader.ReadSingle() : 0f;
        if (float.IsNaN(velocityX) || float.IsInfinity(velocityX) || float.IsNaN(velocityY) || float.IsInfinity(velocityY))
            throw new InvalidDataException("Invalid carry release velocity.");
        return new PlayerCarryPacket(carrying, carrier, target, velocityX, velocityY, hasVelocity);
    }
}