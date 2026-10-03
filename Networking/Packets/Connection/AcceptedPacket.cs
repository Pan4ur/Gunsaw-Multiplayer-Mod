internal readonly struct AcceptedPacket : INetworkPacket
{
    internal readonly string PlayerName;

    internal AcceptedPacket(string playerName) => PlayerName = playerName ?? "";

    public PacketType Type => PacketType.Accepted;
    
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer) => writer.WriteUtf8(PlayerName);

    internal static AcceptedPacket Read(ref PacketReader reader) => new AcceptedPacket(reader.ReadRemainingUtf8());
}