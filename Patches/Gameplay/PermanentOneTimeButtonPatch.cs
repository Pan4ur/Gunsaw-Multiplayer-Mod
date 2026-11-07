using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

internal sealed class PermanentOneTimeButton : MonoBehaviour;

[HarmonyPatch(typeof(LevelLoader), "Start")]
internal static class PermanentOneTimeButtonPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var field = AccessTools.Field(typeof(ButtonScript), nameof(ButtonScript.activateOnce));
        for (var i = 2; i < codes.Count; i++)
        {
            if (!codes[i].StoresField(field) || codes[i - 1].opcode != OpCodes.Cgt || codes[i - 2].opcode != OpCodes.Ldc_I4_0)
                continue;
            
            codes[i].opcode = OpCodes.Call;
            codes[i].operand = AccessTools.Method(typeof(PermanentOneTimeButtonPatch), nameof(Configure));
            codes[i].MoveLabelsFrom(codes[i - 2]);
            codes.RemoveRange(i - 2, 2);
            return codes;
        }
        
        throw new InvalidOperationException("Button activation mode assignment not found");
    }

    private static void Configure(ButtonScript button, int mode)
    {
        button.activateOnce = mode > 0;
        if (mode == 2) button.gameObject.AddComponent<PermanentOneTimeButton>();
    }
}