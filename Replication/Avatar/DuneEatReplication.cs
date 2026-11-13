using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Nom nom nom
internal static class DuneEatReplication
{
    private static readonly Dictionary<uint, PendingBite> pending = new();
    private static uint nextRequestId;

    internal static DismemberManager[] YummyParts(BodyScript victim) => [.. victim.GetComponentsInChildren<DismemberManager>().Where(IsAvailable)];

    internal static bool TryBite(DismemberManager manager, float damage, AnglerScript eater, BodyScript victim)
    {
        if (!CanEat(victim, eater.body)) return false;
        if (!MultiplayerSession.IsConnected || (!NetworkAvatarManager.IsRemoteAvatarBody(victim) && !NpcReplication.IsClientProxy(victim)))
            return Consume(manager, victim, eater.body, damage);

        eater.StartCoroutine(RequestBite(eater, victim, manager));
        return false;
    }

    private static IEnumerator RequestBite(AnglerScript eater, BodyScript victim, DismemberManager manager)
    {
        eater.eating = true;
        try
        {
            var body = eater.body;
            var remotePlayer = NetworkAvatarManager.IsRemoteAvatarBody(victim);
            var peerId = remotePlayer ? NetworkAvatarManager.PeerIdOfBody(victim) : (ushort)0;
            var npcId = remotePlayer ? 0 : NpcReplication.ClientNpcId(victim);
            var index = victim.limbs.IndexOf(manager.GetComponent<LimbScript>());
            if (index < 0 || index > short.MaxValue || (remotePlayer ? peerId == 0 : npcId == 0)) yield break;
            var authority = remotePlayer ? peerId : (ushort)1;
            var request = new DuneEatPacket(++nextRequestId, npcId, peerId, (short)index);
            var bite = new PendingBite(authority, eater);
            pending.Add(request.RequestId, bite);
            try
            {
                if (!MultiplayerSession.Send(request, authority)) yield break;
                while (bite.Result == 0 && MultiplayerSession.IsConnected &&
                    SceneManager.GetActiveScene().handle == bite.SceneHandle && Time.unscaledTime < bite.ExpiresAt)
                    yield return null;
                if (bite.Result != 1 || body == null || !body.isAlive || eater.body != body) yield break;
                Heal(eater);
            }
            finally { pending.Remove(request.RequestId); }
        }
        finally
        {
            eater.Let(it => it.eating = false);
        }
    }

    internal static void Update()
    {
        foreach (var pair in pending.ToArray())
        {
            if (pair.Value.Eater != null && Time.unscaledTime < pair.Value.ExpiresAt && MultiplayerSession.IsConnected && pair.Value.SceneHandle == SceneManager.GetActiveScene().handle)
                continue;
            pair.Value.Result = 2;
            pending.Remove(pair.Key);
        }
        while (MultiplayerSession.TryTakeDuneEat(out var sender, out var packet))
        {
            if (packet.Result != 0)
            {
                if (pending.TryGetValue(packet.RequestId, out var bite) && sender == bite.Authority && bite.Result == 0)
                    bite.Result = packet.Result;
                continue;
            }
            var victim = packet.VictimPeerId == MultiplayerSession.LocalPeerId ? PlayerScript.player?.bodyScript : packet.VictimPeerId == 0 ? NpcReplication.HostNpcForId(packet.NpcId) : null;
            var source = NetworkAvatarManager.RemoteBodyForPeer(sender);
            var accepted = false;
            if (victim != null && source != null && source.IsConsc() && source.GetComponent<AnglerScript>() != null && packet.LimbIndex >= 0 && packet.LimbIndex < victim.limbs.Count)
            {
                var limb = victim.limbs[packet.LimbIndex];
                var manager = limb == null ? null : limb.GetComponent<DismemberManager>();
                if (manager != null && Vector2.Distance(source.transform.position, manager.transform.position) <= 3f)
                    accepted = Consume(manager, victim, source);
            }
            MultiplayerSession.Send(new DuneEatPacket(packet.RequestId, packet.NpcId, packet.VictimPeerId, packet.LimbIndex, accepted ? (byte)1 : (byte)2), sender);
        }
    }

    internal static bool IsAvailable(DismemberManager manager) =>
        manager != null && manager.currentDamage <= manager.damageReq && (manager.dismemberLimbs == null || !manager.dismemberLimbs.Any(limb => limb != null && limb.dismembered));

    internal static bool CanEat(BodyScript victim, BodyScript eater)
    {
        if (victim == null || victim.isRobot || victim.IsConsc()) return false;
        if (!MultiplayerSession.IsConnected) return true;
        if (KartPassengers.IsProtectedPassenger(victim)) return false;
        if (!victim.isAlive || !victim.isPlayer) return true;
        var victimId = NetworkAvatarManager.PeerIdOfBody(victim);
        var eaterId = NetworkAvatarManager.PeerIdOfBody(eater);
        if (PropHuntSystem.IsProtectedHunter(victim) || PropHuntSystem.FriendlyHunters(eaterId, victimId)) return false;
        if (victim == PlayerScript.player?.bodyScript && NetworkAvatarManager.HasLocalRespawnProtection) return false;
        return !eater.isPlayer || (MultiplayerSession.PvpEnabled && !TeamSystem.Same(eaterId, victimId) && !NoPvpZoneRuntime.Protects(victim, victimId));
    }

    internal static bool Consume(DismemberManager manager, BodyScript victim, BodyScript eater, float damage = 1E+12f)
    {
        if (!IsAvailable(manager) || !CanEat(victim, eater))
            return false;

        if (victim.isAlive)
        {
            NetworkAvatarManager.RecordEnvironmentalDeathCause(victim, PlayerDeathCause.EatenByDune);
            NetworkAvatarManager.RecordDamageSource(victim, eater);
        }
        manager.currentDamage = damage;
        return true;
    }

    internal static void Heal(AnglerScript eater)
    {
        eater.body.health = Mathf.Clamp(eater.body.health + 35f, 0f, eater.body.maxHealth);
        eater.body.stamina += 35f;
        eater.bloodSprite.gameObject.SetActive(true);
    }

    private sealed class PendingBite
    {
        internal readonly ushort Authority;
        internal readonly AnglerScript Eater;
        internal readonly float ExpiresAt = Time.unscaledTime + 5f;
        internal readonly int SceneHandle = SceneManager.GetActiveScene().handle;
        internal byte Result;

        internal PendingBite(ushort authority, AnglerScript eater)
        {
            Authority = authority;
            Eater = eater;
        }
    }
}