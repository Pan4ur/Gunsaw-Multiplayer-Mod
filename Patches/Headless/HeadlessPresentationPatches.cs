using System.Reflection;
using DigitalRuby.RainMaker;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[HarmonyPatch(typeof(RainScript2D), "Update")]
internal static class HeadlessRainUpdatePatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !GunsawMultiplayerPlugin.IsHeadlessMode;
}

[HarmonyPatch(typeof(Graphic), "OnRectTransformDimensionsChange")]
internal static class HeadlessGraphicDimensionsPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !GunsawMultiplayerPlugin.IsHeadlessMode;
}

[HarmonyPatch(typeof(TextMeshProUGUI), "OnRectTransformDimensionsChange")]
internal static class HeadlessTextMeshProDimensionsPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !GunsawMultiplayerPlugin.IsHeadlessMode;
}

[HarmonyPatch]
internal static class HeadlessMouseEventsPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("UnityEngine.SendMouseEvents"), "DoSendMouseEvents");

    [HarmonyPrefix]
    private static bool Prefix() => !GunsawMultiplayerPlugin.IsHeadlessMode;
}

internal static class HeadlessPresentation
{
    internal static void Enable()
    {
        AudioListener.pause = true;
        SceneManager.sceneLoaded += OnSceneLoaded;
        DisableScenePresentation();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => DisableScenePresentation();
    
    private static void DisableScenePresentation()
    {
        var rainScripts = UnityEngine.Object.FindObjectsOfType<RainScript2D>(true);
        for (var index = 0; index < rainScripts.Length; index++)
        {
            var rain = rainScripts[index];
            if (rain != null) rain.enabled = false;
        }
    }
}
