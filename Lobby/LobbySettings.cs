using System.Globalization;
using UnityEngine;

internal enum GunGameDeathMode : byte
{
    Reset,
    Rollback,
    None
}

[Serializable]
internal sealed class LobbySettings
{
    [LobbySetting("Pvp", "Enable PvP in new lobbies.", "--pvp")]
    public bool Pvp;
    [LobbySetting("CanGrab", "Allow player grabbing in new lobbies.", "--can-grab")]
    public bool CanGrab = true;
    [LobbySetting("GrabOnlyUnconscious", "Limit grabbing to unconscious players in new lobbies.", "--grab-only-unconscious")]
    [LobbySettingVisibleWhen(nameof(CanGrab))]
    public bool GrabOnlyUnconscious = true;
    [LobbySetting("AllowRespawn", "Allow respawning in new lobbies.", "--allow-respawn")]
    public bool AllowRespawn = true;
    [LobbySetting("AutoRestart", "Restart the level after every player or team but one is eliminated.", "--auto-restart")]
    public bool AutoRestart;
    [LobbySetting("RespawnAtStart", "Respawn players at level start in new lobbies.", "--respawn-at-start")]
    [LobbySettingVisibleWhen(nameof(AllowRespawn))]
    public bool RespawnAtStart = true;
    [LobbySetting("PlayerCollisions", "Allow players to collide with each other in new lobbies.", "--player-collisions")]
    public bool PlayerCollisions = true;
    [LobbySetting("Cheats", "Allow built-in cheats in new lobbies.", "--cheats")]
    public bool Cheats;
    [LobbySetting("AllowSwap", "Allow changing character with /swap while playing.", "--allow-swap")]
    public bool AllowSwap = true;
    [LobbySetting("AllowScaleChanging", "Allow players to change their character scale.", "--allow-scale-changing")]
    public bool AllowScaleChanging = true;
    [LobbySetting("AllowObserver", "Allow players to activate Observer.", "--allow-observer")]
    public bool AllowObserver = true;
    [LobbySetting("Teams", "Enable teams in new lobbies.", "--teams")]
    public bool Teams;
    [LobbySetting("TeamsCfg", "Teams in Name:color format.", "--teams-cfg")]
    [LobbySettingVisibleWhen(nameof(Teams))]
    public string TeamsCfg = "Milkies:blue;Expies:red";
    [LobbySetting("InitialScale", "Character scale assigned when a player joins or respawns.", "--initial-scale")]
    public string InitialScale = "1.0";
    [LobbySetting("StartingWeapon", "Weapons assigned when a player joins, in Slot1;Slot2;Slot3 format. Also supports random: Random, Random[in=Name;Name], and Random[ex=Name;Name].", "--starting-weapon")]
    public string StartingWeapon = "Default";
    [LobbySetting("RespawnWeapon", "Weapons assigned when a player respawns, in Slot1;Slot2;Slot3 format. Also supports random: Random, Random[in=Name;Name], and Random[ex=Name;Name].", "--respawn-weapon")]
    [LobbySettingVisibleWhen(nameof(AllowRespawn))]
    public string RespawnWeapon = "Default";
    [LobbySetting("StartingAmmo", "Ammo assigned when a player joins, in Pistol;Rifle;Heavy;Grenade format.", "--starting-ammo")]
    public string StartingAmmo = LobbyAmmoRules.StartingDefault;
    [LobbySetting("RespawnAmmo", "Ammo assigned when a player respawns, in Pistol;Rifle;Heavy;Grenade format.", "--respawn-ammo")]
    [LobbySettingVisibleWhen(nameof(AllowRespawn))]
    public string RespawnAmmo = LobbyAmmoRules.RespawnDefault;
    [LobbySetting("RespawnTime", "Default respawn delay in seconds.", "--respawn-seconds")]
    [LobbySettingVisibleWhen(nameof(AllowRespawn))]
    public string RespawnTime = "5";
    [LobbySetting("NumberOfLives", "Lives available to each player per level. Zero means unlimited lives.", "--lives")]
    [LobbySettingVisibleWhen(nameof(AllowRespawn))]
    public string NumberOfLives = "0";
    [LobbySetting("UniformHP", "Give every character species the same maximum health, overriding HealthFactor.", "--uniform-hp")]
    public bool UniformHP;
    [LobbySetting("UHPValue", "Maximum health when Uniform HP is enabled. Allowed range: 1 to 10000.", "--uhp-value")]
    [LobbySettingVisibleWhen(nameof(UniformHP))]
    public string UHPValue = "100";
    [LobbySetting("HealthFactor", "Multiplier for each player's maximum health. Allowed range: 0.01 to 10.", "--health-factor")]
    [LobbySettingVisibleWhen(nameof(UniformHP), false)]
    public string HealthFactor = "1.0";
    [LobbySetting("RegenFactor", "Multiplier for each player's health regeneration speed. Allowed range: 0 to 10.", "--regen-factor")]
    public string RegenFactor = "1.0";
    [LobbySetting("Blackout", "Enables darkness with a headlamp on every player.")]
    public bool Blackout;
    [LobbySetting("RestrictLight", "Makes headlamp light stop at obstacles.")]
    [LobbySettingVisibleWhen(nameof(Blackout))]
    public bool RestrictLight;
    [LobbySetting("MaxPlayers", "Default maximum player count.", "--max-players")]
    public string MaxPlayers = "4";
    [LobbySetting(null, "", "--connection")]
    public ConnectionMode ConnectionMode = ConnectionMode.Relay;
    
    [LobbySetting("GunGame", "Advance through GG Sequence after each kill.", "--gungame")]
    public bool GunGame;
    [LobbySetting("GGSequence", "Weapon names in kill order, separated by semicolons.", "--gg-sequence")]
    [LobbySettingVisibleWhen(nameof(GunGame))]
    public string GGSequence = "Derringer;Vitya;Sniper rifle";
    [LobbySetting("GGOnDeath", "GunGame progression after death: Reset, Rollback, or None.", "--gg-on-death")]
    [LobbySettingVisibleWhen(nameof(GunGame))]
    public GunGameDeathMode GGOnDeath = GunGameDeathMode.Reset;

    [LobbySetting("PropHunt", "Enable Prop Hunt rounds.", "--prop-hunt")]
    public bool PropHunt;
    [LobbySetting("PropHuntTime", "Hunting time in seconds, after the 30 second hiding period.", "--prop-hunt-time")]
    [LobbySettingVisibleWhen(nameof(PropHunt))]
    public string PropHuntTime = "300";

    [LobbySetting("PropHuntHunters", "Number of hunters.", "--prop-hunt-hunters")]
    [LobbySettingVisibleWhen(nameof(PropHunt))]
    public string PropHuntHunters = "1";

    internal LobbySettings Clone() => (LobbySettings)MemberwiseClone();

    internal ParsedLobbySettings Parse(int defaultMaxPlayers)
    {
        if (!int.TryParse(PropHuntTime, out var propHuntTime)) propHuntTime = 300;
        PropHuntTime = Mathf.Clamp(propHuntTime, 1, 3600).ToString();
        if (!int.TryParse(PropHuntHunters, out var hunters)) hunters = 1;
        PropHuntHunters = Mathf.Clamp(hunters, 1, 63).ToString();
        GGSequence = (GGSequence ?? "").Trim();
        if (!Enum.IsDefined(typeof(GunGameDeathMode), GGOnDeath)) GGOnDeath = GunGameDeathMode.Reset;
        if (!int.TryParse(RespawnTime, out var respawnTime)) respawnTime = 5;
        respawnTime = Mathf.Clamp(respawnTime, 0, 3600);
        RespawnTime = respawnTime.ToString();
        if (!int.TryParse(NumberOfLives, out var numberOfLives)) numberOfLives = 0;
        numberOfLives = Mathf.Clamp(numberOfLives, 0, ushort.MaxValue);
        NumberOfLives = numberOfLives.ToString();
        if (!int.TryParse(MaxPlayers, out var maxPlayers)) maxPlayers = defaultMaxPlayers;
        maxPlayers = Mathf.Clamp(maxPlayers, 2, 64);
        MaxPlayers = maxPlayers.ToString();
        if (!float.TryParse(InitialScale, NumberStyles.Float, CultureInfo.InvariantCulture, out var initialScale)) initialScale = 1f;
        initialScale = AvatarScaleHandler.Clamp(initialScale);
        InitialScale = initialScale.ToString("0.##", CultureInfo.InvariantCulture);
        if (!float.TryParse((HealthFactor ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var healthFactor))
            healthFactor = LobbyHealthRule.DefaultFactor;
        healthFactor = LobbyHealthRule.Clamp(healthFactor);
        HealthFactor = healthFactor.ToString("0.##", CultureInfo.InvariantCulture);
        if (!float.TryParse((RegenFactor ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var regenFactor))
            regenFactor = LobbyRegenRule.DefaultFactor;
        regenFactor = LobbyRegenRule.Clamp(regenFactor);
        RegenFactor = regenFactor.ToString("0.##", CultureInfo.InvariantCulture);
        if (!float.TryParse((UHPValue ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var uhpValue))
            uhpValue = LobbyHealthRule.DefaultUniformHPValue;
        uhpValue = LobbyHealthRule.ClampUniformHPValue(uhpValue);
        UHPValue = uhpValue.ToString("0.##", CultureInfo.InvariantCulture);
        return new ParsedLobbySettings(respawnTime, numberOfLives, maxPlayers, initialScale, healthFactor, regenFactor, uhpValue);
    }
}

internal readonly struct ParsedLobbySettings
{
    internal readonly int RespawnTime;
    internal readonly int NumberOfLives;
    internal readonly int MaxPlayers;
    internal readonly float InitialScale;
    internal readonly float HealthFactor;
    internal readonly float RegenFactor;
    internal readonly float UHPValue;

    internal ParsedLobbySettings(int respawnTime, int numberOfLives, int maxPlayers, float initialScale, float healthFactor, float regenFactor, float uhpValue)
    {
        RespawnTime = respawnTime;
        NumberOfLives = numberOfLives;
        MaxPlayers = maxPlayers;
        InitialScale = initialScale;
        HealthFactor = healthFactor;
        RegenFactor = regenFactor;
        UHPValue = uhpValue;
    }
}
