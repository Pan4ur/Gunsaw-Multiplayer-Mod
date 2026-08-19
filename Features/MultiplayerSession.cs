using BepInEx;
using BepInEx.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal enum ConnectionMode
{
    Relay,
    P2P,
    Auto
}

internal static partial class MultiplayerSession
{
    private static ManualLogSource sessionLogger;
    private static UdpClient socket;
    private static volatile bool relayConnected;
    private static IPEndPoint relayEndpoint;
    private static CancellationTokenSource socketCancellation;
    private static readonly object sendLock = new();
    private static readonly object sendQueueLock = new();
    private static readonly Queue<byte[]> prioritySendQueue = new();
    private static readonly Queue<byte[]> sendQueue = new();
    private static readonly AutoResetEvent sendSignal = new(false);
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
    private static readonly Dictionary<long, FragmentTransfer> fragmentTransfers = new();
    private static readonly ReliableChannel reliableChannel = new();
    private const int MaxQueuedPackets = 2048;
    private const int MaxPendingEventPackets = 256;
    private const int MaxPendingIdentities = 64;
    private static readonly object statusLock = new();
    private static string status = "";
    private static bool isHost;
    private static ConnectionMode connectionMode = ConnectionMode.Relay;
    private static bool relayFallback;
    private static bool p2pHelloSent;
    private static long p2pConnectStartedTicks;
    private static long nextP2PKeepAliveTicks;
    private static byte[] p2pKey;
    private static readonly Dictionary<ushort, P2PPeer> p2pPeers = new();
    
    private static readonly byte[] hello = PacketHeader.Create(PacketType.Hello);
    private static readonly byte[] accepted = PacketHeader.Create(PacketType.Accepted);
    private static readonly byte[] sceneHeader = PacketHeader.Create(PacketType.Scene);
    private static readonly byte[] identityHeader = PacketHeader.Create(PacketType.Identity);
    private static readonly byte[] snapshotHeader = PacketHeader.Create(PacketType.PlayerSnapshot);
    private static readonly byte[] worldHeader = PacketHeader.Create(PacketType.WorldSnapshot);
    private static readonly byte[] worldInputHeader = PacketHeader.Create(PacketType.WorldInput);
    private static readonly byte[] worldDamageHeader = PacketHeader.Create(PacketType.WorldDamage);
    private static readonly byte[] npcHeader = PacketHeader.Create(PacketType.NpcSnapshot);
    private static readonly byte[] npcDamageHeader = PacketHeader.Create(PacketType.NpcDamage);
    private static readonly byte[] npcSpeechHeader = PacketHeader.Create(PacketType.NpcSpeech);
    private static readonly byte[] worldInteractionHeader = PacketHeader.Create(PacketType.WorldInteraction);
    private static readonly byte[] playerDamageHeader = PacketHeader.Create(PacketType.PlayerDamage);
    private static readonly byte[] pvpDamageHeader = PacketHeader.Create(PacketType.PvpDamage);
    private static readonly byte[] settingsHeader = PacketHeader.Create(PacketType.Settings);
    private static readonly byte[] pingHeader = PacketHeader.Create(PacketType.Ping);
    private static readonly byte[] pongHeader = PacketHeader.Create(PacketType.Pong);
    private static readonly byte[] customLevelHeader = PacketHeader.Create(PacketType.CustomLevel);
    private static readonly byte[] worldEnvironmentHeader = PacketHeader.Create(PacketType.WorldEnvironment);
    private static readonly byte[] playerTeleportHeader = PacketHeader.Create(PacketType.PlayerTeleport);
    private static readonly byte[] vehicleEjectHeader = PacketHeader.Create(PacketType.VehicleEject);
    private static readonly byte[] vehicleImpactHeader = PacketHeader.Create(PacketType.VehicleImpact);
    private static readonly byte[] missionFinishedHeader = PacketHeader.Create(PacketType.MissionFinished);
    private static readonly byte[] observerHeader = PacketHeader.Create(PacketType.Observer);
    private static readonly byte[] observerKillHeader = PacketHeader.Create(PacketType.ObserverKill);
    private static readonly byte[] playerPerformanceHeader = PacketHeader.Create(PacketType.PlayerPerformance);
    
    private static string hostScene = "";
    private static string pendingScene = "";
    private static bool pendingSceneReload;
    private static bool pendingSceneAdvanced;
    private static int hostSceneEpoch;
    private static int lastHostSceneHandle;
    private static int expectedSceneEpoch = -1;
    private static string lastReceivedHostScene = "";
    private static string hostCustomLevel = "";
    private static string pendingCustomLevel = "";
    private static readonly PeerRegistry peers = new PeerRegistry();
    private static readonly Queue<ushort> disconnectedPeers = new Queue<ushort>();
    private static int peerListRevision;
    private static readonly HashSet<ushort> blockedPeers = new HashSet<ushort>();
    private static readonly Queue<PeerIdentity> identities = new Queue<PeerIdentity>();
    private static readonly Dictionary<ushort, PlayerSnapshotPacket> snapshots = new Dictionary<ushort, PlayerSnapshotPacket>();
    private static readonly Queue<PeerPayload> worldSnapshots = new Queue<PeerPayload>();
    private static readonly Queue<PeerPayload> worldEnvironments = new Queue<PeerPayload>();
    private static readonly Queue<PeerPacket<WorldInputPacket>> worldInputs = new Queue<PeerPacket<WorldInputPacket>>();
    private static readonly Queue<PeerPacket<WorldDamagePacket>> worldDamage = new Queue<PeerPacket<WorldDamagePacket>>();
    private static readonly Queue<PeerPayload> npcSnapshots = new Queue<PeerPayload>();
    private static readonly Queue<PeerPacket<NpcDamagePacket>> npcDamage = new Queue<PeerPacket<NpcDamagePacket>>();
    private static readonly Queue<PeerPacket<NpcSpeechPacket>> npcSpeech = new Queue<PeerPacket<NpcSpeechPacket>>();
    private static readonly Queue<PeerPacket<WorldInteractionPacket>> worldInteractions = new Queue<PeerPacket<WorldInteractionPacket>>();
    private static readonly Queue<PeerPacket<PlayerDamagePacket>> playerDamage = new Queue<PeerPacket<PlayerDamagePacket>>();
    private static readonly Queue<PeerPacket<PlayerDamagePacket>> pvpDamage = new Queue<PeerPacket<PlayerDamagePacket>>();
    private static readonly Queue<PeerPacket<ShotVisualPacket>> shotVisuals = new Queue<PeerPacket<ShotVisualPacket>>();
    private static readonly Queue<PeerPacket<ProjectileImpactPacket>> projectileImpacts = new Queue<PeerPacket<ProjectileImpactPacket>>();
    private static readonly Queue<PeerPacket<VelvetWebPacket>> velvetWebs = new Queue<PeerPacket<VelvetWebPacket>>();
    private static readonly Queue<PeerPacket<PlayerTeleportPacket>> playerTeleports = new Queue<PeerPacket<PlayerTeleportPacket>>();
    private static readonly Queue<PeerPacket<VehicleEjectPacket>> vehicleEjects = new Queue<PeerPacket<VehicleEjectPacket>>();
    private static readonly Queue<PeerPacket<VehicleImpactPacket>> vehicleImpacts = new Queue<PeerPacket<VehicleImpactPacket>>();
    private static readonly Queue<PeerPacket<TeleportRequestPacket>> teleportRequests = new Queue<PeerPacket<TeleportRequestPacket>>();
    private static readonly Queue<PeerPacket<PlayerGrabPacket>> playerGrabs = new Queue<PeerPacket<PlayerGrabPacket>>();
    private static readonly Queue<PeerPacket<NpcGrabPacket>> npcGrabs = new Queue<PeerPacket<NpcGrabPacket>>();
    private static readonly Queue<PeerPacket<NpcPossessionPacket>> npcPossessions = new Queue<PeerPacket<NpcPossessionPacket>>();
    private static readonly Queue<PeerPacket<MissionFinishedPacket>> missionFinished = new Queue<PeerPacket<MissionFinishedPacket>>();
    private static readonly Queue<PeerPacket<PlayerPerformancePacket>> playerPerformance = new Queue<PeerPacket<PlayerPerformancePacket>>();
    private static readonly Queue<PeerPacket<PlayerCarryPacket>> playerCarries = new Queue<PeerPacket<PlayerCarryPacket>>();
    private static readonly Queue<PeerPacket<HostFpsPacket>> hostFpsPackets = new Queue<PeerPacket<HostFpsPacket>>();
    private static readonly Queue<ChatMessage> chatMessages = new Queue<ChatMessage>();
    private static readonly HashSet<long> receivedChatIds = new HashSet<long>();
    private static readonly Queue<long> receivedChatOrder = new Queue<long>();
    private static readonly Dictionary<int, NpcTransfer> npcTransfers = new Dictionary<int, NpcTransfer>();
    private static CustomLevelTransfer customLevelTransfer;
    private static int customLevelTransferId;
    private static long nextPingTicks;
    private static long pendingPingTicks;
    private static bool hostDisconnectPending;
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
    private const long PeerTimeoutTicks = TimeSpan.TicksPerSecond * 30;

    internal static void StartHost(string lobbyId, string relayKey, string relayAddress, bool pvpEnabled,
        bool canGrabPlayers, bool grabOnlyUnconscious, bool allowRespawn, int respawnTimeSeconds,
        bool respawnAtStart, bool playerCollisions, bool cheatsEnabled, bool allowSwap, bool allowScaleChanging, float initialScale, bool allowObserver, string playerName, ushort assignedPeerId, int lobbyMaxPlayers,
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
            pendingSceneReload = false;
            pendingSceneAdvanced = false;
            expectedSceneEpoch = -1;
            lastReceivedHostScene = "";
            customLevelTransfer = null;
            localPlayerName = NormalizePlayerName(playerName);
            localPeerId = assignedPeerId == 0 ? (ushort)1 : assignedPeerId;
            hostPeerId = localPeerId;
            hostSceneEpoch = 0;
            lastHostSceneHandle = 0;
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
        PlayerCollisions = playerCollisions;
        CheatsEnabled = cheatsEnabled;
        AllowSwap = allowSwap;
        AllowScaleChanging = allowScaleChanging;
        InitialScale = AvatarScaleHandler.Clamp(initialScale);
        AllowObserver = allowObserver;
        RefreshHostBrutalMode();
        ResetPing();
        ThreadPool.QueueUserWorkItem(_ => Receive(null));
        RPCManager.CheckInstance();
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
                pendingSceneReload = false;
                pendingSceneAdvanced = false;
                expectedSceneEpoch = -1;
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
            PlayerCollisions = true;
            CheatsEnabled = false;
            AllowSwap = true;
            AllowScaleChanging = true;
            InitialScale = 1f;
            BrutalModeEnabled = false;
            AllowObserver = true;
            ResetPing();
            socket = ConnectRelay(relayAddress, lobbyId, relayKey);
            if (connectionMode == ConnectionMode.Relay) SendInitialHello();
            else EnableP2P();
            ThreadPool.QueueUserWorkItem(_ => Receive(null));
            RPCManager.CheckInstance();
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
        Send(new ScenePacket(scene + "\n" + hostSceneEpoch), 0, false);
    }

    internal static void NoteHostSceneHandle(int handle)
    {
        if (!isHost || handle == 0) return;
        lock (statusLock)
        {
            if (handle == lastHostSceneHandle) return;
            lastHostSceneHandle = handle;
            hostSceneEpoch++;
        }
    }

    internal static int SnapshotEpoch { get { lock (statusLock) return hostSceneEpoch; } }

    internal static bool IsSnapshotEpochCurrent(int epoch)
    {
        lock (statusLock) return expectedSceneEpoch < 0 || epoch == expectedSceneEpoch;
    }

    internal static void ResendHostScene()
    {
        if (!isHost) return;
        string scene;
        int epoch;
        lock (statusLock) { scene = hostScene; epoch = hostSceneEpoch; }
        if (string.IsNullOrEmpty(scene)) return;
        Send(new ScenePacket(scene + "\n" + epoch), 0, false);
    }

    internal static void NotifyHostSceneReload(string scene)
    {
        if (!isHost || string.IsNullOrEmpty(scene)) return;
        ObserverSystem.BroadcastResetForLevelChange();
        hostScene = scene;
        Send(new ScenePacket(scene + "\n" + (hostSceneEpoch + 1) + "\nR"), 0, false);
    }

    internal static void EndHostCustomLevel(string scene)
    {
        if (!isHost || string.IsNullOrEmpty(hostCustomLevel)) return;
        lock (statusLock) hostCustomLevel = "";
        hostScene = scene;
        Send(new ScenePacket(scene + "\n" + hostSceneEpoch), 0, false);
    }

    internal static void StartHostCustomLevel(string levelJson)
    {
        if (!isHost) throw new InvalidOperationException("Only the host can start a custom level.");
        if (string.IsNullOrWhiteSpace(levelJson) || Encoding.UTF8.GetByteCount(levelJson) > 4 * 1024 * 1024)
            throw new InvalidOperationException("Custom level data is empty or too large.");
        lock (statusLock) hostCustomLevel = levelJson;
        hostScene = "LevelLoader";
        QueueCustomLevelTransfer(levelJson);
        Send(new SettingsPacket(PvpEnabled, CanGrabPlayers, GrabOnlyUnconscious, AllowRespawn,
            RespawnAtStart, (ushort)RespawnTimeSeconds, (byte)MaxPlayers, PlayerCollisions, CheatsEnabled, AllowSwap, AllowScaleChanging, InitialScale, BrutalModeEnabled, AllowObserver));
        Send(new ScenePacket(hostScene + "\n" + hostSceneEpoch), 0, false);
    }

    internal static bool TryTakeScene(out string scene, out bool reload, out bool epochAdvanced)
    {
        lock (statusLock)
        {
            scene = pendingScene;
            reload = pendingSceneReload;
            epochAdvanced = pendingSceneAdvanced;
            pendingScene = "";
            pendingSceneReload = false;
            pendingSceneAdvanced = false;
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

    // Is connected and more then 1 players
    internal static bool IsConnected { get { lock (statusLock) return socket != null &&
        relayConnected && peers.Count > 0; } }

    internal static bool IsActive { get { return socket != null; } }
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
    internal static bool PlayerCollisions { get; private set; } = true;
    internal static bool CheatsEnabled { get; private set; }
    internal static bool AllowSwap { get; private set; } = true;
    internal static bool AllowScaleChanging { get; private set; } = true;
    internal static float InitialScale { get; private set; } = 1f;
    internal static bool BrutalModeEnabled { get; private set; }
    internal static bool AllowObserver { get; private set; } = true;

    internal static void SyncBrutalMode()
    {
        var manager = GameManager.main;
        if (manager == null) return;
        if (isHost) BrutalModeEnabled = manager.hardMode;
        else if (IsConnected) manager.hardMode = BrutalModeEnabled;
    }

    private static void RefreshHostBrutalMode()
    {
        if (GameManager.main != null) BrutalModeEnabled = GameManager.main.hardMode;
    }
    
    internal static int PingMs { get { lock (statusLock)
        {
            foreach (var peer in peers.All) return peer.PingMs;
            return -1;
        } } }
    internal static string LocalPlayerName { get { lock (statusLock) return localPlayerName; } }
    internal static string RemotePlayerName { get { lock (statusLock)
        {
            foreach (var peer in peers.All) return peer.Name;
            return "";
        } } }
    internal static ushort LocalPeerId { get { lock (statusLock) return localPeerId; } }
    internal static ushort HostPeerId { get { lock (statusLock) return hostPeerId; } }
    internal static int MaxPlayers { get { lock (statusLock) return maxPlayers; } }
    internal static int PlayerCount { get { lock (statusLock) return 1 + peers.Count; } }
    internal static int PeerListRevision { get { lock (statusLock) return peerListRevision; } }

    internal static bool TryTakePeerDisconnected(out ushort peerId)
    {
        lock (statusLock)
        {
            if (disconnectedPeers.Count == 0)
            {
                peerId = 0;
                return false;
            }
            peerId = disconnectedPeers.Dequeue();
            return true;
        }
    }
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
        lock (statusLock) return peers.Contains(peerId);
    }

    internal static string PlayerName(ushort peerId)
    {
        lock (statusLock)
        {
            if (peerId == localPeerId) return localPlayerName;
            PeerState peer;
            return peers.TryGet(peerId, out peer) ? peer.Name : "Player";
        }
    }

    internal static int PeerPing(ushort peerId)
    {
        lock (statusLock)
        {
            PeerState peer;
            return peers.TryGet(peerId, out peer) ? peer.PingMs : -1;
        }
    }

    internal static ushort[] PeerIds()
    {
        lock (statusLock)
        {
            return peers.Ids();
        }
    }

    internal static void KickPeer(ushort peerId, string message)
    {
        if (!isHost || peerId == 0 || peerId == localPeerId) return;
        lock (statusLock) blockedPeers.Add(peerId);
        Send(DisconnectPacket.ClientClosed(), peerId);
        Send(DisconnectPacket.PeerLeft(peerId));
        DropPeer(peerId, false, message);
    }

    internal static void UpdateConnection()
    {
        var now = DateTime.UtcNow.Ticks;
        UpdateP2PConnection(now);
        var timedOut = new List<ushort>();
        lock (statusLock)
            foreach (var pair in peers.Entries)
                if (pair.Value.LastPacketTicks > 0 && now - pair.Value.LastPacketTicks > PeerTimeoutTicks)
                    timedOut.Add(pair.Key);
        foreach (var peerId in timedOut)
        {
            if (isHost && peerId != localPeerId) Send(DisconnectPacket.PeerLeft(peerId));
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
        var helloPacket = PacketCodec.Encode(new HelloPacket(localPlayerName));
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
        PlayerCollisions = true;
        CheatsEnabled = false;
        AllowSwap = true;
        AllowScaleChanging = true;
        InitialScale = 1f;
        BrutalModeEnabled = false;
        AllowObserver = true;
        lock (statusLock)
        {
            peers.Clear();
            ClearPeerQueuesLocked();
            pendingScene = "";
            pendingSceneReload = false;
            pendingSceneAdvanced = false;
            expectedSceneEpoch = -1;
            lastReceivedHostScene = "";
            hostCustomLevel = "";
            pendingCustomLevel = "";
            hostDisconnectPending = false;
            maxPlayers = 2;
        }
    }

    internal static bool UpdateHostSettings(bool pvpEnabled, bool canGrabPlayers,
        bool grabOnlyUnconscious, bool allowRespawn, int respawnTimeSeconds,
        bool respawnAtStart, bool playerCollisions, bool cheatsEnabled, bool allowSwap, bool allowScaleChanging, float initialScale, bool allowObserver, int lobbyMaxPlayers)
    {
        if (!IsHosting) return false;
        PvpEnabled = pvpEnabled;
        CanGrabPlayers = canGrabPlayers;
        GrabOnlyUnconscious = canGrabPlayers && grabOnlyUnconscious;
        AllowRespawn = allowRespawn;
        RespawnTimeSeconds = Math.Max(0, Math.Min(3600, respawnTimeSeconds));
        RespawnAtStart = allowRespawn && respawnAtStart;
        PlayerCollisions = playerCollisions;
        CheatsEnabled = cheatsEnabled;
        AllowSwap = allowSwap;
        AllowScaleChanging = allowScaleChanging;
        InitialScale = AvatarScaleHandler.Clamp(initialScale);
        AllowObserver = allowObserver;
        RefreshHostBrutalMode();
        lock (statusLock) maxPlayers = Math.Max(2, Math.Min(16, lobbyMaxPlayers));
        Send(new SettingsPacket(PvpEnabled, CanGrabPlayers, GrabOnlyUnconscious, AllowRespawn,
            RespawnAtStart, (ushort)RespawnTimeSeconds, (byte)MaxPlayers, PlayerCollisions, CheatsEnabled, AllowSwap, AllowScaleChanging, InitialScale, BrutalModeEnabled, AllowObserver));
        return true;
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

    private sealed class PeerPacket<TPacket>
    {
        internal ushort PeerId;
        internal TPacket Packet;
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
