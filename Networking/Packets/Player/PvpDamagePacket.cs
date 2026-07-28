internal readonly struct PvpDamagePacket : INetworkPacket
{
    internal readonly float Amount;
    internal readonly bool Critical;

    internal PvpDamagePacket(float amount, bool critical) { Amount = amount; Critical = critical; }

    public PacketType Type => PacketType.PvpDamage;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(Amount);
        writer.WriteBoolean(Critical);
    }

    internal static PvpDamagePacket Read(ref PacketReader reader) => new PvpDamagePacket(reader.ReadSingle(), reader.ReadBoolean());
}