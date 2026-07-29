using HarmonyLib;

[HarmonyPatch(typeof(BodyScript), "Start")]
internal static class NpcBodyRegistryPatch
{
    private static void Postfix(BodyScript __instance)
    {
        if (NpcReplication.Instance != null) NpcReplication.Instance.RegisterBody(__instance);
    }
}
