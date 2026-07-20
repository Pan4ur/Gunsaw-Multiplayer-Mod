using System;
using System.Collections.Generic;
using System.Diagnostics;

internal static class MultiplayerProfiler
{
    private sealed class Sample
    {
        internal long Count;
        internal double TotalMs;
        internal double MaxMs;
        internal int LastBytes;
    }

    private static readonly object Gate = new object();
    private static readonly Dictionary<string, Sample> Samples = new Dictionary<string, Sample>();
    private static float nextFlush;

    internal static long Begin()
    {
        return Stopwatch.GetTimestamp();
    }

    internal static void End(string name, long started, int bytes = 0)
    {
        if (string.IsNullOrEmpty(name)) return;
        var elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        lock (Gate)
        {
            Sample sample;
            if (!Samples.TryGetValue(name, out sample))
            {
                sample = new Sample();
                Samples[name] = sample;
            }
            sample.Count++;
            sample.TotalMs += elapsed;
            if (elapsed > sample.MaxMs) sample.MaxMs = elapsed;
            if (bytes > 0) sample.LastBytes = bytes;
        }
        FlushIfDue();
    }

    private static void FlushIfDue()
    {
        if (UnityEngine.Time.unscaledTime < nextFlush) return;
        nextFlush = UnityEngine.Time.unscaledTime + 1f;
        var lines = new List<string>();
        lock (Gate)
        {
            foreach (var pair in Samples)
            {
                var sample = pair.Value;
                lines.Add(pair.Key + " count=" + sample.Count +
                    " totalMs=" + sample.TotalMs.ToString("F2") +
                    " maxMs=" + sample.MaxMs.ToString("F2") +
                    " lastBytes=" + sample.LastBytes);
                sample.Count = 0;
                sample.TotalMs = 0.0;
                sample.MaxMs = 0.0;
            }
        }
        if (lines.Count == 0) return;
        MultiplayerDiagnostics.Write("PROFILE queues=" + MultiplayerSession.SendQueueDepth +
            " payloads=" + MultiplayerSession.PayloadQueueDepth + " " + string.Join(" | ", lines));
    }
}

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
