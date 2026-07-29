internal readonly struct PlayerWoundPacket : INetworkPacket
{
    private readonly PacketType packetType;
    internal readonly short LimbIndex;
    internal readonly float LocalPointX;
    internal readonly float LocalPointY;
    internal readonly float DirectionX;
    internal readonly float DirectionY;
    internal readonly string WeaponSprite;
    internal readonly string WoundSprite;
    internal readonly bool HasSplash;
    internal readonly bool CreateScreenCrack;

    internal PlayerWoundPacket(PacketType packetType, short limbIndex, float localPointX, float localPointY,
        float directionX, float directionY, string weaponSprite, string woundSprite, bool hasSplash,
        bool createScreenCrack)
    {
        this.packetType = packetType;
        LimbIndex = limbIndex;
        LocalPointX = localPointX;
        LocalPointY = localPointY;
        DirectionX = directionX;
        DirectionY = directionY;
        WeaponSprite = weaponSprite ?? "";
        WoundSprite = woundSprite ?? "";
        HasSplash = hasSplash;
        CreateScreenCrack = createScreenCrack;
    }

    public PacketType Type => packetType;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(0f);
        writer.WriteBoolean(false);
        writer.WriteByte(1);
        writer.WriteInt16(LimbIndex);
        writer.WriteSingle(LocalPointX);
        writer.WriteSingle(LocalPointY);
        writer.WriteSingle(DirectionX);
        writer.WriteSingle(DirectionY);
        writer.WriteBinaryString(WeaponSprite);
        writer.WriteBinaryString(WoundSprite);
        writer.WriteBoolean(HasSplash);
        writer.WriteBoolean(CreateScreenCrack);
    }
}
