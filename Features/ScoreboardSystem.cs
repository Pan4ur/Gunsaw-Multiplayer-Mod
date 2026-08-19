using UnityEngine;
using UnityEngine.SceneManagement;

internal readonly struct PlayerPerformance
{
    internal readonly int Hits, Misses, Heads, Kills, Deaths;
    internal readonly float DamageDealt, DamageReceived;
    internal PlayerPerformance(int hits, int misses, int heads, float dealt, float received, int kills, int deaths)
    { Hits = hits; Misses = misses; Heads = heads; DamageDealt = dealt; DamageReceived = received; Kills = kills; Deaths = deaths; }
    internal float Accuracy => Ratio(Hits, Misses);
    internal float HeadshotRatio => Hits == 0 ? 0f : Mathf.Clamp01((float)Heads / Hits);
    internal float DamageRatio => DamageDealt + DamageReceived <= 0f ? 0f : (DamageDealt > DamageReceived ? 1f - DamageReceived / (DamageDealt + DamageReceived) : DamageDealt / (DamageDealt + DamageReceived));
    private static float Ratio(int hits, int misses) { var total = hits + misses; return total <= 0 ? 0f : (hits > misses ? 1f - (float)misses / total : (float)hits / total); }
}

internal static class ScoreboardSystem
{
    private static readonly Dictionary<ushort, PlayerPerformance> scores = [];
    private static readonly Dictionary<ushort, int> hostKills = [];
    private static int scene = int.MinValue, localDeaths;
    private static int localPvpHeadShots, localPvpKills;
    private static float localDamageDealt, localDamageReceived, localPvpDamageDealt;
    private static bool localWasAlive;
    private static float nextSend;

    internal static PlayerPerformance ForPlayer(ushort peerId) => scores.TryGetValue(peerId, out var score) ? score : default;

    internal static void Tick()
    {
        if (!MultiplayerSession.IsActive) { scores.Clear(); hostKills.Clear(); scene = int.MinValue; localDeaths = 0; localPvpHeadShots = localPvpKills = 0; localDamageDealt = localDamageReceived = localPvpDamageDealt = 0f; return; }
        var currentScene = SceneManager.GetActiveScene().handle;
        if (scene != currentScene)
        {
            scene = currentScene; scores.Clear(); hostKills.Clear(); localDeaths = 0; localPvpHeadShots = localPvpKills = 0; localDamageDealt = localDamageReceived = localPvpDamageDealt = 0f;
            localWasAlive = PlayerScript.player?.bodyScript != null && PlayerScript.player.bodyScript.isAlive;
        }
        var body = PlayerScript.player?.bodyScript;
        if (body != null) { if (localWasAlive && !body.isAlive) localDeaths++; localWasAlive = body.isAlive; }

        ushort senderId; PlayerPerformancePacket packet;
        while (MultiplayerSession.TryTakePlayerPerformance(out senderId, out packet))
        {
            if (MultiplayerSession.IsHost)
            {
                if (senderId == 0 || (packet.PlayerId != 0 && packet.PlayerId != senderId)) continue;
                var score = new PlayerPerformance(packet.HitShots, packet.MissedShots, packet.HeadShots, packet.DamageDealt, packet.DamageReceived, KillsFor(senderId) + Mathf.Max(0, packet.Kills), packet.Deaths);
                scores[senderId] = score;
                MultiplayerSession.Send(ToPacket(senderId, score));
            }
            else if (senderId == 1 && packet.PlayerId != 0) scores[packet.PlayerId] = FromPacket(packet);
        }

        if (Time.unscaledTime < nextSend) return;
        nextSend = Time.unscaledTime + 1f;
        var mission = MissionManager.main;
        var localKills = MultiplayerSession.IsHost ? KillsFor(MultiplayerSession.LocalPeerId) + localPvpKills : ForPlayer(MultiplayerSession.LocalPeerId).Kills;
        var local = mission == null ? new PlayerPerformance(0, 0, localPvpHeadShots, localDamageDealt + localPvpDamageDealt, localDamageReceived, localKills, localDeaths) :
            new PlayerPerformance(mission.hitShots, mission.missedShots, mission.headShots + localPvpHeadShots,
                Mathf.Max(localDamageDealt, mission.damageDealt) + localPvpDamageDealt,
                Mathf.Max(localDamageReceived, Mathf.Max(0f, mission.damageReceived - localPvpDamageDealt)),
                localKills, localDeaths);
        scores[MultiplayerSession.LocalPeerId] = local;
        if (MultiplayerSession.IsHost) MultiplayerSession.Send(ToPacket(MultiplayerSession.LocalPeerId, local));
        else MultiplayerSession.Send(ToPacket(0, new PlayerPerformance(local.Hits, local.Misses, local.Heads,
            local.DamageDealt, local.DamageReceived, localPvpKills, local.Deaths)), 1);
    }

    internal static void RecordHostNpcKill(BodyScript victim)
    {
        if (!MultiplayerSession.IsHost || victim == null || victim.isPlayer) return;
        var killer = NetworkAvatarReplication.DamageSourceFor(victim);
        if (killer == null) return;
        var peerId = killer == PlayerScript.player?.bodyScript ? MultiplayerSession.LocalPeerId : (NetworkAvatarRegistry.ReplicaForBody(killer)?.remotePeerId ?? 0);
        if (peerId != 0) hostKills[peerId] = KillsFor(peerId) + 1;
    }

    internal static void RecordLocalDamageDealt(float amount)
    {
        if (MultiplayerSession.IsActive && amount > 0f) localDamageDealt += Mathf.Min(amount, 1000f);
    }

    internal static void RecordLocalDamageReceived(float amount)
    {
        if (MultiplayerSession.IsActive && amount > 0f) localDamageReceived += Mathf.Min(amount, 1000f);
    }

    internal static void RecordLocalPvpHit(float amount, bool critical)
    {
        if (!MultiplayerSession.PvpEnabled || amount <= 0f) return;
        localPvpDamageDealt += Mathf.Min(amount, 1000f);
        if (critical) localPvpHeadShots++;
    }

    internal static void RecordLocalPvpKill()
    {
        if (MultiplayerSession.PvpEnabled) localPvpKills++;
    }

    internal static bool IsMvp(ushort peerId)
    {
        if (!scores.TryGetValue(peerId, out var candidate) || !HasActivity(candidate)) return false;
        var candidateValue = PerformanceValue(candidate);
        foreach (var pair in scores)
        {
            if (!HasActivity(pair.Value)) continue;
            var value = PerformanceValue(pair.Value);
            if (value > candidateValue || (value == candidateValue && pair.Key != peerId)) return false;
        }
        return true;
    }

    internal static string Rank(PlayerPerformance score)
    {
        var total = PerformanceValue(score);
        var mission = MissionManager.main;
        var kills = mission == null || mission.totalEnemyCount <= 0 ? 0f : Mathf.Clamp01((float)mission.killAmount / mission.totalEnemyCount);
        if (total < .75f) return "D";
        if (total < .85f) return "C";
        if (total < .9f) return "B";
        if (total < .95f) return "A";
        if (kills >= 1f && GameManager.main != null && GameManager.main.swapAmount > 0)
            return GameManager.main.hardMode ? "X" : "U";
        return "S";
    }

    internal static string RankColor(string rank) => rank switch { "X" => "#F05CFF", "U" => "#FFE35A", "S" => "#FFD34D", "A" => "#75E06B", "B" => "#5EC8FF", "C" => "#C9C9C9", _ => "#FF6B6B" };
    private static bool HasActivity(PlayerPerformance score) => score.Hits != 0 || score.Misses != 0 || score.Heads != 0 || score.Kills != 0 || score.Deaths != 0 || score.DamageDealt > 0f || score.DamageReceived > 0f;
    internal static float PerformanceValue(PlayerPerformance score)
    {
        var mission = MissionManager.main;
        var kills = mission == null || mission.totalEnemyCount <= 0 ? 0f : Mathf.Clamp01((float)mission.killAmount / mission.totalEnemyCount);
        var headshotForRank = score.Hits == 0 ? 1f : score.HeadshotRatio;
        return kills * Mathf.Clamp01(score.Accuracy * .86f + headshotForRank * .26f + score.DamageRatio * .86f);
    }
    private static int KillsFor(ushort peerId) => hostKills.TryGetValue(peerId, out var kills) ? kills : 0;
    private static PlayerPerformance FromPacket(PlayerPerformancePacket packet) => new(packet.HitShots, packet.MissedShots, packet.HeadShots, packet.DamageDealt, packet.DamageReceived, packet.Kills, packet.Deaths);
    private static PlayerPerformancePacket ToPacket(ushort peerId, PlayerPerformance score) => new(peerId, score.Hits, score.Misses, score.Heads, score.DamageDealt, score.DamageReceived, score.Kills, score.Deaths);
}
