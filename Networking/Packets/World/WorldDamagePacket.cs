internal readonly struct WorldDamageEntry
{
    internal readonly ulong TargetId;
    internal readonly float Amount;

    internal WorldDamageEntry(ulong targetId, float amount)
    {
        TargetId = targetId;
        Amount = amount;
    }
}

internal readonly struct WorldDamagePacket : INetworkPacket
{
    internal readonly WorldDamageEntry[] Entries;

    internal WorldDamagePacket(WorldDamageEntry[] entries)
    {
        Entries = entries ?? new WorldDamageEntry[0];
    }

    public PacketType Type => PacketType.WorldDamage;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt16((ushort)System.Math.Min(Entries.Length, ushort.MaxValue));
        for (var index = 0; index < Entries.Length && index < ushort.MaxValue; index++)
        {
            writer.WriteUInt64(Entries[index].TargetId);
            writer.WriteSingle(Entries[index].Amount);
        }
    }

    internal static WorldDamagePacket Read(ref PacketReader reader)
    {
        var count = reader.ReadUInt16();
        var entries = new WorldDamageEntry[count];
        for (var index = 0; index < entries.Length; index++)
            entries[index] = new WorldDamageEntry(reader.ReadUInt64(), reader.ReadSingle());
        return new WorldDamagePacket(entries);
    }
}
