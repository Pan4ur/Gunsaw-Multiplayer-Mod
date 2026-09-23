using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(CustJoint), "FixedUpdate")]
internal static class TailScalePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.PropertySetter(typeof(Transform), nameof(Transform.localScale));
        var replacement = AccessTools.Method(typeof(TailScalePatch), nameof(SetTailScale));
        foreach (var i in instructions)
        {
            if (i.Calls(original))
            {
                i.opcode = OpCodes.Call;
                i.operand = replacement;
            }
            yield return i;
        }
    }

    private static void SetTailScale(Transform segment, Vector3 scale)
    {
        scale.y = Mathf.Sign(scale.y);
        scale.z = segment.localScale.z;
        if (segment.localScale != scale)
            segment.localScale = scale;
    }
}
