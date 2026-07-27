using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using HarmonyLib;

// Prevent abomination from being created when on death screen and hides responsbale text to prevent confussion
[HarmonyPatch(typeof(GameManager), "Update")]
internal static class PreventAbomination
{
    // Replace all KeyCode.R with KeyCode.None
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        bool found = false;

        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.OperandIs(114)) // KeyCode.R
            {
                instruction.opcode = OpCodes.Ldc_I4_0;
                instruction.operand = null; // KeyCode.None
            }

            yield return instruction;
        }

        if (!found)
        {
            UnityEngine.Debug.LogError("[GunsawMP] Failed to patch prevent abomination");
        }
    }

    private static void Postfix(GameManager __instance)
    {
        PlayerScript.player.reformText.SetActive(false);
    }
}
