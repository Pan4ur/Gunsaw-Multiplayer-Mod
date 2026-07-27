internal readonly struct PlayerSnapshotPacket : INetworkPacket
{
    internal readonly int Sequence; internal readonly byte[] Data;

    internal PlayerSnapshotPacket(int sequence, byte[] data) { Sequence = sequence; Data = data ?? new byte[0]; }

    public PacketType Type => PacketType.PlayerSnapshot;

    public void Write(ref PacketWriter writer) { writer.WriteInt32(Sequence); writer.WriteBytes(Data); }

    internal static PlayerSnapshotPacket Read(ref PacketReader reader) => new PlayerSnapshotPacket(reader.ReadInt32(), reader.ReadRemainingBytes());
}
