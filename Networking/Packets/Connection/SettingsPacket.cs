internal readonly struct SettingsPacket : INetworkPacket
{
    internal readonly bool PvpEnabled;
    internal readonly bool CanGrabPlayers;
    internal readonly bool GrabOnlyUnconscious;
    internal readonly bool AllowRespawn;
    internal readonly bool RespawnAtStart;
    internal readonly ushort RespawnTimeSeconds;
    internal readonly byte MaxPlayers;
    internal readonly bool PlayerCollisions;
    internal readonly bool CheatsEnabled;
    internal readonly bool AllowSwap;
    internal readonly bool AllowScaleChanging;
    internal readonly float InitialScale;

    internal SettingsPacket(bool pvpEnabled, bool canGrabPlayers, bool grabOnlyUnconscious, bool allowRespawn, bool respawnAtStart, ushort respawnTimeSeconds, byte maxPlayers, bool playerCollisions, bool cheatsEnabled, bool allowSwap, bool allowScaleChanging, float initialScale)
    {
        PvpEnabled = pvpEnabled;
        CanGrabPlayers = canGrabPlayers;
        GrabOnlyUnconscious = grabOnlyUnconscious;
        AllowRespawn = allowRespawn;
        RespawnAtStart = respawnAtStart;
        RespawnTimeSeconds = respawnTimeSeconds;
        MaxPlayers = maxPlayers;
        PlayerCollisions = playerCollisions;
        CheatsEnabled = cheatsEnabled;
        AllowSwap = allowSwap;
        AllowScaleChanging = allowScaleChanging;
        InitialScale = CharacterScaleRules.Clamp(initialScale);
    }

    public PacketType Type => PacketType.Settings;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteByte(PvpEnabled ? (byte)1 : (byte)0);
        writer.WriteByte(CanGrabPlayers ? (byte)1 : (byte)0);
        writer.WriteByte(GrabOnlyUnconscious ? (byte)1 : (byte)0);
        writer.WriteByte(AllowRespawn ? (byte)1 : (byte)0);
        writer.WriteByte(RespawnAtStart ? (byte)1 : (byte)0);
        writer.WriteUInt16(RespawnTimeSeconds);
        writer.WriteByte(MaxPlayers);
        writer.WriteByte(PlayerCollisions ? (byte)1 : (byte)0);
        writer.WriteByte(CheatsEnabled ? (byte)1 : (byte)0);
        writer.WriteByte(AllowSwap ? (byte)1 : (byte)0);
        writer.WriteByte(AllowScaleChanging ? (byte)1 : (byte)0);
        writer.WriteSingle(InitialScale);
    }

    internal static SettingsPacket Read(ref PacketReader reader) => new SettingsPacket(reader.ReadByte() != 0, reader.ReadByte() != 0, reader.ReadByte() != 0, reader.ReadByte() != 0, reader.ReadByte() != 0, reader.ReadUInt16(), reader.ReadByte(), reader.ReadByte() != 0, reader.ReadByte() != 0, reader.ReadByte() != 0, reader.Remaining >= 1 ? reader.ReadByte() != 0 : true, reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 1f);
}
