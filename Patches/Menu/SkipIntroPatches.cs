using HarmonyLib;
using UnityEngine.SceneManagement;

[HarmonyPatch(typeof(ViolenceScreen), "Update")]
internal static class SkipViolenceWarningPatch
{
    private static bool Prefix()
    {
        SceneManager.LoadScene("LevelSelect");
        return false;
    }
}
