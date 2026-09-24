using System.Globalization;
using UnityEngine;

[Serializable]
internal sealed class LobbySettings
{
    [LobbySetting("Pvp", "Enable PvP in new lobbies.", "--pvp")]
    public bool Pvp;
    [LobbySetting("CanGrab", "Allow player grabbing in new lobbies.", "--can-grab")]
    public bool CanGrab = true;
    [LobbySetting("GrabOnlyUnconscious", "Limit grabbing to unconscious players in new lobbies.", "--grab-only-unconscious")]
    public bool GrabOnlyUnconscious = true;
    [LobbySetting("AllowRespawn", "Allow respawning in new lobbies.", "--allow-respawn")]
    public bool AllowRespawn = true;
    [LobbySetting("AutoRestart", "Restart the level after every player or team but one is eliminated.", "--auto-restart")]
    public bool AutoRestart;
    [LobbySetting("RespawnAtStart", "Respawn players at level start in new lobbies.", "--respawn-at-start")]
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
    public string TeamsCfg = "Milkies:blue;Expies:red";
    [LobbySetting("InitialScale", "Character scale assigned when a player joins or respawns.", "--initial-scale")]
    public string InitialScale = "1.0";
    [LobbySetting("StartingWeapon", "Weapons assigned when a player joins, in Slot1;Slot2;Slot3 format. Also supports random: Random, Random[in=Name;Name], and Random[ex=Name;Name].", "--starting-weapon")]
    public string StartingWeapon = "Default";
    [LobbySetting("RespawnWeapon", "Weapons assigned when a player respawns, in Slot1;Slot2;Slot3 format. Also supports random: Random, Random[in=Name;Name], and Random[ex=Name;Name].", "--respawn-weapon")]
    public string RespawnWeapon = "Default";
    [LobbySetting("StartingAmmo", "Ammo assigned when a player joins, in Pistol;Rifle;Heavy;Grenade format.", "--starting-ammo")]
    public string StartingAmmo = LobbyAmmoRules.StartingDefault;
    [LobbySetting("RespawnAmmo", "Ammo assigned when a player respawns, in Pistol;Rifle;Heavy;Grenade format.", "--respawn-ammo")]
    public string RespawnAmmo = LobbyAmmoRules.RespawnDefault;
    [LobbySetting("RespawnTime", "Default respawn delay in seconds.", "--respawn-seconds")]
    public string RespawnTime = "5";
    [LobbySetting("NumberOfLives", "Lives available to each player per level. Zero means unlimited lives.", "--lives")]
    public string NumberOfLives = "0";
    [LobbySetting("HealthFactor", "Multiplier for each player's maximum health. Allowed range: 0.01 to 10.", "--health-factor")]
    public string HealthFactor = "1.0";
    [LobbySetting("RegenFactor", "Multiplier for each player's health regeneration speed. Allowed range: 0 to 10.", "--regen-factor")]
    public string RegenFactor = "1.0";
    [LobbySetting("Blackout", "Enables darkness with a headlamp on every player.")]
    public bool Blackout;
    [LobbySetting("RestrictLight", "Makes headlamp light stop at obstacles.")]
    public bool RestrictLight;
    [LobbySetting("MaxPlayers", "Default maximum player count.", "--max-players")]
    public string MaxPlayers = "4";
    [LobbySetting(null, "", "--connection")]
    public ConnectionMode ConnectionMode = ConnectionMode.Relay;

    internal LobbySettings Clone() => (LobbySettings)MemberwiseClone();

    internal ParsedLobbySettings Parse(int defaultMaxPlayers)
    {
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
        return new ParsedLobbySettings(respawnTime, numberOfLives, maxPlayers, initialScale, healthFactor, regenFactor);
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

    internal ParsedLobbySettings(int respawnTime, int numberOfLives, int maxPlayers, float initialScale, float healthFactor, float regenFactor)
    {
        RespawnTime = respawnTime;
        NumberOfLives = numberOfLives;
        MaxPlayers = maxPlayers;
        InitialScale = initialScale;
        HealthFactor = healthFactor;
        RegenFactor = regenFactor;
    }
}
