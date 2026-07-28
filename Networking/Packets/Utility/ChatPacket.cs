internal readonly struct ChatPacket : INetworkPacket
{
    internal readonly int MessageId;
    internal readonly bool IsSystem;
    internal readonly string Text;

    internal ChatPacket(int messageId, bool isSystem, string text)
    {
        MessageId = messageId;
        IsSystem = isSystem;
        Text = text ?? "";
    }

    public PacketType Type => PacketType.Chat;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(MessageId);
        writer.WriteByte(IsSystem ? (byte)1 : (byte)0);
        writer.WriteUtf8(Text);
    }

    internal static ChatPacket Read(ref PacketReader reader) => new ChatPacket(reader.ReadInt32(), reader.ReadByte() != 0, reader.ReadRemainingUtf8());
}
