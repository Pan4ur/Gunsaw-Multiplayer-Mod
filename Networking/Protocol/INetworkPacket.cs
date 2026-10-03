internal interface INetworkPacket
{
    PacketType Type { get; }
    
    DeliverySettings Settings { get; }

    void Write(ref PacketWriter writer);
}
