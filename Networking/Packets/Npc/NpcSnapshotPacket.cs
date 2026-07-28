internal readonly struct NpcSnapshotPacket : INetworkPacket
{
    internal readonly int TransferId;
    internal readonly ushort ChunkIndex;
    internal readonly ushort ChunkCount;
    internal readonly int TotalLength;
    internal readonly byte[] Data;

    internal NpcSnapshotPacket(int transferId, ushort chunkIndex, ushort chunkCount, int totalLength, byte[] data)
    {
        TransferId = transferId;
        ChunkIndex = chunkIndex;
        ChunkCount = chunkCount;
        TotalLength = totalLength;
        Data = data ?? new byte[0];
    }

    public PacketType Type => PacketType.NpcSnapshot;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(TransferId);
        writer.WriteUInt16(ChunkIndex);
        writer.WriteUInt16(ChunkCount);
        writer.WriteInt32(TotalLength);
        writer.WriteBytes(Data);
    }

    internal static NpcSnapshotPacket Read(ref PacketReader reader) => new NpcSnapshotPacket(
        reader.ReadInt32(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadInt32(), reader.ReadRemainingBytes());
}
