internal enum PlayerDamageEffect : byte
{
    Damage = 0,
    Wound = 1,
    Explosion = 2
}

internal readonly struct PlayerDamagePacket : INetworkPacket
{
    internal readonly float Amount;
    internal readonly bool Critical;
    internal readonly PlayerDamageEffect Effect;
    internal readonly bool HasPlayerSource;
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
    internal readonly float ExplosionX;
    internal readonly float ExplosionY;
    internal readonly float ExplosionRange;
    internal readonly float ExplosionForce;

    private PlayerDamagePacket(float amount, bool critical, PlayerDamageEffect effect, bool hasPlayerSource = false,
        short limbIndex = 0,
        float localPointX = 0f, float localPointY = 0f, float directionX = 0f, float directionY = 0f,
        string weaponSprite = "", string woundSprite = "", bool hasSplash = false,
        bool createScreenCrack = false, float explosionX = 0f, float explosionY = 0f,
        float explosionRange = 0f, float explosionForce = 0f, string sourceName = "", string sourceWeapon = "")
    {
        Amount = amount;
        Critical = critical;
        Effect = effect;
        HasPlayerSource = hasPlayerSource;
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
        ExplosionX = explosionX;
        ExplosionY = explosionY;
        ExplosionRange = explosionRange;
        ExplosionForce = explosionForce;
    }

    internal static PlayerDamagePacket Damage(float amount, bool critical, bool hasPlayerSource = false,
        string sourceName = "", string sourceWeapon = "")
        => new PlayerDamagePacket(amount, critical, PlayerDamageEffect.Damage, hasPlayerSource,
            sourceName: sourceName, sourceWeapon: sourceWeapon);

    internal static PlayerDamagePacket Wound(short limbIndex, float localPointX, float localPointY,
        float directionX, float directionY, string weaponSprite, string woundSprite, bool hasSplash,
        bool createScreenCrack)
        => new PlayerDamagePacket(0f, false, PlayerDamageEffect.Wound, false, limbIndex, localPointX, localPointY,
            directionX, directionY, weaponSprite, woundSprite, hasSplash, createScreenCrack);

    internal static PlayerDamagePacket Explosion(float positionX, float positionY, float range, float force)
        => new PlayerDamagePacket(0f, false, PlayerDamageEffect.Explosion, false, explosionX: positionX,
            explosionY: positionY, explosionRange: range, explosionForce: force);

    public PacketType Type => PacketType.PlayerDamage;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(Amount);
        writer.WriteBoolean(Critical);
        writer.WriteByte((byte)Effect);
        writer.WriteBoolean(HasPlayerSource);
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
                break;
            case PlayerDamageEffect.Explosion:
                writer.WriteSingle(ExplosionX);
                writer.WriteSingle(ExplosionY);
                writer.WriteSingle(ExplosionRange);
                writer.WriteSingle(ExplosionForce);
                break;
        }
    }

    internal static PlayerDamagePacket Read(ref PacketReader reader)
    {
        var amount = reader.ReadSingle();
        var critical = reader.ReadBoolean();
        var effect = reader.Remaining > 0 ? (PlayerDamageEffect)reader.ReadByte() : PlayerDamageEffect.Damage;
        var hasPlayerSource = reader.Remaining > 0 && reader.ReadBoolean();
        var sourceName = reader.Remaining > 0 ? reader.ReadBinaryString() : "";
        var sourceWeapon = reader.Remaining > 0 ? reader.ReadBinaryString() : "";
        switch (effect)
        {
            case PlayerDamageEffect.Damage: return Damage(amount, critical, hasPlayerSource, sourceName, sourceWeapon);
            case PlayerDamageEffect.Wound:
                return Wound(reader.ReadInt16(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadBinaryString(), reader.ReadBinaryString(), reader.ReadBoolean(),
                    reader.ReadBoolean());
            case PlayerDamageEffect.Explosion:
                return Explosion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            default: throw new System.IO.InvalidDataException("Unknown player damage effect.");
        }
    }
}
