using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(BodyWepPickup), "Update")]
internal static class GunGamePickupPatch
{
    /*
    [Harmony dif]
    --- BodyWepPickup.Update
    +++ BodyWepPickup.Update (patched)
    @@
    - Input.GetKeyDown(key)
    + GunGameRule.PickupKeyDown(key)
    */
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var pickupKey = AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) });
        var guardedKey = AccessTools.Method(typeof(GunGameRule), nameof(GunGameRule.PickupKeyDown));
        foreach (var instruction in instructions)
        {
            if (Equals(instruction.operand, pickupKey)) instruction.operand = guardedKey;
            yield return instruction;
        }
    }
}