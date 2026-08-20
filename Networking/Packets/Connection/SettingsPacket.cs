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
    internal readonly bool BrutalModeEnabled;
    internal readonly bool AllowObserver;
    internal readonly bool Teams;
    internal readonly string TeamsCfg;

    internal SettingsPacket(bool pvpEnabled, bool canGrabPlayers, bool grabOnlyUnconscious, bool allowRespawn, bool respawnAtStart, ushort respawnTimeSeconds, byte maxPlayers, bool playerCollisions, bool cheatsEnabled, bool allowSwap, bool allowScaleChanging, float initialScale, bool brutalModeEnabled, bool allowObserver, bool teams = false, string teamsCfg = "")
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
        InitialScale = AvatarScaleHandler.Clamp(initialScale);
        BrutalModeEnabled = brutalModeEnabled;
        AllowObserver = allowObserver;
        Teams = teams;
        TeamsCfg = teamsCfg ?? "";
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
        writer.WriteByte(BrutalModeEnabled ? (byte)1 : (byte)0);
        writer.WriteByte(AllowObserver ? (byte)1 : (byte)0);
        writer.WriteByte(Teams ? (byte)1 : (byte)0);
        writer.WriteBinaryString(TeamsCfg);
    }

    internal static SettingsPacket Read(ref PacketReader reader)
    {
        var pvp = reader.ReadByte() != 0; var grab = reader.ReadByte() != 0; var unconscious = reader.ReadByte() != 0;
        var respawn = reader.ReadByte() != 0; var atStart = reader.ReadByte() != 0; var time = reader.ReadUInt16(); var max = reader.ReadByte();
        var collisions = reader.ReadByte() != 0; var cheats = reader.ReadByte() != 0; var swap = reader.ReadByte() != 0;
        var scaleChanging = reader.Remaining >= 1 ? reader.ReadByte() != 0 : true; var scale = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 1f;
        var brutal = reader.Remaining >= 1 && reader.ReadByte() != 0; var observer = reader.Remaining >= 1 ? reader.ReadByte() != 0 : true;
        var teams = reader.Remaining >= 1 && reader.ReadByte() != 0; var cfg = reader.Remaining > 0 ? reader.ReadBinaryString() : "";
        return new SettingsPacket(pvp, grab, unconscious, respawn, atStart, time, max, collisions, cheats, swap, scaleChanging, scale, brutal, observer, teams, cfg);
    }
}
