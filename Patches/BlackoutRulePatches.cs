using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(FireScript), "Awake")]
internal static class BlackoutFirePatch
{
    private static void Postfix(FireScript __instance) => BlackoutRule.RegisterFire(__instance);
}

[HarmonyPatch(typeof(RocketProjectile), "Update")]
internal static class BlackoutRocketVisualPatch
{
    private static void Prefix(RocketProjectile __instance)
    {
        if (BlackoutRule.IsApplied && __instance != null)
            BlackoutRule.MakeAlwaysBright(__instance.gameObject);
    }
}

[HarmonyPatch(typeof(GrenadeScript), "OnCollisionEnter2D")]
internal static class BlackoutGrenadeVisualPatch
{
    private static void Prefix(GrenadeScript __instance, out GameObject __state)
    {
        __state = BlackoutRule.IsApplied && __instance != null ? __instance.objOnDestroy : null;
    }

    private static void Postfix(GrenadeScript __instance, GameObject __state)
    {
        if (BlackoutRule.IsApplied && __instance != null)
            BlackoutRule.MakeInstantiatedEffectBright(__state, __instance.transform.position);
    }
}

[HarmonyPatch(typeof(BodyScript), "ChangeWeapon")]
internal static class BlackoutWeaponLaserMaterialPatch
{
    private static void Postfix(BodyScript __instance)
    {
        if (BlackoutRule.IsApplied && __instance != null)
            BlackoutRule.MakeWeaponLaserBright(__instance.wepLaserLine);
    }
}

[HarmonyPatch(typeof(ScarfPhysics), "Start")]
internal static class BlackoutScarfMaterialPatch
{
    private static void Postfix(ScarfPhysics __instance)
    {
        if (BlackoutRule.IsApplied)
            BlackoutRule.ApplyScarfMaterial(__instance);
    }
}

[HarmonyPatch(typeof(LimbScript), "DoWallDrop")]
internal static class BlackoutWallBloodPatch
{
    private static void Postfix(LimbScript __instance)
    {
        if (BlackoutRule.IsApplied && __instance != null)
            BlackoutRule.ApplyBloodAt(__instance.transform.position);
    }
}

[HarmonyPatch(typeof(CrateScript), "Damage")]
internal static class BlackoutPropParticlePatch
{
    private static void Prefix(CrateScript __instance, out GameObject __state)
    {
        __state = BlackoutRule.IsApplied && __instance != null ? __instance.objOnDestroy : null;
        BlackoutRule.ApplyToObject(__state);
    }
}