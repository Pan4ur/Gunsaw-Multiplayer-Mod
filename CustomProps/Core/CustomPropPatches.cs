using HarmonyLib;
using System.Reflection;
using UnityEngine;

[HarmonyPatch(typeof(LevelEditor), "Start")]
internal static class CustomPropEditorStartPatch
{
    private static void Postfix(LevelEditor __instance)
    {
        CustomPropEditorController.Ensure(__instance);
    }
}

[HarmonyPatch(typeof(LevelEditor), "LoadLevel")]
internal static class CustomPropEditorLoadPatch
{
    private static void Prefix(LevelEditor __instance, bool __runOriginal)
    {
        if (!__runOriginal || !LevelEditorReplication.CanEdit) return;
        CustomPropEditorController.ReadLevel(__instance);
    }

    private static void Postfix(LevelEditor __instance, bool __runOriginal)
    {
        if (!__runOriginal || !LevelEditorReplication.CanEdit) return;
        CustomPropEditorController.FinishLevelLoad(__instance);
    }
}

[HarmonyPatch(typeof(LevelEditor), "GetLevelCode")]
internal static class CustomPropEditorSavePatch
{
    private static void Prefix(LevelEditor __instance)
    {
        var controller = CustomPropEditorController.Ensure(__instance);
        if (controller != null)
        {
            controller.CommitPlayerSpawnTeam();
            controller.CommitColoredLampId();
        }
    }

    private static void Postfix(LevelEditor __instance, bool __0, ref string __result)
    {
        __result = CustomPropEditorController.WriteLevel(__instance, __result);
    }
}

[HarmonyPatch(typeof(LevelEditor), "DuplicateObject")]
internal static class CustomPropEditorDuplicatePatch
{
    private static void Prefix(LevelEditor __instance)
    {
        if (!LevelEditorReplication.CanEdit) return;
        var controller = CustomPropEditorController.Ensure(__instance);
        if (controller != null)
            controller.PrepareDuplicate();
    }

    private static void Postfix(LevelEditor __instance)
    {
        if (!LevelEditorReplication.CanEdit) return;
        var controller = CustomPropEditorController.Ensure(__instance);
        if (controller != null)
            controller.DuplicateSelected();
    }
}

[HarmonyPatch]
internal static class CustomPropEditorPastePatch
{
    private static readonly FieldInfo? clipboard = typeof(LevelEditor).GetField("objectClipboard", AccessTools.all);
    private static readonly MethodInfo? saveState = typeof(LevelEditor).GetMethod("SaveState", AccessTools.all);

    private sealed class PasteState
    {
        internal List<LevelPart> Clipboard = null!;
        internal GameObject[] Created = [];
        internal bool OnlyCustom;
    }

    private static bool Prepare() => clipboard != null;

    private static IEnumerable<MethodBase> TargetMethods() =>
        typeof(LevelEditor).GetMethods(AccessTools.all).Where(method => method.Name == "PasteFromClipboard");

    private static bool Prefix(LevelEditor __instance, object[] __args, out PasteState? __state)
    {
        __state = null;
        if (!LevelEditorReplication.CanEdit) return false;
        if (clipboard!.GetValue(__instance) is not List<LevelPart> parts) return true;
        var custom = parts.Where(part => part.path == "MP/CustomProp").ToArray();
        if (custom.Length == 0) return true;
        var center = __args.Length > 0 && __args[0] is Vector3 position
            ? position
            : (Vector3)__instance.alignToGrid(Camera.main.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, Camera.main.nearClipPlane + 10f)));
        __state = new PasteState { Clipboard = parts, OnlyCustom = custom.Length == parts.Count };
        __state.Created = CustomPropEditorController.Ensure(__instance).PasteCustomParts(custom, center);
        clipboard.SetValue(__instance, parts.Where(part => part.path != "MP/CustomProp").ToList());
        return true;
    }

    private static void Postfix(LevelEditor __instance, PasteState? __state, bool __runOriginal)
    {
        if (__state == null || !__runOriginal) return;
        LevelEditorPlusCompatibility.SelectPasted(__instance, __state.Created, __state.OnlyCustom);
        __instance.SetInfoText(__state.Clipboard.Count + " object(s) pasted.");
        if (__state.OnlyCustom) saveState?.Invoke(__instance, null);
    }

    private static Exception? Finalizer(LevelEditor __instance, PasteState? __state, Exception? __exception)
    {
        if (__state != null) clipboard!.SetValue(__instance, __state.Clipboard);
        return __exception;
    }
}

[HarmonyPatch(typeof(LevelLoader), "Start")]
internal static class CustomPropRuntimeLoadPatch
{
    private static void Prefix(LevelLoader __instance)
    {
        CustomPropEditorController.PrepareRuntime(__instance);
    }

    private static void Postfix()
    {
        WeaponPropRespawnSystem.AttachRuntime();
        ToggleableLampSystem.AttachRuntime();
        
        CustomPropEditorController.CreateRuntime();
    }
}

[HarmonyPatch(typeof(LevelEditor), "SelectPart")]
internal static class CustomPropEditorSelectPartPatch
{
    private static void Prefix(LevelEditor __instance)
    {
        var controller = CustomPropEditorController.Ensure(__instance);
        if (controller != null) controller.RestoreNativeInspector();
    }

    private static void Postfix(LevelEditor __instance)
    {
        var controller = CustomPropEditorController.Ensure(__instance);
        if (controller != null)
        {
            controller.RefreshInspectorImmediate();
            controller.EnableTeamFieldForPlayerSpawn();
            controller.EnableIdFieldForColoredLamp();
            controller.EnableSizeFieldForLampAngle();
            controller.EnableRespawnTimeForWeapon();
        }
    }
}
