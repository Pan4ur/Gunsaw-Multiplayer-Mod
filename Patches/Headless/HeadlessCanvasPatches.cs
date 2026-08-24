using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(Canvas), "SendPreWillRenderCanvases")]
internal static class HeadlessPreWillRenderCanvasesPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !GunsawMultiplayerPlugin.IsHeadlessMode;
    }
}

[HarmonyPatch(typeof(Canvas), "SendWillRenderCanvases")]
internal static class HeadlessWillRenderCanvasesPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !GunsawMultiplayerPlugin.IsHeadlessMode;
    }
}