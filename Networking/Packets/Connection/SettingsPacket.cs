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
    internal readonly string StartingWeapon;
    internal readonly string RespawnWeapon;
    internal readonly string StartingAmmo;
    internal readonly string RespawnAmmo;
    internal readonly ushort NumberOfLives;
    internal readonly bool AutoRestart;

    internal SettingsPacket(bool pvpEnabled, bool canGrabPlayers, bool grabOnlyUnconscious, bool allowRespawn, bool respawnAtStart, ushort respawnTimeSeconds, byte maxPlayers, bool playerCollisions, bool cheatsEnabled, bool allowSwap, bool allowScaleChanging, float initialScale, bool brutalModeEnabled, bool allowObserver, bool teams = false, string teamsCfg = "", string startingWeapon = "Default", string respawnWeapon = "Default", string startingAmmo = LobbyAmmoRules.StartingDefault, string respawnAmmo = LobbyAmmoRules.RespawnDefault, ushort numberOfLives = 0, bool autoRestart = false)
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
        StartingWeapon = startingWeapon ?? "Default";
        RespawnWeapon = respawnWeapon ?? "Default";
        StartingAmmo = startingAmmo ?? LobbyAmmoRules.StartingDefault;
        RespawnAmmo = respawnAmmo ?? LobbyAmmoRules.RespawnDefault;
        NumberOfLives = numberOfLives;
        AutoRestart = autoRestart;
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
        writer.WriteBinaryString(StartingWeapon);
        writer.WriteBinaryString(RespawnWeapon);
        writer.WriteBinaryString(StartingAmmo);
        writer.WriteBinaryString(RespawnAmmo);
        writer.WriteUInt16(NumberOfLives);
        writer.WriteByte(AutoRestart ? (byte)1 : (byte)0);
    }

    internal static SettingsPacket Read(ref PacketReader reader)
    {
        var pvp = reader.ReadByte() != 0; var grab = reader.ReadByte() != 0; var unconscious = reader.ReadByte() != 0;
        var respawn = reader.ReadByte() != 0; var atStart = reader.ReadByte() != 0; var time = reader.ReadUInt16(); var max = reader.ReadByte();
        var collisions = reader.ReadByte() != 0; var cheats = reader.ReadByte() != 0; var swap = reader.ReadByte() != 0;
        var scaleChanging = reader.Remaining >= 1 ? reader.ReadByte() != 0 : true; var scale = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 1f;
        var brutal = reader.Remaining >= 1 && reader.ReadByte() != 0; var observer = reader.Remaining >= 1 ? reader.ReadByte() != 0 : true;
        var teams = reader.Remaining >= 1 && reader.ReadByte() != 0; var cfg = reader.Remaining > 0 ? reader.ReadBinaryString() : "";
        var startingWeapon = reader.Remaining > 0 ? reader.ReadBinaryString() : "Default";
        var respawnWeapon = reader.Remaining > 0 ? reader.ReadBinaryString() : "Default";
        var startingAmmo = reader.Remaining > 0 ? reader.ReadBinaryString() : LobbyAmmoRules.StartingDefault;
        var respawnAmmo = reader.Remaining > 0 ? reader.ReadBinaryString() : LobbyAmmoRules.RespawnDefault;
        var lives = reader.Remaining >= sizeof(ushort) ? reader.ReadUInt16() : (ushort)0;
        var autoRestart = reader.Remaining >= 1 && reader.ReadByte() != 0;
        return new SettingsPacket(pvp, grab, unconscious, respawn, atStart, time, max, collisions, cheats, swap, scaleChanging, scale, brutal, observer, teams, cfg, startingWeapon, respawnWeapon, startingAmmo, respawnAmmo, lives, autoRestart);
    }
}
