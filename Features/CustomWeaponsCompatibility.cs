using System.Collections;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

internal readonly struct CustomProjectileVisualInfo
{
    internal readonly Sprite Sprite;
    internal readonly float Speed;
    internal readonly float Lifetime;
    internal readonly int Ricochets;

    internal CustomProjectileVisualInfo(Sprite sprite, float speed, float lifetime, int ricochets)
    {
        Sprite = sprite;
        Speed = speed;
        Lifetime = lifetime;
        Ricochets = ricochets;
    }
}

internal static class CustomWeaponsCompatibility
{
    private static Type registryType;
    private static Type assetManagerType;
    private static MethodInfo getWeaponData;
    private static MethodInfo getProjectileData;
    private static MethodInfo loadProjectileSprites;
    private static Type meleeWeaponType;
    private static Type addVfxType;
    private static Type meleeVisualType;
    private static Type meleeComboType;
    private static readonly HashSet<object> remoteMeleeControllers = new();
    private static bool meleeRaycastPatched;
    private static bool lookupComplete;

    internal static bool TryGetProjectileVisual(WeaponPreset weapon, out CustomProjectileVisualInfo info)
    {
        info = default;
        if (weapon == null || !EnsureLookup())
            return false;
        
        var weaponData = getWeaponData.Invoke(null, new object[] { weapon.name });
        if (weaponData == null || ReadInt(weaponData, "ShootType") != 1)
            return false;
       
        var projectileName = ReadString(weaponData, "ProjectileDataName");
        if (string.IsNullOrEmpty(projectileName))
            return false;
       
        var projectileData = getProjectileData.Invoke(null, new object[] { projectileName });
        if (projectileData == null)
            return false;
       
        var speed = Mathf.Max(0.01f, ReadFloat(projectileData, "MoveSpeed", 50f));
        var lifetime = Mathf.Max(0.05f, ReadFloat(projectileData, "LifeTime", 2f));
        info = new CustomProjectileVisualInfo(LoadSprite(ReadString(projectileData, "SpritePath")), speed, lifetime, ReadRicochets(projectileData));
        return true;
    }

    internal static bool IsFiredCustomMelee(WeaponScript weapon)
    {
        if (weapon == null || weapon.stats == null || weapon.stats.range != 0f || weapon.cooldown <= 0f || !EnsureLookup())
            return false;
        
        return getWeaponData.Invoke(null, new object[] { weapon.stats.name }) != null;
    }

    internal static bool TryPlayRemoteMelee(WeaponPreset weapon, WeaponScript remoteWeapon, BodyScript wielder)
    {
        if (weapon == null || wielder == null || !EnsureLookup()) 
            return false;
        
        try
        {
            var weaponData = getWeaponData.Invoke(null, new object[] { weapon.name });
            if (weaponData == null || weapon.range != 0f)
                return false;
            
            var getConfig = meleeWeaponType.GetMethod("GetMeleeConfig", BindingFlags.Public | BindingFlags.Static);
           
            var config = getConfig.Invoke(null, new object[] { weapon.name });
            if (config == null)
            {
                var description = ReadString(weaponData, "HitscanVFX");
               
                if (string.IsNullOrEmpty(description)) 
                    return false;
                
                config = meleeWeaponType.GetMethod("ParseMeleeConfig", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { description, remoteWeapon });
                meleeWeaponType.GetMethod("RegisterMeleeWeapon", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { weapon.name, config });
            }
            
            EnsureMeleeRaycastPatch();
            var controller = new GameObject("MP CWF Melee Visual").AddComponent(meleeComboType);
            remoteMeleeControllers.Add(controller);
            meleeComboType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance) .Invoke(controller, new object[] { config, remoteWeapon, wielder });
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool AllowCustomMeleeRaycast(object __instance) => !remoteMeleeControllers.Contains(__instance);

    private static void EnsureMeleeRaycastPatch()
    {
        if (meleeRaycastPatched)
            return;
        
        var raycast = meleeComboType.GetMethod("PerformRaycastAttack", BindingFlags.NonPublic | BindingFlags.Instance);
        if (raycast == null) 
            throw new MissingMethodException(meleeComboType.FullName, "PerformRaycastAttack");
       
        new Harmony("gunsaw.multiplayer.customweapons.melee").Patch(raycast, prefix: new HarmonyMethod(typeof(CustomWeaponsCompatibility), nameof(AllowCustomMeleeRaycast)));
        meleeRaycastPatched = true;
    }

    private static bool EnsureLookup()
    {
        if (lookupComplete) 
            return registryType != null && assetManagerType != null && getWeaponData != null && getProjectileData != null && loadProjectileSprites != null;
       
        lookupComplete = true;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            registryType ??= assembly.GetType("CustomWeapons.HelperScripts.WeaponRegistry");
            assetManagerType ??= assembly.GetType("AssetManager");
            meleeWeaponType ??= assembly.GetType("CustomWeapons.HelperScripts.CustomMeleeWeapon");
            addVfxType ??= assembly.GetType("CustomWeapons.ProjectileModifierScripts.AddVFX");
            meleeVisualType ??= assembly.GetType("CustomWeapons.HelperScripts.MeleeVisualEffect");
            meleeComboType ??= assembly.GetType("CustomWeapons.HelperScripts.MeleeComboController");
        }
      
        if (registryType == null || assetManagerType == null)
            return false;
       
        getWeaponData = registryType.GetMethod("GetWeaponData", BindingFlags.Public | BindingFlags.Static);
        getProjectileData = registryType.GetMethod("GetProjectileData", BindingFlags.Public | BindingFlags.Static);
        loadProjectileSprites = assetManagerType.GetMethod("LoadProjectileSprites", BindingFlags.Public | BindingFlags.Static);
        return getWeaponData != null && getProjectileData != null && loadProjectileSprites != null && meleeWeaponType != null && addVfxType != null && meleeVisualType != null && meleeComboType != null;
    }

    private static Sprite LoadSprite(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
      
        var sprites = loadProjectileSprites.Invoke(null, new object[] { path, 25f }) as IEnumerable;
        if (sprites == null)
            return null;
       
        foreach (var item in sprites)
            if (item is Sprite sprite) 
                return sprite;
    
        return null;
    }

    private static int ReadRicochets(object projectileData)
    {
        var data = ReadProperty(projectileData, "CustomScriptData") as IDictionary;
        if (data == null) 
            return 0;
       
        foreach (DictionaryEntry entry in data)
        {
            if (!(entry.Key is string key) || !key.Equals("ProjectileRicochet", StringComparison.OrdinalIgnoreCase))
                continue;
           
            if (!(entry.Value is IEnumerable values))
                return 0;
           
            foreach (var value in values)
                if (int.TryParse(value as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                    return Mathf.Max(0, count);
        }
        return 0;
    }

    private static object ReadProperty(object instance, string name) => instance?.GetType().GetProperty(name)?.GetValue(instance);
    
    private static string ReadString(object instance, string name) => ReadProperty(instance, name) as string;
    
    private static int ReadInt(object instance, string name) => ReadProperty(instance, name) is int result ? result : 0;
    
    private static float ReadFloat(object instance, string name, float fallback) => ReadProperty(instance, name) is float result ? result : fallback;
}
