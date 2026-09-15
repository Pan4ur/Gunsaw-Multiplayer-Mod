internal readonly struct PlayerSoundPacket : INetworkPacket
{
    internal readonly uint SoundId;
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly byte Volume;
    internal readonly byte Pitch;

    internal PlayerSoundPacket(uint soundId, float positionX, float positionY, byte volume, byte pitch)
    {
        SoundId = soundId;
        PositionX = positionX;
        PositionY = positionY;
        Volume = volume;
        Pitch = pitch;
    }

    public PacketType Type => PacketType.PlayerSound;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt32(SoundId);
        writer.WriteSingle(PositionX);
        writer.WriteSingle(PositionY);
        writer.WriteByte(Volume);
        writer.WriteByte(Pitch);
    }

    internal static PlayerSoundPacket Read(ref PacketReader reader)
        => new(reader.ReadUInt32(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadByte(), reader.ReadByte());
}