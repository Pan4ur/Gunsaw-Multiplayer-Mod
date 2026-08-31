using HarmonyLib;

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
    private static void Prefix(LevelEditor __instance)
    {
        CustomPropEditorController.ReadLevel(__instance);
    }

    private static void Postfix(LevelEditor __instance)
    {
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

[HarmonyPatch(typeof(LevelLoader), "Start")]
internal static class CustomPropRuntimeLoadPatch
{
    private static void Prefix(LevelLoader __instance)
    {
        CustomPropEditorController.PrepareRuntime(__instance);
    }

    private static void Postfix()
    {
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
        }
    }
}
