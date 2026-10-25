internal readonly struct NpcDamagePacket : INetworkPacket
{
    internal readonly ulong NpcId;
    internal readonly float Amount;
    internal readonly bool Critical;
    internal readonly bool IsRozaShot;

    internal NpcDamagePacket(ulong npcId, float amount, bool critical, bool isRozaShot = false)
    {
        NpcId = npcId;
        Amount = amount;
        Critical = critical;
        IsRozaShot = isRozaShot;
    }

    public PacketType Type => PacketType.NpcDamage;
    
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt64(NpcId);
        writer.WriteSingle(Amount);
        writer.WriteBoolean(Critical);
        if (IsRozaShot) writer.WriteBoolean(IsRozaShot);
    }

    internal static NpcDamagePacket Read(ref PacketReader reader)
        => new NpcDamagePacket(reader.ReadUInt64(), reader.ReadSingle(), reader.ReadBoolean(), reader.Remaining > 0 && reader.ReadBoolean());
}