internal readonly struct ReliableAckPacket : INetworkPacket
{
    internal readonly int SequenceId;

    internal ReliableAckPacket(int sequenceId) => SequenceId = sequenceId;

    public PacketType Type => PacketType.ReliableAck;

    public void Write(ref PacketWriter writer) => writer.WriteInt32(SequenceId);

    internal static ReliableAckPacket Read(ref PacketReader reader) => new ReliableAckPacket(reader.ReadInt32());
}
