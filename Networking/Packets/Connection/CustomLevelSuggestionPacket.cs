internal readonly struct CustomLevelSuggestionPacket : INetworkPacket
{
    internal readonly string LevelCode;
    internal readonly int SizeKiB;

    internal CustomLevelSuggestionPacket(string levelCode, int sizeKiB)
    {
        LevelCode = levelCode ?? "";
        SizeKiB = sizeKiB;
    }

    public PacketType Type => PacketType.CustomLevelSuggestion;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteBinaryString(LevelCode);
        writer.WriteInt32(SizeKiB);
    }

    internal static CustomLevelSuggestionPacket Read(ref PacketReader reader) => new (reader.ReadBinaryString(), reader.ReadInt32());
}
