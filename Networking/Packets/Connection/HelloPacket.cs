internal readonly struct HelloPacket : INetworkPacket
{
    internal readonly string PlayerName;

    internal HelloPacket(string playerName) => PlayerName = playerName ?? "";

    public PacketType Type => PacketType.Hello;

    public void Write(ref PacketWriter writer) => writer.WriteUtf8(PlayerName);

    internal static HelloPacket Read(ref PacketReader reader) => new HelloPacket(reader.ReadRemainingUtf8());
}
