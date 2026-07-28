internal readonly struct NpcDamagePacket : INetworkPacket
{
    internal readonly ulong NpcId;
    internal readonly float Amount;
    internal readonly bool Critical;

    internal NpcDamagePacket(ulong npcId, float amount, bool critical)
    {
        NpcId = npcId;
        Amount = amount;
        Critical = critical;
    }

    public PacketType Type => PacketType.NpcDamage;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt64(NpcId);
        writer.WriteSingle(Amount);
        writer.WriteBoolean(Critical);
    }

    internal static NpcDamagePacket Read(ref PacketReader reader)
        => new NpcDamagePacket(reader.ReadUInt64(), reader.ReadSingle(), reader.ReadBoolean());
}