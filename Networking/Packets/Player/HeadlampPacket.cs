internal readonly struct HeadlampPacket : INetworkPacket
{
    internal readonly ushort OwnerId;
    internal readonly bool Enabled;
    internal readonly uint Sequence;

    internal HeadlampPacket(ushort ownerId, bool enabled, uint sequence)
    {
        OwnerId = ownerId;
        Enabled = enabled;
        Sequence = sequence;
    }

    public PacketType Type => PacketType.Headlamp;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt16(OwnerId);
        writer.WriteBoolean(Enabled);
        writer.WriteUInt32(Sequence);
    }

    internal static HeadlampPacket Read(ref PacketReader reader) => new(reader.ReadUInt16(), reader.ReadBoolean(), reader.ReadUInt32());
}