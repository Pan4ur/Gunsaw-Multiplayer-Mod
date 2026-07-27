internal readonly struct DisconnectPacket : INetworkPacket
{
    internal readonly byte[] Payload;

    internal DisconnectPacket(byte[] payload) => Payload = payload ?? new byte[0];

    public PacketType Type => PacketType.Disconnect;

    public void Write(ref PacketWriter writer) => writer.WriteBytes(Payload);

    internal static DisconnectPacket Read(ref PacketReader reader) => new DisconnectPacket(reader.ReadRemainingBytes());
}
