using System.Diagnostics;
using UnityEngine;

internal static class MultiplayerPerformance
{
    private static double pendingNpcMs;
    private static double pendingWorldMs;
    private static double pendingAvatarMs;
    private static double pendingAvatarSerializeMs;
    private static double pendingAvatarApplyMs;
    private static double pendingDistanceMs;
    private static float nextSample;

    internal static float NpcMillisecondsPerSecond { get; private set; }
    internal static float WorldMillisecondsPerSecond { get; private set; }
    internal static float AvatarMillisecondsPerSecond { get; private set; }
    internal static float AvatarSerializeMillisecondsPerSecond { get; private set; }
    internal static float AvatarApplyMillisecondsPerSecond { get; private set; }
    internal static float DistanceMillisecondsPerSecond { get; private set; }

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
        pendingNpcMs = pendingWorldMs = pendingAvatarMs = pendingAvatarSerializeMs =
            pendingAvatarApplyMs = pendingDistanceMs = 0d;
    }

    private static double ElapsedMilliseconds(long started)
    {
        return (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
    }
}
