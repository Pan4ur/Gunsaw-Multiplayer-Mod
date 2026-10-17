internal readonly struct EditorSnapshotRequestPacket : INetworkPacket
{
    internal readonly int Epoch;

    internal EditorSnapshotRequestPacket(int epoch)
    {
        Epoch = epoch;
    }

    public PacketType Type => PacketType.EditorSnapshotRequest;
    
    public DeliverySettings Settings => new(true, false, false);

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(Epoch);
    }

    internal static EditorSnapshotRequestPacket Read(ref PacketReader reader)
    {
        var epoch = EditorPacketData.ReadNonNegative(ref reader);
        return new EditorSnapshotRequestPacket(epoch);
    }
}