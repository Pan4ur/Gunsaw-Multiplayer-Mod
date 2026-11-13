using HarmonyLib;

[HarmonyPatch(typeof(BodyScript), "ChangeWeapon")]
internal static class PlayerCarryWeaponPatch
{
    private static bool Prefix(BodyScript __instance) => !PlayerCarrySystem.IsLocalCarrier || __instance != PlayerScript.player?.bodyScript;
}

[HarmonyPatch(typeof(BodyScript), "SwitchDir")]
internal static class PlayerCarryDirectionPatch
{
    private static bool Prefix(BodyScript __instance) => !PlayerCarrySystem.IsCarriedTarget(__instance);
}

[HarmonyPatch(typeof(BodyScript), "Update")]
internal static class PlayerCarryBodyPosePatch
{
    private static void Prefix(BodyScript __instance) => PlayerCarrySystem.PreBodyUpdate(__instance);

    private static void Postfix(BodyScript __instance) => PlayerCarrySystem.PostBodyUpdate(__instance);
}

[HarmonyPatch(typeof(PlayerScript), "Update")]
internal static class PlayerCarryPickupCrouchPatch
{
    private static void Postfix() => PlayerCarrySystem.ApplyPickupCrouch();
}

[HarmonyPatch(typeof(BodyScript), "FixedUpdate")]
internal static class PlayerCarryBodyPhysicsPatch
{
    private static void Prefix(BodyScript __instance) => PlayerCarrySystem.PreBodyFixedUpdate(__instance);
}