using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(PlayerScript), nameof(PlayerScript.Observify))]
internal static class ObserverActivationPatch
{
    private static bool Prefix(out bool __state)
    {
        __state = ObserverSystem.AllowActivation();
        if (!__state) ObserverSystem.ResetActivationSequence();
        if (__state) ObserverSystem.BeginOriginalActivation();
        return __state;
    }

    private static void Postfix(bool __state)
    {
        if (!__state) return;
        ObserverSystem.EndOriginalActivation();
        ObserverSystem.MarkActive();
        Application.targetFrameRate = -1;
    }

    private static Exception Finalizer(Exception __exception, bool __state)
    {
        if (__state) ObserverSystem.EndOriginalActivation();
        return __exception;
    }
}

[HarmonyPatch(typeof(PlayerScript), "Update")]
internal static class ObserverTargetPatch
{
    private static void Prefix(PlayerScript __instance, out bool __state)
    {
        __state = __instance.observed;
        if (!__state) return;
        __instance.sequence2Index = 0;
        __instance.observed = false;
    }

    private static void Postfix(PlayerScript __instance, bool __state)
    {
        if (__state) __instance.observed = true;
        ObserverSystem.UpdateTarget(__instance);
    }

    private static Exception Finalizer(PlayerScript __instance, bool __state, Exception __exception)
    {
        if (__state) __instance.observed = true;
        return __exception;
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.Pause))]
internal static class ObserverPausePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var observed = AccessTools.Field(typeof(PlayerScript), nameof(PlayerScript.observed));

        foreach (var i in instructions)
        {
            if (i.LoadsField(observed))
            {
                yield return new CodeInstruction(OpCodes.Pop);
                yield return new CodeInstruction(OpCodes.Ldc_I4_0);
            }
            else yield return i;
        }
    }
}

[HarmonyPatch(typeof(Application), nameof(Application.Quit), new Type[0])]
internal static class ObserverQuitPatch
{
    private static bool Prefix() => ObserverSystem.AllowQuit();
}

[HarmonyPatch(typeof(Debug), nameof(Debug.LogError), new[] { typeof(object) })]
internal static class ObserverErrorLogPatch
{
    private static bool Prefix(object message) => !ObserverSystem.SuppressError(message);
}
