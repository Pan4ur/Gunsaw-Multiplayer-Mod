using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

[HarmonyPatch]
internal static class UiMaskComponentPoolPatch
{
    private static readonly ObjectPool<List<Component>> maskComponents = new(() => new List<Component>(), actionOnRelease: list => list.Clear());

    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return typeof(MaskUtilities).GetMethod(nameof(MaskUtilities.Notify2DMaskStateChanged));
        yield return typeof(MaskUtilities).GetMethod(nameof(MaskUtilities.NotifyStencilStateChanged));
    }

    /*
    [Harmony dif]
    --- MaskUtilities.Notify2DMaskStateChanged / NotifyStencilStateChanged
    +++ MaskUtilities.Notify2DMaskStateChanged / NotifyStencilStateChanged (patched)
    @@
    - CollectionPool<List<Component>, Component>.Get()
    + UiMaskComponentPoolPatch.GetComponents()
    - CollectionPool<List<Component>, Component>.Release(components)
    + UiMaskComponentPoolPatch.ReleaseComponents(components)
    */
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var get = typeof(CollectionPool<List<Component>, Component>).GetMethod("Get", Type.EmptyTypes);
        var release = typeof(CollectionPool<List<Component>, Component>).GetMethod("Release");
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        foreach (var i in instructions)
        {
            if (i.Calls(get))
                i.operand = typeof(UiMaskComponentPoolPatch).GetMethod(nameof(GetComponents), flags);
            else if (i.Calls(release))
                i.operand = typeof(UiMaskComponentPoolPatch).GetMethod(nameof(ReleaseComponents), flags);
            yield return i;
        }
    }

    private static List<Component> GetComponents() => maskComponents.Get();

    private static void ReleaseComponents(List<Component> components) => maskComponents.Release(components);
}
