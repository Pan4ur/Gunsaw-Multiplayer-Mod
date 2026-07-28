internal readonly struct ReliablePacket : INetworkPacket
{
    internal readonly int SequenceId;
    internal readonly byte[] InnerPacket;

    internal ReliablePacket(int sequenceId, byte[] innerPacket)
    {
        SequenceId = sequenceId;
        InnerPacket = innerPacket ?? new byte[0];
    }

    public PacketType Type => PacketType.Reliable;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(SequenceId);
        writer.WriteBytes(InnerPacket);
    }

    internal static ReliablePacket Read(ref PacketReader reader) => new ReliablePacket(reader.ReadInt32(), reader.ReadRemainingBytes());
}
