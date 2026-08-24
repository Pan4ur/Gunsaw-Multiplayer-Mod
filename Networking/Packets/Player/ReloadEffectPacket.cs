internal readonly struct ReloadEffectPacket : INetworkPacket
{
    internal readonly bool Mag;

    internal ReloadEffectPacket(bool val) => Mag = val;

    public PacketType Type => PacketType.ReloadEffect;

    public void Write(ref PacketWriter writer) => writer.WriteBoolean(Mag);

    internal static ReloadEffectPacket Read(ref PacketReader reader) => new(reader.ReadBoolean());
}
