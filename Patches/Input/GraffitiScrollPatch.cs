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

    /*
    [Harmony dif]
    --- PlayerScript.Update / LevelEditor.Update
    +++ PlayerScript.Update / LevelEditor.Update (patched)
    @@
    - Input.mouseScrollDelta
    + GraffitiSystem.GameplayScrollDelta()
    */
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