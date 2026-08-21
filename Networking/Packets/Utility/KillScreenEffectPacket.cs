internal readonly struct KillScreenEffectPacket : INetworkPacket
{
    public PacketType Type => PacketType.KillScreenEffect;

    public void Write(ref PacketWriter writer) { }
}
