using HarmonyLib;
using System.Reflection.Emit;
using UnityEngine;

[HarmonyPatch(typeof(BodyScript), "FixedUpdate")]
internal static class PropHuntCrushPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var overlap = AccessTools.Method(typeof(Physics2D), nameof(Physics2D.OverlapPoint), new[] { typeof(Vector2), typeof(int) });
        var replacement = AccessTools.Method(typeof(PropHuntCrushPatch), nameof(OverlapGround));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(overlap))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                instruction.operand = replacement;
            }
            yield return instruction;
        }
    }

    internal static Collider2D OverlapGround(Vector2 point, int mask, BodyScript body)
    {
        if (!PropHuntSystem.Enabled) return Physics2D.OverlapPoint(point, mask);
        if (PropHuntSystem.IsProp(body)) return null;
        foreach (var collider in Physics2D.OverlapPointAll(point, mask))
            if (collider.GetComponentInParent<PropHuntHitbox>() == null) return collider;
        return null;
    }
}
