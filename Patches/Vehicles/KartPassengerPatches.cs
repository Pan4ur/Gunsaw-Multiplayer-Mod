using HarmonyLib;

[HarmonyPatch(typeof(VehicleBase), "EnterVehicle")]
internal static class KartPassengerEntryPatch
{
    private static bool Prefix(VehicleBase __instance, BodyScript body) =>
        !MultiplayerSession.IsConnected || (!PlayerCarrySystem.IsCarriedTarget(body) && !KartPassengers.TryEnter(__instance, body));
    private static void Postfix(VehicleBase __instance, BodyScript body)
    {
        if (MultiplayerSession.IsConnected) KartPassengers.RegisterDriver(__instance, body);
    }
}

[HarmonyPatch(typeof(BodyScript), "ExitVehicle")]
internal static class KartPassengerExitPatch
{
    private static bool Prefix(BodyScript __instance, out VehicleBase __state)
    {
        __state = null;
        if (!MultiplayerSession.IsConnected) return true;
        var vehicle = __instance?.curVehicle;
        if (vehicle == null || vehicle.mainPart == null || vehicle.mainPart.rb == null) return false;
        __state = vehicle;
        if (vehicle.occupant == __instance && vehicle.occupJoint != null)
            vehicle.occupJoint.enabled = false;
        return !KartPassengers.ExitPassenger(__instance);
    }

    private static void Postfix(BodyScript __instance, VehicleBase __state)
    {
        if (MultiplayerSession.IsConnected && __state != null && !__instance.inVehicle)
            KartPassengers.Exit(__instance, __state);
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

[HarmonyPatch(typeof(BodyScript), "EnterHalfControl")]
internal static class KartPassengerRagdollPatch
{
    private static bool Prefix(BodyScript __instance)
    {
        if (!MultiplayerSession.IsConnected || !KartPassengers.IsProtectedPassenger(__instance)) return true;
        KartPassengers.ReportPassengerRagdoll(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(BodyScript), "FixedUpdate")]
internal static class KartPassengerGroundSafetyPatch
{
    private static void Prefix(BodyScript __instance)
    {
        if (MultiplayerSession.IsConnected) KartPassengers.MaintainPassenger(__instance);
    }

    private static void Postfix(BodyScript __instance)
    {
        if (MultiplayerSession.IsConnected) KartPassengers.MaintainPassenger(__instance);
    }
}

[HarmonyPatch(typeof(BodyScript), "Update")]
internal static class KartPassengerUpdateSafetyPatch
{
    private static void Postfix(BodyScript __instance)
    {
        if (MultiplayerSession.IsConnected) KartPassengers.MaintainPassenger(__instance);
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

[HarmonyPatch(typeof(VehicleBase), "FixedUpdate")]
internal static class ClientVehiclePhysicsPatch
{
    private static bool Prefix(VehicleBase __instance)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost)
            return true;

        var driver = __instance.occupant;
        if (driver != null && (driver.controlState != BodyScript.RagdollState.FullControl || __instance.health <= 0f))
        {
            driver.ExitVehicle();
            driver = null;
        }

        __instance.gasPressed = driver != null && __instance.health > 0f && !__instance.engineDisabled && (driver.CurrentState == BodyScript.EntityState.MoveLeft || driver.CurrentState == BodyScript.EntityState.MoveRight);
        return false;
    }
}