using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

internal static partial class CustomWeaponsCompatibility
{
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
}