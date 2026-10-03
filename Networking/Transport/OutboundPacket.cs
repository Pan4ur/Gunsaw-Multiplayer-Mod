internal readonly struct OutboundPacket
{
    internal const int ReliableHeaderSize = PacketHeader.Size + sizeof(int);
    internal readonly PacketType Type;
    internal readonly DeliverySettings Policy;
    internal readonly ushort TargetId;
    internal readonly byte[] Data;
    internal readonly bool Priority;
    internal readonly bool Reliable;
    internal readonly int SequenceId;
    internal readonly int StateGeneration;
    internal readonly int StateSlot;
    internal int WireLength => Data.Length + (Reliable ? ReliableHeaderSize : 0);

    internal OutboundPacket(PacketType type, DeliverySettings policy, ushort targetId, byte[] data, bool? priority = null, bool reliable = false, int sequenceId = 0, int stateGeneration = 0, int stateSlot = 0)
    {
        Type = type;
        Policy = policy;
        TargetId = targetId;
        Data = data;
        Priority = priority ?? policy.Priority;
        Reliable = reliable;
        SequenceId = sequenceId;
        StateGeneration = stateGeneration;
        StateSlot = stateSlot;
    }

    internal void CopyWireBytes(int offset, byte[] destination, int destinationOffset, int count)
    {
        if (Reliable)
        {
            if (offset == 0)
            {
                PacketHeader.Write(destination, destinationOffset, PacketType.Reliable);
                var sequenceOffset = destinationOffset + PacketHeader.Size;
                for (var i = 0; i < sizeof(int); i++) destination[sequenceOffset + i] = (byte)(SequenceId >> (8 * i));
                destinationOffset += ReliableHeaderSize;
                count -= ReliableHeaderSize;
            }
            else offset -= ReliableHeaderSize;
        }
        if (count > 0) 
            Buffer.BlockCopy(Data, offset, destination, destinationOffset, count);
    }
}