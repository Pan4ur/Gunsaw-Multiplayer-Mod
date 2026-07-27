using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(VehicleBase), "EnterVehicle")]
internal static class KartPassengerEntryPatch
{
    private static bool Prefix(VehicleBase __instance, BodyScript body) => !KartPassengers.TryEnter(__instance, body);
    private static void Postfix(VehicleBase __instance, BodyScript body) => KartPassengers.RegisterDriver(__instance, body);
}

[HarmonyPatch(typeof(BodyScript), "ExitVehicle")]
internal static class KartPassengerExitPatch
{
    private static bool Prefix(BodyScript __instance) => !KartPassengers.ExitPassenger(__instance);
    private static void Postfix(BodyScript __instance) => KartPassengers.Exit(__instance);
}

[HarmonyPatch(typeof(CameraFollow), "LateUpdate")]
internal static class KartCameraSafetyPatch
{
    private static void Prefix(CameraFollow __instance)
    {
        if (__instance != null && __instance.followedBody != null && __instance.followedBody.inVehicle &&
            __instance.followedBody.rb == null)
            __instance.followedBody.inVehicle = false;
    }
}
