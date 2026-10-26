using UnityEngine;

internal static class NametagSettings
{
    internal static bool Ping { get; private set; } = true;
    internal static bool Health { get; private set; }
    internal static bool Background { get; private set; }
    internal static bool Ammo { get; private set; }
    internal static bool KillDeath { get; private set; }
    internal static float Scale { get; private set; } = 1f;

    internal static void Initialize()
    {
        Ping = PlayerPrefs.GetInt("mpNametagPing", 1) != 0;
        Health = PlayerPrefs.GetInt("mpNametagHealth", 0) != 0;
        Background = PlayerPrefs.GetInt("mpNametagBackground", 0) != 0;
        Ammo = PlayerPrefs.GetInt("mpNametagAmmo", 0) != 0;
        KillDeath = PlayerPrefs.GetInt("mpNametagKD", 0) != 0;
        var scale = PlayerPrefs.GetFloat("mpNametagScale", 1f);
        Scale = float.IsNaN(scale) || float.IsInfinity(scale) ? 1f : Mathf.Clamp(scale, 0.5f, 1.5f);
    }

    internal static void SetPing(bool value) => Set("mpNametagPing", Ping = value);
    
    internal static void SetHealth(bool value) => Set("mpNametagHealth", Health = value);
    
    internal static void SetBackground(bool value) => Set("mpNametagBackground", Background = value);
    
    internal static void SetAmmo(bool value) => Set("mpNametagAmmo", Ammo = value);
    
    internal static void SetKillDeath(bool value) => Set("mpNametagKD", KillDeath = value);

    internal static void SetScale(float value)
    {
        Scale = Mathf.Clamp(value, 0.5f, 1.5f);
        PlayerPrefs.SetFloat("mpNametagScale", Scale);
    }

    private static void Set(string key, bool value) => PlayerPrefs.SetInt(key, value ? 1 : 0);
}