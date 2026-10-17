internal readonly struct EditorCommitPacket : INetworkPacket
{
    internal readonly int Epoch;
    internal readonly Guid Session;
    internal readonly int Revision;
    internal readonly int Request;
    internal readonly ushort Author;
    internal readonly bool Locked;
    internal readonly string Checksum;
    internal readonly Level? Environment;
    internal readonly EditorObjectChange[] Changes;

    internal EditorCommitPacket(int epoch, Guid session, int revision, int request, ushort author, bool locked, string checksum, Level? environment, EditorObjectChange[] changes)
    {
        Epoch = epoch;
        Session = session;
        Revision = revision;
        Request = request;
        Author = author;
        Locked = locked;
        Checksum = checksum;
        Environment = environment;
        Changes = changes;
    }

    public PacketType Type => PacketType.EditorCommit;
    
    public DeliverySettings Settings => new(true, false, false, true);

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(Epoch);
        writer.WriteBytes(Session.ToByteArray());
        writer.WriteInt32(Revision);
        writer.WriteInt32(Request);
        writer.WriteUInt16(Author);
        writer.WriteBoolean(Locked);
        writer.WriteBinaryString(Checksum);
        EditorPacketData.WriteOptionalEnvironment(ref writer, Environment);
        EditorPacketData.WriteChanges(ref writer, Changes);
    }

    internal static EditorCommitPacket Read(ref PacketReader reader)
    {
        var epoch = EditorPacketData.ReadNonNegative(ref reader);
        var session = EditorPacketData.ReadSession(ref reader);
        var revision = EditorPacketData.ReadNonNegative(ref reader);
        var request = EditorPacketData.ReadNonNegative(ref reader);
        var author = reader.ReadUInt16();
        var locked = reader.ReadBoolean();
        var checksum = EditorPacketData.ReadChecksum(ref reader);
        var environment = EditorPacketData.ReadOptionalEnvironment(ref reader);
        var changes = EditorPacketData.ReadChanges(ref reader);
        
        if (revision == 0 || changes.Any(change => change.Version != revision))
            throw new InvalidDataException("Invalid editor commit version.");
        
        return new EditorCommitPacket(epoch, session, revision, request, author, locked, checksum, environment, changes);
    }
}