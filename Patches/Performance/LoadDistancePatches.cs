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

[HarmonyPatch(typeof(DroppedWeapon), "Awake")]
internal static class MultiplayerDroppedWeaponAmmoIconPatch
{
    private static void Postfix(DroppedWeapon __instance)
    {
        if (__instance == null) return;
        var icon = __instance.transform.Find("myAmmo");
        if (icon == null) return;
        icon.localPosition = Vector3.up * 0.6f;
        icon.localRotation = Quaternion.identity;
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

[HarmonyPatch(typeof(MiniCrateSpawner), "Update")]
internal static class MultiplayerCrateSpawnerTickCullPatch
{
    private static bool Prefix(MiniCrateSpawner __instance, ref float ___curSpawnCool)
    {
        if (!MultiplayerLoadDistance.ShouldTickWorld(__instance)) return false;

        ___curSpawnCool -= Time.deltaTime;
        if (___curSpawnCool >= 0f || __instance.spawnCool == 0f) return false;

        ___curSpawnCool = __instance.spawnCool;
        var occupied = false;
        foreach (var collider in Physics2D.OverlapCircleAll(__instance.transform.position, 0.5f,
                     LayerMask.GetMask("Ground")))
        {
            if (collider.gameObject != __instance.gameObject) occupied = true;
        }

        var renderer = __instance.GetComponent<SpriteRenderer>();
        if (occupied || renderer == null || !renderer.enabled) return false;

        var spawned = UnityEngine.Object.Instantiate(__instance.spawnPrefab, __instance.transform.position,
            __instance.transform.rotation);
        if (spawned != null) spawned.AddComponent<RuntimeSpawnedCrate>();
        GunsawMultiplayerPlugin.World.RegisterRuntimeWorldBodies(spawned);
        return false;
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

[HarmonyPatch(typeof(FireScript), "Awake")]
internal static class MultiplayerFireRegistrationPatch
{
    private static void Postfix(FireScript __instance)
    {
        WorldReplication.RegisterRuntimeWorldFire(__instance);
    }
}
