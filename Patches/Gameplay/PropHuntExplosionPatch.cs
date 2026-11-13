using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch]
internal static class PropHuntExplosionPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(FireScript), "Update");
        yield return AccessTools.Method(typeof(ExplosionHandler), nameof(ExplosionHandler.CreateExplosion));
    }

    /*
    [Harmony dif]
    --- FireScript.Update / ExplosionHandler.CreateExplosion
    +++ FireScript.Update / ExplosionHandler.CreateExplosion (patched)
    @@
    - Physics2D.OverlapCircleAll(point, radius)
    + PropHuntExplosionPatch.Filter(Physics2D.OverlapCircleAll(point, radius))
    */
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.Method(typeof(Physics2D), nameof(Physics2D.OverlapCircleAll), [typeof(Vector2), typeof(float)]);
        var replacement = AccessTools.Method(typeof(PropHuntExplosionPatch), nameof(Filter));
        
        foreach (var i in instructions)
        {
            yield return i;
            if (i.Calls(original)) 
                yield return new CodeInstruction(OpCodes.Call, replacement);
        }
    }

    internal static Collider2D[] Filter(Collider2D[] list)
    {
        if (!PropHuntSystem.Enabled) 
            return list;
        
        // todo profile
        return Array.FindAll(list, c => c != null && c.GetComponentInParent<PropHuntHitbox>() == null &&
            !PropHuntSystem.IsProp(c.GetComponent<LimbScript>()?.body ?? c.GetComponentInParent<BodyScript>()));
    }
}