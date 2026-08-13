using HarmonyLib;

[HarmonyPatch(typeof(PlayerScript), "Update")]
internal static class CheatRulePatch
{
    private static void Postfix(PlayerScript __instance)
    {
        if (!MultiplayerSession.IsActive || MultiplayerSession.CheatsEnabled || __instance == null) return;
        if (GameManager.main != null) GameManager.main.debugMode = false;
        if (__instance.debugText != null) __instance.debugText.SetActive(false);
    }
}
