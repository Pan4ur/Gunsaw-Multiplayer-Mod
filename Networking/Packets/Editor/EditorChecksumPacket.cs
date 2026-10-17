internal readonly struct EditorChecksumPacket : INetworkPacket
{
    internal readonly int Epoch;
    internal readonly Guid Session;
    internal readonly int Revision;
    internal readonly string Checksum;

    internal EditorChecksumPacket(int epoch, Guid session, int revision, string checksum)
    {
        Epoch = epoch;
        Session = session;
        Revision = revision;
        Checksum = checksum;
    }

    public PacketType Type => PacketType.EditorChecksum;
    
    public DeliverySettings Settings => new(true, false, false);

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(Epoch);
        writer.WriteBytes(Session.ToByteArray());
        writer.WriteInt32(Revision);
        writer.WriteBinaryString(Checksum);
    }

    internal static EditorChecksumPacket Read(ref PacketReader reader)
    {
        var epoch = EditorPacketData.ReadNonNegative(ref reader);
        var session = EditorPacketData.ReadSession(ref reader);
        var revision = EditorPacketData.ReadNonNegative(ref reader);
        var checksum = EditorPacketData.ReadChecksum(ref reader);
        return new EditorChecksumPacket(epoch, session, revision, checksum);
    }
}