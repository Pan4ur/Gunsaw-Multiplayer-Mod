internal readonly struct NpcImpulsePacket : INetworkPacket
{
    internal readonly ulong NpcId;
    internal readonly ulong RigidBodyId;
    internal readonly float ForceX;
    internal readonly float ForceY;
    internal readonly float LocalPointX;
    internal readonly float LocalPointY;

    internal NpcImpulsePacket(ulong npcId, ulong rigidBodyId, float forceX, float forceY, float localPointX, float localPointY)
    {
        NpcId = npcId;
        RigidBodyId = rigidBodyId;
        ForceX = forceX;
        ForceY = forceY;
        LocalPointX = localPointX;
        LocalPointY = localPointY;
    }

    public PacketType Type => PacketType.NpcImpulse;
    
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt64(NpcId);
        writer.WriteUInt64(RigidBodyId);
        writer.WriteSingle(ForceX);
        writer.WriteSingle(ForceY);
        writer.WriteSingle(LocalPointX);
        writer.WriteSingle(LocalPointY);
    }

    internal static NpcImpulsePacket Read(ref PacketReader reader) => new (reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}