using System.Text;

internal static partial class MultiplayerSession
{
    internal static void Send(INetworkPacket packet, ushort targetPeerId = 0, bool? priority = null)
    {
        if (packet == null) return;
        SendPacket(PacketCodec.Encode(packet), targetPeerId, priority);
    }

    internal static void UpdatePing()
    {
        if (!IsActive) return;
        var now = DateTime.UtcNow.Ticks;
        lock (statusLock)
        {
            if (now < nextPingTicks) return;
            nextPingTicks = now + TimeSpan.TicksPerSecond;
            pendingPingTicks = now;
        }
        SendPacket(PacketCodec.Encode(new PingPacket(now)));
    }

    private static void QueueCustomLevelTransfer(string levelJson, ushort targetId = 0)
    {
        var data = Encoding.UTF8.GetBytes(levelJson);
        const int chunkSize = 60 * 1024;
        var transferId = Interlocked.Increment(ref customLevelTransferId);
        var chunkCount = Math.Max(1, (data.Length + chunkSize - 1) / chunkSize);
        for (var index = 0; index < chunkCount; index++)
        {
            var sourceOffset = index * chunkSize;
            var length = Math.Min(chunkSize, data.Length - sourceOffset);
            var chunk = new byte[length];
            if (length > 0) Buffer.BlockCopy(data, sourceOffset, chunk, 0, length);
            SendPacket(PacketCodec.Encode(new CustomLevelPacket(transferId, (ushort)index,
                (ushort)chunkCount, data.Length, chunk)), targetId);
        }
    }

    internal static bool TryTakeIdentity(out ushort peerId, out string identity)
    {
        lock (statusLock)
        {
            var item = identities.Count == 0 ? null : identities.Dequeue();
            peerId = item == null ? (ushort)0 : item.PeerId;
            identity = item == null ? "" : item.Identity;
            return item != null && !string.IsNullOrEmpty(identity);
        }
    }

    internal static bool TryTakeSnapshot(out ushort peerId, out PlayerSnapshotPacket packet)
    {
        lock (statusLock)
        {
            peerId = 0;
            packet = default(PlayerSnapshotPacket);
            foreach (var pair in snapshots)
            {
                peerId = pair.Key;
                packet = pair.Value;
                break;
            }
            if (peerId == 0) return false;
            snapshots.Remove(peerId);
            return true;
        }
    }

    internal static bool TryTakeWorldSnapshot(out byte[] data)
    {
        ushort ignored; return TryTakePayload(worldSnapshots, out ignored, out data);
    }

    internal static bool TryTakeWorldEnvironment(out byte[] data)
    {
        ushort ignored; return TryTakePayload(worldEnvironments, out ignored, out data);
    }

    internal static bool TryTakeDoorState(out ushort peerId, out DoorStatePacket packet)
        => TryTakePacket(doorStates, out peerId, out packet);

    internal static bool TryTakeWorldInput(out ushort peerId, out WorldInputPacket packet)
    {
        lock (statusLock)
        {
            var item = worldInputs.Count == 0 ? null : worldInputs.Dequeue();
            peerId = item == null ? (ushort)0 : item.PeerId;
            packet = item == null ? default(WorldInputPacket) : item.Packet;
            return item != null;
        }
    }

    internal static bool TryTakeWorldDamage(out WorldDamagePacket packet)
    {
        lock (statusLock)
        {
            var item = worldDamage.Count == 0 ? null : worldDamage.Dequeue();
            packet = item == null ? default(WorldDamagePacket) : item.Packet;
            return item != null;
        }
    }

    internal static bool TryTakeNpcSnapshot(out byte[] data)
    {
        ushort ignored; return TryTakePayload(npcSnapshots, out ignored, out data);
    }

    internal static bool TryTakeNpcDamage(out ushort peerId, out NpcDamagePacket packet)
        => TryTakePacket(npcDamage, out peerId, out packet);

    internal static bool TryTakeNpcSpeech(out ushort peerId, out NpcSpeechPacket packet)
        => TryTakePacket(npcSpeech, out peerId, out packet);

    internal static bool TryTakeWorldInteraction(out ushort peerId, out byte[] data)
    {
        lock (statusLock)
        {
            if (worldInteractions.Count == 0)
            {
                peerId = 0;
                data = null;
                return false;
            }
            var item = worldInteractions.Dequeue();
            peerId = item.PeerId;
            var writer = new PacketWriter(32);
            item.Packet.Write(ref writer);
            data = writer.ToArray();
            return true;
        }
    }

    internal static bool TryTakePlayerDamage(out ushort peerId, out PlayerDamagePacket packet)
    {
        lock (statusLock)
        {
            var item = playerDamage.Count == 0 ? null : playerDamage.Dequeue();
            peerId = item == null ? (ushort)0 : item.PeerId;
            packet = item == null ? default(PlayerDamagePacket) : item.Packet;
            return item != null;
        }
    }

    internal static bool TryTakePvpDamage(out ushort peerId, out PlayerDamagePacket packet)
    {
        lock (statusLock)
        {
            var item = pvpDamage.Count == 0 ? null : pvpDamage.Dequeue();
            peerId = item == null ? (ushort)0 : item.PeerId;
            packet = item == null ? default(PlayerDamagePacket) : item.Packet;
            return item != null;
        }
    }

    internal static bool TryTakeShotVisual(out ushort peerId, out ShotVisualPacket packet)
    {
        lock (statusLock)
        {
            var item = shotVisuals.Count == 0 ? null : shotVisuals.Dequeue();
            peerId = item == null ? (ushort)0 : item.PeerId;
            packet = item == null ? default(ShotVisualPacket) : item.Packet;
            return item != null;
        }
    }

    internal static bool TryTakeProjectileImpact(out ushort peerId, out ProjectileImpactPacket packet)
        => TryTakePacket(projectileImpacts, out peerId, out packet);

    internal static bool TryTakeVelvetWeb(out ushort peerId, out VelvetWebPacket packet)
        => TryTakePacket(velvetWebs, out peerId, out packet);

    internal static bool TryTakePlayerTeleport(out ushort peerId, out PlayerTeleportPacket packet)
        => TryTakePacket(playerTeleports, out peerId, out packet);

    internal static bool TryTakeVehicleEject(out ushort peerId, out VehicleEjectPacket packet)
        => TryTakePacket(vehicleEjects, out peerId, out packet);

    internal static bool TryTakeVehicleImpact(out ushort peerId, out VehicleImpactPacket packet)
        => TryTakePacket(vehicleImpacts, out peerId, out packet);

    internal static bool TryTakeTeleportRequest(out ushort peerId, out TeleportRequestPacket packet)
        => TryTakePacket(teleportRequests, out peerId, out packet);

    internal static bool TryTakePlayerGrab(out ushort peerId, out PlayerGrabPacket packet)
        => TryTakePacket(playerGrabs, out peerId, out packet);

    internal static bool TryTakeNpcGrab(out ushort peerId, out NpcGrabPacket packet)
        => TryTakePacket(npcGrabs, out peerId, out packet);

    internal static bool TryTakeNpcPossession(out ushort peerId, out NpcPossessionPacket packet)
        => TryTakePacket(npcPossessions, out peerId, out packet);

    internal static bool TryTakeMissionFinished(out ushort peerId, out MissionFinishedPacket packet)
        => TryTakePacket(missionFinished, out peerId, out packet);

    internal static bool TryTakePlayerPerformance(out ushort peerId, out PlayerPerformancePacket packet)
        => TryTakePacket(playerPerformance, out peerId, out packet);

    internal static bool TryTakePlayerCarry(out ushort peerId, out PlayerCarryPacket packet)
        => TryTakePacket(playerCarries, out peerId, out packet);

    internal static bool TryTakeHostFps(out ushort peerId, out HostFpsPacket packet)
        => TryTakePacket(hostFpsPackets, out peerId, out packet);

    internal static bool TryTakeChat(out ushort peerId, out string sender, out string message)
    {
        lock (statusLock)
        {
            var chat = chatMessages.Count == 0 ? null : chatMessages.Dequeue();
            peerId = chat == null || chat.System ? (ushort)0 : chat.PeerId;
            sender = chat == null || !chat.System ? PlayerName(peerId) : "SYSTEM";
            message = chat == null ? "" : chat.Message;
            return !string.IsNullOrEmpty(message);
        }
    }

    // TODO remove the legacy shit
    internal static void SendWorldInteraction(byte[] serialized)
    {
        if (serialized == null || !IsConnected || IsHost) return;
        try
        {
            var reader = new PacketReader(serialized);
            Send(WorldInteractionPacket.Read(ref reader), 1);
        }
        catch (System.IO.InvalidDataException) { }
        catch (System.IndexOutOfRangeException) { }
        catch (System.IO.IOException) { }
        catch (ObjectDisposedException) { }
    }

    private static bool TryTakePayload(Queue<PeerPayload> queue, out ushort peerId, out byte[] data)
    {
        lock (statusLock)
        {
            var item = queue.Count == 0 ? null : queue.Dequeue();
            peerId = item == null ? (ushort)0 : item.PeerId;
            data = item == null ? null : item.Data;
            return item != null;
        }
    }

    private static bool TryTakePacket<TPacket>(Queue<PeerPacket<TPacket>> queue, out ushort peerId,
        out TPacket packet)
    {
        lock (statusLock)
        {
            var item = queue.Count == 0 ? null : queue.Dequeue();
            peerId = item == null ? (ushort)0 : item.PeerId;
            packet = item == null ? default(TPacket) : item.Packet;
            return item != null;
        }
    }

}
