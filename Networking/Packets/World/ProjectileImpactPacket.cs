internal readonly struct ProjectileImpactPacket : INetworkPacket
{
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly string WeaponSpriteId;
    internal readonly bool HasExplosionTrace;
    internal readonly bool HasBackgroundCrack;
    internal readonly float BackgroundCrackRotation;
    internal readonly bool BackgroundCrackFlipX;
    internal readonly bool BackgroundCrackFlipY;
    internal readonly bool HasFloorCrack;
    internal readonly float FloorCrackX;
    internal readonly float FloorCrackY;
    internal readonly bool FloorCrackFlipX;

    internal ProjectileImpactPacket(float positionX, float positionY, string weaponSpriteId,
        bool hasExplosionTrace = false, bool hasBackgroundCrack = false, float backgroundCrackRotation = 0f,
        bool backgroundCrackFlipX = false, bool backgroundCrackFlipY = false, bool hasFloorCrack = false,
        float floorCrackX = 0f, float floorCrackY = 0f, bool floorCrackFlipX = false)
    {
        PositionX = positionX;
        PositionY = positionY;
        WeaponSpriteId = weaponSpriteId;
        HasExplosionTrace = hasExplosionTrace;
        HasBackgroundCrack = hasBackgroundCrack;
        BackgroundCrackRotation = backgroundCrackRotation;
        BackgroundCrackFlipX = backgroundCrackFlipX;
        BackgroundCrackFlipY = backgroundCrackFlipY;
        HasFloorCrack = hasFloorCrack;
        FloorCrackX = floorCrackX;
        FloorCrackY = floorCrackY;
        FloorCrackFlipX = floorCrackFlipX;
    }

    public PacketType Type => PacketType.ProjectileImpact;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(PositionX);
        writer.WriteSingle(PositionY);
        writer.WriteBinaryString(WeaponSpriteId);
        writer.WriteBoolean(HasExplosionTrace);
        writer.WriteBoolean(HasBackgroundCrack);
        writer.WriteSingle(BackgroundCrackRotation);
        writer.WriteBoolean(BackgroundCrackFlipX);
        writer.WriteBoolean(BackgroundCrackFlipY);
        writer.WriteBoolean(HasFloorCrack);
        writer.WriteSingle(FloorCrackX);
        writer.WriteSingle(FloorCrackY);
        writer.WriteBoolean(FloorCrackFlipX);
    }

    internal static ProjectileImpactPacket Read(ref PacketReader reader)
    {
        var positionX = reader.ReadSingle();
        var positionY = reader.ReadSingle();
        var weaponSpriteId = reader.ReadBinaryString();
        if (reader.Remaining == 0) return new ProjectileImpactPacket(positionX, positionY, weaponSpriteId);
        var hasExplosionTrace = reader.ReadBoolean();
        var hasBackgroundCrack = reader.ReadBoolean();
        var backgroundCrackRotation = reader.ReadSingle();
        var backgroundCrackFlipX = reader.ReadBoolean();
        var backgroundCrackFlipY = reader.ReadBoolean();
        var hasFloorCrack = reader.ReadBoolean();
        var floorCrackX = reader.ReadSingle();
        var floorCrackY = reader.ReadSingle();
        var floorCrackFlipX = reader.ReadBoolean();
        return new ProjectileImpactPacket(positionX, positionY, weaponSpriteId, hasExplosionTrace,
            hasBackgroundCrack, backgroundCrackRotation, backgroundCrackFlipX, backgroundCrackFlipY,
            hasFloorCrack, floorCrackX, floorCrackY, floorCrackFlipX);
    }
}
