internal readonly struct ProjectileImpactPacket : INetworkPacket
{
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly string WeaponSpriteId;

    internal ProjectileImpactPacket(float positionX, float positionY, string weaponSpriteId)
    {
        PositionX = positionX;
        PositionY = positionY;
        WeaponSpriteId = weaponSpriteId;
    }

    public PacketType Type => PacketType.ProjectileImpact;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(PositionX);
        writer.WriteSingle(PositionY);
        writer.WriteBinaryString(WeaponSpriteId);
    }

    internal static ProjectileImpactPacket Read(ref PacketReader reader)
        => new ProjectileImpactPacket(reader.ReadSingle(), reader.ReadSingle(), reader.ReadBinaryString());
}
