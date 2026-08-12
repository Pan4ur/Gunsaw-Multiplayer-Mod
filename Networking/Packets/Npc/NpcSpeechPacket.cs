internal readonly struct NpcSpeechPacket : INetworkPacket
{
    internal readonly ulong NpcId;
    internal readonly string Text;
    internal readonly int Priority;
    internal readonly float Duration;

    internal NpcSpeechPacket(ulong npcId, string text, int priority, float duration)
    {
        NpcId = npcId;
        Text = text ?? "";
        Priority = priority;
        Duration = duration;
    }

    public PacketType Type => PacketType.NpcSpeech;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt64(NpcId);
        writer.WriteBinaryString(Text);
        writer.WriteInt32(Priority);
        writer.WriteSingle(Duration);
    }

    internal static NpcSpeechPacket Read(ref PacketReader reader)
        => new(reader.ReadUInt64(), reader.ReadBinaryString(), reader.ReadInt32(), reader.ReadSingle());
}
