internal readonly struct PvpDamagePacket : INetworkPacket
{
    internal readonly float Amount;
    internal readonly bool Critical;
    internal readonly PlayerDamageEffect Effect;

    internal PvpDamagePacket(float amount, bool critical, PlayerDamageEffect effect = PlayerDamageEffect.Damage)
    {
        Amount = amount;
        Critical = critical;
        Effect = effect;
    }

    public PacketType Type => PacketType.PvpDamage;
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(Amount);
        writer.WriteBoolean(Critical);
        if (Effect != PlayerDamageEffect.Damage) writer.WriteByte((byte)Effect);
    }

    internal static PvpDamagePacket Read(ref PacketReader reader)
    {
        var packet = PlayerDamagePacket.Read(ref reader);
        return new PvpDamagePacket(packet.Amount, packet.Critical, packet.Effect);
    }
}