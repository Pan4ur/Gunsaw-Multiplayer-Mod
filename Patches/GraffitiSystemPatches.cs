using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(Cursor), "set_visible")]
internal static class GraffitiCursorVisibilityPatch
{
    private static void Prefix(ref bool value)
    {
        if (GraffitiSystem.IsPickerOpen) value = true;
    }
}

[HarmonyPatch(typeof(Cursor), "set_lockState")]
internal static class GraffitiCursorLockPatch
{
    private static void Prefix(ref CursorLockMode value)
    {
        if (GraffitiSystem.IsPickerOpen) value = CursorLockMode.None;
    }
}

[HarmonyPatch(typeof(WeaponScript), "Shoot")]
internal static class GraffitiWeaponShotPatch
{
    private static bool Prefix() => !GraffitiSystem.IsPlacementPreview;
}

[HarmonyPatch(typeof(VelvetScript), "Shoot")]
internal static class GraffitiVelvetShotPatch
{
    private static bool Prefix() => !GraffitiSystem.IsPlacementPreview;
}