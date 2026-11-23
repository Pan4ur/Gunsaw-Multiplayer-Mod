internal readonly struct KillScreenEffectPacket : IGamePacket
{
    public PacketType Type => PacketType.KillScreenEffect;
    
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer) { }
}