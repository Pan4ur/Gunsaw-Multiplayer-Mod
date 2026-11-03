using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(Canvas), "SendPreWillRenderCanvases")]
internal static class HeadlessPreWillRenderCanvasesPatch
{
    private static bool Prepare() => HeadlessLobbyService.IsHeadlessMode;

    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !HeadlessLobbyService.IsHeadlessMode;
    }
}

[HarmonyPatch(typeof(Canvas), "SendWillRenderCanvases")]
internal static class HeadlessWillRenderCanvasesPatch
{
    private static bool Prepare() => HeadlessLobbyService.IsHeadlessMode;

    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !HeadlessLobbyService.IsHeadlessMode;
    }
}
