internal readonly struct PlayerTeleportPacket : INetworkPacket
{
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly bool ResetVelocity;

    internal PlayerTeleportPacket(float positionX, float positionY, bool resetVelocity = false)
    {
        PositionX = positionX;
        PositionY = positionY;
        ResetVelocity = resetVelocity;
    }

    public PacketType Type => PacketType.PlayerTeleport;
    
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(PositionX);
        writer.WriteSingle(PositionY);
        writer.WriteBoolean(ResetVelocity);
    }

    internal static PlayerTeleportPacket Read(ref PacketReader reader)
        => new PlayerTeleportPacket(reader.ReadSingle(), reader.ReadSingle(), reader.ReadBoolean());
}