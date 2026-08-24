using HarmonyLib;

[HarmonyPatch(typeof(VehicleBase), "EnterVehicle")]
internal static class KartPassengerEntryPatch
{
    private static bool Prefix(VehicleBase __instance, BodyScript body) =>
        !MultiplayerSession.IsConnected || !KartPassengers.TryEnter(__instance, body);
    private static void Postfix(VehicleBase __instance, BodyScript body)
    {
        if (MultiplayerSession.IsConnected) KartPassengers.RegisterDriver(__instance, body);
    }
}

[HarmonyPatch(typeof(BodyScript), "ExitVehicle")]
internal static class KartPassengerExitPatch
{
    private static bool Prefix(BodyScript __instance) =>
        !MultiplayerSession.IsConnected || !KartPassengers.ExitPassenger(__instance);
    private static void Postfix(BodyScript __instance)
    {
        if (MultiplayerSession.IsConnected) KartPassengers.Exit(__instance);
    }
}

[HarmonyPatch(typeof(VehicleBase), "LateUpdate")]
internal static class ClientVehicleExplosionPatch
{
    private static void Prefix(VehicleBase __instance)
    {
        if (__instance == null || !MultiplayerSession.IsConnected || MultiplayerSession.IsHost || __instance.health >= -100f || __instance.exploded)
            return;
        
        if (__instance.mainPart != null)
            Sound.Play(__instance.explodeSound, __instance.mainPart.transform.position);
        
        __instance.exploded = true;
        
        foreach (var part in __instance.GetComponentsInChildren<VehiclePart>())
        {
            var renderer = part.GetComponent<UnityEngine.SpriteRenderer>();
            if (renderer != null) renderer.color = new UnityEngine.Color(0.25f, 0.25f, 0.25f, 1f);
        }
    }
}

[HarmonyPatch(typeof(BodyScript), "DoFallDamage")]
internal static class KartPassengerFallDamagePatch
{
    private static bool Prefix(BodyScript __instance) =>
        !MultiplayerSession.IsConnected || !KartPassengers.IsProtectedPassenger(__instance);
}

[HarmonyPatch(typeof(BodyScript), "OnCollisionEnter2D")]
internal static class KartPassengerCollisionDamagePatch
{
    private static bool Prefix(BodyScript __instance) =>
        !MultiplayerSession.IsConnected || !KartPassengers.IsProtectedPassenger(__instance);
}

[HarmonyPatch(typeof(LimbScript), "OnCollisionEnter2D")]
internal static class KartPassengerLimbCollisionDamagePatch
{
    private static bool Prefix(LimbScript __instance) =>
        !MultiplayerSession.IsConnected || __instance == null || !KartPassengers.IsProtectedPassenger(__instance.body);
}

[HarmonyPatch(typeof(BodyScript), "FixedUpdate")]
internal static class KartPassengerGroundSafetyPatch
{
    private static void Postfix(BodyScript __instance)
    {
        if (!MultiplayerSession.IsConnected || !KartPassengers.IsProtectedPassenger(__instance)) return;
        __instance.framesInGround = 0;
        __instance.grounded = false;
        if (__instance.isAlive && __instance.controlState != BodyScript.RagdollState.FullControl)
            __instance.EnterFullControl();
    }
}

[HarmonyPatch(typeof(DismemberManager), "Update")]
internal static class KartPassengerDismemberSafetyPatch
{
    private static bool Prefix(DismemberManager __instance)
    {
        if (!MultiplayerSession.IsConnected || __instance == null || !KartPassengers.IsProtectedPassenger(__instance.body)) return true;
        __instance.currentDamage = 0f;
        return false;
    }
}

[HarmonyPatch(typeof(CameraFollow), "LateUpdate")]
internal static class KartCameraSafetyPatch
{
    private static void Prefix(CameraFollow __instance)
    {
        if (MultiplayerSession.IsConnected && __instance != null && __instance.followedBody != null && __instance.followedBody.inVehicle &&
            __instance.followedBody.rb == null)
            __instance.followedBody.inVehicle = false;
    }
}
