internal enum PlayerDamageEffect : byte
{
    Damage = 0,
    Wound = 1
}

internal readonly struct PlayerDamagePacket : INetworkPacket
{
    internal readonly float Amount;
    internal readonly bool Critical;
    internal readonly PlayerDamageEffect Effect;
    internal readonly bool HasPlayerSource;
    internal readonly ushort SourcePeerId;
    internal readonly string SourceName;
    internal readonly string SourceWeapon;
    internal readonly short LimbIndex;
    internal readonly float LocalPointX;
    internal readonly float LocalPointY;
    internal readonly float DirectionX;
    internal readonly float DirectionY;
    internal readonly string WeaponSprite;
    internal readonly string WoundSprite;
    internal readonly bool HasSplash;
    internal readonly bool CreateScreenCrack;
    internal readonly float BaseDamage;
    internal readonly bool BodyColliderHit;
    
    private PlayerDamagePacket(float amount, bool critical, PlayerDamageEffect effect, bool hasPlayerSource = false,
        ushort sourcePeerId = 0,
        short limbIndex = 0,
        float localPointX = 0f, float localPointY = 0f, float directionX = 0f, float directionY = 0f,
        string weaponSprite = "", string woundSprite = "", bool hasSplash = false,
        bool createScreenCrack = false, string sourceName = "", string sourceWeapon = "",
        float baseDamage = 0f, bool bodyColliderHit = false)
    {
        Amount = amount;
        Critical = critical;
        Effect = effect;
        HasPlayerSource = hasPlayerSource;
        SourcePeerId = sourcePeerId;
        SourceName = sourceName ?? "";
        SourceWeapon = sourceWeapon ?? "";
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
    }

    internal static PlayerDamagePacket Damage(float amount, bool critical, bool hasPlayerSource = false,
        ushort sourcePeerId = 0,
        string sourceName = "", string sourceWeapon = "")
        => new PlayerDamagePacket(amount, critical, PlayerDamageEffect.Damage, hasPlayerSource, sourcePeerId,
            sourceName: sourceName, sourceWeapon: sourceWeapon);

    internal static PlayerDamagePacket Wound(
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
        bool bodyColliderHit)
        => new(
            amount: 0f,
            critical: false,
            effect: PlayerDamageEffect.Wound,
            hasPlayerSource: false,
            limbIndex: limbIndex,
            localPointX: localPointX,
            localPointY: localPointY,
            directionX: directionX,
            directionY: directionY,
            weaponSprite: weaponSprite,
            woundSprite: woundSprite,
            hasSplash: hasSplash,
            createScreenCrack: createScreenCrack,
            baseDamage: baseDamage,
            bodyColliderHit: bodyColliderHit
        );

    public PacketType Type => PacketType.PlayerDamage;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(Amount);
        writer.WriteBoolean(Critical);
        writer.WriteByte((byte)Effect);
        writer.WriteBoolean(HasPlayerSource);
        writer.WriteUInt16(SourcePeerId);
        writer.WriteBinaryString(SourceName);
        writer.WriteBinaryString(SourceWeapon);
        switch (Effect)
        {
            case PlayerDamageEffect.Wound:
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
                break;
        }
    }

    internal static PlayerDamagePacket Read(ref PacketReader reader)
    {
        var amount = reader.ReadSingle();
        var critical = reader.ReadBoolean();
        var effect = reader.Remaining > 0 ? (PlayerDamageEffect)reader.ReadByte() : PlayerDamageEffect.Damage;
        var hasPlayerSource = reader.Remaining > 0 && reader.ReadBoolean();
        var sourcePeerId = reader.Remaining >= 2 ? reader.ReadUInt16() : (ushort)0;
        var sourceName = reader.Remaining > 0 ? reader.ReadBinaryString() : "";
        var sourceWeapon = reader.Remaining > 0 ? reader.ReadBinaryString() : "";
        switch (effect)
        {
            case PlayerDamageEffect.Damage: return Damage(amount, critical, hasPlayerSource, sourcePeerId, sourceName, sourceWeapon);
            case PlayerDamageEffect.Wound:
            {
                var limbIndex = reader.ReadInt16();
                var localPointX = reader.ReadSingle();
                var localPointY = reader.ReadSingle();
                var directionX = reader.ReadSingle();
                var directionY = reader.ReadSingle();
                var weaponSprite = reader.ReadBinaryString();
                var woundSprite = reader.ReadBinaryString();
                var hasSplash = reader.ReadBoolean();
                var createScreenCrack = reader.ReadBoolean();
                var baseDamage = reader.ReadSingle();
                var bodyColliderHit = reader.ReadBoolean();
                
                return Wound(
                    limbIndex,
                    localPointX,
                    localPointY,
                    directionX,
                    directionY,
                    weaponSprite,
                    woundSprite,
                    hasSplash,
                    createScreenCrack,
                    baseDamage,
                    bodyColliderHit
                );
            }
            default: throw new System.IO.InvalidDataException("Unknown player damage effect.");
        }
    }
}
