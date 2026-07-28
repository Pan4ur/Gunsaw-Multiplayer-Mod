internal readonly struct PlayerTeleportPacket : INetworkPacket
{
    internal readonly float PositionX;
    internal readonly float PositionY;

    internal PlayerTeleportPacket(float positionX, float positionY)
    {
        PositionX = positionX;
        PositionY = positionY;
    }

    public PacketType Type => PacketType.PlayerTeleport;

    public void Write(ref PacketWriter writer) { writer.WriteSingle(PositionX); writer.WriteSingle(PositionY); }

    internal static PlayerTeleportPacket Read(ref PacketReader reader)
        => new PlayerTeleportPacket(reader.ReadSingle(), reader.ReadSingle());
}
