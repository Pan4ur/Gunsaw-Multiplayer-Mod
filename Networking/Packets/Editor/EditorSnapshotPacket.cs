internal readonly struct EditorSnapshotPacket : INetworkPacket
{
    internal readonly int Epoch;
    internal readonly Guid Session;
    internal readonly int Revision;
    internal readonly int Request;
    internal readonly bool Locked;
    internal readonly string Checksum;
    internal readonly Level Environment;
    internal readonly EditorObjectChange[] Changes;

    internal EditorSnapshotPacket(int epoch, Guid session, int revision, int request, bool locked, string checksum, Level environment, EditorObjectChange[] changes)
    {
        Epoch = epoch;
        Session = session;
        Revision = revision;
        Request = request;
        Locked = locked;
        Checksum = checksum;
        Environment = environment;
        Changes = changes;
    }

    public PacketType Type => PacketType.EditorSnapshot;
    
    public DeliverySettings Settings => new(true, false, false, compress: true);

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(Epoch);
        writer.WriteBytes(Session.ToByteArray());
        writer.WriteInt32(Revision);
        writer.WriteInt32(Request);
        writer.WriteBoolean(Locked);
        writer.WriteBinaryString(Checksum);
        EditorPacketData.WriteEnvironment(ref writer, Environment);
        EditorPacketData.WriteChanges(ref writer, Changes);
    }

    internal static EditorSnapshotPacket Read(ref PacketReader reader)
    {
        var epoch = EditorPacketData.ReadNonNegative(ref reader);
        var session = EditorPacketData.ReadSession(ref reader);
        var revision = EditorPacketData.ReadNonNegative(ref reader);
        var request = EditorPacketData.ReadNonNegative(ref reader);
        var locked = reader.ReadBoolean();
        var checksum = EditorPacketData.ReadChecksum(ref reader);
        var environment = EditorPacketData.ReadEnvironment(ref reader);
        var changes = EditorPacketData.ReadChanges(ref reader);
        if (changes.Any(change => change.Part == null || change.Version == 0 || change.Version > revision))
            throw new InvalidDataException("Invalid editor snapshot version.");
        
        return new EditorSnapshotPacket(epoch, session, revision, request, locked, checksum, environment, changes);
    }
}