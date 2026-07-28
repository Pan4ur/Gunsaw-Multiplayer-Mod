internal readonly struct VelvetWebPacket : INetworkPacket
{
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly float DirectionX;
    internal readonly float DirectionY;

    internal VelvetWebPacket(float positionX, float positionY, float directionX, float directionY)
    {
        PositionX = positionX;
        PositionY = positionY;
        DirectionX = directionX;
        DirectionY = directionY;
    }

    public PacketType Type => PacketType.VelvetWeb;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(PositionX);
        writer.WriteSingle(PositionY);
        writer.WriteSingle(DirectionX);
        writer.WriteSingle(DirectionY);
    }

    internal static VelvetWebPacket Read(ref PacketReader reader)
        => new VelvetWebPacket(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
