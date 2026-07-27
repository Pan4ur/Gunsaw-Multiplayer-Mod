internal interface INetworkPacket
{
    PacketType Type { get; }

    void Write(ref PacketWriter writer);
}
