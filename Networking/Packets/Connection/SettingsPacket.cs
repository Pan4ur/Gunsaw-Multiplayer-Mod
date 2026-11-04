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
    internal readonly bool UniformHP;
    internal readonly float UHPValue;
    internal readonly float HealthFactor;
    internal readonly float RegenFactor;
    internal readonly bool BlackoutEnabled;
    internal readonly bool RestrictLightEnabled;
    internal readonly bool PropHunt;
    internal readonly ushort PropHuntTime;
    internal readonly byte PropHuntHunters;
    internal readonly float PropHuntShotPenalty;
    internal readonly float PropHuntShotHealing;
    internal readonly float PropHuntTelekinesisPenalty;
    internal readonly float PropHuntTelekinesisInterval;
    internal readonly float PropHuntStunCooldown;
    internal readonly float PropHuntChangeCooldown;
    internal readonly bool GunGame;
    internal readonly string GGSequence;
    internal readonly GunGameDeathMode GGOnDeath;

    internal SettingsPacket(bool pvpEnabled, bool canGrabPlayers, bool grabOnlyUnconscious, bool allowRespawn, bool respawnAtStart, ushort respawnTimeSeconds, byte maxPlayers, bool playerCollisions, bool cheatsEnabled, bool allowSwap, bool allowScaleChanging, float initialScale, bool brutalModeEnabled, bool allowObserver, bool teams = false, string teamsCfg = "", string startingWeapon = "Default", string respawnWeapon = "Default", string startingAmmo = LobbyAmmoRules.StartingDefault, string respawnAmmo = LobbyAmmoRules.RespawnDefault, ushort numberOfLives = 0, bool autoRestart = false, float healthFactor = 1f, float regenFactor = 1f, bool blackoutEnabled = false, bool restrictLightEnabled = false, bool gunGame = false, string ggSequence = "", GunGameDeathMode ggOnDeath = GunGameDeathMode.Reset, bool uniformHP = false, float uhpValue = LobbyHealthRule.DefaultUniformHPValue, bool propHunt = false, ushort propHuntTime = 300, byte propHuntHunters = 1, float propHuntShotPenalty = 5f, float propHuntShotHealing = 15f, float propHuntTelekinesisPenalty = 5f, float propHuntTelekinesisInterval = 0.5f, float propHuntStunCooldown = 30f, float propHuntChangeCooldown = 30f)
    {
        PropHunt = propHunt;
        PropHuntShotPenalty = LobbySettings.ClampPropHuntValue(propHuntShotPenalty, 5f, 0f, 100f);
        PropHuntShotHealing = LobbySettings.ClampPropHuntValue(propHuntShotHealing, 15f, 0f, 100f);
        PropHuntTelekinesisPenalty = LobbySettings.ClampPropHuntValue(propHuntTelekinesisPenalty, 5f, 0f, 100f);
        PropHuntTelekinesisInterval = LobbySettings.ClampPropHuntValue(propHuntTelekinesisInterval, 0.5f, 0.01f, 3600f);
        PropHuntStunCooldown = LobbySettings.ClampPropHuntValue(propHuntStunCooldown, 30f, 0f, 3600f);
        PropHuntChangeCooldown = LobbySettings.ClampPropHuntValue(propHuntChangeCooldown, 30f, 0f, 3600f);

        PropHuntHunters = (byte)Math.Max(1, Math.Min(63, (int)propHuntHunters));
        PropHuntTime = (ushort)Math.Max(1, Math.Min(3600, (int)propHuntTime));
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
        UniformHP = uniformHP;
        UHPValue = LobbyHealthRule.ClampUniformHPValue(uhpValue);
        HealthFactor = LobbyHealthRule.Clamp(healthFactor);
        RegenFactor = LobbyRegenRule.Clamp(regenFactor);
        BlackoutEnabled = blackoutEnabled;
        RestrictLightEnabled = restrictLightEnabled;
        GunGame = gunGame;
        GGSequence = ggSequence ?? "";
        GGOnDeath = ggOnDeath;
    }

    public PacketType Type => PacketType.Settings;
    
    public DeliverySettings Settings => DeliverySettings.Lossless;

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
        writer.WriteSingle(HealthFactor);
        writer.WriteSingle(RegenFactor);
        writer.WriteByte(BlackoutEnabled ? (byte)1 : (byte)0);
        writer.WriteByte(RestrictLightEnabled ? (byte)1 : (byte)0);
        writer.WriteByte(GunGame ? (byte)1 : (byte)0);
        writer.WriteBinaryString(GGSequence);
        writer.WriteByte((byte)GGOnDeath);
        writer.WriteBoolean(UniformHP);
        writer.WriteSingle(UHPValue);
        writer.WriteBoolean(PropHunt);
        writer.WriteUInt16(PropHuntTime);
        writer.WriteByte(PropHuntHunters);
        writer.WriteSingle(PropHuntShotPenalty);
        writer.WriteSingle(PropHuntShotHealing);
        writer.WriteSingle(PropHuntTelekinesisPenalty);
        writer.WriteSingle(PropHuntTelekinesisInterval);
        writer.WriteSingle(PropHuntStunCooldown);
        writer.WriteSingle(PropHuntChangeCooldown);
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
        var healthFactor = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 1f;
        var regenFactor = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 1f;
        var blackoutEnabled = reader.Remaining >= 1 && reader.ReadByte() != 0;
        var restrictLightEnabled = reader.Remaining >= 1 && reader.ReadByte() != 0;
        var gunGame = reader.Remaining >= 1 && reader.ReadByte() != 0;
        var ggSequence = reader.Remaining > 0 ? reader.ReadBinaryString() : "";
        var ggOnDeath = reader.Remaining >= 1 ? (GunGameDeathMode)reader.ReadByte() : GunGameDeathMode.Reset;
        if (!System.Enum.IsDefined(typeof(GunGameDeathMode), ggOnDeath)) ggOnDeath = GunGameDeathMode.Reset;
        var uniformHP = reader.Remaining >= 1 && reader.ReadBoolean();
        var uhpValue = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : LobbyHealthRule.DefaultUniformHPValue;
        var propHunt = reader.Remaining >= 1 && reader.ReadBoolean();
        var propHuntTime = reader.Remaining >= 2 ? reader.ReadUInt16() : (ushort)300;
        var hunters = reader.Remaining >= 1 ? reader.ReadByte() : (byte)1;
        var propHuntShotPenalty = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 5f;
        var propHuntShotHealing = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 15f;
        var propHuntTelekinesisPenalty = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 5f;
        var propHuntTelekinesisInterval = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 0.5f;
        var propHuntStunCooldown = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 30f;
        var propHuntChangeCooldown = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 30f;
        return new SettingsPacket(pvp, grab, unconscious, respawn, atStart, time, max, collisions, cheats, swap, scaleChanging, scale, brutal, observer, teams, cfg, startingWeapon, respawnWeapon, startingAmmo, respawnAmmo, lives, autoRestart, healthFactor, regenFactor, blackoutEnabled, restrictLightEnabled, gunGame, ggSequence, ggOnDeath, uniformHP, uhpValue, propHunt, propHuntTime, hunters, propHuntShotPenalty, propHuntShotHealing, propHuntTelekinesisPenalty, propHuntTelekinesisInterval, propHuntStunCooldown, propHuntChangeCooldown);
    }
}