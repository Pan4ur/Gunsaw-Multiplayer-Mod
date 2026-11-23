namespace GunsawMultiplayer
{
    public interface INetworkPacket
    {
        DeliverySettings Settings { get; }

        void Write(ref PacketWriter writer);
    }
}

internal interface IGamePacket : GunsawMultiplayer.INetworkPacket
{
    PacketType Type { get; }
}
