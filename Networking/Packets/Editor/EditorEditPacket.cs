internal readonly struct EditorEditPacket : INetworkPacket
{
    internal readonly int Epoch;
    internal readonly Guid Session;
    internal readonly int BaseRevision;
    internal readonly int Request;
    internal readonly Level? Environment;
    internal readonly EditorObjectChange[] Changes;

    internal EditorEditPacket(int epoch, Guid session, int baseRevision, int request, Level? environment, EditorObjectChange[] changes)
    {
        Epoch = epoch;
        Session = session;
        BaseRevision = baseRevision;
        Request = request;
        Environment = environment;
        Changes = changes;
    }

    public PacketType Type => PacketType.EditorEdit;
    public DeliverySettings Settings => new(true, false, false, compress: true);

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(Epoch);
        writer.WriteBytes(Session.ToByteArray());
        writer.WriteInt32(BaseRevision);
        writer.WriteInt32(Request);
        EditorPacketData.WriteOptionalEnvironment(ref writer, Environment);
        EditorPacketData.WriteChanges(ref writer, Changes);
    }

    internal static EditorEditPacket Read(ref PacketReader reader)
    {
        var epoch = EditorPacketData.ReadNonNegative(ref reader);
        var session = EditorPacketData.ReadSession(ref reader);
        var baseRevision = EditorPacketData.ReadNonNegative(ref reader);
        var request = EditorPacketData.ReadNonNegative(ref reader);
        var environment = EditorPacketData.ReadOptionalEnvironment(ref reader);
        var changes = EditorPacketData.ReadChanges(ref reader);
        if (request == 0) throw new InvalidDataException("Missing editor edit request.");
        return new EditorEditPacket(epoch, session, baseRevision, request, environment, changes);
    }
}