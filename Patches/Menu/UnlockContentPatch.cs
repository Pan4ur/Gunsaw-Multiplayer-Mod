using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(MainMenuManager), "Update")]
internal static class UnlockContentPatch
{
    private static void Postfix(MainMenuManager __instance, ref int ___currentScreen, ref int ___progress)
    {
        if (__instance == null || ___currentScreen != 3 || !Input.GetKeyDown(KeyCode.U))
            return;

        var characterCount = __instance.characters == null ? 0 : __instance.characters.Count;
        for (var index = 0; index < characterCount; index++)
            PlayerPrefs.SetInt("charUnlocked" + index, 1);

        var progress = 17;
        if (MissionSelect.main != null && MissionSelect.main.missions != null)
            foreach (var mission in MissionSelect.main.missions)
                if (mission != null) 
                    progress = Mathf.Max(progress, mission.progressReq);

        PlayerPrefs.SetInt("progress", progress);
        PlayerPrefs.Save();
        ___progress = progress;
        __instance.charUnlocked.text = characterCount + "/" + characterCount + " species unlocked";
        __instance.UpdateCharacter();
    }
}
