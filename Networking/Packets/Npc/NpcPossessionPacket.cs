internal readonly struct NpcPossessionPacket : INetworkPacket
{
    internal readonly ulong NpcId;

    internal NpcPossessionPacket(ulong npcId) => NpcId = npcId;

    public PacketType Type => PacketType.NpcPossession;

    public void Write(ref PacketWriter writer) => writer.WriteUInt64(NpcId);

    internal static NpcPossessionPacket Read(ref PacketReader reader)
        => new NpcPossessionPacket(reader.ReadUInt64());
}