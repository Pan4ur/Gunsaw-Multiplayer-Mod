using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

internal static partial class MultiplayerSession
{
    private static void Receive(IAsyncResult result)
    {
        try
        {
            var receiveBuffer = new byte[64 * 1024];
            while (socket != null)
            {
            ushort senderId;
            var packet = ReadPacket(receiveBuffer, out senderId);
            if (packet == null) return;
            lock (statusLock)
                if (isHost && blockedPeers.Contains(senderId)) continue;
            if (!ProcessReliablePacket(ref packet, senderId)) continue;
            PayloadPacket decodedPacket;
            if (!PacketCodec.TryDecode(packet, out decodedPacket)) continue;
            if ((decodedPacket.Type == PacketType.Hello) && isHost)
            {
                string connectedName;
                lock (statusLock)
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    connectedName = NormalizePlayerName(HelloPacket.Read(ref reader).PlayerName);
                    TouchPeerLocked(senderId, connectedName);
                }
                var acceptedPacket = PacketCodec.Encode(new AcceptedPacket(localPlayerName));
                SendPacket(acceptedPacket, senderId);
                string customLevel;
                lock (statusLock) customLevel = hostCustomLevel;
                if (!string.IsNullOrEmpty(customLevel)) QueueCustomLevelTransfer(customLevel, senderId);
                var scene = Encoding.UTF8.GetBytes(hostScene + "\n" + hostSceneEpoch);
                var scenePacket = new byte[sceneHeader.Length + scene.Length];
                Buffer.BlockCopy(sceneHeader, 0, scenePacket, 0, sceneHeader.Length);
                Buffer.BlockCopy(scene, 0, scenePacket, sceneHeader.Length, scene.Length);
                SendPacket(scenePacket, senderId, false);
                Send(new SettingsPacket(PvpEnabled, CanGrabPlayers, GrabOnlyUnconscious, AllowRespawn,
                    RespawnAtStart, (ushort)RespawnTimeSeconds, (byte)MaxPlayers), senderId);
                SetStatus(connectedName + " connected. Sent scene " + hostScene + ".");
            }
            else if ((decodedPacket.Type == PacketType.Accepted) && !isHost)
            {
                lock (statusLock)
                {
                    hostPeerId = senderId;
                    var reader = new PacketReader(decodedPacket.Payload);
                    TouchPeerLocked(senderId, NormalizePlayerName(AcceptedPacket.Read(ref reader).PlayerName));
                }
                SetStatus("Connected to host. Receiving match scene...");
            }
            else if (decodedPacket.Type == PacketType.Disconnect)
            {
                var reader = new PacketReader(decodedPacket.Payload);
                var disconnect = DisconnectPacket.Read(ref reader);
                if (disconnect.Reason == DisconnectReason.PeerLeft)
                {
                    var departedPeerId = disconnect.PeerId;
                    if (departedPeerId != 0 && departedPeerId != localPeerId && departedPeerId != senderId)
                        DropPeer(departedPeerId, false, PlayerName(departedPeerId) + " left the lobby.");
                    continue;
                }
                if (isHost && senderId != localPeerId)
                    Send(DisconnectPacket.PeerLeft(senderId));
                var hostLeft = senderId == hostPeerId && !isHost;
                DropPeer(senderId, hostLeft, hostLeft ? "Host closed the lobby." :
                    PlayerName(senderId) + " left the lobby.");
                continue;
            }
            else if (!isHost && decodedPacket.Type == PacketType.CustomLevel)
            {
                if (decodedPacket.Payload.Length < 12) continue;
                var reader = new PacketReader(decodedPacket.Payload);
                ReceiveCustomLevelChunk(CustomLevelPacket.Read(ref reader));
            }
            else if (!isHost && decodedPacket.Type == PacketType.Scene)
            {
                var reader = new PacketReader(decodedPacket.Payload);
                var payload = ScenePacket.Read(ref reader).Scene;
                if (!string.IsNullOrEmpty(payload))
                {
                    lock (statusLock)
                    {
                        if (payload == lastReceivedHostScene) continue;
                        lastReceivedHostScene = payload;
                        var scene = payload;
                        var reload = false;
                        var split = payload.IndexOf('\n');
                        if (split > 0)
                        {
                            scene = payload.Substring(0, split);
                            var rest = payload.Substring(split + 1);
                            if (rest.EndsWith("\nR", StringComparison.Ordinal))
                            {
                                reload = true;
                                rest = rest.Substring(0, rest.Length - 2);
                            }
                            int epoch;
                            if (int.TryParse(rest, out epoch) && epoch != expectedSceneEpoch)
                            {
                                pendingSceneAdvanced = expectedSceneEpoch >= 0;
                                expectedSceneEpoch = epoch;
                            }
                        }
                        pendingScene = scene;
                        pendingSceneReload = reload;
                    }
                }
            }
            else if (decodedPacket.Type == PacketType.Identity)
            {
                var reader = new PacketReader(decodedPacket.Payload);
                var identityPacket = IdentityPacket.Read(ref reader);
                var identity = identityPacket.Name + "\n" + identityPacket.Prefab;
                lock (statusLock)
                {
                    var name = NormalizePlayerName(identityPacket.Name);
                    TouchPeerLocked(senderId, name);
                    identities.Enqueue(new PeerIdentity { PeerId = senderId, Identity = identity });
                    while (identities.Count > MaxPendingIdentities) identities.Dequeue();
                }
            }
            else if (decodedPacket.Type == PacketType.PlayerSnapshot)
            {
                if (decodedPacket.Payload.Length < sizeof(int)) continue;
                var reader = new PacketReader(decodedPacket.Payload);
                var snapshot = PlayerSnapshotPacket.Read(ref reader);
                var sequence = snapshot.Sequence;
                lock (statusLock)
                {
                    TouchPeerLocked(senderId, null);
                    int previous;
                    if (receivedSnapshotSequences.TryGetValue(senderId, out previous) && sequence <= previous)
                        continue;
                    if (previous > 0 && sequence > previous + 1)
                        Interlocked.Add(ref lostSnapshotPackets, sequence - previous - 1);
                    else if (previous == 0 && sequence > 1)
                        Interlocked.Add(ref lostSnapshotPackets, sequence - 1);
                    receivedSnapshotSequences[senderId] = sequence;
                    Interlocked.Increment(ref receivedSnapshotPackets);
                    snapshots[senderId] = snapshot;
                }
            }
            else if (!isHost && decodedPacket.Type == PacketType.WorldSnapshot)
            {
                var data = new byte[packet.Length - worldHeader.Length];
                Buffer.BlockCopy(packet, worldHeader.Length, data, 0, data.Length);
                EnqueueLatestPayload(worldSnapshots, senderId, data);
            }
            else if (!isHost && decodedPacket.Type == PacketType.WorldEnvironment)
            {
                var data = new byte[packet.Length - worldEnvironmentHeader.Length];
                Buffer.BlockCopy(packet, worldEnvironmentHeader.Length, data, 0, data.Length);
                EnqueueLatestPayload(worldEnvironments, senderId, data);
            }
            else if (isHost && decodedPacket.Type == PacketType.WorldInput)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueueLatestWorldInput(senderId, WorldInputPacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (isHost && decodedPacket.Type == PacketType.WorldDamage)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueueWorldDamage(senderId, WorldDamagePacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (!isHost && decodedPacket.Type == PacketType.NpcSnapshot)
            {
                if (decodedPacket.Payload.Length < 12) continue;
                var reader = new PacketReader(decodedPacket.Payload);
                ReceiveNpcChunk(senderId, NpcSnapshotPacket.Read(ref reader));
            }
            else if (isHost && decodedPacket.Type == PacketType.NpcDamage)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueueNpcDamage(senderId, NpcDamagePacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (isHost && decodedPacket.Type == PacketType.NpcPossession)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueueNpcPossession(senderId, NpcPossessionPacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (isHost && decodedPacket.Type == PacketType.WorldInteraction)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueueWorldInteraction(senderId, WorldInteractionPacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (!isHost && decodedPacket.Type == PacketType.PlayerDamage)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueuePlayerDamage(senderId, PlayerDamagePacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (decodedPacket.Type == PacketType.PvpDamage)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueuePvpDamage(senderId, PlayerDamagePacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (!isHost && decodedPacket.Type == PacketType.Settings)
            {
                if (decodedPacket.Payload.Length < 8) continue;
                var reader = new PacketReader(decodedPacket.Payload);
                var settings = SettingsPacket.Read(ref reader);
                PvpEnabled = settings.PvpEnabled;
                CanGrabPlayers = settings.CanGrabPlayers;
                GrabOnlyUnconscious = CanGrabPlayers && settings.GrabOnlyUnconscious;
                AllowRespawn = settings.AllowRespawn;
                RespawnAtStart = settings.RespawnAtStart;
                RespawnTimeSeconds = settings.RespawnTimeSeconds;
                lock (statusLock)
                    maxPlayers = Math.Max(2, Math.Min(16, (int)settings.MaxPlayers));
                SetStatus("Lobby settings received. PVP " + (PvpEnabled ? "enabled" : "disabled") +
                    "; player grab " + (CanGrabPlayers ? (GrabOnlyUnconscious ? "unconscious only" : "enabled") : "disabled") +
                    "; respawn " + (AllowRespawn ? RespawnTimeSeconds + "s." : "disabled."));
            }
            else if (decodedPacket.Type == PacketType.ShotVisual)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueueShotVisual(senderId, ShotVisualPacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (decodedPacket.Type == PacketType.ProjectileImpact)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueueProjectileImpact(senderId, ProjectileImpactPacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (decodedPacket.Type == PacketType.VelvetWeb)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueueVelvetWeb(senderId, VelvetWebPacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (!isHost && decodedPacket.Type == PacketType.PlayerTeleport)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueuePlayerTeleport(senderId, PlayerTeleportPacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (!isHost && decodedPacket.Type == PacketType.VehicleEject)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueueVehicleEject(senderId, VehicleEjectPacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (isHost && decodedPacket.Type == PacketType.TeleportRequest)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueueTeleportRequest(senderId, TeleportRequestPacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (decodedPacket.Type == PacketType.PlayerGrab)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueuePlayerGrab(senderId, PlayerGrabPacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (isHost && decodedPacket.Type == PacketType.NpcGrab)
            {
                try
                {
                    var reader = new PacketReader(decodedPacket.Payload);
                    EnqueueNpcGrab(senderId, NpcGrabPacket.Read(ref reader));
                }
                catch (System.Exception) { }
            }
            else if (decodedPacket.Type == PacketType.Chat && decodedPacket.Payload.Length > sizeof(int) + 1)
            {
                var reader = new PacketReader(decodedPacket.Payload);
                var chat = ChatPacket.Read(ref reader);
                var chatKey = ((long)senderId << 32) | (uint)chat.MessageId;
                var system = chat.IsSystem;
                var text = chat.Text.Trim();
                if (text.Length > 160) text = text.Substring(0, 160);
                lock (statusLock)
                {
                    if (!string.IsNullOrEmpty(text) && receivedChatIds.Add(chatKey))
                    {
                        chatMessages.Enqueue(new ChatMessage { PeerId = senderId, Message = text, System = system });
                        receivedChatOrder.Enqueue(chatKey);
                        while (receivedChatOrder.Count > 128)
                            receivedChatIds.Remove(receivedChatOrder.Dequeue());
                    }
                }
            }
            else if (decodedPacket.Type == PacketType.Ping && packet.Length == pingHeader.Length + sizeof(long))
            {
                var data = new byte[sizeof(long)];
                Buffer.BlockCopy(packet, pingHeader.Length, data, 0, data.Length);
                Send(pongHeader, data, senderId);
            }
            else if (decodedPacket.Type == PacketType.Pong && packet.Length == pongHeader.Length + sizeof(long))
            {
                var sent = BitConverter.ToInt64(packet, pongHeader.Length);
                var now = DateTime.UtcNow.Ticks;
                lock (statusLock)
                {
                    if (sent == pendingPingTicks && now >= sent && now - sent <= TimeSpan.TicksPerSecond * 30)
                    {
                        var sample = (int)Math.Min(9999, (now - sent) / TimeSpan.TicksPerMillisecond);
                        PeerState peer;
                        if (peers.TryGet(senderId, out peer))
                            peer.PingMs = peer.PingMs < 0 ? sample : (peer.PingMs * 3 + sample) / 4;
                    }
                }
            }
            lock (statusLock) TouchPeerLocked(senderId, null);
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { DropRelay(!isHost, "Relay connection closed."); }
        catch (SocketException) { DropRelay(!isHost, "UDP relay connection closed."); }
        catch (OperationCanceledException) { }
    }

    private static bool Matches(byte[] packet, byte[] expected)
    {
        if (packet.Length != expected.Length) return false;
        for (var index = 0; index < packet.Length; index++) if (packet[index] != expected[index]) return false;
        return true;
    }

    private static bool HasHeader(byte[] packet, byte[] header)
    {
        if (packet.Length <= header.Length) return false;
        for (var index = 0; index < header.Length; index++) if (packet[index] != header[index]) return false;
        return true;
    }

    private static void SetStatus(string value)
    {
        lock (statusLock) status = value;
    }

    private static void ResetPing()
    {
        lock (statusLock)
        {
            nextPingTicks = 0;
            pendingPingTicks = 0;
            foreach (var peer in peers.All) peer.PingMs = -1;
        }
    }

    private static void ResetNetworkStats()
    {
        PacketSequences.Reset();
        Interlocked.Exchange(ref transportMessageSequence, 0);
        reliableChannel.Reset();
        Interlocked.Exchange(ref receivedSnapshotPackets, 0);
        Interlocked.Exchange(ref lostSnapshotPackets, 0);
        Interlocked.Exchange(ref receivedBytes, 0);
        Interlocked.Exchange(ref sentBytes, 0);
        Interlocked.Exchange(ref receivedPackets, 0);
        Interlocked.Exchange(ref sentPackets, 0);
        Interlocked.Exchange(ref sentNpcBytes, 0);
        Interlocked.Exchange(ref sentWorldBytes, 0);
        Interlocked.Exchange(ref sentAvatarBytes, 0);
        Interlocked.Exchange(ref sentOtherBytes, 0);
        lock (networkStatsLock)
        {
            statsSampleTicks = 0;
            sampledReceivedBytes = 0;
            sampledSentBytes = 0;
            sampledSentNpcBytes = 0;
            sampledSentWorldBytes = 0;
            sampledSentAvatarBytes = 0;
            sampledSentOtherBytes = 0;
            receivedBytesPerSecond = 0;
            sentBytesPerSecond = 0;
            sentNpcBytesPerSecond = 0;
            sentWorldBytesPerSecond = 0;
            sentAvatarBytesPerSecond = 0;
            sentOtherBytesPerSecond = 0;
        }
    }

    private static void DropPeer(ushort peerId, bool hostLeft, string message)
    {
        UdpClient close = null;
        CancellationTokenSource cancel = null;
        lock (statusLock)
        {
            if (!peers.Remove(peerId)) return;
            peerListRevision++;
            if (isHost && !hostLeft) disconnectedPeers.Enqueue(peerId);
            status = message;
            if (hostLeft)
            {
                peers.Clear();
                ClearPeerQueuesLocked();
                hostDisconnectPending = true;
                close = socket;
                cancel = socketCancellation;
                socket = null;
                socketCancellation = null;
                relayConnected = false;
                PvpEnabled = false;
                CanGrabPlayers = false;
                GrabOnlyUnconscious = false;
                AllowRespawn = false;
                RespawnTimeSeconds = 0;
                RespawnAtStart = false;
            }
            nextPingTicks = 0;
            pendingPingTicks = 0;
        }
        try { if (cancel != null) cancel.Cancel(); } catch { }
        try { if (close != null) close.Close(); } catch { }
        if (close != null) close.Dispose();
        if (cancel != null) cancel.Dispose();
    }

    private static void DropRelay(bool hostLeft, string message)
    {
        UdpClient close;
        CancellationTokenSource cancel;
        lock (statusLock)
        {
            close = socket;
            cancel = socketCancellation;
            socket = null;
            socketCancellation = null;
            relayConnected = false;
            peers.Clear();
            ClearPeerQueuesLocked();
            status = message;
            if (hostLeft)
            {
                hostDisconnectPending = true;
                PvpEnabled = false;
                CanGrabPlayers = false;
                GrabOnlyUnconscious = false;
                AllowRespawn = false;
                RespawnTimeSeconds = 0;
                RespawnAtStart = false;
            }
            nextPingTicks = 0;
            pendingPingTicks = 0;
        }
        try { if (cancel != null) cancel.Cancel(); } catch { }
        try { if (close != null) close.Close(); } catch { }
        if (close != null) close.Dispose();
        if (cancel != null) cancel.Dispose();
        reliableChannel.Reset();
        fragmentTransfers.Clear();
    }

    private static void ClearPeerQueuesLocked()
    {
        blockedPeers.Clear();
        identities.Clear();
        snapshots.Clear();
        receivedSnapshotSequences.Clear();
        worldSnapshots.Clear();
        worldEnvironments.Clear();
        worldInputs.Clear();
        worldDamage.Clear();
        npcSnapshots.Clear();
        npcTransfers.Clear();
        npcDamage.Clear();
        worldInteractions.Clear();
        playerDamage.Clear();
        pvpDamage.Clear();
        shotVisuals.Clear();
        projectileImpacts.Clear();
        velvetWebs.Clear();
        playerTeleports.Clear();
        vehicleEjects.Clear();
        teleportRequests.Clear();
        playerGrabs.Clear();
        npcGrabs.Clear();
        npcPossessions.Clear();
        chatMessages.Clear();
        receivedChatIds.Clear();
        receivedChatOrder.Clear();
    }

    private static void EnqueueWorldInteraction(ushort peerId, WorldInteractionPacket packet)
    {
        lock (statusLock)
        {
            TouchPeerLocked(peerId, null);
            while (worldInteractions.Count >= MaxPendingEventPackets) worldInteractions.Dequeue();
            worldInteractions.Enqueue(new PeerPacket<WorldInteractionPacket> { PeerId = peerId, Packet = packet });
        }
    }

    private static void EnqueuePvpDamage(ushort peerId, PlayerDamagePacket packet)
    {
        lock (statusLock)
        {
            TouchPeerLocked(peerId, null);
            while (pvpDamage.Count >= MaxPendingEventPackets) pvpDamage.Dequeue();
            pvpDamage.Enqueue(new PeerPacket<PlayerDamagePacket> { PeerId = peerId, Packet = packet });
        }
    }

    private static void EnqueueWorldDamage(ushort peerId, WorldDamagePacket packet)
    {
        lock (statusLock)
        {
            TouchPeerLocked(peerId, null);
            while (worldDamage.Count >= MaxPendingEventPackets) worldDamage.Dequeue();
            worldDamage.Enqueue(new PeerPacket<WorldDamagePacket> { PeerId = peerId, Packet = packet });
        }
    }

    private static void EnqueueLatestWorldInput(ushort peerId, WorldInputPacket packet)
    {
        lock (statusLock)
        {
            TouchPeerLocked(peerId, null);
            var retained = new Queue<PeerPacket<WorldInputPacket>>(worldInputs.Count + 1);
            while (worldInputs.Count > 0)
            {
                var item = worldInputs.Dequeue();
                if (item.PeerId != peerId) retained.Enqueue(item);
            }
            while (retained.Count > 0) worldInputs.Enqueue(retained.Dequeue());
            worldInputs.Enqueue(new PeerPacket<WorldInputPacket> { PeerId = peerId, Packet = packet });
        }
    }

    private static void EnqueuePlayerDamage(ushort peerId, PlayerDamagePacket packet)
    {
        lock (statusLock)
        {
            TouchPeerLocked(peerId, null);
            while (playerDamage.Count >= MaxPendingEventPackets) playerDamage.Dequeue();
            playerDamage.Enqueue(new PeerPacket<PlayerDamagePacket> { PeerId = peerId, Packet = packet });
        }
    }

    private static void EnqueueShotVisual(ushort peerId, ShotVisualPacket packet)
    {
        lock (statusLock)
        {
            TouchPeerLocked(peerId, null);
            while (shotVisuals.Count >= MaxPendingEventPackets) shotVisuals.Dequeue();
            shotVisuals.Enqueue(new PeerPacket<ShotVisualPacket> { PeerId = peerId, Packet = packet });
        }
    }

    private static void EnqueueNpcDamage(ushort peerId, NpcDamagePacket packet)
        => EnqueueEvent(npcDamage, peerId, packet);

    private static void EnqueueNpcPossession(ushort peerId, NpcPossessionPacket packet)
        => EnqueueEvent(npcPossessions, peerId, packet);

    private static void EnqueueProjectileImpact(ushort peerId, ProjectileImpactPacket packet)
        => EnqueueEvent(projectileImpacts, peerId, packet);

    private static void EnqueueVelvetWeb(ushort peerId, VelvetWebPacket packet)
        => EnqueueEvent(velvetWebs, peerId, packet);

    private static void EnqueuePlayerTeleport(ushort peerId, PlayerTeleportPacket packet)
        => EnqueueEvent(playerTeleports, peerId, packet);

    private static void EnqueueVehicleEject(ushort peerId, VehicleEjectPacket packet)
        => EnqueueEvent(vehicleEjects, peerId, packet);

    private static void EnqueueTeleportRequest(ushort peerId, TeleportRequestPacket packet)
        => EnqueueEvent(teleportRequests, peerId, packet);

    private static void EnqueuePlayerGrab(ushort peerId, PlayerGrabPacket packet)
        => EnqueueEvent(playerGrabs, peerId, packet);

    private static void EnqueueNpcGrab(ushort peerId, NpcGrabPacket packet)
        => EnqueueEvent(npcGrabs, peerId, packet);

    private static void EnqueueEvent<TPacket>(Queue<PeerPacket<TPacket>> queue, ushort peerId, TPacket packet)
    {
        lock (statusLock)
        {
            TouchPeerLocked(peerId, null);
            while (queue.Count >= MaxPendingEventPackets) queue.Dequeue();
            queue.Enqueue(new PeerPacket<TPacket> { PeerId = peerId, Packet = packet });
        }
    }

    private static void EnqueueLatestPayload(Queue<PeerPayload> queue, ushort peerId, byte[] data)
    {
        lock (statusLock)
        {
            TouchPeerLocked(peerId, null);
            if (queue.Count > 0)
            {
                var retained = new Queue<PeerPayload>(queue.Count + 1);
                while (queue.Count > 0)
                {
                    var item = queue.Dequeue();
                    if (item.PeerId != peerId) retained.Enqueue(item);
                }
                while (retained.Count > 0) queue.Enqueue(retained.Dequeue());
            }
            queue.Enqueue(new PeerPayload { PeerId = peerId, Data = data });
        }
    }

    private static void TouchPeerLocked(ushort peerId, string name)
    {
        if (peerId == 0 || peerId == localPeerId) return;
        var existed = peers.Contains(peerId);
        PeerState peer;
        peer = peers.Touch(peerId, DateTime.UtcNow.Ticks);
        if (!existed) peerListRevision++;
        if (!string.IsNullOrEmpty(name)) peer.Name = NormalizePlayerName(name);
    }

    private static void ReceiveNpcChunk(ushort senderId, NpcSnapshotPacket packet)
    {
        var transferId = packet.TransferId;
        var chunkIndex = packet.ChunkIndex;
        var chunkCount = packet.ChunkCount;
        var totalLength = packet.TotalLength;
        var chunkLength = packet.Data.Length;
        if (chunkCount < 1 || chunkIndex >= chunkCount || totalLength < 0 || totalLength > 4 * 1024 * 1024) return;

        lock (statusLock)
        {
            NpcTransfer transfer;
            if (!npcTransfers.TryGetValue(transferId, out transfer) ||
                transfer.TotalLength != totalLength || transfer.Chunks.Length != chunkCount)
            {
                transfer = new NpcTransfer(totalLength, chunkCount);
                npcTransfers[transferId] = transfer;
            }
            if (transfer.Chunks[chunkIndex] == null)
            {
                transfer.Chunks[chunkIndex] = packet.Data;
                transfer.Received++;
            }
            if (transfer.Received == transfer.Chunks.Length)
            {
                var data = new byte[transfer.TotalLength];
                var destination = 0;
                foreach (var chunk in transfer.Chunks)
                {
                    if (destination + chunk.Length > data.Length) { npcTransfers.Remove(transferId); return; }
                    Buffer.BlockCopy(chunk, 0, data, destination, chunk.Length);
                    destination += chunk.Length;
                }
                if (destination == data.Length)
                    EnqueueLatestPayload(npcSnapshots, senderId, data);
                npcTransfers.Remove(transferId);
            }
            if (npcTransfers.Count > 8)
            {
                var oldest = int.MaxValue;
                foreach (var id in npcTransfers.Keys) if (id < oldest) oldest = id;
                if (oldest != int.MaxValue) npcTransfers.Remove(oldest);
            }
        }
    }

    private static void ReceiveCustomLevelChunk(CustomLevelPacket packet)
    {
        var transferId = packet.TransferId;
        var chunkIndex = packet.ChunkIndex;
        var chunkCount = packet.ChunkCount;
        var totalLength = packet.TotalLength;
        var chunkLength = packet.Data.Length;
        if (chunkCount < 1 || chunkIndex >= chunkCount || totalLength < 2 || totalLength > 4 * 1024 * 1024) return;

        lock (statusLock)
        {
            if (customLevelTransfer == null || customLevelTransfer.TransferId != transferId ||
                customLevelTransfer.TotalLength != totalLength || customLevelTransfer.Chunks.Length != chunkCount)
                customLevelTransfer = new CustomLevelTransfer(transferId, totalLength, chunkCount);
            if (customLevelTransfer.Chunks[chunkIndex] == null)
            {
                customLevelTransfer.Chunks[chunkIndex] = packet.Data;
                customLevelTransfer.Received++;
            }
            if (customLevelTransfer.Received != customLevelTransfer.Chunks.Length) return;

            var data = new byte[customLevelTransfer.TotalLength];
            var destination = 0;
            foreach (var chunk in customLevelTransfer.Chunks)
            {
                if (destination + chunk.Length > data.Length) { customLevelTransfer = null; return; }
                Buffer.BlockCopy(chunk, 0, data, destination, chunk.Length);
                destination += chunk.Length;
            }
            if (destination == data.Length) pendingCustomLevel = Encoding.UTF8.GetString(data);
            customLevelTransfer = null;
        }
    }

}
