internal readonly struct ObserverEventPacket : INetworkPacket
{
    public PacketType Type => PacketType.Observer;
    public void Write(ref PacketWriter writer) { }
}

internal readonly struct ObserverKillPacket : INetworkPacket
{
    public PacketType Type => PacketType.ObserverKill;
    public void Write(ref PacketWriter writer) { }
}

internal readonly struct ObserverStatePacket : INetworkPacket
{
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly float Rotation;
    internal readonly bool Active;

    internal ObserverStatePacket(float positionX, float positionY, float rotation, bool active)
    {
        PositionX = positionX;
        PositionY = positionY;
        Rotation = rotation;
        Active = active;
    }

    public PacketType Type => PacketType.ObserverState;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(PositionX);
        writer.WriteSingle(PositionY);
        writer.WriteSingle(Rotation);
        writer.WriteBoolean(Active);
    }

    internal static ObserverStatePacket Read(ref PacketReader reader)
        => new ObserverStatePacket(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadBoolean());
}
