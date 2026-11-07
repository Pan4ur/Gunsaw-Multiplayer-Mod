using System.Reflection;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch]
internal static class GraffitiScrollPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
    [
        AccessTools.Method(typeof(PlayerScript), "Update"),
        AccessTools.Method(typeof(LevelEditor), "Update")
    ];

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.PropertyGetter(typeof(Input), nameof(Input.mouseScrollDelta));
        var replacement = AccessTools.Method(typeof(GraffitiSystem), nameof(GraffitiSystem.GameplayScrollDelta));
        
        foreach (var i in instructions)
        {
            if (Equals(i.operand, original))
                i.operand = replacement;
            
            yield return i;
        }
    }
}