internal readonly struct PlayerGrabPacket : INetworkPacket
{
    internal readonly bool IsGrabbing;
    internal readonly byte PartKind;
    internal readonly short PartIndex;
    internal readonly float PointX;
    internal readonly float PointY;
    internal readonly float LocalPointX;
    internal readonly float LocalPointY;

    internal PlayerGrabPacket(bool isGrabbing, byte partKind = 0, short partIndex = 0, float pointX = 0f,
        float pointY = 0f, float localPointX = 0f, float localPointY = 0f)
    { IsGrabbing = isGrabbing; PartKind = partKind; PartIndex = partIndex; PointX = pointX; PointY = pointY; LocalPointX = localPointX; LocalPointY = localPointY; }

    public PacketType Type => PacketType.PlayerGrab;
    public void Write(ref PacketWriter writer)
    {
        writer.WriteBoolean(IsGrabbing);
        if (!IsGrabbing) return;
        writer.WriteByte(PartKind); writer.WriteInt16(PartIndex); writer.WriteSingle(PointX); writer.WriteSingle(PointY);
        writer.WriteSingle(LocalPointX); writer.WriteSingle(LocalPointY);
    }
    internal static PlayerGrabPacket Read(ref PacketReader reader)
    {
        if (!reader.ReadBoolean()) return new PlayerGrabPacket(false);
        return new PlayerGrabPacket(true, reader.ReadByte(), reader.ReadInt16(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}