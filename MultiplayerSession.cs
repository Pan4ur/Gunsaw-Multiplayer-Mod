using BepInEx;
using BepInEx.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Threading;

internal enum ConnectionMode
{
    Relay,
    P2P,
    Auto
}

internal static class MultiplayerSession
{
    private static ManualLogSource sessionLogger;
    private static UdpClient socket;
    private static volatile bool relayConnected;
    private static IPEndPoint relayEndpoint;
    private static CancellationTokenSource socketCancellation;
    private static readonly object sendLock = new object();
    private static readonly object sendQueueLock = new object();
    private static readonly Queue<byte[]> prioritySendQueue = new Queue<byte[]>();
    private static readonly Queue<byte[]> sendQueue = new Queue<byte[]>();
    private static readonly AutoResetEvent sendSignal = new AutoResetEvent(false);
    private static Thread sendThread;
    private static readonly byte[] udpMagic = new byte[] { 0x47, 0x55, 0x44, 0x50 };
    private const byte UdpAuth = 1;
    private const byte UdpAuthOk = 2;
    private const byte UdpData = 3;
    private const byte UdpForwarded = 4;
    private const byte UdpAuthFailed = 5;
    private const byte UdpP2PEnable = 6;
    private const byte UdpCandidate = 7;
    private const byte UdpDirectData = 8;
    private const byte UdpKeepAlive = 9;
    private const int P2PKeySize = 16;
    private const long P2PConnectTimeoutTicks = TimeSpan.TicksPerSecond * 5;
    private const long P2PKeepAliveTicks = TimeSpan.TicksPerSecond * 10;
    private const long P2PProbeRetryTicks = TimeSpan.TicksPerMillisecond * 500;
    private const int UdpFragmentPayload = 1000;
    private static int transportMessageSequence;
    private static readonly Dictionary<long, FragmentTransfer> fragmentTransfers =
        new Dictionary<long, FragmentTransfer>();
    private static readonly object reliableLock = new object();
    private static readonly Dictionary<int, PendingReliablePacket> pendingReliable =
        new Dictionary<int, PendingReliablePacket>();
    private static readonly HashSet<long> receivedReliable = new HashSet<long>();
    private static readonly Queue<long> receivedReliableOrder = new Queue<long>();
    private static int reliableSequence;
    private const long ReliableRetryTicks = TimeSpan.TicksPerMillisecond * 150;
    private const int MaxReliableAttempts = 30;
    private const int MaxQueuedPackets = 2048;
    private const int MaxPendingEventPackets = 256;
    private const int MaxPendingIdentities = 64;
    private static readonly object statusLock = new object();
    private static string status = "";
    private static bool isHost;
    private static ConnectionMode connectionMode = ConnectionMode.Relay;
    private static bool relayFallback;
    private static bool p2pHelloSent;
    private static long p2pConnectStartedTicks;
    private static long nextP2PKeepAliveTicks;
    private static byte[] p2pKey;
    private static readonly Dictionary<ushort, P2PPeer> p2pPeers = new Dictionary<ushort, P2PPeer>();
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
    private static readonly byte[] npcPossessHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x17 };
    private static readonly byte[] reliableHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x18 };
    private static readonly byte[] reliableAckHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x19 };
    private static readonly byte[] worldEnvironmentHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x1A };
    private static readonly byte[] projectileImpactHeader = new byte[] { 0x47, 0x4D, 0x50, 0x31, 0x1B };
    private static string hostScene = "";
    private static string pendingScene = "";
    private static string lastReceivedHostScene = "";
    private static string hostCustomLevel = "";
    private static string pendingCustomLevel = "";
    private static readonly Dictionary<ushort, PeerState> peers = new Dictionary<ushort, PeerState>();
    private static readonly Queue<PeerIdentity> identities = new Queue<PeerIdentity>();
    private static readonly Dictionary<ushort, byte[]> snapshots = new Dictionary<ushort, byte[]>();
    private static readonly Queue<PeerPayload> worldSnapshots = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> worldEnvironments = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> worldInputs = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> worldDamage = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> npcSnapshots = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> npcDamage = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> worldInteractions = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> playerDamage = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> pvpDamage = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> shotVisuals = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> projectileImpacts = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> playerGrabs = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> npcGrabs = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> npcPossessions = new Queue<PeerPayload>();
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
    private static long sentNpcBytes;
    private static long sentWorldBytes;
    private static long sentAvatarBytes;
    private static long sentOtherBytes;
    private static readonly object networkStatsLock = new object();
    private static long statsSampleTicks;
    private static long sampledReceivedBytes;
    private static long sampledSentBytes;
    private static long sampledSentNpcBytes;
    private static long sampledSentWorldBytes;
    private static long sampledSentAvatarBytes;
    private static long sampledSentOtherBytes;
    private static int receivedBytesPerSecond;
    private static int sentBytesPerSecond;
    private static int sentNpcBytesPerSecond;
    private static int sentWorldBytesPerSecond;
    private static int sentAvatarBytesPerSecond;
    private static int sentOtherBytesPerSecond;
    private static string localPlayerName = "Player";
    private static ushort localPeerId;
    private static ushort hostPeerId;
    private static int maxPlayers = 2;
    private const long PeerTimeoutTicks = TimeSpan.TicksPerSecond * 4;

    internal static void StartHost(string lobbyId, string relayKey, string relayAddress, bool pvpEnabled,
        bool canGrabPlayers, bool grabOnlyUnconscious, bool allowRespawn, int respawnTimeSeconds,
        bool respawnAtStart, string playerName, ushort assignedPeerId, int lobbyMaxPlayers,
        ConnectionMode mode, ManualLogSource logger)
    {
        CloseSocket();
        ResetNetworkStats();
        sessionLogger = logger;
        lock (statusLock)
        {
            peers.Clear();
            ClearPeerQueuesLocked();
            hostDisconnectPending = false;
            hostCustomLevel = "";
            pendingCustomLevel = "";
            pendingScene = "";
            lastReceivedHostScene = "";
            customLevelTransfer = null;
            localPlayerName = NormalizePlayerName(playerName);
            localPeerId = assignedPeerId == 0 ? (ushort)1 : assignedPeerId;
            hostPeerId = localPeerId;
            maxPlayers = Math.Max(2, Math.Min(16, lobbyMaxPlayers));
            connectionMode = mode;
            relayFallback = mode == ConnectionMode.Relay;
            p2pHelloSent = true;
            p2pConnectStartedTicks = DateTime.UtcNow.Ticks;
        }
        isHost = true;
        MultiplayerDiagnosticLog.StartSession(true);
        socket = ConnectRelay(relayAddress, lobbyId, relayKey);
        if (connectionMode != ConnectionMode.Relay) EnableP2P();
        PvpEnabled = pvpEnabled;
        CanGrabPlayers = canGrabPlayers;
        GrabOnlyUnconscious = canGrabPlayers && grabOnlyUnconscious;
        AllowRespawn = allowRespawn;
        RespawnTimeSeconds = Math.Max(0, Math.Min(3600, respawnTimeSeconds));
        RespawnAtStart = respawnAtStart;
        ResetPing();
        ThreadPool.QueueUserWorkItem(_ => Receive(null));
        logger.LogInfo("Host connected to UDP relay " + relayAddress + " for lobby " + lobbyId + ".");
    }

    internal static bool Connect(string relayAddress, string lobbyId, string relayKey, string playerName,
        ushort assignedPeerId, ushort assignedHostPeerId, int lobbyMaxPlayers, ConnectionMode mode,
        ManualLogSource logger, out string error)
    {
        error = "";
        try
        {
            CloseSocket();
            ResetNetworkStats();
            sessionLogger = logger;
            lock (statusLock)
            {
                peers.Clear();
                ClearPeerQueuesLocked();
                hostDisconnectPending = false;
                hostCustomLevel = "";
                pendingCustomLevel = "";
                pendingScene = "";
                lastReceivedHostScene = "";
                customLevelTransfer = null;
                localPlayerName = NormalizePlayerName(playerName);
                localPeerId = assignedPeerId;
                hostPeerId = assignedHostPeerId == 0 ? (ushort)1 : assignedHostPeerId;
                maxPlayers = Math.Max(2, Math.Min(16, lobbyMaxPlayers));
                connectionMode = mode;
                relayFallback = mode == ConnectionMode.Relay;
                p2pHelloSent = false;
                p2pConnectStartedTicks = DateTime.UtcNow.Ticks;
            }
            isHost = false;
            MultiplayerDiagnosticLog.StartSession(false);
            PvpEnabled = false;
            CanGrabPlayers = false;
            GrabOnlyUnconscious = false;
            AllowRespawn = false;
            RespawnTimeSeconds = 0;
            RespawnAtStart = false;
            ResetPing();
            socket = ConnectRelay(relayAddress, lobbyId, relayKey);
            if (connectionMode == ConnectionMode.Relay) SendInitialHello();
            else EnableP2P();
            ThreadPool.QueueUserWorkItem(_ => Receive(null));
            logger.LogInfo("UDP relay handshake sent to " + relayAddress + ".");
            return true;
        }
        catch (Exception e)
        {
            CloseSocket();
            error = "UDP connection failed: " + e.Message;
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
        if (!isHost || string.IsNullOrEmpty(scene) || !string.IsNullOrEmpty(hostCustomLevel)) return;
        if (hostScene == scene) return;
        hostScene = scene;
        SendScene(scene);
    }

    internal static void EndHostCustomLevel(string scene)
    {
        if (!isHost || string.IsNullOrEmpty(hostCustomLevel)) return;
        lock (statusLock) hostCustomLevel = "";
        hostScene = scene;
        SendScene(scene);
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
        relayConnected && peers.Count > 0; } }
    internal static bool IsHosting { get { return socket != null && relayConnected && isHost; } }
    internal static bool IsHost { get { return isHost; } }
    internal static int SendQueueDepth
    {
        get { lock (sendQueueLock) return sendQueue.Count + prioritySendQueue.Count; }
    }
    internal static int PayloadQueueDepth
    {
        get
        {
            lock (statusLock)
                return worldSnapshots.Count + worldInputs.Count + worldDamage.Count +
                    npcSnapshots.Count + npcDamage.Count + worldInteractions.Count +
                    playerDamage.Count + pvpDamage.Count + shotVisuals.Count +
                    projectileImpacts.Count +
                    playerGrabs.Count + npcGrabs.Count;
        }
    }
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
    internal static string ActiveTransport
    {
        get
        {
            lock (statusLock)
            {
                if (connectionMode == ConnectionMode.Relay) return "RELAY";
                var direct = 0;
                foreach (var peer in p2pPeers.Values) if (peer.Connected) direct++;
                var total = peers.Count;
                if (connectionMode == ConnectionMode.P2P)
                {
                    if (total == 0) return relayFallback ? "P2P + RELAY" : "P2P: CONNECTING";
                    return direct >= total ? "P2P" : "P2P + RELAY";
                }
                if (total == 0) return relayFallback ? "AUTO: RELAY" : "AUTO: CONNECTING";
                if (direct >= total) return "AUTO: P2P";
                if (direct == 0) return "AUTO: RELAY";
                return "AUTO: P2P " + direct + "/" + total + " + RELAY";
            }
        }
    }

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
                var txNpc = Interlocked.Read(ref sentNpcBytes);
                var txWorld = Interlocked.Read(ref sentWorldBytes);
                var txAvatar = Interlocked.Read(ref sentAvatarBytes);
                var txOther = Interlocked.Read(ref sentOtherBytes);
                if (statsSampleTicks != 0 && elapsedTicks > 0)
                {
                    var seconds = elapsedTicks / (double)TimeSpan.TicksPerSecond;
                    receivedBytesPerSecond = (int)Math.Max(0, (rx - sampledReceivedBytes) / seconds);
                    sentBytesPerSecond = (int)Math.Max(0, (tx - sampledSentBytes) / seconds);
                    sentNpcBytesPerSecond = (int)Math.Max(0, (txNpc - sampledSentNpcBytes) / seconds);
                    sentWorldBytesPerSecond = (int)Math.Max(0, (txWorld - sampledSentWorldBytes) / seconds);
                    sentAvatarBytesPerSecond = (int)Math.Max(0, (txAvatar - sampledSentAvatarBytes) / seconds);
                    sentOtherBytesPerSecond = (int)Math.Max(0, (txOther - sampledSentOtherBytes) / seconds);
                }
                sampledReceivedBytes = rx;
                sampledSentBytes = tx;
                sampledSentNpcBytes = txNpc;
                sampledSentWorldBytes = txWorld;
                sampledSentAvatarBytes = txAvatar;
                sampledSentOtherBytes = txOther;
                statsSampleTicks = now;
            }

            var received = Interlocked.Read(ref receivedSnapshotPackets);
            var lost = Interlocked.Read(ref lostSnapshotPackets);
            return new NetworkDebugStats
            {
                PingMs = ping,
                ReceivedBytesPerSecond = receivedBytesPerSecond,
                SentBytesPerSecond = sentBytesPerSecond,
                SentNpcBytesPerSecond = sentNpcBytesPerSecond,
                SentWorldBytesPerSecond = sentWorldBytesPerSecond,
                SentAvatarBytesPerSecond = sentAvatarBytesPerSecond,
                SentOtherBytesPerSecond = sentOtherBytesPerSecond,
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
        var now = DateTime.UtcNow.Ticks;
        UpdateP2PConnection(now);
        var timedOut = new List<ushort>();
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

    private static void UpdateP2PConnection(long now)
    {
        if (socket == null || !relayConnected || connectionMode == ConnectionMode.Relay) return;
        if (now >= nextP2PKeepAliveTicks)
        {
            nextP2PKeepAliveTicks = now + P2PKeepAliveTicks;
            SendControlToRelay(UdpKeepAlive);
        }
        RetryP2PProbes(now);
        if (isHost || p2pHelloSent) return;
        if (IsP2PConnected(hostPeerId))
        {
            SendInitialHello();
            return;
        }
        if (now - p2pConnectStartedTicks < P2PConnectTimeoutTicks) return;
        if (connectionMode == ConnectionMode.Auto)
        {
            relayFallback = true;
            LogP2PWarning("P2P direct connection timed out; falling back to relay.");
            SendInitialHello();
            return;
        }
        LogP2PWarning("P2P direct connection timed out.");
        DropRelay(true, "P2P connection timed out. Try Auto or Relay mode.");
    }

    private static void RetryP2PProbes(long now)
    {
        var peersToProbe = new List<ushort>();
        lock (statusLock)
        {
            foreach (var pair in p2pPeers)
            {
                var peer = pair.Value;
                if (peer == null || peer.Endpoint == null || peer.Connected || now < peer.NextProbeTicks)
                    continue;
                peer.NextProbeTicks = now + P2PProbeRetryTicks;
                peersToProbe.Add(pair.Key);
            }
        }
        foreach (var peerId in peersToProbe) SendDirectProbe(peerId);
    }

    private static void SendInitialHello()
    {
        if (p2pHelloSent) return;
        p2pHelloSent = true;
        var helloPacket = PacketWithPayload(hello, Encoding.UTF8.GetBytes(localPlayerName));
        SendPacket(helloPacket, hostPeerId, true, true, true);
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
        isHost = false;
        PvpEnabled = false;
        CanGrabPlayers = false;
        GrabOnlyUnconscious = false;
        AllowRespawn = false;
        RespawnTimeSeconds = 0;
        RespawnAtStart = false;
        lock (statusLock)
        {
            peers.Clear();
            ClearPeerQueuesLocked();
            pendingScene = "";
            lastReceivedHostScene = "";
            hostCustomLevel = "";
            pendingCustomLevel = "";
            hostDisconnectPending = false;
            maxPlayers = 2;
        }
    }

    internal static bool UpdateHostSettings(bool pvpEnabled, bool canGrabPlayers,
        bool grabOnlyUnconscious, bool allowRespawn, int respawnTimeSeconds,
        bool respawnAtStart, int lobbyMaxPlayers)
    {
        if (!IsHosting) return false;
        PvpEnabled = pvpEnabled;
        CanGrabPlayers = canGrabPlayers;
        GrabOnlyUnconscious = canGrabPlayers && grabOnlyUnconscious;
        AllowRespawn = allowRespawn;
        RespawnTimeSeconds = Math.Max(0, Math.Min(3600, respawnTimeSeconds));
        RespawnAtStart = allowRespawn && respawnAtStart;
        lock (statusLock) maxPlayers = Math.Max(2, Math.Min(16, lobbyMaxPlayers));
        SendSettings();
        return true;
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

    internal static void SendWorldEnvironment(byte[] data)
    {
        if (isHost && data != null) Send(worldEnvironmentHeader, data);
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

    internal static void SendNpcPossession(ulong id)
    {
        if (isHost || id == 0UL) return;
        Send(npcPossessHeader, BitConverter.GetBytes(id), 1);
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
        var packet = new byte[settingsHeader.Length + 8];
        Buffer.BlockCopy(settingsHeader, 0, packet, 0, settingsHeader.Length);
        packet[settingsHeader.Length] = PvpEnabled ? (byte)1 : (byte)0;
        packet[settingsHeader.Length + 1] = CanGrabPlayers ? (byte)1 : (byte)0;
        packet[settingsHeader.Length + 2] = GrabOnlyUnconscious ? (byte)1 : (byte)0;
        packet[settingsHeader.Length + 3] = AllowRespawn ? (byte)1 : (byte)0;
        packet[settingsHeader.Length + 4] = RespawnAtStart ? (byte)1 : (byte)0;
        Buffer.BlockCopy(BitConverter.GetBytes((ushort)RespawnTimeSeconds), 0,
            packet, settingsHeader.Length + 5, sizeof(ushort));
        packet[settingsHeader.Length + 7] = (byte)MaxPlayers;
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

    internal static void SendProjectileImpact(byte[] data)
    {
        Send(projectileImpactHeader, data);
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

    internal static bool TryTakeWorldEnvironment(out byte[] data)
    {
        ushort ignored; return TryTakePayload(worldEnvironments, out ignored, out data);
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

    internal static bool TryTakeProjectileImpact(out ushort peerId, out byte[] data)
    {
        return TryTakePayload(projectileImpacts, out peerId, out data);
    }

    internal static bool TryTakePlayerGrab(out ushort peerId, out byte[] data)
    {
        return TryTakePayload(playerGrabs, out peerId, out data);
    }

    internal static bool TryTakeNpcGrab(out ushort peerId, out byte[] data)
    {
        return TryTakePayload(npcGrabs, out peerId, out data);
    }

    internal static bool TryTakeNpcPossession(out ushort peerId, out byte[] data)
    {
        return TryTakePayload(npcPossessions, out peerId, out data);
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
            if (!ProcessReliablePacket(ref packet, senderId)) continue;
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
                    lock (statusLock)
                    {
                        if (scene == lastReceivedHostScene) continue;
                        lastReceivedHostScene = scene;
                        pendingScene = scene;
                    }
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
            else if (!isHost && HasHeader(packet, worldEnvironmentHeader))
            {
                var data = new byte[packet.Length - worldEnvironmentHeader.Length];
                Buffer.BlockCopy(packet, worldEnvironmentHeader.Length, data, 0, data.Length);
                EnqueueLatestPayload(worldEnvironments, senderId, data);
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
            else if (isHost && HasHeader(packet, npcPossessHeader))
            {
                var data = new byte[packet.Length - npcPossessHeader.Length];
                Buffer.BlockCopy(packet, npcPossessHeader.Length, data, 0, data.Length);
                EnqueuePayload(npcPossessions, senderId, data);
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
                if (packet.Length > settingsHeader.Length + 7)
                {
                    lock (statusLock)
                        maxPlayers = Math.Max(2, Math.Min(16, (int)packet[settingsHeader.Length + 7]));
                }
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
            else if (HasHeader(packet, projectileImpactHeader))
            {
                var data = new byte[packet.Length - projectileImpactHeader.Length];
                Buffer.BlockCopy(packet, projectileImpactHeader.Length, data, 0, data.Length);
                EnqueuePayload(projectileImpacts, senderId, data);
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
            foreach (var peer in peers.Values) peer.PingMs = -1;
        }
    }

    private static void ResetNetworkStats()
    {
        Interlocked.Exchange(ref playerSnapshotSequence, 0);
        Interlocked.Exchange(ref transportMessageSequence, 0);
        Interlocked.Exchange(ref reliableSequence, 0);
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
        lock (reliableLock)
        {
            pendingReliable.Clear();
            receivedReliable.Clear();
            receivedReliableOrder.Clear();
        }
        fragmentTransfers.Clear();
    }

    private static void ClearPeerQueuesLocked()
    {
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
        playerGrabs.Clear();
        npcGrabs.Clear();
        npcPossessions.Clear();
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
        if (current == null || !relayConnected) return;
        try
        {
            var packet = new byte[header.Length + payload.Length];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length);
            Buffer.BlockCopy(payload, 0, packet, header.Length, payload.Length);
            SendPacket(packet, targetId);
        }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
        catch (IOException) { }
    }

    private static void SendDisconnectImmediately()
    {
        UdpClient current;
        CancellationTokenSource cancellation;
        ushort targetId;
        lock (statusLock)
        {
            current = socket;
            cancellation = socketCancellation;
            targetId = isHost ? (ushort)0 : hostPeerId;
        }
        if (current == null || cancellation == null || !relayConnected) return;

        var payload = PacketWithPayload(disconnectHeader, new byte[] { 1 });
        var routed = new byte[sizeof(ushort) + payload.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(targetId), 0, routed, 0, sizeof(ushort));
        Buffer.BlockCopy(payload, 0, routed, sizeof(ushort), payload.Length);
        try { SendPacketBlocking(current, cancellation, routed); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
        catch (IOException) { }
    }

    private static byte[] PeerDeparturePayload(ushort peerId)
    {
        var payload = new byte[3];
        payload[0] = 0;
        Buffer.BlockCopy(BitConverter.GetBytes(peerId), 0, payload, 1, sizeof(ushort));
        return payload;
    }

    private static UdpClient ConnectRelay(string address, string lobbyId, string relayKey)
    {
        if (lobbyId == null || lobbyId.Length != 32 || relayKey == null || relayKey.Length != 32)
            throw new InvalidOperationException("Invalid relay credentials.");

        var candidate = (address ?? "").Trim();
        if (!candidate.Contains("://")) candidate = "udp://" + candidate;
        Uri uri;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Host))
            throw new InvalidOperationException("Invalid UDP relay address.");
        if (uri.Scheme != "udp")
            throw new InvalidOperationException("UDP relay address must start with udp://.");
        var port = uri.IsDefaultPort ? 27015 : uri.Port;
        if (port < 1 || port > 65535) throw new InvalidOperationException("Invalid UDP relay port.");

        var addresses = Dns.GetHostAddresses(uri.Host);
        Array.Sort(addresses, (left, right) =>
        {
            var leftRank = left.AddressFamily == AddressFamily.InterNetwork ? 0 : 1;
            var rightRank = right.AddressFamily == AddressFamily.InterNetwork ? 0 : 1;
            return leftRank.CompareTo(rightRank);
        });

        UdpClient client = null;
        IPEndPoint endpoint = null;
        Exception lastConnectError = null;
        foreach (var relayAddress in addresses)
        {
            if (relayAddress.AddressFamily != AddressFamily.InterNetwork &&
                relayAddress.AddressFamily != AddressFamily.InterNetworkV6) continue;
            try
            {
                var candidateClient = new UdpClient(relayAddress.AddressFamily);
                endpoint = new IPEndPoint(relayAddress, port);
                client = candidateClient;
                break;
            }
            catch (Exception exception)
            {
                lastConnectError = exception;
            }
        }
        if (client == null)
            throw new IOException("Could not create a UDP socket for " + uri.Host + ".", lastConnectError);
        client.Client.ReceiveTimeout = 500;
        socketCancellation = new CancellationTokenSource();
        socket = client;
        relayEndpoint = endpoint;
        relayConnected = false;

        var auth = new byte[5 + 64];
        Buffer.BlockCopy(udpMagic, 0, auth, 0, udpMagic.Length);
        auth[4] = UdpAuth;
        Buffer.BlockCopy(Encoding.ASCII.GetBytes(lobbyId), 0, auth, 5, 32);
        Buffer.BlockCopy(Encoding.ASCII.GetBytes(relayKey), 0, auth, 37, 32);

        var authenticated = false;
        for (var attempt = 0; attempt < 10 && !authenticated; attempt++)
        {
            client.Send(auth, auth.Length, endpoint);

            if (!client.Client.Poll(500000, SelectMode.SelectRead)) continue;
            try
            {
                IPEndPoint remote = null;
                var response = client.Receive(ref remote);
                if (response != null && response.Length >= 7 && HasUdpMagic(response))
                {
                    if (response[4] == UdpAuthFailed)
                        throw new InvalidOperationException("UDP relay rejected the lobby key.");
                    authenticated = response[4] == UdpAuthOk;
                    if (authenticated)
                    {
						if (response.Length >= 7 + P2PKeySize)
						{
							p2pKey = new byte[P2PKeySize];
							Buffer.BlockCopy(response, 7, p2pKey, 0, P2PKeySize);
						}
                        var authenticatedPeer = BitConverter.ToUInt16(response, 5);
                        if (localPeerId != 0 && authenticatedPeer != localPeerId)
                            throw new InvalidOperationException("UDP relay returned a different peer ID.");
                    }
                }
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.TimedOut) throw;
            }
        }
        if (!authenticated)
        {
            client.Close();
            socket = null;
            throw new IOException("UDP relay did not answer authentication.");
        }

        client.Client.ReceiveTimeout = 0;
        relayConnected = true;
        StartSendWorker(client, socketCancellation);
        return client;
    }

    private static byte[] ReadPacket(byte[] buffer, out ushort senderId)
    {
        senderId = 0;
        var current = socket;
        if (current == null || !relayConnected) return null;
        while (relayConnected)
        {
            IPEndPoint remote = null;
            var datagram = current.Receive(ref remote);
            if (datagram == null || !HasUdpMagic(datagram)) continue;
            var metadata = 0;
            if (datagram.Length >= 5 && datagram[4] == UdpCandidate && EndpointsEqual(remote, relayEndpoint))
            {
                RegisterCandidate(datagram);
                continue;
            }
            if (datagram.Length >= 19 && datagram[4] == UdpForwarded && EndpointsEqual(remote, relayEndpoint))
            {
                senderId = BitConverter.ToUInt16(datagram, 5);
                metadata = 7;
            }
            else if (TryAcceptDirectPacket(datagram, remote, out senderId)) metadata = 23;
            else continue;
            if (datagram.Length < metadata + 12) continue;
            var messageId = BitConverter.ToInt32(datagram, metadata);
            var fragmentIndex = BitConverter.ToUInt16(datagram, metadata + 4);
            var fragmentCount = BitConverter.ToUInt16(datagram, metadata + 6);
            var totalLength = BitConverter.ToInt32(datagram, metadata + 8);
            var payloadOffset = metadata + 12;
            var fragmentLength = datagram.Length - payloadOffset;
            if (senderId == 0 || fragmentCount == 0 || fragmentIndex >= fragmentCount ||
                totalLength < 0 || totalLength > 4 * 1024 * 1024 || fragmentLength < 0) continue;
            Interlocked.Add(ref receivedBytes, datagram.Length);
            Interlocked.Increment(ref receivedPackets);
            if (fragmentCount == 1)
            {
                if (fragmentLength != totalLength) continue;
                var single = new byte[fragmentLength];
                if (fragmentLength > 0) Buffer.BlockCopy(datagram, payloadOffset, single, 0, fragmentLength);
                return single;
            }
            var key = ((long)senderId << 32) | (uint)messageId;
            FragmentTransfer transfer;
            if (!fragmentTransfers.TryGetValue(key, out transfer) || transfer.TotalLength != totalLength ||
                transfer.Fragments.Length != fragmentCount)
            {
                transfer = new FragmentTransfer(totalLength, fragmentCount);
                fragmentTransfers[key] = transfer;
            }
            if (transfer.Fragments[fragmentIndex] == null)
            {
                var fragment = new byte[fragmentLength];
                if (fragmentLength > 0) Buffer.BlockCopy(datagram, payloadOffset, fragment, 0, fragmentLength);
                transfer.Fragments[fragmentIndex] = fragment;
                transfer.Received++;
            }
            CleanupFragmentTransfers();
            if (transfer.Received != transfer.Fragments.Length) continue;
            var packet = new byte[transfer.TotalLength];
            var destination = 0;
            foreach (var fragment in transfer.Fragments)
            {
                if (fragment == null || destination + fragment.Length > packet.Length) { packet = null; break; }
                Buffer.BlockCopy(fragment, 0, packet, destination, fragment.Length);
                destination += fragment.Length;
            }
            fragmentTransfers.Remove(key);
            if (packet != null && destination == packet.Length) return packet;
        }
        return null;
    }

    private static void EnableP2P()
    {
        if (p2pKey == null || p2pKey.Length != P2PKeySize)
        {
            LogP2PWarning("P2P unavailable: UDP relay did not provide a valid P2P key.");
            return;
        }
        LogP2PInfo("P2P enabled; waiting for relay candidates.");
        SendControlToRelay(UdpP2PEnable);
    }

    private static void SendControlToRelay(byte type)
    {
        var current = socket;
        var endpoint = relayEndpoint;
        if (current == null || endpoint == null || !relayConnected) return;
        var control = new byte[] { udpMagic[0], udpMagic[1], udpMagic[2], udpMagic[3], type };
        try { current.Send(control, control.Length, endpoint); }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    private static void RegisterCandidate(byte[] packet)
    {
        if (packet.Length < 10) return;
        var peerId = BitConverter.ToUInt16(packet, 5);
        var littleEndianPort = BitConverter.ToUInt16(packet, 7);
        var networkEndianPort = (ushort)((packet[7] << 8) | packet[8]);
        var length = packet[9];
        if (peerId == 0 || peerId == localPeerId || littleEndianPort == 0 ||
            (length != 4 && length != 16) ||
            packet.Length != 10 + length) return;
        var address = new byte[length];
        Buffer.BlockCopy(packet, 10, address, 0, length);
        var endpoint = new IPEndPoint(new IPAddress(address), littleEndianPort);
        var alternateEndpoint = networkEndianPort == 0 || networkEndianPort == littleEndianPort
            ? null
            : new IPEndPoint(new IPAddress(address), networkEndianPort);
        var shouldProbe = false;
        lock (statusLock)
        {
            P2PPeer peer;
            var knownEndpoint = p2pPeers.TryGetValue(peerId, out peer) &&
                (EndpointsEqual(peer.Endpoint, endpoint) || EndpointsEqual(peer.Endpoint, alternateEndpoint) ||
                 EndpointsEqual(peer.AlternateEndpoint, endpoint) ||
                 EndpointsEqual(peer.AlternateEndpoint, alternateEndpoint));
            if (!knownEndpoint)
            {
                peer = new P2PPeer { Endpoint = endpoint, AlternateEndpoint = alternateEndpoint };
                p2pPeers[peerId] = peer;
                shouldProbe = true;
            }
            else
            {
                peer.AlternateEndpoint = alternateEndpoint;
                if (!peer.Connected) shouldProbe = true;
            }
            if (shouldProbe) peer.NextProbeTicks = DateTime.UtcNow.Ticks + P2PProbeRetryTicks;
        }
        LogP2PInfo("P2P candidate for peer " + peerId + ": " + endpoint +
            (alternateEndpoint == null ? "" : " (alternate byte order: " + alternateEndpoint + ")") + ".");
        if (shouldProbe) SendDirectProbe(peerId);
    }

    private static bool TryAcceptDirectPacket(byte[] datagram, IPEndPoint remote, out ushort senderId)
    {
        senderId = 0;
        if (datagram.Length < 35 || datagram[4] != UdpDirectData || p2pKey == null) return false;
        senderId = BitConverter.ToUInt16(datagram, 5);
        if (senderId == 0 || senderId == localPeerId)
        {
            LogP2PWarning("P2P direct packet rejected from " + remote + ": invalid peer ID " + senderId + ".");
            return false;
        }
        for (var index = 0; index < P2PKeySize; index++)
            if (datagram[7 + index] != p2pKey[index])
            {
                LogP2PWarning("P2P direct packet rejected from " + remote + ": lobby key mismatch.");
                return false;
            }
        var wasConnected = false;
        lock (statusLock)
        {
            P2PPeer peer;
            if (!p2pPeers.TryGetValue(senderId, out peer))
            {
                LogP2PWarning("P2P direct packet rejected from " + remote + ": peer " + senderId + " has no candidate.");
                return false;
            }

            if (!EndpointsEqual(peer.Endpoint, remote)) peer.Endpoint = remote;
            wasConnected = peer.Connected;
            peer.Connected = true;
            peer.NextProbeTicks = 0;
            p2pPeers[senderId] = peer;
        }
        if (!wasConnected) LogP2PInfo("P2P direct path authenticated with peer " + senderId + " from " + remote + ".");
        if (BitConverter.ToInt32(datagram, 23) == 0 && BitConverter.ToUInt16(datagram, 27) == 0 &&
            BitConverter.ToUInt16(datagram, 29) == 1 && BitConverter.ToInt32(datagram, 31) == 0)
        {
            if (!wasConnected) SendDirectProbe(senderId);
            return false;
        }
        return true;
    }

    private static bool IsP2PConnected(ushort peerId)
    {
        lock (statusLock)
        {
            P2PPeer peer;
            return p2pPeers.TryGetValue(peerId, out peer) && peer.Connected;
        }
    }

    private static bool TryGetP2PEndpoint(ushort peerId, out IPEndPoint endpoint)
    {
        endpoint = null;
        if (connectionMode == ConnectionMode.Relay) return false;
        lock (statusLock)
        {
            P2PPeer peer;
            if (!p2pPeers.TryGetValue(peerId, out peer) || !peer.Connected) return false;
            endpoint = peer.Endpoint;
            return endpoint != null;
        }
    }

    private static void SendDirectProbe(ushort peerId)
    {
        IPEndPoint endpoint;
        IPEndPoint alternateEndpoint;
        if (!TryGetP2PCandidates(peerId, out endpoint, out alternateEndpoint)) return;
        var probe = new byte[35];
        Buffer.BlockCopy(udpMagic, 0, probe, 0, udpMagic.Length);
        probe[4] = UdpDirectData;
        Buffer.BlockCopy(BitConverter.GetBytes(localPeerId), 0, probe, 5, sizeof(ushort));
        Buffer.BlockCopy(p2pKey, 0, probe, 7, P2PKeySize);
        Buffer.BlockCopy(BitConverter.GetBytes((ushort)1), 0, probe, 29, sizeof(ushort));
        try { socket.Send(probe, probe.Length, endpoint); } catch (SocketException) { } catch (ObjectDisposedException) { }
        if (alternateEndpoint != null && !EndpointsEqual(endpoint, alternateEndpoint))
            try { socket.Send(probe, probe.Length, alternateEndpoint); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
    }

    private static bool TryGetP2PCandidates(ushort peerId, out IPEndPoint endpoint,
        out IPEndPoint alternateEndpoint)
    {
        endpoint = null;
        alternateEndpoint = null;
        lock (statusLock)
        {
            P2PPeer peer;
            if (!p2pPeers.TryGetValue(peerId, out peer)) return false;
            endpoint = peer.Endpoint;
            alternateEndpoint = peer.AlternateEndpoint;
            return endpoint != null && p2pKey != null;
        }
    }

    private static bool EndpointsEqual(IPEndPoint left, IPEndPoint right)
    {
        return left != null && right != null && left.Port == right.Port && left.Address.Equals(right.Address);
    }

    private static void LogP2PInfo(string message)
    {
        sessionLogger?.LogInfo(message);
        MultiplayerDiagnosticLog.Write(isHost, "INFO", message);
    }

    private static void LogP2PWarning(string message)
    {
        sessionLogger?.LogWarning(message);
        MultiplayerDiagnosticLog.Write(isHost, "WARN", message);
    }

    private static void SendPacket(byte[] packet, ushort targetId = 0, bool? priority = null,
        bool allowReliable = true, bool sendImmediately = false)
    {
        if (packet == null || packet.Length == 0) return;
        if (socket == null || !relayConnected) throw new IOException("Relay connection is closed.");

        if (targetId == 0 && connectionMode != ConnectionMode.Relay)
        {
            var targets = PeerIds();
            if (targets.Length > 0)
            {
                foreach (var peerId in targets) SendPacket(packet, peerId, priority, allowReliable, sendImmediately);
                return;
            }
        }

        if (allowReliable && ShouldSendReliable(packet))
        {
            if (targetId == 0 && isHost)
            {
                var targetPeers = PeerIds();
                if (targetPeers.Length > 0)
                {
                    foreach (var peerId in targetPeers) SendPacket(packet, peerId, priority, true);
                    return;
                }
            }
            var reliableId = Interlocked.Increment(ref reliableSequence);
            var wrapped = new byte[reliableHeader.Length + sizeof(int) + packet.Length];
            Buffer.BlockCopy(reliableHeader, 0, wrapped, 0, reliableHeader.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(reliableId), 0, wrapped, reliableHeader.Length, sizeof(int));
            Buffer.BlockCopy(packet, 0, wrapped, reliableHeader.Length + sizeof(int), packet.Length);
            var routedReliable = RoutePacket(wrapped, targetId);
            lock (reliableLock)
            {
                if (pendingReliable.Count >= 1024)
                {
                    foreach (var oldId in new List<int>(pendingReliable.Keys))
                    {
                        pendingReliable.Remove(oldId);
                        break;
                    }
                }
                pendingReliable[reliableId] = new PendingReliablePacket
                {
                    Id = reliableId,
                    TargetId = targetId,
                    RoutedPacket = routedReliable,
                    LastSentTicks = DateTime.UtcNow.Ticks,
                    Attempts = 1
                };
            }
            if (sendImmediately) SendPacketImmediately(wrapped, targetId);
            else EnqueueRoutedPacket(routedReliable, true);
            return;
        }

        EnqueueRoutedPacket(RoutePacket(packet, targetId), priority);
    }

    private static byte[] RoutePacket(byte[] packet, ushort targetId)
    {
        var routed = new byte[sizeof(ushort) + packet.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(targetId), 0, routed, 0, sizeof(ushort));
        Buffer.BlockCopy(packet, 0, routed, sizeof(ushort), packet.Length);
        return routed;
    }

    private static void SendPacketImmediately(byte[] packet, ushort targetId)
    {
        var current = socket;
        var cancellation = socketCancellation;
        if (current == null || cancellation == null || !relayConnected)
            throw new IOException("Relay connection is closed.");
        SendPacketBlocking(current, cancellation, RoutePacket(packet, targetId));
    }


    private static void EnqueueRoutedPacket(byte[] routed, bool? priority = null)
    {
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

    private static bool ShouldSendReliable(byte[] packet)
    {
        return Matches(packet, hello) || HasHeader(packet, hello) ||
            Matches(packet, accepted) || HasHeader(packet, accepted) ||
            HasHeader(packet, sceneHeader) || HasHeader(packet, settingsHeader) ||
            HasHeader(packet, customLevelHeader) ||
            HasHeader(packet, worldDamageHeader) || HasHeader(packet, npcDamageHeader) ||
            HasHeader(packet, worldEnvironmentHeader) ||
            HasHeader(packet, worldInteractionHeader) ||
            HasHeader(packet, playerDamageHeader) || HasHeader(packet, pvpDamageHeader);
    }

    private static bool ProcessReliablePacket(ref byte[] packet, ushort senderId)
    {
        if (HasHeader(packet, reliableAckHeader) && packet.Length == reliableAckHeader.Length + sizeof(int))
        {
            var acknowledgedId = BitConverter.ToInt32(packet, reliableAckHeader.Length);
            lock (reliableLock)
            {
                PendingReliablePacket pending;
                if (pendingReliable.TryGetValue(acknowledgedId, out pending) &&
                    (pending.TargetId == 0 || pending.TargetId == senderId))
                    pendingReliable.Remove(acknowledgedId);
            }
            return false;
        }
        if (!HasHeader(packet, reliableHeader) ||
            packet.Length <= reliableHeader.Length + sizeof(int)) return true;

        var reliableId = BitConverter.ToInt32(packet, reliableHeader.Length);
        var acknowledgement = PacketWithPayload(reliableAckHeader, BitConverter.GetBytes(reliableId));
        SendPacket(acknowledgement, senderId, true, false);

        var key = ((long)senderId << 32) | (uint)reliableId;
        lock (reliableLock)
        {
            if (!receivedReliable.Add(key)) return false;
            receivedReliableOrder.Enqueue(key);
            while (receivedReliableOrder.Count > 512)
                receivedReliable.Remove(receivedReliableOrder.Dequeue());
        }
        var innerLength = packet.Length - reliableHeader.Length - sizeof(int);
        var inner = new byte[innerLength];
        Buffer.BlockCopy(packet, reliableHeader.Length + sizeof(int), inner, 0, innerLength);
        packet = inner;
        return true;
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
            !HasRoutedHeader(packet, worldEnvironmentHeader) &&
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

    private static void StartSendWorker(UdpClient client, CancellationTokenSource cancellation)
    {
        lock (sendQueueLock)
        {
            sendQueue.Clear();
            prioritySendQueue.Clear();
        }
        var worker = new Thread(() => SendLoop(client, cancellation));
        worker.IsBackground = true;
        worker.Name = "Gunsaw UDP sender";
        worker.Priority = ThreadPriority.AboveNormal;
        sendThread = worker;
        worker.Start();
    }

    private static void SendLoop(UdpClient client, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested && relayConnected)
            {
                byte[] packet = null;
                lock (sendQueueLock)
                {
                    if (prioritySendQueue.Count > 0) packet = prioritySendQueue.Dequeue();
                    else if (sendQueue.Count > 0) packet = sendQueue.Dequeue();
                }
                if (packet != null) SendPacketBlocking(client, cancellation, packet);
                ResendReliablePackets(client, cancellation);
                if (packet == null) sendSignal.WaitOne(25);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException exception)
        {
            if (relayConnected) SetStatus("UDP send failed: " + exception.Message);
        }
        catch (IOException exception)
        {
            if (relayConnected) SetStatus("UDP send failed: " + exception.Message);
        }
    }

    private static void ResendReliablePackets(UdpClient client, CancellationTokenSource cancellation)
    {
        var now = DateTime.UtcNow.Ticks;
        var due = new List<byte[]>();
        lock (reliableLock)
        {
            var remove = new List<int>();
            foreach (var pair in pendingReliable)
            {
                var pending = pair.Value;
                if (now - pending.LastSentTicks < ReliableRetryTicks) continue;
                if (pending.Attempts >= MaxReliableAttempts)
                {
                    remove.Add(pair.Key);
                    continue;
                }
                pending.Attempts++;
                pending.LastSentTicks = now;
                due.Add(pending.RoutedPacket);
            }
            foreach (var id in remove) pendingReliable.Remove(id);
        }
        foreach (var packet in due) SendPacketBlocking(client, cancellation, packet);
    }

    private static void SendPacketBlocking(UdpClient client, CancellationTokenSource cancellation,
        byte[] routedPacket)
    {
        lock (sendLock)
        {
            if (client == null || cancellation == null || cancellation.IsCancellationRequested || !relayConnected)
                throw new IOException("Relay connection is closed.");
            if (routedPacket == null || routedPacket.Length < sizeof(ushort)) return;

            var targetId = BitConverter.ToUInt16(routedPacket, 0);
            IPEndPoint directEndpoint;
            if (TryGetP2PEndpoint(targetId, out directEndpoint))
            {
                SendDirectPacketBlocking(client, routedPacket, directEndpoint);
                return;
            }

            if (connectionMode == ConnectionMode.P2P) relayFallback = true;
            var totalLength = routedPacket.Length - sizeof(ushort);
            var messageId = Interlocked.Increment(ref transportMessageSequence);
            var fragmentCount = Math.Max(1, (totalLength + UdpFragmentPayload - 1) / UdpFragmentPayload);
            var trafficKind = ClassifyOutgoingTraffic(routedPacket);
            if (fragmentCount > ushort.MaxValue) throw new InvalidDataException("UDP packet is too large.");

            for (var index = 0; index < fragmentCount; index++)
            {
                var sourceOffset = sizeof(ushort) + index * UdpFragmentPayload;
                var length = Math.Min(UdpFragmentPayload, totalLength - index * UdpFragmentPayload);
                var datagram = new byte[19 + length];
                Buffer.BlockCopy(udpMagic, 0, datagram, 0, udpMagic.Length);
                datagram[4] = UdpData;
                Buffer.BlockCopy(BitConverter.GetBytes(targetId), 0, datagram, 5, sizeof(ushort));
                Buffer.BlockCopy(BitConverter.GetBytes(messageId), 0, datagram, 7, sizeof(int));
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)index), 0, datagram, 11, sizeof(ushort));
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)fragmentCount), 0, datagram, 13, sizeof(ushort));
                Buffer.BlockCopy(BitConverter.GetBytes(totalLength), 0, datagram, 15, sizeof(int));
                if (length > 0) Buffer.BlockCopy(routedPacket, sourceOffset, datagram, 19, length);
                client.Send(datagram, datagram.Length, relayEndpoint);
                Interlocked.Add(ref sentBytes, datagram.Length);
                AddOutgoingTrafficBytes(trafficKind, datagram.Length);
                Interlocked.Increment(ref sentPackets);
            }
        }
    }

    private static void SendDirectPacketBlocking(UdpClient client, byte[] routedPacket, IPEndPoint endpoint)
    {
        var totalLength = routedPacket.Length - sizeof(ushort);
        var messageId = Interlocked.Increment(ref transportMessageSequence);
        var fragmentCount = Math.Max(1, (totalLength + UdpFragmentPayload - 1) / UdpFragmentPayload);
        var trafficKind = ClassifyOutgoingTraffic(routedPacket);
        for (var index = 0; index < fragmentCount; index++)
        {
            var sourceOffset = sizeof(ushort) + index * UdpFragmentPayload;
            var length = Math.Min(UdpFragmentPayload, totalLength - index * UdpFragmentPayload);
            var datagram = new byte[35 + length];
            Buffer.BlockCopy(udpMagic, 0, datagram, 0, udpMagic.Length);
            datagram[4] = UdpDirectData;
            Buffer.BlockCopy(BitConverter.GetBytes(localPeerId), 0, datagram, 5, sizeof(ushort));
            Buffer.BlockCopy(p2pKey, 0, datagram, 7, P2PKeySize);
            Buffer.BlockCopy(BitConverter.GetBytes(messageId), 0, datagram, 23, sizeof(int));
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)index), 0, datagram, 27, sizeof(ushort));
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)fragmentCount), 0, datagram, 29, sizeof(ushort));
            Buffer.BlockCopy(BitConverter.GetBytes(totalLength), 0, datagram, 31, sizeof(int));
            if (length > 0) Buffer.BlockCopy(routedPacket, sourceOffset, datagram, 35, length);
            client.Send(datagram, datagram.Length, endpoint);
            Interlocked.Add(ref sentBytes, datagram.Length);
            AddOutgoingTrafficBytes(trafficKind, datagram.Length);
            Interlocked.Increment(ref sentPackets);
        }
    }

    private static byte ClassifyOutgoingTraffic(byte[] routedPacket)
    {
        const int packetOffset = sizeof(ushort);
        if (HasHeaderAt(routedPacket, packetOffset, npcHeader)) return 1;
        if (HasHeaderAt(routedPacket, packetOffset, worldHeader)) return 2;
        if (HasHeaderAt(routedPacket, packetOffset, snapshotHeader)) return 3;
        return 0;
    }

    private static bool HasHeaderAt(byte[] packet, int offset, byte[] header)
    {
        if (packet == null || header == null || offset < 0 || packet.Length < offset + header.Length) return false;
        for (var index = 0; index < header.Length; index++)
            if (packet[offset + index] != header[index]) return false;
        return true;
    }

    private static void AddOutgoingTrafficBytes(byte kind, int bytes)
    {
        if (kind == 1) Interlocked.Add(ref sentNpcBytes, bytes);
        else if (kind == 2) Interlocked.Add(ref sentWorldBytes, bytes);
        else if (kind == 3) Interlocked.Add(ref sentAvatarBytes, bytes);
        else Interlocked.Add(ref sentOtherBytes, bytes);
    }

    private static bool HasUdpMagic(byte[] packet)
    {
        return packet != null && packet.Length >= udpMagic.Length &&
            packet[0] == udpMagic[0] && packet[1] == udpMagic[1] &&
            packet[2] == udpMagic[2] && packet[3] == udpMagic[3];
    }

    private static void CleanupFragmentTransfers()
    {
        if (fragmentTransfers.Count < 128) return;
        var cutoff = DateTime.UtcNow.Ticks - TimeSpan.TicksPerSecond * 5;
        var stale = new List<long>();
        foreach (var pair in fragmentTransfers)
            if (pair.Value.CreatedTicks < cutoff) stale.Add(pair.Key);
        foreach (var key in stale) fragmentTransfers.Remove(key);
    }

    private static void CloseSocket(bool graceful = false)
    {
        var current = socket;
        var cancellation = socketCancellation;
        relayConnected = false;
        socket = null;
        socketCancellation = null;
        relayEndpoint = null;
        p2pKey = null;
        p2pPeers.Clear();
        try { if (cancellation != null) cancellation.Cancel(); } catch { }
        sendSignal.Set();
        lock (sendQueueLock)
        {
            sendQueue.Clear();
            prioritySendQueue.Clear();
        }
        lock (reliableLock)
        {
            pendingReliable.Clear();
            receivedReliable.Clear();
            receivedReliableOrder.Clear();
        }
        fragmentTransfers.Clear();
        try { if (current != null) current.Close(); } catch { }
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

    private sealed class FragmentTransfer
    {
        internal readonly int TotalLength;
        internal readonly byte[][] Fragments;
        internal readonly long CreatedTicks;
        internal int Received;

        internal FragmentTransfer(int totalLength, int fragmentCount)
        {
            TotalLength = totalLength;
            Fragments = new byte[fragmentCount][];
            CreatedTicks = DateTime.UtcNow.Ticks;
        }
    }

    private sealed class PendingReliablePacket
    {
        internal int Id;
        internal ushort TargetId;
        internal byte[] RoutedPacket = new byte[0];
        internal long LastSentTicks;
        internal int Attempts;
    }

    private sealed class P2PPeer
    {
        internal IPEndPoint Endpoint;
        internal IPEndPoint AlternateEndpoint;
        internal bool Connected;
        internal long NextProbeTicks;
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

internal static class MultiplayerDiagnosticLog
{
    private static readonly object fileLock = new object();

    internal static void StartSession(bool host)
    {
        try
        {
            lock (fileLock)
                File.WriteAllText(PathFor(host), Timestamp() + " session started (" +
                    (host ? "host" : "client") + ")." + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void Write(bool host, string level, string message)
    {
        try
        {
            lock (fileLock)
                File.AppendAllText(PathFor(host), Timestamp() + " [" + level + "] " +
                    (message ?? "") + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string PathFor(bool host)
    {
        return Path.Combine(Paths.BepInExRootPath,
            "GunsawMultiplayer-" + (host ? "host" : "client") + ".log");
    }

    private static string Timestamp()
    {
        return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
    }
}

internal struct NetworkDebugStats
{
    internal int PingMs;
    internal int ReceivedBytesPerSecond;
    internal int SentBytesPerSecond;
    internal int SentNpcBytesPerSecond;
    internal int SentWorldBytesPerSecond;
    internal int SentAvatarBytesPerSecond;
    internal int SentOtherBytesPerSecond;
    internal float PacketLossPercent;
}
