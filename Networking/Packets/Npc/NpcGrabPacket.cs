internal readonly struct NpcGrabPacket : INetworkPacket
{
    internal readonly ulong NpcId;
    internal readonly ulong RigidBodyId;
    internal readonly float PointX;
    internal readonly float PointY;
    internal readonly float LocalPointX;
    internal readonly float LocalPointY;

    internal NpcGrabPacket(ulong npcId, ulong rigidBodyId, float pointX, float pointY,
        float localPointX, float localPointY)
    {
        NpcId = npcId;
        RigidBodyId = rigidBodyId;
        PointX = pointX;
        PointY = pointY;
        LocalPointX = localPointX;
        LocalPointY = localPointY;
    }

    public PacketType Type => PacketType.NpcGrab;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt64(NpcId);
        writer.WriteUInt64(RigidBodyId);
        writer.WriteSingle(PointX);
        writer.WriteSingle(PointY);
        writer.WriteSingle(LocalPointX);
        writer.WriteSingle(LocalPointY);
    }

    internal static NpcGrabPacket Read(ref PacketReader reader) => new NpcGrabPacket(
        reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle());
}