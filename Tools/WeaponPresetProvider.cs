using UnityEngine;

public static class WeaponPresetProvider
{
    private static readonly Dictionary<string, WeaponPreset> cache = new();
    private static readonly Dictionary<ulong, WeaponPreset> nameHashCache = new();
    private static readonly Dictionary<ulong, WeaponPreset> spriteHashCache = new();

    private static WeaponPreset FindWeaponPreset<T>(T id, Dictionary<T, WeaponPreset> genericCache, Func<WeaponPreset, bool> allWeaponsMatch, Func<WeaponPreset, bool> resourcesMatch)
    {
        if (genericCache.TryGetValue(id, out var cached) && cached != null)
            return cached;

        if (allWeaponsMatch != null && GameManager.main?.allWeapons != null)
            foreach (var preset in GameManager.main.allWeapons)
                if (preset != null && allWeaponsMatch(preset))
                {
                    genericCache[id] = preset;
                    return preset;
                }

        WeaponPreset fallback = null;

        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
        {
            if (preset == null)
                continue;

            fallback ??= preset;

            if (!resourcesMatch(preset))
                continue;

            genericCache[id] = preset;
            return preset;
        }

        genericCache[id] = fallback;
        GunsawMultiplayerPlugin.LogInfo($"Weapon preset not found for: {id}. Missing some mods?");

        return fallback;
    }

    public static WeaponPreset FindWeaponPresetByNameHash(ulong id)
    {
        if (id == 0) return null;

        if (nameHashCache.TryGetValue(id, out var cached) && cached != null)
            return cached;

        WeaponPreset fallback = null;
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
        {
            if (preset == null)
                continue;

            fallback ??= preset;
            if (NetworkWireId.FromString(preset.name) != id)
                continue;

            nameHashCache[id] = preset;
            return preset;
        }

        if (GameManager.main?.allWeapons != null)
            foreach (var preset in GameManager.main.allWeapons)
                if (preset != null && NetworkWireId.FromString(preset.name) == id)
                {
                    nameHashCache[id] = preset;
                    return preset;
                }

        nameHashCache[id] = fallback;
        GunsawMultiplayerPlugin.LogInfo($"Weapon preset not found for: {id}. Missing some mods?");
        return fallback;
    }

    public static WeaponPreset FindWeaponPresetBySpriteHash(ulong id)
    {
        if (id == 0) return null;

        return FindWeaponPreset(id, spriteHashCache, null, p => NetworkWireId.FromString(NetworkAvatarReplication.SpriteId(p.sprite)) == id);
    }

    public static WeaponPreset FindWeaponPreset(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        
        return FindWeaponPreset(id, cache, p => p.name == id, p => NetworkAvatarReplication.SpriteId(p.sprite) == id);
    }
}