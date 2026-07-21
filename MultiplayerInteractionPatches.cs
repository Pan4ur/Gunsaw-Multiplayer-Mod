using System;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(QDoorOpen), "Update")]
internal static class ClientProximityDoorPatch
{
    private static bool Prefix(QDoorOpen __instance)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        if (!Input.GetKeyDown(KeyCode.Q)) return false;
        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        if (body != null && body.isAlive && __instance != null &&
            ((Vector2)body.transform.position - (Vector2)__instance.transform.position).sqrMagnitude < 784f &&
            GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueDoorActivation(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(ActivateZoneScript), "OnTriggerEnter2D")]
internal static class ClientActivationZonePatch
{
    private static bool Prefix(ActivateZoneScript __instance, Collider2D collision)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        if (__instance == null || collision == null || body == null ||
            collision.transform.root != body.transform.root) return false;
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueZoneActivation(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(WeaponScript), "DoWound")]
internal static class MultiplayerPlayerWoundPatch
{
    private static void Prefix(LimbScript limb,
        out NetworkAvatarReplication.TargetScreenEffectState __state)
    {
        __state = NetworkAvatarReplication.BeginTargetScreenEffect(limb == null ? null : limb.body);
    }

    private static void Postfix(WeaponScript __instance, LimbScript limb, Vector2 hitpoint,
        Vector2 dir, GameObject splash)
    {
        NetworkAvatarReplication.RecordRemoteWound(__instance, limb, hitpoint, dir, splash);
    }

    private static Exception Finalizer(Exception __exception,
        NetworkAvatarReplication.TargetScreenEffectState __state)
    {
        NetworkAvatarReplication.EndTargetScreenEffect(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(GlassScript), "Damage")]
internal static class MultiplayerGlassDamagePatch
{
    internal static bool ApplyingNetworkState;

    private static bool Prefix(GlassScript __instance, float dmg, Vector3 bulletPos)
    {
        if (ApplyingNetworkState || !MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueGlassDamage(__instance, dmg, bulletPos);
        return false;
    }
}
