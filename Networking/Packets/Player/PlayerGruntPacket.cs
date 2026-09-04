internal readonly struct PlayerGruntPacket : INetworkPacket
{
    public PacketType Type => PacketType.PlayerGrunt;

    public void Write(ref PacketWriter writer) { }
}
