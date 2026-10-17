internal readonly struct EditorCloseReadyPacket : INetworkPacket
{
    internal readonly int Epoch;
    internal readonly Guid Session;
    internal readonly int Transition;
    internal readonly int Revision;
    internal readonly string Checksum;
    internal readonly bool Recovered;

    internal EditorCloseReadyPacket(int epoch, Guid session, int transition, int revision, string checksum, bool recovered)
    {
        Epoch = epoch;
        Session = session;
        Transition = transition;
        Revision = revision;
        Checksum = checksum;
        Recovered = recovered;
    }

    public PacketType Type => PacketType.EditorCloseReady;
    public DeliverySettings Settings => new(true, false, true);

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(Epoch);
        writer.WriteBytes(Session.ToByteArray());
        writer.WriteInt32(Transition);
        writer.WriteInt32(Revision);
        writer.WriteBinaryString(Checksum);
        writer.WriteBoolean(Recovered);
    }

    internal static EditorCloseReadyPacket Read(ref PacketReader reader) => new(EditorPacketData.ReadNonNegative(ref reader),
        EditorPacketData.ReadSession(ref reader), EditorPacketData.ReadNonNegative(ref reader),
        EditorPacketData.ReadNonNegative(ref reader), EditorPacketData.ReadChecksum(ref reader), reader.ReadBoolean());
}