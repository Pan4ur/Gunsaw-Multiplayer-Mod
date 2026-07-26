using System.Diagnostics;
using UnityEngine;

internal enum MultiplayerPerformancePhase
{
    WorldDiscovery,
    WorldSerialize,
    WorldSnapshotRead,
    WorldStateApply,
    WorldInput,
    WorldContacts,
    NpcDiscovery,
    NpcAnimation,
    NpcSerialize,
    NpcSnapshotRead,
    NpcStateApply,
    NpcInterpolate,
    WorldSerializeBodies,
    WorldSerializeEnvironment,
    WorldSnapshotObjects,
    NpcSerializeStates,
    NpcCompress,
    NpcDecompress,
    NpcSnapshotParse,
    NpcProxyLookup,
    NpcStatePose,
    NpcVisuals,
    WorldSnapshotParse,
    WorldEnvironmentApply,
    WorldSnapshotWireResolve,
    WorldSnapshotDecode,
    WorldSnapshotDispatch
}

internal static class MultiplayerPerformance
{
    private static double pendingNpcMs;
    private static double pendingWorldMs;
    private static double pendingAvatarMs;
    private static double pendingAvatarSerializeMs;
    private static double pendingAvatarApplyMs;
    private static double pendingDistanceMs;
    private static readonly double[] pendingPhaseMs = new double[28];
    private static readonly float[] phaseMillisecondsPerSecond = new float[28];
    private static float nextSample;

    internal static float NpcMillisecondsPerSecond { get; private set; }
    internal static float WorldMillisecondsPerSecond { get; private set; }
    internal static float AvatarMillisecondsPerSecond { get; private set; }
    internal static float AvatarSerializeMillisecondsPerSecond { get; private set; }
    internal static float AvatarApplyMillisecondsPerSecond { get; private set; }
    internal static float DistanceMillisecondsPerSecond { get; private set; }
    internal static bool AdvancedEnabled { get; set; }

    internal static long Start()
    {
        return Stopwatch.GetTimestamp();
    }

    internal static void AddNpc(long started) { pendingNpcMs += ElapsedMilliseconds(started); }
    internal static void AddWorld(long started) { pendingWorldMs += ElapsedMilliseconds(started); }
    internal static void AddAvatar(long started) { pendingAvatarMs += ElapsedMilliseconds(started); }
    internal static void AddAvatarSerialize(long started) { pendingAvatarSerializeMs += ElapsedMilliseconds(started); }
    internal static void AddAvatarApply(long started) { pendingAvatarApplyMs += ElapsedMilliseconds(started); }
    internal static void AddDistance(long started) { pendingDistanceMs += ElapsedMilliseconds(started); }

    internal static long StartPhase()
    {
        return AdvancedEnabled ? Stopwatch.GetTimestamp() : 0L;
    }

    internal static void AddPhase(MultiplayerPerformancePhase phase, long started)
    {
        if (started != 0L) pendingPhaseMs[(int)phase] += ElapsedMilliseconds(started);
    }

    internal static float PhaseMillisecondsPerSecond(MultiplayerPerformancePhase phase)
    {
        return phaseMillisecondsPerSecond[(int)phase];
    }

    internal static void Reset()
    {
        pendingNpcMs = pendingWorldMs = pendingAvatarMs = pendingAvatarSerializeMs =
            pendingAvatarApplyMs = pendingDistanceMs = 0d;
        for (var index = 0; index < pendingPhaseMs.Length; index++)
        {
            pendingPhaseMs[index] = 0d;
            phaseMillisecondsPerSecond[index] = 0f;
        }
        NpcMillisecondsPerSecond = WorldMillisecondsPerSecond = AvatarMillisecondsPerSecond =
            AvatarSerializeMillisecondsPerSecond = AvatarApplyMillisecondsPerSecond =
            DistanceMillisecondsPerSecond = 0f;
        nextSample = Time.unscaledTime;
    }

    internal static void Sample()
    {
        if (Time.unscaledTime < nextSample) return;
        nextSample = Time.unscaledTime + 1f;
        NpcMillisecondsPerSecond = (float)pendingNpcMs;
        WorldMillisecondsPerSecond = (float)pendingWorldMs;
        AvatarMillisecondsPerSecond = (float)pendingAvatarMs;
        AvatarSerializeMillisecondsPerSecond = (float)pendingAvatarSerializeMs;
        AvatarApplyMillisecondsPerSecond = (float)pendingAvatarApplyMs;
        DistanceMillisecondsPerSecond = (float)pendingDistanceMs;
        for (var index = 0; index < pendingPhaseMs.Length; index++)
        {
            phaseMillisecondsPerSecond[index] = (float)pendingPhaseMs[index];
            pendingPhaseMs[index] = 0d;
        }
        pendingNpcMs = pendingWorldMs = pendingAvatarMs = pendingAvatarSerializeMs =
            pendingAvatarApplyMs = pendingDistanceMs = 0d;
    }

    private static double ElapsedMilliseconds(long started)
    {
        return (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
    }
}
