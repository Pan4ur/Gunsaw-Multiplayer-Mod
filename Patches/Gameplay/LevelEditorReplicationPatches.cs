using System.Reflection;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch]
internal static class LevelEditorMutationPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in new[] { "Update", "UpdatePart", "DuplicateObject", "DeleteObject", "ClearAll", "LoadLevel", "LoadFromClipboard", "SliderUpdated", "SelectObj", "TestSpawnTile", "TestSpawnLamp" })
            yield return AccessTools.Method(typeof(LevelEditor), name);
    }

    [HarmonyPriority(Priority.First)]
    private static bool Prefix(LevelEditor __instance, MethodBase __originalMethod, out GameObject[] __state)
    {
        __state = [];
        if ((__originalMethod.Name == "LoadLevel" || __originalMethod.Name == "LoadFromClipboard") &&
            MultiplayerSession.IsActive && !MultiplayerSession.IsHosting && !LevelEditorReplication.IsLocalEditor)
        {
            __instance.SetInfoText("Only the host can load a level.");
            return false;
        }
        if (!LevelEditorReplication.CanEdit) return false;
        if (__originalMethod.Name == "ClearAll")
            __state = __instance.transform.Cast<Transform>().Select(child => child.gameObject).ToArray();
        else if (__originalMethod.Name == "DeleteObject" && __instance.currentlySelected != null)
            __state = [__instance.currentlySelected];
        return true;
    }

    private static void Postfix(LevelEditor __instance, MethodBase __originalMethod, GameObject[] __state, bool __runOriginal)
    {
        if (!__runOriginal || !MultiplayerSession.IsActive) return;
        if (__originalMethod.Name == "LoadLevel") LevelEditorReplication.DeferCapture();
        foreach (var removed in __state)
        {
            if (removed == null) continue;
            var part = removed.GetComponent<LevelPartGame>();
            if (part != null) CustomPropEditorController.Ensure(__instance).ForgetReplicatedPart(part);
            removed.SetActive(false);
            removed.transform.SetParent(null);
        }
    }
}

[HarmonyPatch]
internal static class LevelEditorPendingChangesPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(LevelEditor), "PlayTest");
        yield return AccessTools.Method(typeof(LevelEditor), "BackToMenu");
    }

    [HarmonyPriority(Priority.First)]
    private static bool Prefix(LevelEditor __instance, MethodBase __originalMethod)
    {
        return LevelEditorReplication.RequestLeave(__instance, __originalMethod.Name == "PlayTest");
    }
}

[HarmonyPatch(typeof(SceneLoader), "LoadScene")]
internal static class LevelEditorSceneTransitionPatch
{
    private static bool Prefix() => LevelEditorReplication.PrepareSceneChange();
}