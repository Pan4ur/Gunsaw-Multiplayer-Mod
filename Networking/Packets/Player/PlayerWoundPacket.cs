internal readonly struct PlayerWoundPacket : INetworkPacket
{
    private readonly PacketType packetType;
    private readonly short LimbIndex;
    private readonly float LocalPointX;
    private readonly float LocalPointY;
    private readonly float DirectionX;
    private readonly float DirectionY;
    private readonly string WeaponSprite;
    private readonly string WoundSprite;
    private readonly bool HasSplash;
    private readonly bool CreateScreenCrack;
    private readonly float BaseDamage;
    private readonly bool BodyColliderHit;
    private readonly bool IsRozaShot;
    private readonly bool HasPlayerSource;
    private readonly ushort SourcePeerId;
    
    internal PlayerWoundPacket(
        PacketType packetType,
        short limbIndex,
        float localPointX,
        float localPointY,
        float directionX,
        float directionY,
        string weaponSprite,
        string woundSprite,
        bool hasSplash,
        bool createScreenCrack,
        float baseDamage,
        bool bodyColliderHit,
        bool isRozaShot = false,
        bool hasPlayerSource = false,
        ushort sourcePeerId = 0)
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
        BaseDamage = baseDamage;
        BodyColliderHit = bodyColliderHit;
        IsRozaShot = isRozaShot;
        HasPlayerSource = hasPlayerSource;
        SourcePeerId = sourcePeerId;
    }

    public PacketType Type => packetType;
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(0f);
        writer.WriteBoolean(false);
        writer.WriteByte((byte)PlayerDamageEffect.Wound);
        
        writer.WriteBoolean(HasPlayerSource);
        writer.WriteUInt16(SourcePeerId);
        writer.WriteBinaryString("");
        writer.WriteBinaryString("");
        
        writer.WriteInt16(LimbIndex);
        writer.WriteSingle(LocalPointX);
        writer.WriteSingle(LocalPointY);
        writer.WriteSingle(DirectionX);
        writer.WriteSingle(DirectionY);
        writer.WriteBinaryString(WeaponSprite);
        writer.WriteBinaryString(WoundSprite);
        writer.WriteBoolean(HasSplash);
        writer.WriteBoolean(CreateScreenCrack);
        writer.WriteSingle(BaseDamage);
        writer.WriteBoolean(BodyColliderHit);
        if (IsRozaShot) writer.WriteBoolean(IsRozaShot);
    }
}
