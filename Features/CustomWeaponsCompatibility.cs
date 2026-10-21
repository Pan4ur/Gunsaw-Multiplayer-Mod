using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
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
    private static MethodInfo getReloadSound;
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

    internal static bool DidFireWeapon(WeaponScript weapon, int ammoBefore, float cooldownBefore)
    {
        if (weapon == null) return false;
        var data = weapon.stats != null && EnsureLookup() ? getWeaponData.Invoke(null, new object[] { weapon.stats.name }) : null;
        if (ReadInt(data, "BurstShotCount") > 1) return false;
        return weapon.ammo < ammoBefore || (data != null && cooldownBefore <= 0f && weapon.cooldown > 0f);
    }

    internal static bool SuppressMagazine(WeaponPreset weapon)
    {
        if (weapon == null || !EnsureLookup()) return false;
        var data = getWeaponData.Invoke(null, new object[] { weapon.name });
        return ReadProperty(data, "NoMagazineDropping") is true;
    }

    internal static void Initialize()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetType("CustomWeapons.HelperScripts.WeaponRegistry") != null);
        if (assembly == null) 
            return;
        
        getReloadSound = assembly.GetType("CustomWeapons.HelperScripts.ReloadSoundManager")?
                                 .GetMethod("GetReloadSound", BindingFlags.Public | BindingFlags.Static);
       
        var prefix = assembly.GetType("CustomWeapons.HelperScripts.ReloadSoundPatch")?
                                        .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
       
        var coroutine = assembly.GetType("CustomWeapons.HelperScripts.SpecialReloadManager")?
                                            .GetMethod("CustomReloadCoroutine", BindingFlags.NonPublic | BindingFlags.Static);
        
        var harmony = new Harmony("gunsaw.multiplayer.customweapons.reload");
        var transpiler = new HarmonyMethod(typeof(CustomWeaponsCompatibility), nameof(ReplicateReloadSound));
     
        if (prefix != null) 
            harmony.Patch(prefix, transpiler: transpiler);
      
        if (coroutine != null) 
            harmony.Patch(AccessTools.EnumeratorMoveNext(coroutine), transpiler: transpiler);
    }

    private static IEnumerable<CodeInstruction> ReplicateReloadSound(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var original = AccessTools.Method(typeof(Sound), nameof(Sound.Play));
        var replacement = AccessTools.Method(typeof(CustomWeaponsCompatibility), nameof(PlayReloadSound));
        var weaponField = __originalMethod.IsStatic ? null : AccessTools.GetDeclaredFields(__originalMethod.DeclaringType).Single(f => f.FieldType == typeof(WeaponScript));
      
        foreach (var i in instructions)
        {
            if (i.Calls(original))
            {
                var loadWeapon = new CodeInstruction(OpCodes.Ldarg_0);
                loadWeapon.MoveLabelsFrom(i);
                loadWeapon.MoveBlocksFrom(i);
                
                yield return loadWeapon;
               
                if (weaponField != null) 
                    yield return new CodeInstruction(OpCodes.Ldfld, weaponField);
                else if (__originalMethod.GetParameters()[0].ParameterType != typeof(WeaponScript))
                    yield return new CodeInstruction(OpCodes.Castclass, typeof(WeaponScript));
               
                i.opcode = OpCodes.Call;
                i.operand = replacement;
            }
            yield return i;
        }
    }

    private static float PlayReloadSound(AudioClip clip, Vector2 position, bool twoDimensional, bool pitchShift, Transform parent, float volume, float pitch, WeaponScript weapon)
    {
        var result = Sound.Play(clip, position, twoDimensional, pitchShift, parent, volume, pitch);
        var player = PlayerScript.player;
        if (clip != null && weapon != null && player != null && weapon.body == player.bodyScript && MultiplayerSession.IsConnected)
            MultiplayerSession.Send(new PlayerSoundPacket(0x60000000u | NetworkAvatarManager.PlayerActionSoundId(clip.name), position.x, position.y, (byte)Mathf.Clamp(Mathf.RoundToInt(volume * 64f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(pitch * 64f), 0, 255)));
      
        return result;
    }

    internal static AudioClip FindReloadSound(WeaponPreset weapon, uint soundId)
    {
        var clip = weapon == null ? null : getReloadSound?.Invoke(null, [weapon.name]) as AudioClip;
        if (clip != null && (0x60000000u | NetworkAvatarManager.PlayerActionSoundId(clip.name)) == soundId)
            return clip;
        
        return Resources.FindObjectsOfTypeAll<AudioClip>().FirstOrDefault(c => c != null && (0x60000000u | NetworkAvatarManager.PlayerActionSoundId(c.name)) == soundId);
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
