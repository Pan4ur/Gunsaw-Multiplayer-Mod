using HarmonyLib;
using UnityEngine;

internal static partial class TeamSystem
{
    [HarmonyPatch(typeof(Cursor), "set_lockState")]
    private static class CursorLockPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => panel == null || cursorWrite;
    }
}

internal static partial class TeamSystem
{
    [HarmonyPatch(typeof(Cursor), "set_visible")]
    private static class CursorVisiblePatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => panel == null || cursorWrite;
    }
}