using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

[HarmonyPatch]
internal static class MultiplayerSceneLoadPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(SceneLoader), "Load"));

    /*
    [Harmony dif]
    --- a/SceneLoader.cs
    +++ b/SceneLoader.cs
    @@ private IEnumerator Load(string scene)
    -    while (fadeAmount < 1f)
    +    while (fadeAmount < 0f)
             yield return null;
    */
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.ToList();
        var fade = AccessTools.Field(typeof(SceneLoader), "fadeAmount");
        for (var i = 0; i + 2 < codes.Count; i++)
        {
            if (!codes[i].LoadsField(fade) || !codes[i + 1].LoadsConstant(1f) || (codes[i + 2].opcode != OpCodes.Blt && codes[i + 2].opcode != OpCodes.Blt_S))
                continue;

            codes[i + 1].operand = 0f;
            return codes;
        }
        throw new InvalidOperationException("Scene loading patch FAILED");
    }
}