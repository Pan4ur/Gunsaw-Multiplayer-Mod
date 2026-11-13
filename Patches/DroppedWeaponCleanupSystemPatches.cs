using System.Reflection;
using HarmonyLib;

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