internal readonly struct DuneEatPacket : INetworkPacket
{
    internal readonly uint RequestId;
    internal readonly ulong NpcId;
    internal readonly ushort VictimPeerId;
    internal readonly short LimbIndex;
    internal readonly byte Result;

    internal DuneEatPacket(uint requestId, ulong npcId, ushort victimPeerId, short limbIndex, byte result = 0)
    {
        RequestId = requestId;
        NpcId = npcId;
        VictimPeerId = victimPeerId;
        LimbIndex = limbIndex;
        Result = result;
    }

    public PacketType Type => PacketType.DuneEat;
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt32(RequestId);
        writer.WriteUInt64(NpcId);
        writer.WriteUInt16(VictimPeerId);
        writer.WriteInt16(LimbIndex);
        writer.WriteByte(Result);
    }

    internal static DuneEatPacket Read(ref PacketReader reader)
    {
        var packet = new DuneEatPacket(reader.ReadUInt32(), reader.ReadUInt64(), reader.ReadUInt16(), reader.ReadInt16(), reader.ReadByte());
        if (packet.Result > 2 || packet.LimbIndex < 0 || (packet.NpcId == 0) == (packet.VictimPeerId == 0))
            throw new InvalidDataException("Invalid Dune bite.");
        return packet;
    }
}