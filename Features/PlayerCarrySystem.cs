using UnityEngine;

/*
    А, с нами молодая Astra, Astra 6.0
    Она умеет юзать тулы, ебашит в консоль
    Эта модель любит лимиты кушать под 0
    Astra в codex — это боль, а-а-а, а-а
*/
internal static class PlayerCarrySystem
{
    internal const float Range = 2.4f;
    private static readonly List<PlayerCarryPair> pairs = new();

    internal static string Prompt { get; private set; } = "";
    internal static bool IsLocalCarrier => PairForPeer(MultiplayerSession.LocalPeerId)?.IsLocalCarrier == true;
    internal static bool IsLocalTarget => IsCarriedPeer(MultiplayerSession.LocalPeerId);
    internal static bool IsCarriedPeer(ushort peerId) => PairForPeer(peerId)?.targetId == peerId;
    internal static bool IsCarriedTarget(BodyScript body) => pairs.Any(pair => pair.IsCarriedTarget(body));
    internal static bool MustLockRemoteCarryPose(BodyScript body) => pairs.Any(pair => pair.MustLockRemoteCarryPose(body));

    private static PlayerCarryPair PairForPeer(ushort peerId) =>
        pairs.FirstOrDefault(pair => pair.carrying && (pair.carrierId == peerId || pair.targetId == peerId));

    internal static void Tick()
    {
        Prompt = "";
        if (!MultiplayerSession.IsActive)
        {
            foreach (var pair in pairs) pair.Stop(Vector2.zero);
            pairs.Clear();
            return;
        }

        while (MultiplayerSession.TryTakePlayerCarry(out var sender, out var packet)) Receive(sender, packet);
        var hadLocalPair = PairForPeer(MultiplayerSession.LocalPeerId) != null;
        for (var index = pairs.Count - 1; index >= 0; index--)
        {
            var pair = pairs[index];
            pair.Tick();
            if (!pair.carrying) pairs.RemoveAt(index);
        }

        var localPair = PairForPeer(MultiplayerSession.LocalPeerId);
        if (localPair != null)
        {
            Prompt = localPair.Prompt;
            return;
        }
        if (hadLocalPair) return;
        var local = PlayerScript.player?.bodyScript;
        if (local == null || !local.isAlive) return;
        var target = FindCarryTarget(local);
        if (target == 0) return;
        Prompt = "Press C to carry player";
        if (!Input.GetKeyDown(KeyCode.C)) return;
        var start = new PlayerCarryPacket(true, MultiplayerSession.LocalPeerId, target);
        if (MultiplayerSession.IsHost) Receive(MultiplayerSession.LocalPeerId, start);
        else
        {
            pairs.Add(new PlayerCarryPair(start.CarrierId, start.TargetId, true));
            MultiplayerSession.Send(start, 1, true);
        }
    }

    internal static void Receive(ushort sender, PlayerCarryPacket packet)
    {
        var pair = pairs.FirstOrDefault(value => value.carrying && value.carrierId == packet.CarrierId && value.targetId == packet.TargetId);
        if (MultiplayerSession.IsHost)
        {
            if (packet.Carrying)
            {
                if (sender != packet.CarrierId || packet.CarrierId == 0 || packet.TargetId == 0 || packet.CarrierId == packet.TargetId) return;
                var carrier = BodyForPeer(packet.CarrierId);
                var target = BodyForPeer(packet.TargetId);
                if (PairForPeer(packet.CarrierId) != null || PairForPeer(packet.TargetId) != null ||
                    carrier == null || target == null || !carrier.isAlive || !target.isAlive || carrier.inVehicle || target.inVehicle ||
                    PropHuntSystem.IsProp(carrier) || PropHuntSystem.IsProp(target))
                {
                    MultiplayerSession.Send(new PlayerCarryPacket(false, packet.CarrierId, packet.TargetId, 0f, 0f, true), sender, true);
                    return;
                }
                pairs.Add(new PlayerCarryPair(packet.CarrierId, packet.TargetId));
            }
            else
            {
                if (sender != packet.CarrierId && sender != packet.TargetId) return;
                if (pair == null)
                {
                    MultiplayerSession.Send(new PlayerCarryPacket(false, packet.CarrierId, packet.TargetId, 0f, 0f, true), sender, true);
                    return;
                }
                if (pair.IsLocalCarrier) packet = pair.StopPacketWithVelocity();
                else if (sender != pair.carrierId || !packet.HasReleaseVelocity)
                {
                    MultiplayerSession.Send(new PlayerCarryPacket(false, pair.carrierId, pair.targetId), pair.carrierId, true);
                    return;
                }
                pair.Stop(new Vector2(packet.ReleaseVelocityX, packet.ReleaseVelocityY));
            }
            MultiplayerSession.Send(packet, 0, true);
        }
        else if (sender == 1)
        {
            if (packet.Carrying)
            {
                if (pair != null) pair.Confirm();
                else
                {
                    var carrierPair = PairForPeer(packet.CarrierId);
                    var targetPair = PairForPeer(packet.TargetId);
                    carrierPair?.Stop(Vector2.zero);
                    if (targetPair != carrierPair) targetPair?.Stop(Vector2.zero);
                    pairs.Add(new PlayerCarryPair(packet.CarrierId, packet.TargetId));
                }
            }
            else if (pair != null)
            {
                if (!packet.HasReleaseVelocity && pair.IsLocalCarrier)
                    MultiplayerSession.Send(pair.StopPacketWithVelocity(), 1, true);
                else
                    pair.Stop(new Vector2(packet.ReleaseVelocityX, packet.ReleaseVelocityY));
            }
        }
    }

    private static BodyScript BodyForPeer(ushort peerId) => peerId == MultiplayerSession.LocalPeerId
        ? PlayerScript.player?.bodyScript : NetworkAvatarManager.RemoteBodyForPeer(peerId);

    internal static void FixedTick()
    {
        foreach (var pair in pairs) pair.FixedTick();
    }

    internal static void LateTick()
    {
        foreach (var pair in pairs) pair.LateTick();
    }

    internal static void RestoreRenderPose()
    {
        foreach (var pair in pairs) pair.RestoreRenderPose();
    }

    internal static void PreBodyUpdate(BodyScript body)
    {
        foreach (var pair in pairs) pair.PreBodyUpdate(body);
    }

    internal static void PostBodyUpdate(BodyScript body)
    {
        foreach (var pair in pairs) pair.PostBodyUpdate(body);
    }

    internal static void PreBodyFixedUpdate(BodyScript body)
    {
        foreach (var pair in pairs) pair.PreBodyFixedUpdate(body);
    }

    internal static void ApplyPickupCrouch()
    {
        foreach (var pair in pairs) pair.ApplyPickupCrouch();
    }

    internal static bool SetRemoteArmRotation(BodyScript body, Rigidbody2D part, Quaternion rotation) =>
        pairs.Any(pair => pair.SetRemoteArmRotation(body, part, rotation));

    internal static bool SetRemoteArmsRotation(BodyScript body, Quaternion rotation) =>
        pairs.Any(pair => pair.SetRemoteArmsRotation(body, rotation));
    
    private static ushort FindCarryTarget(BodyScript local)
    {
        if (local.inVehicle || PropHuntSystem.IsProp(local)) return 0;
        var camera = Camera.main;
        if (camera == null) return 0;
        var point = (Vector2)camera.ScreenToWorldPoint(Input.mousePosition);
        foreach (var remote in NetworkAvatarManager.replicas)
        {
            if (remote.Value == null)
                continue;
            
            var body = remote.Value.remoteBody;
            if (PairForPeer(remote.Key) != null || body == null || !body.isAlive || body.inVehicle || PropHuntSystem.IsProp(body) || Vector2.Distance(local.transform.position, body.transform.position) > Range) continue;
            if (PointerOverBody(body, point)) return remote.Key;
        }
        return 0;
    }

    internal static bool PointerOverBody(BodyScript body, Vector2 point) =>
        body.GetComponentsInChildren<Collider2D>(true).OfType<Collider2D>().Any(collider => collider.enabled && collider.OverlapPoint(point)) || body.GetComponentsInChildren<Rigidbody2D>(true).OfType<Rigidbody2D>().Any(rigidbody => Vector2.Distance(rigidbody.position, point) <= 0.38f);
}