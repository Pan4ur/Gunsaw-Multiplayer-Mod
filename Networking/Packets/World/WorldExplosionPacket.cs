internal readonly struct WorldExplosionPacket : INetworkPacket
{
    internal readonly ulong SourceId;
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly float Range;
    internal readonly float Force;
    internal readonly ushort ShooterId;
    internal readonly bool PlaySound;

    internal WorldExplosionPacket(ulong sourceId, float positionX, float positionY, float range, float force, ushort shooterId, bool playSound)
    {
        SourceId = sourceId;
        PositionX = positionX;
        PositionY = positionY;
        Range = range;
        Force = force;
        ShooterId = shooterId;
        PlaySound = playSound;
    }

    public PacketType Type => PacketType.WorldExplosion;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt64(SourceId);
        writer.WriteSingle(PositionX);
        writer.WriteSingle(PositionY);
        writer.WriteSingle(Range);
        writer.WriteSingle(Force);
        writer.WriteUInt16(ShooterId);
        writer.WriteBoolean(PlaySound);
    }

    internal static WorldExplosionPacket Read(ref PacketReader reader)
        => new(reader.ReadUInt64(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadUInt16(), reader.ReadBoolean());
}