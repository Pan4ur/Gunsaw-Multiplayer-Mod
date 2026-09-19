using UnityEngine;

public static class WeaponPresetProvider
{
    private static readonly Dictionary<string, WeaponPreset> weaponPresetCache = new();
    private static readonly Dictionary<ulong, WeaponPreset> weaponPresetNameHashCache = new();
    private static readonly Dictionary<ulong, WeaponPreset> weaponPresetSpriteHashCache = new();
    
    public static WeaponPreset FindWeaponPresetByNameHash(ulong weaponId)
    {
        if (weaponId == 0UL) 
            return null;
        
        WeaponPreset cached;
        if (weaponPresetNameHashCache.TryGetValue(weaponId, out cached) && cached != null)
            return cached;
            
        if (GameManager.main != null && GameManager.main.allWeapons != null)
            foreach (var candidate in GameManager.main.allWeapons)
                if (candidate != null && NetworkWireId.FromString(candidate.name) == weaponId)
                {
                    weaponPresetNameHashCache[weaponId] = candidate;
                    return candidate;
                }
        
        WeaponPreset fallback = null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (candidate != null)
            {
                if (fallback == null) fallback = candidate;
                if (NetworkWireId.FromString(candidate.name) == weaponId)
                {
                    weaponPresetNameHashCache[weaponId] = candidate;
                    return candidate; // should be dead now (fallback)
                }
            }
        
        weaponPresetNameHashCache[weaponId] = fallback;
        GunsawMultiplayerPlugin.LogInfo("Weapon preset not found for hash: " + weaponId + ". Missing some mods?");
        return fallback;
    }
    
    public static WeaponPreset FindWeaponPresetBySpriteHash(ulong spriteId)
    {
        if (spriteId == 0UL) return null;

        WeaponPreset cached;
        if (weaponPresetSpriteHashCache.TryGetValue(spriteId, out cached) && cached != null)
            return cached;

        WeaponPreset fallback = null;
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (preset != null)
            {
                if (fallback == null) fallback = preset;
                if (NetworkWireId.FromString(NetworkAvatarReplication.SpriteId(preset.sprite)) != spriteId) continue;
                weaponPresetSpriteHashCache[spriteId] = preset;
                return preset;
            }

        weaponPresetSpriteHashCache[spriteId] = fallback;
        GunsawMultiplayerPlugin.LogInfo("Weapon preset not found for sprite hash: " + spriteId + ". Missing some mods?");
        return fallback;
    }
    public static WeaponPreset FindWeaponPreset(string spriteId)
    {
        if (string.IsNullOrEmpty(spriteId)) return null;
        
        WeaponPreset cached;
        if (weaponPresetCache.TryGetValue(spriteId, out cached) && cached != null)
            return cached;
        
        if (GameManager.main != null && GameManager.main.allWeapons != null)
            foreach (var preset in GameManager.main.allWeapons)
                if (preset != null && preset.name == spriteId)
                {
                    weaponPresetCache[spriteId] = preset;
                    return preset;
                }
        
        WeaponPreset fallback = null;
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (preset != null)
            {
                if (fallback == null) fallback = preset;
                if (NetworkAvatarReplication.SpriteId(preset.sprite) == spriteId)
                {
                    weaponPresetCache[spriteId] = preset;
                    return preset;
                }
            }

        weaponPresetCache[spriteId] = fallback;
        GunsawMultiplayerPlugin.LogInfo("Weapon preset not found for sprite: " + spriteId.Replace("\n", "\\n") + ". Missing some mods?");
        return fallback;
    }
}