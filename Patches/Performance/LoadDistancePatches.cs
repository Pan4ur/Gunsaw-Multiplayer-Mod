using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(ObjectUnloader), "CheckDistance")]
internal static class MultiplayerObjectUnloaderPatch
{
    private static bool Prefix(ObjectUnloader __instance)
    {
        return !MultiplayerLoadDistance.TryApplyObjectUnloader(__instance);
    }
}

[HarmonyPatch(typeof(BodyScript), "FixedUpdate")]
internal static class MultiplayerNpcBodyFixedUpdateCullPatch
{
    private static bool Prefix(BodyScript __instance)
    {
        return MultiplayerLoadDistance.ShouldTickNpc(__instance);
    }
}

[HarmonyPatch(typeof(BodyScript), "Update")]
internal static class MultiplayerNpcBodyUpdateCullPatch
{
    private static bool Prefix(BodyScript __instance)
    {
        return MultiplayerLoadDistance.ShouldTickNpc(__instance);
    }
}

[HarmonyPatch(typeof(LimbScript), "FixedUpdate")]
internal static class MultiplayerNpcLimbFixedUpdateCullPatch
{
    private static bool Prefix(LimbScript __instance)
    {
        return __instance == null || MultiplayerLoadDistance.ShouldTickNpc(__instance.body);
    }
}

[HarmonyPatch(typeof(CrateScript), "FixedUpdate")]
internal static class MultiplayerCrateTickCullPatch
{
    private static bool Prefix(CrateScript __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(DroppedWeapon), "Update")]
internal static class MultiplayerDroppedWeaponTickCullPatch
{
    private static bool Prefix(DroppedWeapon __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(DoorScript), "FixedUpdate")]
internal static class MultiplayerDoorTickCullPatch
{
    private static bool Prefix(DoorScript __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(MovingBelt), "FixedUpdate")]
internal static class MultiplayerBeltTickCullPatch
{
    private static bool Prefix(MovingBelt __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(RbMoveToObj), "FixedUpdate")]
internal static class MultiplayerRbMoveTickCullPatch
{
    private static bool Prefix(RbMoveToObj __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(SawScript), "Update")]
internal static class MultiplayerSawTickCullPatch
{
    private static bool Prefix(SawScript __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(CustJoint), "FixedUpdate")]
internal static class MultiplayerJointTickCullPatch
{
    private static bool Prefix(CustJoint __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(FireScript), "Update")]
internal static class MultiplayerFireTickCullPatch
{
    private static bool Prefix(FireScript __instance)
    {
        if (MultiplayerSession.IsConnected && !MultiplayerSession.IsHost)
            return WorldReplication.ShouldTickClientFire(__instance);
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}
