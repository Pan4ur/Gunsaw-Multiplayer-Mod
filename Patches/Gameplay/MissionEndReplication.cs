using HarmonyLib;

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.FinishMission))]
internal static class MissionEndReplicationPatch
{
    private static bool Prefix() => !PropHuntSystem.Enabled;

    private static void Postfix()
    {
        if (MultiplayerSession.IsHost && !PropHuntSystem.Enabled)
            MultiplayerSession.Send(new MissionFinishedPacket(), 0, true);
    }
}

internal static class MissionEndReplication
{
    private static bool pendingFinish;

    internal static void Tick()
    {
        if (!MultiplayerSession.IsActive)
        {
            pendingFinish = false;
            return;
        }
        ushort senderId;
        MissionFinishedPacket packet;
        while (MultiplayerSession.TryTakeMissionFinished(out senderId, out packet))
            pendingFinish = true;

        var mission = MissionManager.main;
        if (!pendingFinish || mission == null) return;
        pendingFinish = false;
        if (!mission.finished) mission.FinishMission();
    }
}
