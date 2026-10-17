internal readonly struct EditorClosePacket : INetworkPacket
{
    internal readonly int Epoch;
    internal readonly Guid Session;
    internal readonly int Transition;
    internal readonly bool Closing;

    internal EditorClosePacket(int epoch, Guid session, int transition, bool closing)
    {
        Epoch = epoch;
        Session = session;
        Transition = transition;
        Closing = closing;
    }

    public PacketType Type => PacketType.EditorClose;
    public DeliverySettings Settings => new(true, false, true);

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(Epoch);
        writer.WriteBytes(Session.ToByteArray());
        writer.WriteInt32(Transition);
        writer.WriteBoolean(Closing);
    }

    internal static EditorClosePacket Read(ref PacketReader reader) => new(EditorPacketData.ReadNonNegative(ref reader),
        EditorPacketData.ReadSession(ref reader), EditorPacketData.ReadNonNegative(ref reader), reader.ReadBoolean());
}