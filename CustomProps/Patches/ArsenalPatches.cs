using HarmonyLib;

[HarmonyPatch(typeof(GameManager), nameof(GameManager.Pause))]
internal static class ArsenalPausePatch
{
    private static bool Prefix() => !ArsenalMenu.BlocksPause;
}