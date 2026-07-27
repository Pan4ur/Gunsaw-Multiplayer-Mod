using System;
using System.Collections.Generic;
using System.Diagnostics;

[HarmonyLib.HarmonyPatch(typeof(GameManager), "Update")]
internal static class MultiplayerGameManagerProfilerPatch
{
    private static void Prefix(out long __state)
    {
        __state = MultiplayerProfiler.Begin();
    }

    private static void Postfix(long __state)
    {
        MultiplayerProfiler.End("GameManager.Update", __state);
    }
}

[HarmonyLib.HarmonyPatch(typeof(NpcReplication), "SerializeSnapshot")]
internal static class MultiplayerNpcSerializationProfilerPatch
{
    private static void Prefix(out long __state)
    {
        __state = MultiplayerProfiler.Begin();
    }

    private static void Postfix(long __state, byte[] __result)
    {
        MultiplayerProfiler.End("Npc.SerializeSnapshot", __state, __result == null ? 0 : __result.Length);
    }
}

[HarmonyLib.HarmonyPatch(typeof(WorldReplication), "SerializeWorld")]
internal static class MultiplayerWorldSerializationProfilerPatch
{
    private static void Prefix(out long __state)
    {
        __state = MultiplayerProfiler.Begin();
    }

    private static void Postfix(long __state, byte[] __result)
    {
        MultiplayerProfiler.End("World.SerializeWorld", __state, __result == null ? 0 : __result.Length);
    }
}
