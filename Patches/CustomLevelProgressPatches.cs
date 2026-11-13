using HarmonyLib;
using UnityEngine.SceneManagement;

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.FinishMission))]
internal static class CustomLevelProgressPatch
{
    private static void Postfix(MissionManager __instance)
    {
        if (!CustomLevelProgress.HasActive || SceneManager.GetActiveScene().name != "LevelLoader" || __instance.finalRankText == null) return;
        CustomLevelProgress.Record(__instance.finalRankText.text);
    }
}