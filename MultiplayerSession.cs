using BepInEx.Logging;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Threading;

internal static class MultiplayerSession
{
    private static ClientWebSocket socket;
    private static CancellationTokenSource socketCancellation;
    private static readonly object sendLock = new object();
    private static readonly object sendQueueLock = new object();
    private static readonly Queue<byte[]> prioritySendQueue = new Queue<byte[]>();
    private static readonly Queue<byte[]> sendQueue = new Queue<byte[]>();
    private static readonly AutoResetEvent sendSignal = new AutoResetEvent(false);
    private static Thread sendThread;
    private const int MaxQueuedPackets = 2048;
    private const int MaxPendingEventPackets = 256;
    private const int MaxPendingIdentities = 64;
    private static readonly object statusLock = new object();
    private static string status = "";
    private static bool isHost;
    private static readonly byte[] hello = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x01 };
    private static readonly byte[] accepted = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x02 };
    private static readonly byte[] sceneHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x03 };
    private static readonly byte[] identityHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x04 };
    private static readonly byte[] snapshotHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x05 };
    private static readonly byte[] worldHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x06 };
    private static readonly byte[] worldInputHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x07 };
    private static readonly byte[] worldDamageHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x08 };
    private static readonly byte[] npcHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x09 };
    private static readonly byte[] npcDamageHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x0A };
    private static readonly byte[] worldInteractionHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x0B };
    private static readonly byte[] playerDamageHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x0C };
    private static readonly byte[] pvpDamageHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x0D };
    private static readonly byte[] settingsHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x0E };
    private static readonly byte[] shotVisualHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x0F };
    private static readonly byte[] playerGrabHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x10 };
    private static readonly byte[] pingHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x11 };
    private static readonly byte[] pongHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x12 };
    private static readonly byte[] npcGrabHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x13 };
    private static readonly byte[] disconnectHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x14 };
    private static readonly byte[] chatHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x15 };
    private static readonly byte[] customLevelHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x16 };
    private static string hostScene = "";
    private static string pendingScene = "";
    private static string hostCustomLevel = "";
    private static string pendingCustomLevel = "";
    private static readonly Dictionary<ushort, PeerState> peers = new Dictionary<ushort, PeerState>();
    private static readonly Queue<PeerIdentity> identities = new Queue<PeerIdentity>();
    private static readonly Dictionary<ushort, byte[]> snapshots = new Dictionary<ushort, byte[]>();
    private static readonly Queue<PeerPayload> worldSnapshots = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> worldInputs = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> worldDamage = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> npcSnapshots = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> npcDamage = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> worldInteractions = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> playerDamage = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> pvpDamage = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> shotVisuals = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> playerGrabs = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> npcGrabs = new Queue<PeerPayload>();
    private static readonly Queue<ChatMessage> chatMessages = new Queue<ChatMessage>();
    private static readonly HashSet<long> receivedChatIds = new HashSet<long>();
    private static readonly Queue<long> receivedChatOrder = new Queue<long>();
    private static readonly Dictionary<int, NpcTransfer> npcTransfers = new Dictionary<int, NpcTransfer>();
    private static int npcTransferId;
    private static CustomLevelTransfer customLevelTransfer;
    private static int customLevelTransferId;
    private static long nextPingTicks;
    private static long pendingPingTicks;
    private static bool hostDisconnectPending;
    private static int chatSequence;
    private static int playerSnapshotSequence;
    private static readonly Dictionary<ushort, int> receivedSnapshotSequences = new Dictionary<ushort, int>();
    private static long receivedSnapshotPackets;
    private static long lostSnapshotPackets;
    private static long receivedBytes;
    private static long sentBytes;
    private static long receivedPackets;
    private static long sentPackets;
    private static readonly object networkStatsLock = new object();
    private static long statsSampleTicks;
    private static long sampledReceivedBytes;
    private static long sampledSentBytes;
    private static int receivedBytesPerSecond;
    private static int sentBytesPerSecond;
    private static string localPlayerName = "Player";
    private static ushort localPeerId;
    private static ushort hostPeerId;
    private static int maxPlayers = 2;
    private const long PeerTimeoutTicks = TimeSpan.TicksPerSecond * 4;

    internal static void StartHost(string lobbyId, string relayKey, string relayAddress, bool pvpEnabled,
        bool canGrabPlayers, bool grabOnlyUnconscious, bool allowRespawn, int respawnTimeSeconds,
        bool respawnAtStart, string playerName, ushort assignedPeerId, int lobbyMaxPlayers,
        ManualLogSource logger)
    {
        CloseSocket();
        ResetNetworkStats();
        lock (statusLock)
        {
            peers.Clear();
            ClearPeerQueuesLocked();
            hostDisconnectPending = false;
            hostCustomLevel = "";
            pendingCustomLevel = "";
            customLevelTransfer = null;
            localPlayerName = NormalizePlayerName(playerName);
            localPeerId = assignedPeerId == 0 ? (ushort)1 : assignedPeerId;
            hostPeerId = localPeerId;
            maxPlayers = Math.Max(2, Math.Min(16, lobbyMaxPlayers));
        }
        socket = ConnectRelay(relayAddress, lobbyId, relayKey);
        isHost = true;
        PvpEnabled = pvpEnabled;
        CanGrabPlayers = canGrabPlayers;
        GrabOnlyUnconscious = canGrabPlayers && grabOnlyUnconscious;
        AllowRespawn = allowRespawn;
        RespawnTimeSeconds = Math.Max(0, Math.Min(3600, respawnTimeSeconds));
        RespawnAtStart = respawnAtStart;
        ResetPing();
        ThreadPool.QueueUserWorkItem(_ => Receive(null));
        logger.LogInfo("Host connected to WebSocket relay " + relayAddress + " for lobby " + lobbyId + ".");
    }

    internal static bool Connect(string relayAddress, string lobbyId, string relayKey, string playerName,
        ushort assignedPeerId, int lobbyMaxPlayers, ManualLogSource logger, out string error)
    {
        error = "";
        try
        {
            CloseSocket();
            ResetNetworkStats();
            lock (statusLock)
            {
                peers.Clear();
                ClearPeerQueuesLocked();
                hostDisconnectPending = false;
                hostCustomLevel = "";
                pendingCustomLevel = "";
                customLevelTransfer = null;
                localPlayerName = NormalizePlayerName(playerName);
                localPeerId = assignedPeerId;
                hostPeerId = 0;
                maxPlayers = Math.Max(2, Math.Min(16, lobbyMaxPlayers));
            }
            isHost = false;
            PvpEnabled = false;
            CanGrabPlayers = false;
            GrabOnlyUnconscious = false;
            AllowRespawn = false;
            RespawnTimeSeconds = 0;
            RespawnAtStart = false;
            ResetPing();
            socket = ConnectRelay(relayAddress, lobbyId, relayKey);
            var helloPacket = PacketWithPayload(hello, Encoding.UTF8.GetBytes(localPlayerName));
            SendPacket(helloPacket);
            ThreadPool.QueueUserWorkItem(_ => Receive(null));
            logger.LogInfo("WebSocket relay handshake sent to " + relayAddress + ".");
            return true;
        }
        catch (Exception e)
        {
            CloseSocket();
            error = "WebSocket connection failed: " + e.Message;
            logger.LogError(error);
            return false;
        }
    }

    internal static bool TryTakeStatus(out string message)
    {
        lock (statusLock)
        {
            message = status;
            status = "";
            return !string.IsNullOrEmpty(message);
        }
    }

    internal static void SetHostScene(string scene)
    {
        if (isHost && string.IsNullOrEmpty(hostCustomLevel)) hostScene = scene;
    }

    internal static void StartHostCustomLevel(string levelJson)
    {
        if (!isHost) throw new InvalidOperationException("Only the host can start a custom level.");
        if (string.IsNullOrWhiteSpace(levelJson) || Encoding.UTF8.GetByteCount(levelJson) > 4 * 1024 * 1024)
            throw new InvalidOperationException("Custom level data is empty or too large.");
        lock (statusLock) hostCustomLevel = levelJson;
        hostScene = "LevelLoader";
        SendCustomLevel(levelJson);
        SendSettings();
        SendScene(hostScene);
    }

    internal static bool TryTakeScene(out string scene)
    {
        lock (statusLock)
        {
            scene = pendingScene;
            pendingScene = "";
            return !string.IsNullOrEmpty(scene);
        }
    }

    internal static bool TryTakeCustomLevel(out string levelJson)
    {
        lock (statusLock)
        {
            levelJson = pendingCustomLevel;
            pendingCustomLevel = "";
            return !string.IsNullOrEmpty(levelJson);
        }
    }

    internal static bool IsConnected { get { lock (statusLock) return socket != null &&
        socket.State == WebSocketState.Open && peers.Count > 0; } }
    internal static bool IsHosting { get { return socket != null && socket.State == WebSocketState.Open && isHost; } }
    internal static bool IsHost { get { return isHost; } }
    internal static bool PvpEnabled { get; private set; }
    internal static bool CanGrabPlayers { get; private set; }
    internal static bool GrabOnlyUnconscious { get; private set; }
    internal static bool AllowRespawn { get; private set; }
    internal static int RespawnTimeSeconds { get; private set; }
    internal static bool RespawnAtStart { get; private set; }
    internal static int PingMs { get { lock (statusLock)
        {
            foreach (var peer in peers.Values) return peer.PingMs;
            return -1;
        } } }
    internal static string LocalPlayerName { get { lock (statusLock) return localPlayerName; } }
    internal static string RemotePlayerName { get { lock (statusLock)
        {
            foreach (var peer in peers.Values) return peer.Name;
            return "";
        } } }
    internal static ushort LocalPeerId { get { lock (statusLock) return localPeerId; } }
    internal static int MaxPlayers { get { lock (statusLock) return maxPlayers; } }
    internal static int PlayerCount { get { lock (statusLock) return 1 + peers.Count; } }

    internal static NetworkDebugStats DebugStats()
    {
        var ping = PingMs;
        lock (networkStatsLock)
        {
            var now = DateTime.UtcNow.Ticks;
            var elapsedTicks = now - statsSampleTicks;
            if (statsSampleTicks == 0 || elapsedTicks >= TimeSpan.TicksPerMillisecond * 250)
            {
                var rx = Interlocked.Read(ref receivedBytes);
                var tx = Interlocked.Read(ref sentBytes);
                if (statsSampleTicks != 0 && elapsedTicks > 0)
                {
                    var seconds = elapsedTicks / (double)TimeSpan.TicksPerSecond;
                    receivedBytesPerSecond = (int)Math.Max(0, (rx - sampledReceivedBytes) / seconds);
                    sentBytesPerSecond = (int)Math.Max(0, (tx - sampledSentBytes) / seconds);
                }
                sampledReceivedBytes = rx;
                sampledSentBytes = tx;
                statsSampleTicks = now;
            }

            var received = Interlocked.Read(ref receivedSnapshotPackets);
            var lost = Interlocked.Read(ref lostSnapshotPackets);
            return new NetworkDebugStats
            {
                PingMs = ping,
                ReceivedBytesPerSecond = receivedBytesPerSecond,
                SentBytesPerSecond = sentBytesPerSecond,
                PacketLossPercent = received + lost == 0 ? 0f : (float)(lost * 100.0 / (received + lost))
            };
        }
    }

    internal static bool HasPeer(ushort peerId)
    {
        lock (statusLock) return peers.ContainsKey(peerId);
    }

    internal static string PlayerName(ushort peerId)
    {
        lock (statusLock)
        {
            if (peerId == localPeerId) return localPlayerName;
            PeerState peer;
            return peers.TryGetValue(peerId, out peer) ? peer.Name : "Player";
        }
    }

    internal static int PeerPing(ushort peerId)
    {
        lock (statusLock)
        {
            PeerState peer;
            return peers.TryGetValue(peerId, out peer) ? peer.PingMs : -1;
        }
    }

    internal static ushort[] PeerIds()
    {
        lock (statusLock)
        {
            var result = new ushort[peers.Count];
            peers.Keys.CopyTo(result, 0);
            return result;
        }
    }

    internal static void UpdateConnection()
    {
        var timedOut = new List<ushort>();
        var now = DateTime.UtcNow.Ticks;
        lock (statusLock)
            foreach (var pair in peers)
                if (pair.Value.LastPacketTicks > 0 && now - pair.Value.LastPacketTicks > PeerTimeoutTicks)
                    timedOut.Add(pair.Key);
        foreach (var peerId in timedOut)
        {
            if (isHost && peerId != localPeerId) Send(disconnectHeader, PeerDeparturePayload(peerId));
            var hostLeft = !isHost && peerId == hostPeerId;
            DropPeer(peerId, hostLeft,
                hostLeft ? "Host connection timed out." : PlayerName(peerId) + " timed out.");
        }
    }

    internal static bool TryTakeHostDisconnected()
    {
        lock (statusLock)
        {
            var pending = hostDisconnectPending;
            hostDisconnectPending = false;
            return pending;
        }
    }

    internal static void Shutdown()
    {
        SendDisconnectImmediately();
        CloseSocket(true);
        lock (statusLock)
        {
            peers.Clear();
            ClearPeerQueuesLocked();
        }
    }

    internal static void UpdatePing()
    {
        if (!IsConnected) return;
        var now = DateTime.UtcNow.Ticks;
        lock (statusLock)
        {
            if (now < nextPingTicks) return;
            nextPingTicks = now + TimeSpan.TicksPerSecond;
            pendingPingTicks = now;
        }
        Send(pingHeader, BitConverter.GetBytes(now));
    }

    internal static void SendIdentity(string name, string prefab)
    {
        Send(identityHeader, Encoding.UTF8.GetBytes(name + "\n" + prefab));
    }

    internal static void SendSnapshot(byte[] data)
    {
        if (data == null) return;
        var payload = new byte[sizeof(int) + data.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(Interlocked.Increment(ref playerSnapshotSequence)), 0,
            payload, 0, sizeof(int));
        Buffer.BlockCopy(data, 0, payload, sizeof(int), data.Length);
        Send(snapshotHeader, payload);
    }

    internal static void SendWorldSnapshot(byte[] data)
    {
        if (isHost) Send(worldHeader, data);
    }

    internal static void SendWorldInput(byte[] data)
    {
        if (!isHost) Send(worldInputHeader, data, 1);
    }

    internal static void SendWorldDamage(byte[] data)
    {
        if (!isHost) Send(worldDamageHeader, data, 1);
    }

    internal static void SendNpcSnapshot(byte[] data)
    {
        if (!isHost || data == null) return;
        const int chunkSize = 60 * 1024;
        var transferId = Interlocked.Increment(ref npcTransferId);
        var chunkCount = Math.Max(1, (data.Length + chunkSize - 1) / chunkSize);
        for (var index = 0; index < chunkCount; index++)
        {
            var sourceOffset = index * chunkSize;
            var length = Math.Min(chunkSize, data.Length - sourceOffset);
            var chunk = new byte[12 + length];
            Buffer.BlockCopy(BitConverter.GetBytes(transferId), 0, chunk, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)index), 0, chunk, 4, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)chunkCount), 0, chunk, 6, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(data.Length), 0, chunk, 8, 4);
            if (length > 0) Buffer.BlockCopy(data, sourceOffset, chunk, 12, length);
            Send(npcHeader, chunk);
        }
    }

    internal static void SendNpcDamage(byte[] data)
    {
        if (!isHost) Send(npcDamageHeader, data, 1);
    }

    private static void SendCustomLevel(string levelJson, ushort targetId = 0)
    {
        var data = Encoding.UTF8.GetBytes(levelJson);
        const int chunkSize = 60 * 1024;
        var transferId = Interlocked.Increment(ref customLevelTransferId);
        var chunkCount = Math.Max(1, (data.Length + chunkSize - 1) / chunkSize);
        for (var index = 0; index < chunkCount; index++)
        {
            var sourceOffset = index * chunkSize;
            var length = Math.Min(chunkSize, data.Length - sourceOffset);
            var chunk = new byte[12 + length];
            Buffer.BlockCopy(BitConverter.GetBytes(transferId), 0, chunk, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)index), 0, chunk, 4, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)chunkCount), 0, chunk, 6, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(data.Length), 0, chunk, 8, 4);
            if (length > 0) Buffer.BlockCopy(data, sourceOffset, chunk, 12, length);
            Send(customLevelHeader, chunk, targetId);
        }
    }

    private static void SendScene(string scene, ushort targetId = 0)
    {
        var payload = Encoding.UTF8.GetBytes(scene ?? "");
        var packet = PacketWithPayload(sceneHeader, payload);
        SendPacket(packet, targetId, false);
    }

    private static void SendSettings(ushort targetId = 0)
    {
        var packet = new byte[settingsHeader.Length + 7];
        Buffer.BlockCopy(settingsHeader, 0, packet, 0, settingsHeader.Length);
        packet[settingsHeader.Length] = PvpEnabled ? (byte)1 : (byte)0;
        packet[settingsHeader.Length + 1] = CanGrabPlayers ? (byte)1 : (byte)0;
        packet[settingsHeader.Length + 2] = GrabOnlyUnconscious ? (byte)1 : (byte)0;
        packet[settingsHeader.Length + 3] = AllowRespawn ? (byte)1 : (byte)0;
        packet[settingsHeader.Length + 4] = RespawnAtStart ? (byte)1 : (byte)0;
        Buffer.BlockCopy(BitConverter.GetBytes((ushort)RespawnTimeSeconds), 0,
            packet, settingsHeader.Length + 5, sizeof(ushort));
        SendPacket(packet, targetId);
    }

    internal static void SendWorldInteraction(byte[] data)
    {
        if (!isHost) Send(worldInteractionHeader, data, 1);
    }

    internal static void SendPlayerDamage(ushort targetPeerId, byte[] data)
    {
        if (isHost) Send(playerDamageHeader, data, targetPeerId);
    }

    internal static void SendPvpDamage(ushort targetPeerId, byte[] data)
    {
        Send(pvpDamageHeader, data, targetPeerId);
    }

    internal static void SendShotVisual(byte[] data)
    {
        Send(shotVisualHeader, data);
    }

    internal static void SendPlayerGrab(ushort targetPeerId, byte[] data)
    {
        Send(playerGrabHeader, data, targetPeerId);
    }

    internal static void SendNpcGrab(byte[] data)
    {
        if (!isHost) Send(npcGrabHeader, data, 1);
    }

    internal static void SendChat(string message, bool system = false)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(message)) return;
        var text = message.Trim();
        if (text.Length > 160) text = text.Substring(0, 160);
        var encoded = Encoding.UTF8.GetBytes(text);
        var payload = new byte[sizeof(int) + 1 + encoded.Length];
        var id = Interlocked.Increment(ref chatSequence);
        Buffer.BlockCopy(BitConverter.GetBytes(id), 0, payload, 0, sizeof(int));
        payload[sizeof(int)] = system ? (byte)1 : (byte)0;
        Buffer.BlockCopy(encoded, 0, payload, sizeof(int) + 1, encoded.Length);
        // WebSocket is reliable, so the UDP-era duplicate send is unnecessary.
        Send(chatHeader, payload);
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

    internal static bool TryTakeSnapshot(out ushort peerId, out byte[] data)
    {
        lock (statusLock)
        {
            peerId = 0;
            data = null;
            foreach (var pair in snapshots)
            {
                peerId = pair.Key;
                data = pair.Value;
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

    internal static bool TryTakeWorldInput(out ushort peerId, out byte[] data)
    {
        return TryTakePayload(worldInputs, out peerId, out data);
    }

    internal static bool TryTakeWorldDamage(out byte[] data)
    {
        ushort ignored; return TryTakePayload(worldDamage, out ignored, out data);
    }

    internal static bool TryTakeNpcSnapshot(out byte[] data)
    {
        ushort ignored; return TryTakePayload(npcSnapshots, out ignored, out data);
    }

    internal static bool TryTakeNpcDamage(out byte[] data)
    {
        ushort ignored; return TryTakePayload(npcDamage, out ignored, out data);
    }

    internal static bool TryTakeNpcDamage(out ushort peerId, out byte[] data)
    {
        return TryTakePayload(npcDamage, out peerId, out data);
    }

    internal static bool TryTakeWorldInteraction(out ushort peerId, out byte[] data)
    {
        return TryTakePayload(worldInteractions, out peerId, out data);
    }

    internal static bool TryTakePlayerDamage(out ushort peerId, out byte[] data)
    {
        return TryTakePayload(playerDamage, out peerId, out data);
    }

    internal static bool TryTakePvpDamage(out ushort peerId, out byte[] data)
    {
        return TryTakePayload(pvpDamage, out peerId, out data);
    }

    internal static bool TryTakeShotVisual(out ushort peerId, out byte[] data)
    {
        return TryTakePayload(shotVisuals, out peerId, out data);
    }

    internal static bool TryTakePlayerGrab(out ushort peerId, out byte[] data)
    {
        return TryTakePayload(playerGrabs, out peerId, out data);
    }

    internal static bool TryTakeNpcGrab(out ushort peerId, out byte[] data)
    {
        return TryTakePayload(npcGrabs, out peerId, out data);
    }

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
            if ((Matches(packet, hello) || HasHeader(packet, hello)) && isHost)
            {
                string connectedName;
                lock (statusLock)
                {
                    connectedName = ReadPlayerName(packet, hello.Length);
                    TouchPeerLocked(senderId, connectedName);
                }
                var acceptedPacket = PacketWithPayload(accepted, Encoding.UTF8.GetBytes(localPlayerName));
                SendPacket(acceptedPacket, senderId);
                string customLevel;
                lock (statusLock) customLevel = hostCustomLevel;
                if (!string.IsNullOrEmpty(customLevel)) SendCustomLevel(customLevel, senderId);
                var scene = Encoding.UTF8.GetBytes(hostScene);
                var scenePacket = new byte[sceneHeader.Length + scene.Length];
                Buffer.BlockCopy(sceneHeader, 0, scenePacket, 0, sceneHeader.Length);
                Buffer.BlockCopy(scene, 0, scenePacket, sceneHeader.Length, scene.Length);
                SendPacket(scenePacket, senderId, false);
                SendSettings(senderId);
                SetStatus(connectedName + " connected. Sent scene " + hostScene + ".");
            }
            else if ((Matches(packet, accepted) || HasHeader(packet, accepted)) && !isHost)
            {
                lock (statusLock)
                {
                    hostPeerId = senderId;
                    TouchPeerLocked(senderId, ReadPlayerName(packet, accepted.Length));
                }
                SetStatus("Connected to host. Receiving match scene...");
            }
            else if (HasHeader(packet, disconnectHeader))
            {
                if (packet.Length >= disconnectHeader.Length + 3 &&
                    packet[disconnectHeader.Length] == 0)
                {
                    var departedPeerId = BitConverter.ToUInt16(packet, disconnectHeader.Length + 1);
                    if (departedPeerId != 0 && departedPeerId != localPeerId && departedPeerId != senderId)
                        DropPeer(departedPeerId, false, PlayerName(departedPeerId) + " left the lobby.");
                    continue;
                }
                if (isHost && senderId != localPeerId)
                    Send(disconnectHeader, PeerDeparturePayload(senderId));
                var hostLeft = senderId == hostPeerId && !isHost;
                DropPeer(senderId, hostLeft, hostLeft ? "Host closed the lobby." :
                    PlayerName(senderId) + " left the lobby.");
                continue;
            }
            else if (!isHost && HasHeader(packet, customLevelHeader))
            {
                ReceiveCustomLevelChunk(packet);
            }
            else if (!isHost && HasHeader(packet, sceneHeader))
            {
                var scene = Encoding.UTF8.GetString(packet, sceneHeader.Length, packet.Length - sceneHeader.Length);
                if (!string.IsNullOrEmpty(scene))
                {
                    lock (statusLock) pendingScene = scene;
                }
            }
            else if (HasHeader(packet, identityHeader))
            {
                var identity = Encoding.UTF8.GetString(packet, identityHeader.Length, packet.Length - identityHeader.Length);
                lock (statusLock)
                {
                    var split = identity.IndexOf('\n');
                    var name = split > 0 ? NormalizePlayerName(identity.Substring(0, split)) : "Player";
                    TouchPeerLocked(senderId, name);
                    identities.Enqueue(new PeerIdentity { PeerId = senderId, Identity = identity });
                    while (identities.Count > MaxPendingIdentities) identities.Dequeue();
                }
            }
            else if (HasHeader(packet, snapshotHeader))
            {
                if (packet.Length < snapshotHeader.Length + sizeof(int)) continue;
                var sequence = BitConverter.ToInt32(packet, snapshotHeader.Length);
                var data = new byte[packet.Length - snapshotHeader.Length - sizeof(int)];
                Buffer.BlockCopy(packet, snapshotHeader.Length + sizeof(int), data, 0, data.Length);
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
                    snapshots[senderId] = data;
                }
            }
            else if (!isHost && HasHeader(packet, worldHeader))
            {
                var data = new byte[packet.Length - worldHeader.Length];
                Buffer.BlockCopy(packet, worldHeader.Length, data, 0, data.Length);
                EnqueueLatestPayload(worldSnapshots, senderId, data);
            }
            else if (isHost && HasHeader(packet, worldInputHeader))
            {
                var data = new byte[packet.Length - worldInputHeader.Length];
                Buffer.BlockCopy(packet, worldInputHeader.Length, data, 0, data.Length);
                EnqueueLatestPayload(worldInputs, senderId, data);
            }
            else if (isHost && HasHeader(packet, worldDamageHeader))
            {
                var data = new byte[packet.Length - worldDamageHeader.Length];
                Buffer.BlockCopy(packet, worldDamageHeader.Length, data, 0, data.Length);
                EnqueuePayload(worldDamage, senderId, data);
            }
            else if (!isHost && HasHeader(packet, npcHeader))
            {
                ReceiveNpcChunk(senderId, packet);
            }
            else if (isHost && HasHeader(packet, npcDamageHeader))
            {
                var data = new byte[packet.Length - npcDamageHeader.Length];
                Buffer.BlockCopy(packet, npcDamageHeader.Length, data, 0, data.Length);
                EnqueuePayload(npcDamage, senderId, data);
            }
            else if (isHost && HasHeader(packet, worldInteractionHeader))
            {
                var data = new byte[packet.Length - worldInteractionHeader.Length];
                Buffer.BlockCopy(packet, worldInteractionHeader.Length, data, 0, data.Length);
                EnqueuePayload(worldInteractions, senderId, data);
            }
            else if (!isHost && HasHeader(packet, playerDamageHeader))
            {
                var data = new byte[packet.Length - playerDamageHeader.Length];
                Buffer.BlockCopy(packet, playerDamageHeader.Length, data, 0, data.Length);
                EnqueuePayload(playerDamage, senderId, data);
            }
            else if (HasHeader(packet, pvpDamageHeader))
            {
                var data = new byte[packet.Length - pvpDamageHeader.Length];
                Buffer.BlockCopy(packet, pvpDamageHeader.Length, data, 0, data.Length);
                EnqueuePayload(pvpDamage, senderId, data);
            }
            else if (!isHost && HasHeader(packet, settingsHeader))
            {
                PvpEnabled = packet[settingsHeader.Length] != 0;
                CanGrabPlayers = packet.Length > settingsHeader.Length + 1 && packet[settingsHeader.Length + 1] != 0;
                GrabOnlyUnconscious = CanGrabPlayers && packet.Length > settingsHeader.Length + 2 &&
                    packet[settingsHeader.Length + 2] != 0;
                AllowRespawn = packet.Length > settingsHeader.Length + 3 &&
                    packet[settingsHeader.Length + 3] != 0;
                RespawnAtStart = packet.Length > settingsHeader.Length + 4 &&
                    packet[settingsHeader.Length + 4] != 0;
                RespawnTimeSeconds = packet.Length >= settingsHeader.Length + 7
                    ? BitConverter.ToUInt16(packet, settingsHeader.Length + 5)
                    : 0;
                SetStatus("Lobby settings received. PVP " + (PvpEnabled ? "enabled" : "disabled") +
                    "; player grab " + (CanGrabPlayers ? (GrabOnlyUnconscious ? "unconscious only" : "enabled") : "disabled") +
                    "; respawn " + (AllowRespawn ? RespawnTimeSeconds + "s." : "disabled."));
            }
            else if (HasHeader(packet, shotVisualHeader))
            {
                var data = new byte[packet.Length - shotVisualHeader.Length];
                Buffer.BlockCopy(packet, shotVisualHeader.Length, data, 0, data.Length);
                EnqueuePayload(shotVisuals, senderId, data);
            }
            else if (HasHeader(packet, playerGrabHeader))
            {
                var data = new byte[packet.Length - playerGrabHeader.Length];
                Buffer.BlockCopy(packet, playerGrabHeader.Length, data, 0, data.Length);
                EnqueuePayload(playerGrabs, senderId, data);
            }
            else if (isHost && HasHeader(packet, npcGrabHeader))
            {
                var data = new byte[packet.Length - npcGrabHeader.Length];
                Buffer.BlockCopy(packet, npcGrabHeader.Length, data, 0, data.Length);
                EnqueuePayload(npcGrabs, senderId, data);
            }
            else if (HasHeader(packet, chatHeader) && packet.Length > chatHeader.Length + sizeof(int) + 1)
            {
                var id = BitConverter.ToInt32(packet, chatHeader.Length);
                var chatKey = ((long)senderId << 32) | (uint)id;
                var system = packet[chatHeader.Length + sizeof(int)] != 0;
                var text = Encoding.UTF8.GetString(packet, chatHeader.Length + sizeof(int) + 1,
                    packet.Length - chatHeader.Length - sizeof(int) - 1).Trim();
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
            else if (HasHeader(packet, pingHeader) && packet.Length == pingHeader.Length + sizeof(long))
            {
                var data = new byte[sizeof(long)];
                Buffer.BlockCopy(packet, pingHeader.Length, data, 0, data.Length);
                Send(pongHeader, data, senderId);
            }
            else if (HasHeader(packet, pongHeader) && packet.Length == pongHeader.Length + sizeof(long))
            {
                var sent = BitConverter.ToInt64(packet, pongHeader.Length);
                var now = DateTime.UtcNow.Ticks;
                lock (statusLock)
                {
                    if (sent == pendingPingTicks && now >= sent && now - sent <= TimeSpan.TicksPerSecond * 30)
                    {
                        var sample = (int)Math.Min(9999, (now - sent) / TimeSpan.TicksPerMillisecond);
                        PeerState peer;
                        if (peers.TryGetValue(senderId, out peer))
                            peer.PingMs = peer.PingMs < 0 ? sample : (peer.PingMs * 3 + sample) / 4;
                    }
                }
            }
            lock (statusLock) TouchPeerLocked(senderId, null);
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { DropRelay(!isHost, "Relay connection closed."); }
        catch (WebSocketException) { DropRelay(!isHost, "WebSocket relay connection closed."); }
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
            foreach (var peer in peers.Values) peer.PingMs = -1;
        }
    }

    private static void ResetNetworkStats()
    {
        Interlocked.Exchange(ref playerSnapshotSequence, 0);
        Interlocked.Exchange(ref receivedSnapshotPackets, 0);
        Interlocked.Exchange(ref lostSnapshotPackets, 0);
        Interlocked.Exchange(ref receivedBytes, 0);
        Interlocked.Exchange(ref sentBytes, 0);
        Interlocked.Exchange(ref receivedPackets, 0);
        Interlocked.Exchange(ref sentPackets, 0);
        lock (networkStatsLock)
        {
            statsSampleTicks = 0;
            sampledReceivedBytes = 0;
            sampledSentBytes = 0;
            receivedBytesPerSecond = 0;
            sentBytesPerSecond = 0;
        }
    }

    private static void DropPeer(ushort peerId, bool hostLeft, string message)
    {
        ClientWebSocket close = null;
        CancellationTokenSource cancel = null;
        lock (statusLock)
        {
            if (!peers.Remove(peerId)) return;
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
        try { if (close != null) close.Abort(); } catch { }
        if (close != null) close.Dispose();
        if (cancel != null) cancel.Dispose();
    }

    private static void DropRelay(bool hostLeft, string message)
    {
        ClientWebSocket close;
        CancellationTokenSource cancel;
        lock (statusLock)
        {
            close = socket;
            cancel = socketCancellation;
            socket = null;
            socketCancellation = null;
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
        try { if (close != null) close.Abort(); } catch { }
        if (close != null) close.Dispose();
        if (cancel != null) cancel.Dispose();
    }

    private static void ClearPeerQueuesLocked()
    {
        identities.Clear();
        snapshots.Clear();
        receivedSnapshotSequences.Clear();
        worldSnapshots.Clear();
        worldInputs.Clear();
        worldDamage.Clear();
        npcSnapshots.Clear();
        npcTransfers.Clear();
        npcDamage.Clear();
        worldInteractions.Clear();
        playerDamage.Clear();
        pvpDamage.Clear();
        shotVisuals.Clear();
        playerGrabs.Clear();
        npcGrabs.Clear();
        chatMessages.Clear();
        receivedChatIds.Clear();
        receivedChatOrder.Clear();
    }

    private static void EnqueuePayload(Queue<PeerPayload> queue, ushort peerId, byte[] data)
    {
        lock (statusLock)
        {
            TouchPeerLocked(peerId, null);
            while (queue.Count >= MaxPendingEventPackets) queue.Dequeue();
            queue.Enqueue(new PeerPayload { PeerId = peerId, Data = data });
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
        PeerState peer;
        if (!peers.TryGetValue(peerId, out peer))
        {
            peer = new PeerState();
            peers[peerId] = peer;
        }
        if (!string.IsNullOrEmpty(name)) peer.Name = NormalizePlayerName(name);
        peer.LastPacketTicks = DateTime.UtcNow.Ticks;
    }

    private static void ReceiveNpcChunk(ushort senderId, byte[] packet)
    {
        var offset = npcHeader.Length;
        if (packet.Length < offset + 12) return;
        var transferId = BitConverter.ToInt32(packet, offset);
        var chunkIndex = BitConverter.ToUInt16(packet, offset + 4);
        var chunkCount = BitConverter.ToUInt16(packet, offset + 6);
        var totalLength = BitConverter.ToInt32(packet, offset + 8);
        var chunkLength = packet.Length - offset - 12;
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
                var chunk = new byte[chunkLength];
                if (chunkLength > 0) Buffer.BlockCopy(packet, offset + 12, chunk, 0, chunkLength);
                transfer.Chunks[chunkIndex] = chunk;
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

    private static void ReceiveCustomLevelChunk(byte[] packet)
    {
        var offset = customLevelHeader.Length;
        if (packet.Length < offset + 12) return;
        var transferId = BitConverter.ToInt32(packet, offset);
        var chunkIndex = BitConverter.ToUInt16(packet, offset + 4);
        var chunkCount = BitConverter.ToUInt16(packet, offset + 6);
        var totalLength = BitConverter.ToInt32(packet, offset + 8);
        var chunkLength = packet.Length - offset - 12;
        if (chunkCount < 1 || chunkIndex >= chunkCount || totalLength < 2 || totalLength > 4 * 1024 * 1024) return;

        lock (statusLock)
        {
            if (customLevelTransfer == null || customLevelTransfer.TransferId != transferId ||
                customLevelTransfer.TotalLength != totalLength || customLevelTransfer.Chunks.Length != chunkCount)
                customLevelTransfer = new CustomLevelTransfer(transferId, totalLength, chunkCount);
            if (customLevelTransfer.Chunks[chunkIndex] == null)
            {
                var chunk = new byte[chunkLength];
                if (chunkLength > 0) Buffer.BlockCopy(packet, offset + 12, chunk, 0, chunkLength);
                customLevelTransfer.Chunks[chunkIndex] = chunk;
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

    private static void Send(byte[] header, byte[] payload, ushort targetId = 0)
    {
        var current = socket;
        if (current == null || current.State != WebSocketState.Open) return;
        try
        {
            var packet = new byte[header.Length + payload.Length];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length);
            Buffer.BlockCopy(payload, 0, packet, header.Length, payload.Length);
            SendPacket(packet, targetId);
        }
        catch (ObjectDisposedException) { }
        catch (WebSocketException) { }
    }

    private static void SendDisconnectImmediately()
    {
        ClientWebSocket current;
        CancellationTokenSource cancellation;
        ushort targetId;
        lock (statusLock)
        {
            current = socket;
            cancellation = socketCancellation;
            targetId = isHost ? (ushort)0 : hostPeerId;
        }
        if (current == null || cancellation == null || current.State != WebSocketState.Open) return;

        var payload = PacketWithPayload(disconnectHeader, new byte[] { 1 });
        var routed = new byte[sizeof(ushort) + payload.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(targetId), 0, routed, 0, sizeof(ushort));
        Buffer.BlockCopy(payload, 0, routed, sizeof(ushort), payload.Length);
        try { SendPacketBlocking(current, cancellation, routed); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (WebSocketException) { }
        catch (IOException) { }
    }

    private static byte[] PeerDeparturePayload(ushort peerId)
    {
        var payload = new byte[3];
        payload[0] = 0;
        Buffer.BlockCopy(BitConverter.GetBytes(peerId), 0, payload, 1, sizeof(ushort));
        return payload;
    }

    private static ClientWebSocket ConnectRelay(string address, string lobbyId, string relayKey)
    {
        Uri uri;
        if (!Uri.TryCreate(address, UriKind.Absolute, out uri) ||
            (uri.Scheme != "ws" && uri.Scheme != "wss"))
            throw new InvalidOperationException("Invalid WebSocket relay URL.");
        if (lobbyId == null || lobbyId.Length != 32 || relayKey == null || relayKey.Length != 32)
            throw new InvalidOperationException("Invalid relay credentials.");

        var client = new ClientWebSocket();
        client.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socketCancellation = new CancellationTokenSource();
        client.ConnectAsync(uri, socketCancellation.Token).GetAwaiter().GetResult();
        socket = client;
        SendPacketBlocking(client, socketCancellation, Encoding.ASCII.GetBytes(lobbyId + relayKey));
        StartSendWorker(client, socketCancellation);
        return client;
    }

    private static byte[] ReadPacket(byte[] buffer, out ushort senderId)
    {
        senderId = 0;
        var current = socket;
        var cancellation = socketCancellation;
        if (current == null || cancellation == null || current.State != WebSocketState.Open) return null;

        var length = 0;
        while (true)
        {
            if (length >= buffer.Length) throw new InvalidDataException("Invalid WebSocket relay frame.");
            var result = current.ReceiveAsync(
                new ArraySegment<byte>(buffer, length, buffer.Length - length), cancellation.Token)
                .GetAwaiter().GetResult();
            if (result.MessageType == WebSocketMessageType.Close)
                throw new IOException("WebSocket relay closed the connection.");
            if (result.MessageType != WebSocketMessageType.Binary)
                throw new InvalidDataException("Relay sent a non-binary WebSocket message.");
            length += result.Count;
            if (!result.EndOfMessage) continue;
            if (length < sizeof(ushort))
                throw new InvalidDataException("Relay message has no sender ID.");
            senderId = BitConverter.ToUInt16(buffer, 0);
            var packet = new byte[length - sizeof(ushort)];
            Buffer.BlockCopy(buffer, sizeof(ushort), packet, 0, packet.Length);
            Interlocked.Add(ref receivedBytes, length);
            Interlocked.Increment(ref receivedPackets);
            return packet;
        }
    }

    private static void SendPacket(byte[] packet, ushort targetId = 0, bool? priority = null)
    {
        if (packet == null || packet.Length == 0) return;
        var current = socket;
        var cancellation = socketCancellation;
        if (current == null || cancellation == null || current.State != WebSocketState.Open)
            throw new IOException("Relay connection is closed.");

        var routed = new byte[sizeof(ushort) + packet.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(targetId), 0, routed, 0, sizeof(ushort));
        Buffer.BlockCopy(packet, 0, routed, sizeof(ushort), packet.Length);

        var queue = (priority ?? IsLatencySensitivePacket(routed)) ? prioritySendQueue : sendQueue;
        lock (sendQueueLock)
        {
            if (IsReplaceableStatePacket(routed))
            {
                var pending = queue.Count;
                for (var index = 0; index < pending; index++)
                {
                    var queued = queue.Dequeue();
                    if (!SameReplaceableState(queued, routed)) queue.Enqueue(queued);
                }
            }
            if (sendQueue.Count + prioritySendQueue.Count >= MaxQueuedPackets)
            {
                SetStatus("Network send queue is overloaded; dropping a packet.");
                return;
            }
            queue.Enqueue(routed);
        }
        sendSignal.Set();
    }

    private static bool IsReplaceableStatePacket(byte[] packet)
    {
        return HasRoutedHeader(packet, identityHeader) ||
            HasRoutedHeader(packet, snapshotHeader) ||
            HasRoutedHeader(packet, worldHeader) ||
            HasRoutedHeader(packet, worldInputHeader) ||
            HasRoutedHeader(packet, npcHeader);
    }

    private static bool IsLatencySensitivePacket(byte[] packet)
    {
        return !HasRoutedHeader(packet, worldHeader) && !HasRoutedHeader(packet, npcHeader) &&
            !HasRoutedHeader(packet, customLevelHeader) && !HasRoutedHeader(packet, sceneHeader);
    }

    private static bool SameReplaceableState(byte[] left, byte[] right)
    {
        if (!IsReplaceableStatePacket(left) || !IsReplaceableStatePacket(right) ||
            left.Length < sizeof(ushort) + hello.Length || right.Length < sizeof(ushort) + hello.Length)
            return false;
        if (left[0] != right[0] || left[1] != right[1] ||
            left[sizeof(ushort) + hello.Length - 1] != right[sizeof(ushort) + hello.Length - 1])
            return false;

        if (HasRoutedHeader(right, npcHeader))
        {
            var rightOffset = sizeof(ushort) + npcHeader.Length;
            if (right.Length < rightOffset + 8 ||
                BitConverter.ToUInt16(right, rightOffset + 4) != 0) return false;
            var leftOffset = sizeof(ushort) + npcHeader.Length;
            if (left.Length < leftOffset + 8) return false;
            return BitConverter.ToInt32(left, leftOffset) !=
                BitConverter.ToInt32(right, rightOffset);
        }
        return true;
    }

    private static bool HasRoutedHeader(byte[] packet, byte[] header)
    {
        if (packet == null || header == null || packet.Length < sizeof(ushort) + header.Length) return false;
        for (var index = 0; index < header.Length; index++)
            if (packet[sizeof(ushort) + index] != header[index]) return false;
        return true;
    }

    private static void StartSendWorker(ClientWebSocket client, CancellationTokenSource cancellation)
    {
        lock (sendQueueLock)
        {
            sendQueue.Clear();
            prioritySendQueue.Clear();
        }
        var worker = new Thread(() => SendLoop(client, cancellation));
        worker.IsBackground = true;
        worker.Name = "Gunsaw WebSocket sender";
        worker.Priority = ThreadPriority.AboveNormal;
        sendThread = worker;
        worker.Start();
    }

    private static void SendLoop(ClientWebSocket client, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested && client.State == WebSocketState.Open)
            {
                byte[] packet = null;
                lock (sendQueueLock)
                {
                    if (prioritySendQueue.Count > 0) packet = prioritySendQueue.Dequeue();
                    else if (sendQueue.Count > 0) packet = sendQueue.Dequeue();
                }
                if (packet == null)
                {
                    sendSignal.WaitOne(100);
                    continue;
                }
                SendPacketBlocking(client, cancellation, packet);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (WebSocketException exception)
        {
            SetStatus("WebSocket send failed: " + exception.Message);
        }
        catch (IOException exception)
        {
            SetStatus("WebSocket send failed: " + exception.Message);
        }
    }

    private static void SendPacketBlocking(ClientWebSocket client, CancellationTokenSource cancellation,
        byte[] packet)
    {
        lock (sendLock)
        {
            if (client == null || cancellation == null || client.State != WebSocketState.Open)
                throw new IOException("Relay connection is closed.");
            client.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true,
                cancellation.Token).GetAwaiter().GetResult();
            Interlocked.Add(ref sentBytes, packet.Length);
            Interlocked.Increment(ref sentPackets);
        }
    }

    private static void CloseSocket(bool graceful = false)
    {
        var current = socket;
        var cancellation = socketCancellation;
        socket = null;
        socketCancellation = null;
        if (graceful && current != null && current.State == WebSocketState.Open)
        {
            try
            {
                current.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Leaving lobby.",
                    CancellationToken.None).Wait(250);
            }
            catch { }
        }
        try { if (cancellation != null) cancellation.Cancel(); } catch { }
        sendSignal.Set();
        lock (sendQueueLock)
        {
            sendQueue.Clear();
            prioritySendQueue.Clear();
        }

        try { if (current != null && !graceful) current.Abort(); } catch { }
        if (current != null) current.Dispose();
        if (cancellation != null) cancellation.Dispose();
        sendThread = null;
    }


    private static byte[] PacketWithPayload(byte[] header, byte[] payload)
    {
        var packet = new byte[header.Length + payload.Length];
        Buffer.BlockCopy(header, 0, packet, 0, header.Length);
        Buffer.BlockCopy(payload, 0, packet, header.Length, payload.Length);
        return packet;
    }

    private static string ReadPlayerName(byte[] packet, int offset)
    {
        if (packet == null || packet.Length <= offset) return "Player";
        return NormalizePlayerName(Encoding.UTF8.GetString(packet, offset, packet.Length - offset));
    }

    private static string NormalizePlayerName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Player";
        var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length > 32 ? normalized.Substring(0, 32) : normalized;
    }

    private sealed class NpcTransfer
    {
        internal readonly int TotalLength;
        internal readonly byte[][] Chunks;
        internal int Received;

        internal NpcTransfer(int totalLength, int chunkCount)
        {
            TotalLength = totalLength;
            Chunks = new byte[chunkCount][];
        }
    }

    private sealed class CustomLevelTransfer
    {
        internal readonly int TransferId;
        internal readonly int TotalLength;
        internal readonly byte[][] Chunks;
        internal int Received;

        internal CustomLevelTransfer(int transferId, int totalLength, int chunkCount)
        {
            TransferId = transferId;
            TotalLength = totalLength;
            Chunks = new byte[chunkCount][];
        }
    }

    private sealed class ChatMessage
    {
        internal ushort PeerId;
        internal string Message = "";
        internal bool System;
    }

    private sealed class PeerIdentity
    {
        internal ushort PeerId;
        internal string Identity = "";
    }

    private sealed class PeerPayload
    {
        internal ushort PeerId;
        internal byte[] Data = new byte[0];
    }

    private sealed class PeerState
    {
        internal string Name = "Player";
        internal long LastPacketTicks;
        internal int PingMs = -1;
    }
}

internal struct NetworkDebugStats
{
    internal int PingMs;
    internal int ReceivedBytesPerSecond;
    internal int SentBytesPerSecond;
    internal float PacketLossPercent;
}
