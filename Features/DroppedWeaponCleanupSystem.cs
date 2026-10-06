using System.Reflection;
using HarmonyLib;
using UnityEngine;

internal static class DroppedWeaponCleanupSystem
{
    internal static int DropDepth;

    internal static void Register(DroppedWeapon dropped)
    {
        if (dropped == null || dropped.stats == null || (MultiplayerSession.IsConnected && !MultiplayerSession.IsHost)) 
            return;
        
        var lifetime = dropped.GetComponent<DroppedWeaponLifetime>();
        lifetime ??= dropped.gameObject.AddComponent<DroppedWeaponLifetime>();
        lifetime.ExpiresAt = Time.unscaledTime + 60f;
    }
}

internal sealed class DroppedWeaponLifetime : MonoBehaviour
{
    internal float ExpiresAt;

    private void Update()
    {
        if (MultiplayerSession.IsConnected && !MultiplayerSession.IsHost)
            return;
        
        if (Time.unscaledTime >= ExpiresAt) 
            Destroy(gameObject);
    }
}

[HarmonyPatch]
internal static class DroppedWeaponCleanupSourcePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(BodyScript), "DropWeapon");
        yield return AccessTools.Method(typeof(BodyScript), "DropWeaponSingle");
        yield return AccessTools.Method(typeof(BodyScript), "DropAllWeapons");
        yield return AccessTools.Method(typeof(CrateScript), "Damage");
        yield return AccessTools.Method(typeof(DroppedWeapon), "PickupWeapon");
    }

    private static void Prefix(out bool __state)
    {
        __state = true;
        DroppedWeaponCleanupSystem.DropDepth++;
    }

    private static void Finalizer(bool __state)
    {
        if (__state) DroppedWeaponCleanupSystem.DropDepth--;
    }
}