internal readonly struct ShotVisualDirection
{
    internal readonly float X;
    internal readonly float Y;

    internal ShotVisualDirection(float x, float y) { X = x; Y = y; }
}

internal readonly struct ShotVisualPacket : INetworkPacket
{
    internal readonly float OriginX;
    internal readonly float OriginY;
    internal readonly float DirectionX;
    internal readonly float DirectionY;
    internal readonly float UpX;
    internal readonly float UpY;
    internal readonly string WeaponSprite;
    internal readonly bool IsNpcShot;
    internal readonly ushort[] TargetPeerIds;
    internal readonly int SpreadSeed;
    internal readonly ShotVisualDirection[] ExactDirections;
    internal readonly string[] DestroyedLampIds;

    internal ShotVisualPacket(float originX, float originY, float directionX, float directionY, float upX,
        float upY, string weaponSprite, bool isNpcShot, ushort[] targetPeerIds, int spreadSeed,
        ShotVisualDirection[] exactDirections, string[] destroyedLampIds)
    {
        OriginX = originX;
        OriginY = originY;
        DirectionX = directionX;
        DirectionY = directionY;
        UpX = upX;
        UpY = upY;
        WeaponSprite = weaponSprite ?? "";
        IsNpcShot = isNpcShot;
        TargetPeerIds = targetPeerIds ?? new ushort[0];
        SpreadSeed = spreadSeed;
        ExactDirections = exactDirections ?? new ShotVisualDirection[0];
        DestroyedLampIds = destroyedLampIds ?? new string[0];
    }

    public PacketType Type => PacketType.ShotVisual;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(OriginX);
        writer.WriteSingle(OriginY);
        writer.WriteSingle(DirectionX);
        writer.WriteSingle(DirectionY);
        writer.WriteSingle(UpX);
        writer.WriteSingle(UpY);
        writer.WriteBinaryString(WeaponSprite);
        writer.WriteBoolean(IsNpcShot);
        writer.WriteUInt16((ushort)System.Math.Min(TargetPeerIds.Length, ushort.MaxValue));
        for (var index = 0; index < TargetPeerIds.Length && index < ushort.MaxValue; index++)
            writer.WriteUInt16(TargetPeerIds[index]);
        writer.WriteInt32(SpreadSeed);
        writer.WriteByte((byte)System.Math.Min(ExactDirections.Length, byte.MaxValue));
        for (var index = 0; index < ExactDirections.Length && index < byte.MaxValue; index++)
        {
            writer.WriteSingle(ExactDirections[index].X);
            writer.WriteSingle(ExactDirections[index].Y);
        }
        writer.WriteByte((byte)System.Math.Min(DestroyedLampIds.Length, byte.MaxValue));
        for (var index = 0; index < DestroyedLampIds.Length && index < byte.MaxValue; index++)
            writer.WriteBinaryString(DestroyedLampIds[index] ?? "");
    }

    internal static ShotVisualPacket Read(ref PacketReader reader)
    {
        var originX = reader.ReadSingle();
        var originY = reader.ReadSingle();
        var directionX = reader.ReadSingle();
        var directionY = reader.ReadSingle();
        var upX = reader.ReadSingle();
        var upY = reader.ReadSingle();
        var weaponSprite = reader.ReadBinaryString();
        var isNpcShot = reader.Remaining > 0 && reader.ReadBoolean();
        var targetPeerIds = new ushort[reader.Remaining >= sizeof(ushort) ? reader.ReadUInt16() : 0];
        for (var index = 0; index < targetPeerIds.Length; index++) targetPeerIds[index] = reader.ReadUInt16();
        var spreadSeed = reader.Remaining >= sizeof(int) ? reader.ReadInt32() : 0;
        var exactDirections = new ShotVisualDirection[reader.Remaining > 0 ? reader.ReadByte() : 0];
        for (var index = 0; index < exactDirections.Length; index++)
            exactDirections[index] = new ShotVisualDirection(reader.ReadSingle(), reader.ReadSingle());
        var destroyedLampIds = new string[reader.Remaining > 0 ? reader.ReadByte() : 0];
        for (var index = 0; index < destroyedLampIds.Length; index++)
            destroyedLampIds[index] = reader.ReadBinaryString();
        return new ShotVisualPacket(originX, originY, directionX, directionY, upX, upY, weaponSprite,
            isNpcShot, targetPeerIds, spreadSeed, exactDirections, destroyedLampIds);
    }
}
