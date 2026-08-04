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

[HarmonyPatch(typeof(ActivateZoneScript), "OnTriggerEnter2D")]
internal static class HostActivationZonePatch
{
    private static bool Prefix(ActivateZoneScript __instance, Collider2D collision)
    {
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost) return true;
        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        if (__instance == null || collision == null || body == null || collision.transform.root != body.transform.root) return false;
        if (GunsawMultiplayerPlugin.World != null) GunsawMultiplayerPlugin.World.ActivateLocalZone(__instance, false);
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

[HarmonyPatch(typeof(VehiclePart), "OnCollisionEnter2D")]
internal static class ClientVehicleCollisionPatch
{
    private static bool Prefix(VehiclePart __instance, Collision2D col, out float __state)
    {
        __state = __instance == null ? 0f : __instance.health;
        if (__instance == null || col == null || __instance.isWheel || __instance.vehicle == null)
            return !MultiplayerSession.IsConnected || MultiplayerSession.IsHost;

        var localBody = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
        if (MultiplayerSession.IsHost)
        {
            var hostImpact = Mathf.Abs(col.relativeVelocity.magnitude *Mathf.Abs(Vector3.Dot(col.relativeVelocity.normalized, col.GetContact(0).normal)));
            if (hostImpact > 28f)
            {
                if (KartPassengers.IsProtectedPassenger(localBody) && localBody.curVehicle == __instance.vehicle)
                    localBody.ExitVehicle();
                NetworkAvatarReplication.EjectRemoteVehicleOccupants(__instance.vehicle);
            }
            return true;
        }
        
        if (KartPassengers.IsProtectedPassenger(localBody) &&
            localBody.curVehicle == __instance.vehicle && __instance.vehicle.occupant != localBody)
        {
            return false;
        }
        
        var hitBody = col.collider == null ? null : col.collider.GetComponentInParent<BodyScript>();
        
        if (hitBody == null && col.collider != null)
        {
            var hitLimb = col.collider.GetComponentInParent<LimbScript>();
            hitBody = hitLimb == null ? null : hitLimb.body;
        }
        
        if (hitBody != null && hitBody.inVehicle && hitBody.curVehicle == __instance.vehicle && __instance.vehicle.occupant != hitBody) return false;
        if (!MultiplayerSession.IsConnected) return true;
        
        var impact = Mathf.Abs(col.relativeVelocity.magnitude *
            Mathf.Abs(Vector3.Dot(col.relativeVelocity.normalized, col.GetContact(0).normal)));
        if (impact > 6f && GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueVehicleDamage(__instance, impact * __instance.vehicle.impactDamageMult, true);
        return false;
    }

    private static void Postfix(VehiclePart __instance, Collision2D col, float __state)
    {
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || __instance == null ||
            col == null || col.contactCount == 0 || __instance.isWheel || __instance.vehicle == null ||
            __instance.health >= __state || !__instance.doBodyDamage || __instance.health <= 0f ||
            __instance.rb == null || __instance.rb.velocity.magnitude <= 6f) return;

        var impact = Mathf.Abs(col.relativeVelocity.magnitude *
            Mathf.Abs(Vector3.Dot(col.relativeVelocity.normalized, col.GetContact(0).normal)));
        if (impact <= 6f) return;

        BodyScript hitBody;
        bool ragdoll;
        if (col.gameObject.TryGetComponent(out hitBody))
        {
            if (__instance.vehicle.occupant != null && __instance.vehicle.occupant == hitBody) return;
            ragdoll = true;
        }
        else
        {
            LimbScript hitLimb;
            if (!col.gameObject.TryGetComponent(out hitLimb) || hitLimb == null) return;
            hitBody = hitLimb.body;
            ragdoll = false;
        }

        NetworkAvatarReplication.RouteVehicleImpact(hitBody, impact, __instance.transform.position, ragdoll);
    }
}

[HarmonyPatch(typeof(VehiclePart), "Damage")]
internal static class ClientVehicleDamagePatch
{
    private static bool Prefix(VehiclePart __instance, float dmg)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        if (__instance != null && dmg > 0f && GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueVehicleDamage(__instance, dmg, false);
        return false;
    }
}

[HarmonyPatch(typeof(DroneScript), "Damage")]
internal static class ClientDroneDamagePatch
{
    private static bool Prefix(DroneScript __instance, float dmg)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        if (__instance != null && dmg > 0f && GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueDroneDamage(__instance, dmg);
        return false;
    }
}

[HarmonyPatch(typeof(DroneScript), "OnCollisionEnter2D")]
internal static class ClientDroneCollisionPatch
{
    private static bool Prefix(DroneScript __instance, Collision2D col)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        if (__instance == null || col == null || col.contactCount == 0) return false;
        var body = __instance.GetComponent<Rigidbody2D>();
        var impact = Mathf.Abs(col.relativeVelocity.magnitude *
            Mathf.Abs(Vector3.Dot(col.relativeVelocity.normalized, col.GetContact(0).normal)));
        BodyScript hitBody;
        if (col.gameObject.TryGetComponent(out hitBody) &&
            hitBody.controlState == BodyScript.RagdollState.FullControl)
        {
            if (impact > 6f)
            {
                hitBody.lastMoveDir = Vector2.Lerp(hitBody.lastMoveDir,
                    body != null ? body.velocity : Vector2.zero, 0.5f);
                hitBody.shockTime = 1f;
                hitBody.EnterHalfControl();
                if (__instance.hitSound != null) Sound.Play(__instance.hitSound, __instance.transform.position);
            }
        }
        else if (col.gameObject.GetComponent<LimbScript>() == null && impact > 5f &&
            __instance.impactSound != null)
        {
            Sound.Play(__instance.impactSound, __instance.transform.position);
        }
        return false;
    }
}

[HarmonyPatch(typeof(LimbScript), "OnCollisionEnter2D")]
internal static class RemoteVehicleLimbCollisionPatch
{
    private static bool Prefix(LimbScript __instance)
    {
        return __instance == null || !NetworkAvatarReplication.IsRemoteReplicaBody(__instance.body);
    }
}
