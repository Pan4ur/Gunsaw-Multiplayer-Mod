using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class GunsawMultiplayerPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.gunsaw.multiplayer";
    public const string PluginName = "Gunsaw Multiplayer";
    public const string PluginVersion = "0.4.6";
    private const string ReleasesApiUrl = "https://api.github.com/repos/Pan4ur/Gunsaw-Multiplayer-Mod/releases/latest";
    private const string CustomLevelsUrl = "https://github.com/jimmyking9999999/gunsaw-level-editor-plus/raw/refs/heads/main/Levels.json";
    private const string ServersUrl = "https://raw.githubusercontent.com/Pan4ur/Gunsaw-Multiplayer-Mod/main/Assets/servers.json";

    internal static GunsawMultiplayerPlugin Instance { get; private set; }

    internal readonly List<LobbyInfo> lobbies = new List<LobbyInfo>();
    internal readonly List<ServerInfo> servers = new List<ServerInfo>();
    internal bool customLevelCatalogReady;
    internal string customLevelCatalogError = "";
    internal bool serverListLoading;
    internal string serverListError = "";
    private ConfigEntry<string> masterUrl;
    private ConfigEntry<string> savedPlayerName;
    private ConfigEntry<string> savedLobbyName;
    private ConfigEntry<bool> savedCreatePvp;
    private ConfigEntry<bool> savedCreateCanGrab;
    private ConfigEntry<bool> savedCreateGrabOnlyUnconscious;
    private ConfigEntry<bool> savedCreateAllowRespawn;
    private ConfigEntry<bool> savedCreateAutoRestart;
    private ConfigEntry<bool> savedCreateRespawnAtStart;
    private ConfigEntry<bool> savedCreatePlayerCollisions;
    private ConfigEntry<bool> savedCreateCheats;
    private ConfigEntry<bool> savedCreateAllowSwap;
    private ConfigEntry<bool> savedCreateAllowScaleChanging;
    private ConfigEntry<bool> savedCreateAllowObserver;
    private ConfigEntry<bool> savedCreateTeams;
    private ConfigEntry<string> savedCreateTeamsCfg;
    private ConfigEntry<string> savedCreateInitialScale;
    private ConfigEntry<string> savedCreateStartingWeapon;
    private ConfigEntry<string> savedCreateRespawnWeapon;
    private ConfigEntry<string> savedCreateStartingAmmo;
    private ConfigEntry<string> savedCreateRespawnAmmo;
    private ConfigEntry<string> savedCreateRespawnTime;
    private ConfigEntry<string> savedCreateNumberOfLives;
    private ConfigEntry<string> savedCreateMaxPlayers;
    internal bool visible;
    internal string status = "Select an option.";
    internal string updateStatus = "Checking for updates..."; 
    internal string lobbyServerAddress = "expie.fun";
    internal string lobbyName = "Lobby";
    internal string playerName = "Player";
    internal bool createPvp;
    internal bool createCanGrab = true;
    internal bool createGrabOnlyUnconscious = true;
    internal bool createAllowRespawn = true;
    internal bool createAutoRestart;
    internal bool createRespawnAtStart = true;
    internal bool createPlayerCollisions = true;
    internal bool createCheats;
    internal bool createAllowSwap = true;
    internal bool createAllowScaleChanging = true;
    internal bool createAllowObserver = true;
    internal bool warningSkipped;
    internal bool createTeams;
    internal string createTeamsCfg = "Milkies:blue;Expies:red";
    internal string createInitialScale = "1.0";
    internal string createStartingWeapon = "Default";
    internal string createRespawnWeapon = "Default";
    internal string createStartingAmmo = LobbyAmmoRules.StartingDefault;
    internal string createRespawnAmmo = LobbyAmmoRules.RespawnDefault;
    internal string createRespawnTime = "5";
    internal string createNumberOfLives = "0";
    internal string createMaxPlayers = "4";
    internal string customLevelJson = "";
    private string customLevelCode = "";
    internal ConnectionMode createConnectionMode = ConnectionMode.Relay;
    private string receivedCustomLevelJson = "";
    private int receivedCustomLevelTransferId;
    private bool waitingForCustomLevel;
    private int waitingForCustomLevelTransferId;
    private string requestedHostScene = "";
    private float customLevelPhysicsRefreshUntil;
    private float nextCustomLevelPhysicsRefresh;
    private NetworkAvatarReplication avatarReplication;
    private WorldReplication worldReplication;
    private NpcReplication npcReplication;
    private MultiplayerHud multiplayerHud;
    private ChatCommandSystem _chatCommandSystem;
    private MultiplayerLobbyUi multiplayerLobbyUi;
    private MultiplayerReplicationDebugMode replicationDebugMode;
    private bool gameplayTypesLogged;
    private string hostedLobbyId = "";
    private string hostedLobbyDisplayName = "";
    private string hostRelayKey = "";
    private float nextHeartbeat;
    private int lastHostedPeerListRevision = -1;
    private bool shuttingDown;
    private bool headlessMode;
    private bool headlessStartPending;
    private int hiddenHeadlessAvatarScene = int.MinValue;
    private int headlessFixedTicks;
    private int headlessFixedTicksAtLastSample;
    private float headlessTpsSampleTime = -1f;
    private int headlessTps;
    private Timer headlessKeepAliveTimer;
    private int headlessKeepAliveInFlight;
    private string headlessDefaultMapJson = "";
    private readonly Dictionary<string, HashSet<ushort>> headlessVotes = new Dictionary<string, HashSet<ushort>>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ushort> headlessKnownPeers = new HashSet<ushort>();
    private bool joinInProgress;
    private string joinedLobbyId = "";
    private int updateCheckInProgress;
    private readonly object joinLock = new object();
    private readonly Queue<Action> mainThreadActions = new Queue<Action>();
    private readonly object mainThreadActionsLock = new object();

    private void Awake()
    {
        KeepMultiplayerRunningInBackground();
        Instance = this;
        _chatCommandSystem = new ChatCommandSystem(this);
        masterUrl = Config.Bind("Network", "MasterUrl", "https://expie.fun", "Lobby directory URL.");
        string normalizedServer;
        if (TryNormalizeServerAddress(masterUrl.Value, out normalizedServer)) masterUrl.Value = normalizedServer;
        lobbyServerAddress = DisplayServerAddress(masterUrl.Value);
        savedPlayerName = Config.Bind("Lobby", "PlayerName", playerName, "Name shown to other players.");
        savedLobbyName = Config.Bind("Lobby", "LobbyName", lobbyName, "Default name for new lobbies.");
        savedCreatePvp = Config.Bind("Lobby", "Pvp", createPvp, "Enable PvP in new lobbies.");
        savedCreateCanGrab = Config.Bind("Lobby", "CanGrab", createCanGrab, "Allow player grabbing in new lobbies.");
        savedCreateGrabOnlyUnconscious = Config.Bind("Lobby", "GrabOnlyUnconscious", createGrabOnlyUnconscious,
            "Limit grabbing to unconscious players in new lobbies.");
        savedCreateAllowRespawn = Config.Bind("Lobby", "AllowRespawn", createAllowRespawn,
            "Allow respawning in new lobbies.");
        savedCreateAutoRestart = Config.Bind("Lobby", "AutoRestart", createAutoRestart,
            "Restart the level after every player or team but one is eliminated.");
        savedCreateRespawnAtStart = Config.Bind("Lobby", "RespawnAtStart", createRespawnAtStart,
            "Respawn players at level start in new lobbies.");
        savedCreatePlayerCollisions = Config.Bind("Lobby", "PlayerCollisions", createPlayerCollisions,
            "Allow players to collide with each other in new lobbies.");
        savedCreateCheats = Config.Bind("Lobby", "Cheats", createCheats,
            "Allow built-in cheats in new lobbies.");
        savedCreateAllowSwap = Config.Bind("Lobby", "AllowSwap", createAllowSwap,
            "Allow changing character with /swap while playing.");
        savedCreateAllowScaleChanging = Config.Bind("Lobby", "AllowScaleChanging", createAllowScaleChanging,
            "Allow players to change their character scale.");
        savedCreateAllowObserver = Config.Bind("Lobby", "AllowObserver", createAllowObserver,
            "Allow players to activate Observer.");
        savedCreateTeams = Config.Bind("Lobby", "Teams", createTeams, "Enable teams in new lobbies.");
        savedCreateTeamsCfg = Config.Bind("Lobby", "TeamsCfg", createTeamsCfg, "Teams in Name:color format.");
        savedCreateInitialScale = Config.Bind("Lobby", "InitialScale", createInitialScale,
            "Character scale assigned when a player joins or respawns.");
        savedCreateStartingWeapon = Config.Bind("Lobby", "StartingWeapon", createStartingWeapon,
            "Weapons assigned when a player joins, in Slot1;Slot2;Slot3 format.");
        savedCreateRespawnWeapon = Config.Bind("Lobby", "RespawnWeapon", createRespawnWeapon,
            "Weapons assigned when a player respawns, in Slot1;Slot2;Slot3 format.");
        savedCreateStartingAmmo = Config.Bind("Lobby", "StartingAmmo", createStartingAmmo,
            "Ammo assigned when a player joins, in Pistol;Rifle;Heavy;Grenade format.");
        savedCreateRespawnAmmo = Config.Bind("Lobby", "RespawnAmmo", createRespawnAmmo,
            "Ammo assigned when a player respawns, in Pistol;Rifle;Heavy;Grenade format.");
        savedCreateRespawnTime = Config.Bind("Lobby", "RespawnTime", createRespawnTime,
            "Default respawn delay in seconds.");
        savedCreateNumberOfLives = Config.Bind("Lobby", "NumberOfLives", createNumberOfLives,
            "Lives available to each player per level. Zero means unlimited lives.");
        savedCreateMaxPlayers = Config.Bind("Lobby", "MaxPlayers", createMaxPlayers,
            "Default maximum player count.");
        playerName = savedPlayerName.Value;
        lobbyName = savedLobbyName.Value;
        createPvp = savedCreatePvp.Value;
        createCanGrab = savedCreateCanGrab.Value;
        createGrabOnlyUnconscious = savedCreateGrabOnlyUnconscious.Value;
        createAllowRespawn = savedCreateAllowRespawn.Value;
        createAutoRestart = savedCreateAutoRestart.Value;
        createRespawnAtStart = savedCreateRespawnAtStart.Value;
        createPlayerCollisions = savedCreatePlayerCollisions.Value;
        createCheats = savedCreateCheats.Value;
        createAllowSwap = savedCreateAllowSwap.Value;
        createAllowScaleChanging = savedCreateAllowScaleChanging.Value;
        createAllowObserver = savedCreateAllowObserver.Value;
        createTeams = savedCreateTeams.Value;
        createTeamsCfg = savedCreateTeamsCfg.Value;
        createInitialScale = savedCreateInitialScale.Value;
        createStartingWeapon = savedCreateStartingWeapon.Value;
        createRespawnWeapon = savedCreateRespawnWeapon.Value;
        createStartingAmmo = savedCreateStartingAmmo.Value;
        createRespawnAmmo = savedCreateRespawnAmmo.Value;
        createRespawnTime = savedCreateRespawnTime.Value;
        createNumberOfLives = savedCreateNumberOfLives.Value;
        createMaxPlayers = savedCreateMaxPlayers.Value;
        headlessMode = HasCommandLineFlag("-headlessLobby");
        if (headlessMode)
        {
            HeadlessPresentation.Enable();
            ApplyHeadlessCommandLineOptions();
            var mapPath = CommandLineValue("-headlessMap");
            if (string.IsNullOrEmpty(mapPath)) mapPath = Path.Combine(Paths.GameRootPath, "default_map.txt");
            try
            {
                var code = File.ReadAllText(mapPath).Trim();
                customLevelJson = Compression.Decompress(code);
                customLevelCode = code;
                if (string.IsNullOrWhiteSpace(customLevelJson) || JsonUtility.FromJson<Level>(customLevelJson) == null)
                    throw new InvalidDataException("Invalid level code.");
                headlessDefaultMapJson = customLevelJson;
                Logger.LogInfo("Headless lobby map loaded: " + mapPath);
            }
            catch (Exception exception)
            {
                Logger.LogError("Headless lobby could not load map: " + exception.Message);
                customLevelJson = "";
            }
        }
        new Harmony(PluginGuid).PatchAll();
        avatarReplication = gameObject.AddComponent<NetworkAvatarReplication>();
        worldReplication = gameObject.AddComponent<WorldReplication>();
        npcReplication = gameObject.AddComponent<NpcReplication>();
        multiplayerHud = gameObject.AddComponent<MultiplayerHud>();
        replicationDebugMode = gameObject.AddComponent<MultiplayerReplicationDebugMode>();
        multiplayerLobbyUi = gameObject.AddComponent<MultiplayerLobbyUi>();
        World = worldReplication;
        Logger.LogInfo("Gunsaw Multiplayer " + PluginVersion + " loaded.");
        LoadCustomLevelCatalog();
        CheckForUpdates(false);
        RPCManager.CheckInstance();
        EmbeddedAudioLoader.Init();
    }

    private void Start()
    {
        KeepMultiplayerRunningInBackground();
        if (headlessMode)
        {
            if (string.IsNullOrEmpty(customLevelJson)) { Logger.LogError("Headless lobby disabled: no valid map."); return; }
            Logger.LogInfo("Starting headless lobby.");
            if (SceneManager.GetActiveScene().name != "LevelSelect") SceneManager.LoadScene("LevelSelect");
            CreateLobby();
        }
    }

    private void OnApplicationFocus(bool focused)
    {
        KeepMultiplayerRunningInBackground();
    }

    private void OnApplicationPause(bool paused)
    {
        KeepMultiplayerRunningInBackground();
    }

    private void KeepMultiplayerRunningInBackground()
    {
        Application.runInBackground = true;
        if (headlessMode)
        {
            var manager = GameManager.main;
            if (manager != null) manager.paused = false;
            Time.timeScale = 1f;
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
            return;
        }
        
        MultiplayerTimeControl.KeepMultiplayerActive();
    }

    internal static WorldReplication World;
    internal static bool IsHeadlessMode => Instance != null && Instance.headlessMode;
    internal static bool IsHeadlessServer => Instance != null && Instance.headlessMode && MultiplayerSession.IsHosting;

    private void Update()
    {
        KeepMultiplayerRunningInBackground();
        UpdateHeadlessTps();
        if (headlessMode && !warningSkipped)
        {
            var warning = FindObjectOfType<ViolenceScreen>();
            if (warning != null)
            {
                warning.clicked = true;
                warningSkipped = true;
                return;
            }
        }
        lock (mainThreadActionsLock)
            while (mainThreadActions.Count > 0) mainThreadActions.Dequeue()();
        MultiplayerSession.UpdateConnection();
        ushort suggestingPeer;
        CustomLevelSuggestionPacket suggestion;
        while (MultiplayerSession.TryTakeCustomLevelSuggestion(out suggestingPeer, out suggestion))
            multiplayerLobbyUi?.ShowCustomLevelSuggestion(MultiplayerSession.PlayerName(suggestingPeer), suggestion);
        TeamSystem.Tick();
        ScoreboardSystem.Tick();
        AutoRestartSystem.Tick(createAutoRestart);
        MultiplayerSession.SyncBrutalMode();
        ObserverSystem.Tick();
        if (MultiplayerSession.IsHosting)
        {
            ushort disconnectedPeer;
            while (MultiplayerSession.TryTakePeerDisconnected(out disconnectedPeer))
                RemoveHostedPeer(disconnectedPeer);
        }
        LoadDistanceSystem.Apply();
        MultiplayerSession.NoteHostSceneHandle(SceneManager.GetActiveScene().handle);
        MultiplayerSession.SetHostScene(SceneManager.GetActiveScene().name);
        SendHeadlessHelpToNewPlayers();
        HideHeadlessHostAvatar();
        if (headlessStartPending && MultiplayerSession.IsHosting && SceneLoader.main != null)
        {
            headlessStartPending = false;
            try
            {
                MultiplayerSession.StartHostCustomLevel(customLevelJson, customLevelCode);
                StartCustomLevelLocally(customLevelJson);
                Logger.LogInfo("Headless lobby custom level started.");
            }
            catch (Exception exception) { Logger.LogError("Headless lobby could not start map: " + exception.Message); }
        }
        if (Time.unscaledTime < customLevelPhysicsRefreshUntil &&
            Time.unscaledTime >= nextCustomLevelPhysicsRefresh)
        {
            nextCustomLevelPhysicsRefresh = Time.unscaledTime + 0.25f;
            NetworkAvatarReplication.ForceRefreshRemotePhysics();
        }
        var sessionName = MultiplayerSession.IsHosting || MultiplayerSession.IsConnected
            ? MultiplayerSession.LocalPlayerName : playerName;
        multiplayerHud.Configure(sessionName,
            MultiplayerSession.IsHosting ? hostedLobbyDisplayName : lobbyName, visible);
        multiplayerLobbyUi.Configure(this);

        if (!gameplayTypesLogged && UnityEngine.Object.FindObjectOfType<PlayerScript>() != null)
        {
            gameplayTypesLogged = true;
            Logger.LogInfo("Gameplay mapping active: PlayerScript, BodyScript, WeaponScript, LimbScript, SceneLoader.");
        }

        if (MultiplayerSession.IsHosting && !string.IsNullOrEmpty(hostedLobbyId) &&
            lastHostedPeerListRevision != MultiplayerSession.PeerListRevision)
        {
            lastHostedPeerListRevision = MultiplayerSession.PeerListRevision;
            nextHeartbeat = Time.unscaledTime + 10f;
            SendHeartbeat();
        }
        else if (!string.IsNullOrEmpty(hostedLobbyId) && Time.unscaledTime >= nextHeartbeat)
        {
            nextHeartbeat = Time.unscaledTime + 10f;
            SendHeartbeat();
            MultiplayerSession.ResendHostScene();
        }

        string connectionMessage;
        if (MultiplayerSession.TryTakeStatus(out connectionMessage))
            status = connectionMessage;

        PlayerGruntService.Tick();

        if (MultiplayerSession.TryTakeHostDisconnected())
        {
            joinedLobbyId = "";
            status = "Host closed the lobby.";
            Time.timeScale = 1f;
            if (SceneManager.GetActiveScene().name != "LevelSelect")
                SceneManager.LoadScene("LevelSelect");
            return;
        }

        string incomingCustomLevel;
        int incomingCustomLevelTransferId;
        if (MultiplayerSession.TryTakeCustomLevel(out incomingCustomLevel, out incomingCustomLevelTransferId))
        {
            RPCManager.CheckInstance();
            RPCManager.instance?.UpdateCustomLevel(incomingCustomLevel);
            CustomLevelProgress.SetActive(incomingCustomLevel);
            receivedCustomLevelJson = Compression.Decompress(incomingCustomLevel);
            receivedCustomLevelTransferId = incomingCustomLevelTransferId;
            if (waitingForCustomLevel && (waitingForCustomLevelTransferId == 0 ||
                waitingForCustomLevelTransferId == incomingCustomLevelTransferId))
            {
                waitingForCustomLevel = false;
                waitingForCustomLevelTransferId = 0;
                StartCustomLevelLocally(receivedCustomLevelJson);
            }
        }

        string sceneToLoad;
        bool sceneReload, sceneEpochAdvanced;
        int sceneCustomLevelTransferId;
        if (MultiplayerSession.TryTakeScene(out sceneToLoad, out sceneReload, out sceneEpochAdvanced,
            out sceneCustomLevelTransferId))
        {
            var activeScene = SceneManager.GetActiveScene().name;
            var mustReload = sceneReload || (sceneEpochAdvanced && sceneToLoad == activeScene);
            if (!mustReload && (sceneToLoad == requestedHostScene || sceneToLoad == activeScene))
            {
                status = "Already in host scene " + sceneToLoad + ".";
                return;
            }
            requestedHostScene = sceneToLoad;
            ObserverSystem.ResetForLevelChange(mustReload);
            if (sceneToLoad == "LevelLoader")
            {
                if (sceneCustomLevelTransferId != 0 &&
                    receivedCustomLevelTransferId != sceneCustomLevelTransferId)
                {
                    waitingForCustomLevel = true;
                    waitingForCustomLevelTransferId = sceneCustomLevelTransferId;
                    status = "Receiving custom level from host...";
                }
                else if (!string.IsNullOrEmpty(receivedCustomLevelJson))
                    StartCustomLevelLocally(receivedCustomLevelJson);
                else
                {
                    waitingForCustomLevel = true;
                    waitingForCustomLevelTransferId = sceneCustomLevelTransferId;
                    status = "Receiving custom level from host...";
                }
                return;
            }
            waitingForCustomLevel = false;
            waitingForCustomLevelTransferId = 0;
            receivedCustomLevelJson = "";
            receivedCustomLevelTransferId = 0;
            status = mustReload ? "Host restarted the level. Reloading..." :
                "Loading host scene " + sceneToLoad + "...";
            SceneManager.LoadScene(sceneToLoad);
        }

        CsExperienceMode.Tick();
        if (MultiplayerHud.IsTyping || (multiplayerHud != null && multiplayerHud.ChatOpen)) return;
        if (Input.GetKeyDown(Controls.keys[Controls.PAIN_SOUND])) PlayerGruntService.TryPlayLocal();
        if (Input.GetKey(KeyCode.End) && Input.GetKey(KeyCode.Space) &&
            Input.GetKey(KeyCode.C) && Input.GetKeyDown(KeyCode.S))
        {
            CsExperienceMode.Toggle();
            return;
        }
        if (Input.GetKey(KeyCode.Space) && Input.GetKey(KeyCode.End) && Input.GetKeyDown(KeyCode.R))
        {
            multiplayerHud.ToggleReplicationDebugOverlay();
            return;
        }
        if (Input.GetKey(KeyCode.Space) && Input.GetKey(KeyCode.End) && Input.GetKeyDown(KeyCode.L))
        {
            replicationDebugMode.Toggle();
            return;
        }
        if (Input.GetKey(KeyCode.Space) && Input.GetKey(KeyCode.End) && Input.GetKeyDown(KeyCode.S))
        {
            multiplayerHud.ToggleNetworkStats();
            return;
        }
    }

    internal void SaveLobbyPreferences()
    {
        if (MultiplayerSession.IsConnected && !MultiplayerSession.IsHosting) return;
        var changed = false;
        if (savedPlayerName.Value != playerName) { savedPlayerName.Value = playerName; changed = true; }
        if (savedLobbyName.Value != lobbyName) { savedLobbyName.Value = lobbyName; changed = true; }
        if (savedCreatePvp.Value != createPvp) { savedCreatePvp.Value = createPvp; changed = true; }
        if (savedCreateCanGrab.Value != createCanGrab) { savedCreateCanGrab.Value = createCanGrab; changed = true; }
        if (savedCreateGrabOnlyUnconscious.Value != createGrabOnlyUnconscious)
        {
            savedCreateGrabOnlyUnconscious.Value = createGrabOnlyUnconscious;
            changed = true;
        }
        if (savedCreateAllowRespawn.Value != createAllowRespawn)
        {
            savedCreateAllowRespawn.Value = createAllowRespawn;
            changed = true;
        }
        if (savedCreateAutoRestart.Value != createAutoRestart) { savedCreateAutoRestart.Value = createAutoRestart; changed = true; }
        if (savedCreateRespawnAtStart.Value != createRespawnAtStart)
        {
            savedCreateRespawnAtStart.Value = createRespawnAtStart;
            changed = true;
        }
        if (savedCreatePlayerCollisions.Value != createPlayerCollisions) { savedCreatePlayerCollisions.Value = createPlayerCollisions; changed = true; }
        if (savedCreateCheats.Value != createCheats) { savedCreateCheats.Value = createCheats; changed = true; }
        if (savedCreateAllowSwap.Value != createAllowSwap) { savedCreateAllowSwap.Value = createAllowSwap; changed = true; }
        if (savedCreateAllowScaleChanging.Value != createAllowScaleChanging) { savedCreateAllowScaleChanging.Value = createAllowScaleChanging; changed = true; }
        if (savedCreateAllowObserver.Value != createAllowObserver) { savedCreateAllowObserver.Value = createAllowObserver; changed = true; }
        if (savedCreateTeams.Value != createTeams) { savedCreateTeams.Value = createTeams; changed = true; }
        if (savedCreateTeamsCfg.Value != createTeamsCfg) { savedCreateTeamsCfg.Value = createTeamsCfg; changed = true; }
        if (savedCreateInitialScale.Value != createInitialScale) { savedCreateInitialScale.Value = createInitialScale; changed = true; }
        if (savedCreateStartingWeapon.Value != createStartingWeapon) { savedCreateStartingWeapon.Value = createStartingWeapon; changed = true; }
        if (savedCreateRespawnWeapon.Value != createRespawnWeapon) { savedCreateRespawnWeapon.Value = createRespawnWeapon; changed = true; }
        if (savedCreateStartingAmmo.Value != createStartingAmmo) { savedCreateStartingAmmo.Value = createStartingAmmo; changed = true; }
        if (savedCreateRespawnAmmo.Value != createRespawnAmmo) { savedCreateRespawnAmmo.Value = createRespawnAmmo; changed = true; }
        if (savedCreateRespawnTime.Value != createRespawnTime) { savedCreateRespawnTime.Value = createRespawnTime; changed = true; }
        if (savedCreateNumberOfLives.Value != createNumberOfLives) { savedCreateNumberOfLives.Value = createNumberOfLives; changed = true; }
        if (savedCreateMaxPlayers.Value != createMaxPlayers) { savedCreateMaxPlayers.Value = createMaxPlayers; changed = true; }
        if (changed) Config.Save();
    }


    internal void RefreshLobbies()
    {
        var server = masterUrl.Value.TrimEnd('/');
        status = "Refreshing lobbies from " + DisplayServerAddress(server) + "...";
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var response = HttpAt(server, "GET", "/v1/lobbies", null, null);
                var refreshed = ParseAndSortLobbies(response);
                RunOnMainThread(() => { lobbies.Clear(); lobbies.AddRange(refreshed); status = "Connected to " + DisplayServerAddress(masterUrl.Value) + ". Found " + lobbies.Count + " lobby/lobbies."; });
            }
            catch (Exception exception) { RunOnMainThread(() => status = "Lobby server unavailable: " + exception.Message); }
        });
    }

    internal void ConnectLobbyServer()
    {
        if (MultiplayerSession.IsHosting)
        {
            status = "Close the hosted lobby before changing lobby server.";
            return;
        }
        string normalized;
        if (!TryNormalizeServerAddress(lobbyServerAddress, out normalized))
        {
            status = "Invalid lobby server address.";
            return;
        }
        masterUrl.Value = normalized;
        Config.Save();
        lobbyServerAddress = DisplayServerAddress(normalized);
        lobbies.Clear();
        status = "Connecting to lobby server " + lobbyServerAddress + "...";
        RefreshLobbies();
    }

    internal void RefreshServerList()
    {
        if (serverListLoading) return;
        serverListLoading = true;
        serverListError = "";
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var loaded = ParseServerList(new WebClient().DownloadString(ServersUrl));
                if (loaded.Count == 0) throw new InvalidDataException("The server list is empty.");
                foreach (var server in loaded)
                {
                    server.address = server.address.Trim();
                    server.location = string.IsNullOrWhiteSpace(server.location) ? "Unknown location" : server.location.Trim();
                    server.pingMs = MeasureServerPing(server.address);
                }
                RunOnMainThread(() =>
                {
                    servers.Clear();
                    servers.AddRange(loaded);
                    serverListLoading = false;
                });
            }
            catch (Exception exception)
            {
                RunOnMainThread(() =>
                {
                    serverListError = exception.Message;
                    serverListLoading = false;
                });
            }
        });
    }

    internal void SelectLobbyServer(string address)
    {
        lobbyServerAddress = address ?? "";
        ConnectLobbyServer();
    }

    private static int MeasureServerPing(string address)
    {
        try
        {
            using (var ping = new System.Net.NetworkInformation.Ping())
            {
                var reply = ping.Send(address, 1500);
                return reply != null && reply.Status == System.Net.NetworkInformation.IPStatus.Success && reply.RoundtripTime <= int.MaxValue
                    ? (int)reply.RoundtripTime : -1;
            }
        }
        catch { return -1; }
    }

    private static List<ServerInfo> ParseServerList(string source)
    {
        var servers = new List<ServerInfo>();
        var matches = Regex.Matches(source ?? "", "\\{\\s*\\\"address\\\"\\s*:\\s*\\\"(?<address>(?:\\\\.|[^\\\"])*)\\\"\\s*,\\s*\\\"location\\\"\\s*:\\s*\\\"(?<location>(?:\\\\.|[^\\\"])*)\\\"\\s*\\}", RegexOptions.Singleline);
        foreach (Match match in matches)
        {
            var address = Regex.Unescape(match.Groups["address"].Value).Trim();
            if (string.IsNullOrWhiteSpace(address) || servers.Exists(item => string.Equals(item.address, address, StringComparison.OrdinalIgnoreCase))) continue;
            servers.Add(new ServerInfo { address = address, location = Regex.Unescape(match.Groups["location"].Value) });
        }
        return servers;
    }

    internal void PasteCustomLevel()
    {
        var clipboard = (GUIUtility.systemCopyBuffer ?? "").Trim();
        if (string.IsNullOrEmpty(clipboard))
        {
            status = "Clipboard does not contain a custom level.";
            return;
        }
        try
        {
            var levelJson = Compression.Decompress(clipboard);
            var parsed = JsonUtility.FromJson<Level>(levelJson);
            if (parsed == null || string.IsNullOrWhiteSpace(levelJson))
                throw new InvalidDataException("The level JSON is invalid.");
            if (Encoding.UTF8.GetByteCount(levelJson) > 8 * 1024 * 1024)
                throw new InvalidDataException("The level is larger than 8 MB.");
            CustomLevelProgress.ClearActive();
            customLevelJson = levelJson;
            customLevelCode = Compression.Compress(levelJson);
            status = "Custom level loaded (" + Encoding.UTF8.GetByteCount(levelJson) / 1024 + " KiB).";
        }
        catch (Exception exception)
        {
            customLevelJson = "";
            status = "Could not load custom level: " + exception.Message;
        }
    }

    internal void StartCustomLevel()
    {
        if (!MultiplayerSession.IsHosting)
        {
            status = "Create a lobby before starting a custom level.";
            return;
        }
        if (string.IsNullOrEmpty(customLevelJson))
        {
            status = "Paste a custom level first.";
            return;
        }
        try
        {
            MultiplayerSession.StartHostCustomLevel(customLevelJson, customLevelCode);
            StartCustomLevelLocally(customLevelJson);
            RPCManager.CheckInstance();
            RPCManager.instance?.UpdateCustomLevel(customLevelCode);
        }
        catch (Exception exception) { status = "Could not start custom level: " + exception.Message; }
    }

    internal void SuggestCustomLevel()
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHosting)
        {
            status = "Join a lobby before suggesting a custom level.";
            return;
        }
        
        if (string.IsNullOrWhiteSpace(customLevelJson) || string.IsNullOrWhiteSpace(customLevelCode))
        {
            status = "Load a custom level before suggesting it.";
            return;
        }
        
        var sizeKiB = (Encoding.UTF8.GetByteCount(customLevelJson) + 1023) / 1024;
        MultiplayerSession.SuggestCustomLevel(customLevelCode, sizeKiB);
        status = "Custom level suggestion sent to the host.";
    }

    internal void AcceptCustomLevelSuggestion(CustomLevelSuggestionPacket suggestion)
    {
        if (MultiplayerSession.IsHosting) StartCatalogCustomLevel(suggestion.LevelCode, "Untitled");
    }

    private void LoadCustomLevelCatalog()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var catalog = new WebClient().DownloadString(CustomLevelsUrl);
                RunOnMainThread(() =>
                {
                    try
                    {
                        CustomLevelBrowserUi.CacheCatalog(catalog);
                        customLevelCatalogReady = true;
                    }
                    catch (Exception exception) { customLevelCatalogError = exception.Message; }
                });
            }
            catch (Exception exception) { RunOnMainThread(() => customLevelCatalogError = exception.Message); }
        });
    }

    internal void StartCatalogCustomLevel(string code, string levelName)
    {
        if (!MultiplayerSession.IsHosting)
        {
            try
            {
                var levelJson = DecodeCatalogLevelCode(code);
                if (JsonUtility.FromJson<Level>(levelJson) == null || string.IsNullOrWhiteSpace(levelJson))
                    throw new InvalidDataException("The level JSON is invalid.");
                if (!MultiplayerSession.IsConnected)
                {
                    customLevelJson = levelJson;
                    customLevelCode = code;
                    status = "Starting custom level: " + levelName;
                    StartCustomLevelLocally(levelJson);
                    RPCManager.CheckInstance();
                    RPCManager.instance?.UpdateCustomLevel(code);
                    return;
                }
                var sizeKiB = (Encoding.UTF8.GetByteCount(levelJson) + 1023) / 1024;
                MultiplayerSession.SuggestCustomLevel(code, sizeKiB);
                status = "Custom level suggestion sent to the host.";
            }
            catch (Exception exception) { status = "Could not suggest custom level: " + exception.Message; }
            return;
        }
        
        try
        {
            var levelJson = DecodeCatalogLevelCode(code);
            if (JsonUtility.FromJson<Level>(levelJson) == null || string.IsNullOrWhiteSpace(levelJson))
                throw new InvalidDataException("The level JSON is invalid.");
            if (Encoding.UTF8.GetByteCount(levelJson) > 4 * 1024 * 1024)
                throw new InvalidDataException("The level is larger than 4 MB.");
            CustomLevelProgress.SetActive(code);
            customLevelJson = levelJson;
            customLevelCode = code;
            status = "Starting custom level: " + levelName;
            StartCustomLevel();
        }
        catch (Exception exception) { status = "Could not load custom level: " + exception.Message; }
    }

    private void StartCustomLevelLocally(string levelJson)
    {
        if (string.IsNullOrWhiteSpace(levelJson)) return;
        var loader = SceneLoader.main;
        if (loader == null) throw new InvalidOperationException("Scene loader is not ready.");
        loader.levelEditString = levelJson;
        customLevelPhysicsRefreshUntil = Time.unscaledTime + 5f;
        nextCustomLevelPhysicsRefresh = 0f;
        status = "Loading custom level...";
        loader.LoadScene("LevelLoader");
    }

    internal void CreateLobby()
    {
        try
        {
            int respawnTime;
            if (!int.TryParse(createRespawnTime, out respawnTime)) respawnTime = 5;
            respawnTime = Mathf.Clamp(respawnTime, 0, 3600);
            createRespawnTime = respawnTime.ToString();
            int numberOfLives;
            if (!int.TryParse(createNumberOfLives, out numberOfLives)) numberOfLives = 0;
            numberOfLives = Mathf.Clamp(numberOfLives, 0, ushort.MaxValue);
            createNumberOfLives = numberOfLives.ToString();
            int maxPlayers;
            if (!int.TryParse(createMaxPlayers, out maxPlayers)) maxPlayers = 4;
            maxPlayers = Mathf.Clamp(maxPlayers, 2, 16);
            createMaxPlayers = maxPlayers.ToString();
            var body = JsonUtility.ToJson(new CreateLobbyRequest { name = lobbyName, hostName = playerName,
                map = "Host chooses level", maxPlayers = maxPlayers, hostPort = 27016, pvp = createPvp,
                canGrab = createCanGrab, grabOnlyUnconscious = createGrabOnlyUnconscious,
                allowRespawn = createAllowRespawn, respawnTime = respawnTime, numberOfLives = numberOfLives,
                respawnAtStart = createRespawnAtStart,
                playerCollisions = createPlayerCollisions, cheats = createCheats, allowSwap = createAllowSwap,
                allowScaleChanging = createAllowScaleChanging, initialScale = ParseInitialScale(), startingWeapon = createStartingWeapon, respawnWeapon = createRespawnWeapon, startingAmmo = createStartingAmmo, respawnAmmo = createRespawnAmmo,
                allowObserver = createAllowObserver,
                teams = createTeams, teamsCfg = createTeamsCfg,
                brutalMode = MultiplayerSession.ReadBrutalMode(),
                hostP2P = createConnectionMode != ConnectionMode.Relay,
                connectionMode = createConnectionMode.ToString(), modVersion = PluginVersion });
            ThreadPool.QueueUserWorkItem(_ => CreateLobbyInDirectory(body, respawnTime, numberOfLives, maxPlayers));
        }
        catch (Exception e) { status = "Could not create lobby: " + e.Message; }
    }

    internal void UpdateHostedLobby()
    {
        if (!MultiplayerSession.IsHosting)
        {
            status = "Create a lobby first.";
            return;
        }
        int respawnTime;
        if (!int.TryParse(createRespawnTime, out respawnTime)) respawnTime = 5;
        respawnTime = Mathf.Clamp(respawnTime, 0, 3600);
        createRespawnTime = respawnTime.ToString();
        int numberOfLives;
        if (!int.TryParse(createNumberOfLives, out numberOfLives)) numberOfLives = 0;
        numberOfLives = Mathf.Clamp(numberOfLives, 0, ushort.MaxValue);
        createNumberOfLives = numberOfLives.ToString();
        int maxPlayers;
        if (!int.TryParse(createMaxPlayers, out maxPlayers)) maxPlayers = MultiplayerSession.MaxPlayers;
        maxPlayers = Mathf.Clamp(maxPlayers, 2, 16);
        createMaxPlayers = maxPlayers.ToString();
        if (!MultiplayerSession.UpdateHostSettings(createPvp, createCanGrab, createGrabOnlyUnconscious,
            createAllowRespawn, createAutoRestart, respawnTime, numberOfLives, createRespawnAtStart, createPlayerCollisions, createCheats, createAllowSwap,
            createAllowScaleChanging, ParseInitialScale(), createAllowObserver, createTeams, createTeamsCfg, createStartingWeapon, createRespawnWeapon, createStartingAmmo, createRespawnAmmo, maxPlayers))
        {
            status = "Could not update lobby settings.";
            return;
        }
        hostedLobbyDisplayName = lobbyName;
        status = "Lobby settings updated.";
        if (!string.IsNullOrEmpty(hostedLobbyId) && !string.IsNullOrEmpty(hostRelayKey))
            ThreadPool.QueueUserWorkItem(_ => UpdateHostedLobbyInDirectory());
    }

    internal void CloseHostedLobby()
    {
        if (!MultiplayerSession.IsHosting)
        {
            status = "No hosted lobby is active.";
            return;
        }
        var lobbyId = hostedLobbyId;
        var relayKey = hostRelayKey;
        MultiplayerSession.Shutdown();
        hostedLobbyId = "";
        hostedLobbyDisplayName = "";
        hostRelayKey = "";
        requestedHostScene = "";
        waitingForCustomLevel = false;
        waitingForCustomLevelTransferId = 0;
        receivedCustomLevelTransferId = 0;
        nextHeartbeat = 0f;
        lastHostedPeerListRevision = -1;
        status = "Lobby closed.";
        if (!string.IsNullOrEmpty(lobbyId) && !string.IsNullOrEmpty(relayKey))
            DeleteHostedLobby(lobbyId, relayKey);
    }

    internal void JoinLobby(string id)
    {
        lock (joinLock)
        {
            if (joinInProgress || MultiplayerSession.IsHosting || MultiplayerSession.IsActive) return;
            joinInProgress = true;
        }
        try
        {
            var mode = ConnectionMode.Relay;
            foreach (var lobby in lobbies)
                if (lobby.id == id) { mode = lobby.connectionMode; break; }
            status = "Joining lobby...";
            ThreadPool.QueueUserWorkItem(_ => JoinLobbyRequest(id, mode));
        }
        catch (Exception e)
        {
            SetJoinInProgress(false);
            status = "Could not join lobby: " + e.Message;
        }
    }

    internal void CheckForUpdates(bool manual)
    {
        if (Interlocked.CompareExchange(ref updateCheckInProgress, 1, 0) != 0) return;
        if (manual) updateStatus = "Checking GitHub releases...";
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string result;
            try
            {
                var release = ReleaseRequest(ReleasesApiUrl);
                var tag = JsonString(release, "tag_name").Trim();
                if (string.IsNullOrEmpty(tag)) throw new InvalidDataException("Latest release has no tag.");
                var comparison = CompareVersions(PluginVersion, tag);
                result = comparison < 0
                    ? "UPDATE AVAILABLE: " + tag
                    : comparison > 0
                        ? "INSTALLED BUILD IS NEWER THAN (HOW??)" + tag
                        : "YOU ARE UP TO DATE";
            }
            catch (Exception exception)
            {
                result = "UPDATE CHECK FAILED: " + exception.Message;
                Logger.LogWarning(result);
            }
            RunOnMainThread(() =>
            {
                updateStatus = result;
                Interlocked.Exchange(ref updateCheckInProgress, 0);
            });
        });
    }

    internal bool CanJoinLobby
    {
        get
        {
            lock (joinLock)
                return !joinInProgress && !MultiplayerSession.IsHosting && !MultiplayerSession.IsActive;
        }
    }

    internal bool IsJoinedLobby(string id)
    {
        return !string.IsNullOrEmpty(id) && string.Equals(joinedLobbyId, id, StringComparison.Ordinal);
    }

    internal string JoinedLobbyName
    {
        get
        {
            foreach (var lobby in lobbies)
                if (string.Equals(lobby.id, joinedLobbyId, StringComparison.Ordinal)) return lobby.name;
            return "Current lobby";
        }
    }

    internal void LeaveLobby()
    {
        if (!MultiplayerSession.IsActive || MultiplayerSession.IsHosting) return;
        MultiplayerSession.Shutdown();
        joinedLobbyId = "";
        requestedHostScene = "";
        waitingForCustomLevel = false;
        waitingForCustomLevelTransferId = 0;
        receivedCustomLevelJson = "";
        receivedCustomLevelTransferId = 0;
        status = "Left lobby.";
        RefreshLobbies();
    }

    internal bool TryHandleHostCommand(string message)
    {
        return _chatCommandSystem != null && _chatCommandSystem.TryHandle(message);
    }

    internal bool CanBanPlayers => MultiplayerSession.IsHosting &&
        !string.IsNullOrEmpty(hostedLobbyId) && !string.IsNullOrEmpty(hostRelayKey);

    internal void BanPlayerFromCommand(string playerName, ushort peerId)
    {
        var lobbyId = hostedLobbyId;
        var relayKey = hostRelayKey;
        ThreadPool.QueueUserWorkItem(_ => BanPlayerInDirectory(lobbyId, relayKey, playerName, peerId));
    }

    private void ConnectRelay(string address, string lobbyId, string relayKey, ushort peerId, ushort hostPeerId,
        int maxPlayers, ConnectionMode mode)
    {
        string error;
        if (!MultiplayerSession.Connect(address, lobbyId, relayKey, playerName, peerId, hostPeerId, maxPlayers,
            mode, Logger, out error))
        {
            SetJoinInProgress(false);
            joinedLobbyId = "";
            status = error;
            return;
        }
        SetJoinInProgress(false);
        requestedHostScene = "";
        avatarReplication.Configure(playerName);
        multiplayerHud.ResetChat();
        status = "Connecting via " + mode + " through UDP relay " + address + "...";
    }

    private void CreateLobbyInDirectory(string body, int respawnTime, int numberOfLives, int maxPlayers)
    {
        try
        {
            var response = Http("POST", "/v1/lobbies", body, null);
            var lobbyId = JsonString(response, "id");
            var relayKey = JsonString(response, "hostRelayKey");
            var relayAddress = JsonString(response, "relayAddress");
            var hostPeerId = (ushort)Mathf.Clamp(JsonInt(response, "hostPeerId"), 1, 16);
            if (string.IsNullOrEmpty(relayAddress)) relayAddress = DefaultRelayAddress();
            if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(relayKey)) throw new InvalidDataException("Invalid directory response.");
            RunOnMainThread(() =>
            {
                MultiplayerSession.StartHost(lobbyId, relayKey, relayAddress, createPvp, createCanGrab,
                    createGrabOnlyUnconscious, createAllowRespawn, createAutoRestart, respawnTime, numberOfLives, createRespawnAtStart, createPlayerCollisions, createCheats, createAllowSwap,
                    createAllowScaleChanging, ParseInitialScale(), createAllowObserver, createTeams, createTeamsCfg, createStartingWeapon, createRespawnWeapon, createStartingAmmo, createRespawnAmmo,
                    playerName, hostPeerId, maxPlayers, createConnectionMode, Logger);
                avatarReplication.Configure(playerName); multiplayerHud.ResetChat(); hostedLobbyId = lobbyId; hostedLobbyDisplayName = lobbyName; hostRelayKey = relayKey; nextHeartbeat = Time.unscaledTime + 10f; status = "Lobby created, start a level.";
                if (headlessMode) StartHeadlessKeepAlive(lobbyId, relayKey, masterUrl.Value.TrimEnd('/'));
                if (headlessMode) headlessStartPending = true;
            });
        }
        catch (Exception exception) { RunOnMainThread(() => status = "Could not create lobby: " + exception.Message); }
    }

    private void FixedUpdate()
    {
        PlayerCarrySystem.FixedTick();
        if (headlessMode) Interlocked.Increment(ref headlessFixedTicks);
    }

    internal bool TryHandleLobbyChatCommand(ushort senderId, string message)
    {
        if (!headlessMode || !MultiplayerSession.IsHost || string.IsNullOrWhiteSpace(message)) return false;
        var command = message.Trim();
        if (string.Equals(command, "!help", StringComparison.OrdinalIgnoreCase))
        {
            SendHeadlessHelp(senderId);
            return true;
        }
        if (string.Equals(command, "!tps", StringComparison.OrdinalIgnoreCase))
        {
            UpdateHeadlessTps();
            var stats = MultiplayerSession.DebugStats();
            SendHeadlessChat("TPS: " + headlessTps + " | RX: " + (stats.ReceivedBytesPerSecond / 1024f).ToString("0.0") +
                " KiB/s | TX: " + (stats.SentBytesPerSecond / 1024f).ToString("0.0") + " KiB/s");
            return true;
        }
        if (string.Equals(command, "!votedefault", StringComparison.OrdinalIgnoreCase))
            return RegisterHeadlessVote(senderId, "default", "default map");
        if (string.Equals(command, "!vote restart", StringComparison.OrdinalIgnoreCase))
            return RegisterHeadlessVote(senderId, "restart", "restart");
        const string changePrefix = "!vote change ";
        if (command.StartsWith(changePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var target = command.Substring(changePrefix.Length).Trim();
            if (string.IsNullOrEmpty(target))
            {
                SendHeadlessChat("Usage: !vote change <map name or scene>", senderId);
                return true;
            }
            return RegisterHeadlessVote(senderId, "change:" + target, "change to " + target);
        }
        return false;
    }

    private void SendHeadlessHelpToNewPlayers()
    {
        if (!headlessMode || !MultiplayerSession.IsHosting) return;
        var peers = MultiplayerSession.PeerIds();
        foreach (var peerId in peers)
            if (headlessKnownPeers.Add(peerId)) SendHeadlessHelp(peerId);
        headlessKnownPeers.RemoveWhere(peerId => Array.IndexOf(peers, peerId) < 0);
    }

    private void StartHeadlessKeepAlive(string lobbyId, string relayKey, string directoryUrl)
    {
        if (headlessKeepAliveTimer != null) headlessKeepAliveTimer.Dispose();
        headlessKeepAliveTimer = new Timer(_ =>
        {
            if (Interlocked.CompareExchange(ref headlessKeepAliveInFlight, 1, 0) != 0) return;
            try
            {
                MultiplayerSession.UpdatePing();
                var players = MultiplayerSession.PlayerCount;
                HttpAt(directoryUrl, "PUT", "/v1/lobbies/" + lobbyId,
                    "{\"players\":" + players + ",\"map\":\"LevelLoader\"}", "Bearer " + relayKey);
            }
            catch (Exception exception) { Logger.LogWarning("Headless keep-alive failed: " + exception.Message); }
            finally { Interlocked.Exchange(ref headlessKeepAliveInFlight, 0); }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private void UpdateHeadlessTps()
    {
        if (!headlessMode) return;
        var now = Time.realtimeSinceStartup;
        if (headlessTpsSampleTime < 0f)
        {
            headlessTpsSampleTime = now;
            headlessFixedTicksAtLastSample = Interlocked.CompareExchange(ref headlessFixedTicks, 0, 0);
            return;
        }
        var elapsed = now - headlessTpsSampleTime;
        if (elapsed < 0.25f) return;
        var ticks = Interlocked.CompareExchange(ref headlessFixedTicks, 0, 0);
        headlessTps = Mathf.RoundToInt((ticks - headlessFixedTicksAtLastSample) / elapsed);
        headlessFixedTicksAtLastSample = ticks;
        headlessTpsSampleTime = now;
    }

    private void HideHeadlessHostAvatar()
    {
        if (!IsHeadlessServer || SceneManager.GetActiveScene().name != "LevelLoader") return;
        var sceneHandle = SceneManager.GetActiveScene().handle;
        if (hiddenHeadlessAvatarScene == sceneHandle) return;
        var player = PlayerScript.player;
        if (player == null || player.bodyScript == null) return;
        hiddenHeadlessAvatarScene = sceneHandle;
        var body = player.bodyScript;
        body.transform.position = new Vector3(100000f, 100000f, 0f);
        foreach (var collider in body.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
        foreach (var rigidbody in body.GetComponentsInChildren<Rigidbody2D>(true))
        {
            rigidbody.velocity = Vector2.zero;
            rigidbody.angularVelocity = 0f;
            rigidbody.simulated = false;
        }
    }

    private bool RegisterHeadlessVote(ushort senderId, string target, string description)
    {
        foreach (var vote in headlessVotes.Values) vote.Remove(senderId);
        HashSet<ushort> voters;
        if (!headlessVotes.TryGetValue(target, out voters))
        {
            voters = new HashSet<ushort>();
            headlessVotes[target] = voters;
        }
        voters.Add(senderId);
        var needed = MultiplayerSession.PeerIds().Length / 2 + 1;
        SendHeadlessChat("Vote " + description + ": " + voters.Count + "/" + needed + ".");
        if (voters.Count < needed) return true;
        headlessVotes.Clear();
        SendHeadlessChat("Vote passed: " + description + ".");
        if (target == "restart") RestartHeadlessCurrentLevel();
        else if (target == "default") StartHeadlessCustomLevel(headlessDefaultMapJson);
        else StartHeadlessMapChange(target.Substring("change:".Length));
        return true;
    }

    private void StartHeadlessMapChange(string mapOrScene)
    {
        if (IsBuiltInHeadlessScene(mapOrScene))
        {
            try
            {
                MultiplayerSession.EndHostCustomLevel(mapOrScene);
                SceneLoader.main.LoadScene(mapOrScene);
            }
            catch (Exception exception) { SendHeadlessChat("Could not load scene: " + exception.Message); }
            return;
        }
        SendHeadlessChat("Looking up map: " + mapOrScene + "...");
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var catalog = new WebClient().DownloadString(CustomLevelsUrl);
                RunOnMainThread(() => LoadHeadlessCatalogMap(catalog, mapOrScene));
            }
            catch (Exception exception) { RunOnMainThread(() => SendHeadlessChat("Could not load map: " + exception.Message)); }
        });
    }

    private void LoadHeadlessCatalogMap(string catalog, string requestedName)
    {
        try
        {
            HeadlessLevelEntry match = null;
            var requestedKey = NormalizeHeadlessLevelName(requestedName);
            var catalogEntries = ParseHeadlessCatalog(catalog);
            foreach (var entry in catalogEntries)
                if (NormalizeHeadlessLevelName(entry.name) == requestedKey) { match = entry; break; }
            if (match == null || string.IsNullOrWhiteSpace(match.code))
            {
                var suggestions = new List<string>();
                foreach (var entry in catalogEntries)
                    if (!string.IsNullOrWhiteSpace(entry.name) &&
                        NormalizeHeadlessLevelName(entry.name).Contains(requestedKey)) suggestions.Add(entry.name);
                throw new InvalidDataException(suggestions.Count == 0 ? "map not found" :
                    "map not found; try: " + string.Join(" | ", suggestions.GetRange(0, Math.Min(3, suggestions.Count)).ToArray()));
            }
            var mapJson = DecodeCatalogLevelCode(match.code);
            if (JsonUtility.FromJson<Level>(mapJson) == null) throw new InvalidDataException("map code is invalid");
            StartHeadlessCustomLevel(mapJson);
        }
        catch (Exception exception) { SendHeadlessChat("Could not load map: " + exception.Message); }
    }

    private void StartHeadlessCustomLevel(string levelJson)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(levelJson)) throw new InvalidDataException("no default map is loaded");
            customLevelJson = levelJson;
            MultiplayerSession.StartHostCustomLevel(levelJson, Compression.Compress(levelJson));
            MultiplayerSession.NotifyHostSceneReload("LevelLoader", true);
            StartCustomLevelLocally(levelJson);
        }
        catch (Exception exception) { SendHeadlessChat("Could not load map: " + exception.Message); }
    }

    private void RestartHeadlessCurrentLevel()
    {
        try
        {
            var loader = SceneLoader.main;
            if (loader == null) throw new InvalidOperationException("scene loader is not ready");
            loader.LoadScene(SceneManager.GetActiveScene().name);
        }
        catch (Exception exception) { SendHeadlessChat("Could not restart level: " + exception.Message); }
    }

    private static string DecodeCatalogLevelCode(string value)
    {
        var code = (value ?? "").Trim();
        using (var compressed = new MemoryStream(Convert.FromBase64String(code)))
        using (var inflater = new DeflateStream(compressed, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            inflater.CopyTo(output);
            return Encoding.UTF8.GetString(output.ToArray()).Trim();
        }
    }

    private static bool IsBuiltInHeadlessScene(string value)
    {
        value = (value ?? "").Trim();
        if (value.StartsWith("actualLevel", StringComparison.OrdinalIgnoreCase)) value = value.Substring("actualLevel".Length);
        else if (value.StartsWith("campaign", StringComparison.OrdinalIgnoreCase)) value = value.Substring("campaign".Length);
        else return false;
        int ignored;
        return int.TryParse(value, out ignored);
    }

    private static string NormalizeHeadlessLevelName(string value)
    {
        var source = value ?? "";
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
        return builder.ToString();
    }

    public string GetCurrentLobbyId()
    {
        if (string.IsNullOrEmpty(joinedLobbyId))
            return hostedLobbyId;
   else     return joinedLobbyId;
    }

    private static List<HeadlessLevelEntry> ParseHeadlessCatalog(string catalog)
    {
        var result = new List<HeadlessLevelEntry>();
        var matches = Regex.Matches(catalog ?? "", "\\\"name\\\"\\s*:\\s*\\\"(?<name>(?:\\\\.|[^\\\"])*)\\\".*?\\\"code\\\"\\s*:\\s*\\\"(?<code>(?:\\\\.|[^\\\"])*)\\\"", RegexOptions.Singleline);
        foreach (Match match in matches)
        {
            var name = Regex.Unescape(match.Groups["name"].Value);
            var code = Regex.Unescape(match.Groups["code"].Value);
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(code))
                result.Add(new HeadlessLevelEntry { name = name, code = code });
        }
        if (result.Count == 0) throw new InvalidDataException("level catalog returned no maps");
        return result;
    }

    private void SendHeadlessHelp(ushort targetPeerId)
    {
        SendHeadlessChat("Maps: !vote change <name>; scenes: actualLevel1/campaign6; !vote restart | !tps | !votedefault | !help", targetPeerId);
    }

    private static void SendHeadlessChat(string text, ushort targetPeerId = 0)
    {
        ChatPacket packet;
        if (ChatService.TryCreate(text, true, out packet)) MultiplayerSession.Send(packet, targetPeerId);
    }

    private sealed class HeadlessLevelEntry { public string name; public string code; }

    private static bool HasCommandLineFlag(string flag)
    {
        foreach (var arg in Environment.GetCommandLineArgs())
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string CommandLineValue(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        for (var index = 0; index + 1 < args.Length; index++)
            if (string.Equals(args[index], flag, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        return "";
    }

    private static void ApplyHeadlessBooleanOption(string flag, ref bool value)
    {
        var text = CommandLineValue(flag);
        bool parsed;
        if (bool.TryParse(text, out parsed))
        {
            value = parsed;
            return;
        }
        if (text == "1") { value = true; return; }
        if (text == "0") { value = false; return; }
        if (HasCommandLineFlag("--no-" + flag.Substring(2))) { value = false; return; }
        if (HasCommandLineFlag(flag)) value = true;
    }

    private void ApplyHeadlessCommandLineOptions()
    {
        var value = CommandLineValue("--master");
        if (!string.IsNullOrWhiteSpace(value) && TryNormalizeServerAddress(value, out var normalized))
        {
            masterUrl.Value = normalized;
            lobbyServerAddress = DisplayServerAddress(normalized);
        }
        value = CommandLineValue("--name");
        if (!string.IsNullOrWhiteSpace(value)) lobbyName = value.Trim();
        value = CommandLineValue("--host");
        if (!string.IsNullOrWhiteSpace(value)) playerName = value.Trim();
        value = CommandLineValue("--max-players");
        if (!string.IsNullOrWhiteSpace(value)) createMaxPlayers = value;
        value = CommandLineValue("--respawn-seconds");
        if (!string.IsNullOrWhiteSpace(value)) createRespawnTime = value;
        value = CommandLineValue("--lives");
        if (!string.IsNullOrWhiteSpace(value)) createNumberOfLives = value;
        value = CommandLineValue("--teams-cfg");
        if (!string.IsNullOrWhiteSpace(value)) createTeamsCfg = value;
        value = CommandLineValue("--initial-scale");
        if (!string.IsNullOrWhiteSpace(value)) createInitialScale = value;
        value = CommandLineValue("--starting-weapon");
        if (!string.IsNullOrWhiteSpace(value)) createStartingWeapon = value;
        value = CommandLineValue("--respawn-weapon");
        if (!string.IsNullOrWhiteSpace(value)) createRespawnWeapon = value;
        value = CommandLineValue("--starting-ammo");
        if (!string.IsNullOrWhiteSpace(value)) createStartingAmmo = value;
        value = CommandLineValue("--respawn-ammo");
        if (!string.IsNullOrWhiteSpace(value)) createRespawnAmmo = value;
        value = CommandLineValue("--connection");
        ConnectionMode connectionMode;
        if (Enum.TryParse(value, true, out connectionMode)) createConnectionMode = connectionMode;
        ApplyHeadlessBooleanOption("--pvp", ref createPvp);
        ApplyHeadlessBooleanOption("--can-grab", ref createCanGrab);
        ApplyHeadlessBooleanOption("--grab-only-unconscious", ref createGrabOnlyUnconscious);
        ApplyHeadlessBooleanOption("--allow-respawn", ref createAllowRespawn);
        ApplyHeadlessBooleanOption("--auto-restart", ref createAutoRestart);
        ApplyHeadlessBooleanOption("--respawn-at-start", ref createRespawnAtStart);
        ApplyHeadlessBooleanOption("--player-collisions", ref createPlayerCollisions);
        ApplyHeadlessBooleanOption("--cheats", ref createCheats);
        ApplyHeadlessBooleanOption("--allow-swap", ref createAllowSwap);
        ApplyHeadlessBooleanOption("--allow-scale-changing", ref createAllowScaleChanging);
        ApplyHeadlessBooleanOption("--allow-observer", ref createAllowObserver);
        ApplyHeadlessBooleanOption("--teams", ref createTeams);
        if (createGrabOnlyUnconscious) createCanGrab = true;
        Logger.LogInfo("Headless settings: lobby=" + lobbyName + ", host=" + playerName + ", max=" + createMaxPlayers + ".");
    }

    private void JoinLobbyRequest(string id, ConnectionMode listedMode)
    {
        try
        {
            var response = Http("POST", "/v1/lobbies/" + id + "/join",
                JsonUtility.ToJson(new JoinLobbyPayload { playerName = playerName, modVersion = PluginVersion }), null);
            var lobbyId = JsonString(response, "id");
            var relayKey = JsonString(response, "relayKey");
            var relayAddress = JsonString(response, "relayAddress");
            var peerId = (ushort)Mathf.Clamp(JsonInt(response, "peerId"), 2, 16);
            var hostPeerId = (ushort)Mathf.Clamp(JsonInt(response, "hostPeerId"), 1, 16);
            var maxPlayers = Mathf.Clamp(JsonInt(response, "maxPlayers"), 2, 16);
            var modeText = JsonString(response, "connectionMode");
            var mode = string.IsNullOrEmpty(modeText) ? listedMode : ParseConnectionMode(modeText);
            if (string.IsNullOrEmpty(relayAddress)) relayAddress = DefaultRelayAddress();
            if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(relayKey)) throw new InvalidDataException("Invalid directory response.");
            RunOnMainThread(() =>
            {
                joinedLobbyId = lobbyId;
                ConnectRelay(relayAddress, lobbyId, relayKey, peerId, hostPeerId, maxPlayers, mode);
            });
        }
        catch (Exception exception)
        {
            RunOnMainThread(() =>
            {
                SetJoinInProgress(false);
                status = "Could not join lobby: " + GetDirectoryErrorMessage(exception.Message);            });
        }
    }

    private void SetJoinInProgress(bool value)
    {
        lock (joinLock) joinInProgress = value;
    }

    private static string GetDirectoryErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "unknown error";

        var jsonStart = message.IndexOf('{');

        if (jsonStart >= 0)
        {
            var json = message.Substring(jsonStart);
            var error = JsonString(json, "error");

            if (!string.IsNullOrWhiteSpace(error))
                return error;
        }

        return message;
    }
    
    private void RunOnMainThread(Action action) { lock (mainThreadActionsLock) mainThreadActions.Enqueue(action); }

    private void BanPlayerInDirectory(string lobbyId, string relayKey, string playerName, ushort expectedPeerId)
    {
        try
        {
            var body = JsonUtility.ToJson(new BanPlayerRequest { playerName = playerName, durationMinutes = 60 });
            var response = Http("POST", "/v1/lobbies/" + lobbyId + "/ban", body, "Bearer " + relayKey);
            var peerId = (ushort)Mathf.Clamp(JsonInt(response, "peerId"), 1, 65534);
            if (peerId == 0) peerId = expectedPeerId;
            var bannedPeerId = peerId;
            RunOnMainThread(() =>
            {
                MultiplayerSession.KickPeer(bannedPeerId, playerName + " was banned for 60 minutes.");
                status = playerName + " was banned for 60 minutes.";
            });
        }
        catch (Exception exception)
        {
            RunOnMainThread(() => status = "Could not ban " + playerName + ": " + exception.Message);
        }
    }

    private string Http(string method, string path, string body, string authorization)
    {
        return HttpAt(masterUrl.Value.TrimEnd('/'), method, path, body, authorization);
    }

    private string DefaultRelayAddress()
    {
        return "udp://expie.fun:27015";
    }

    private static string HttpAt(string server, string method, string path, string body, string authorization)
    {
        Uri uri;
        if (!Uri.TryCreate(server.TrimEnd('/') + path, UriKind.Absolute, out uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Lobby server must use an HTTP or HTTPS URL.");
        return DirectoryRequest(uri, method, body, authorization);
    }

    private static string DirectoryRequest(Uri uri, string method, string body, string authorization)
    {
        var request = (HttpWebRequest)WebRequest.Create(uri);
        request.Method = method;
        request.Accept = "application/json";
        request.ContentType = "application/json; charset=utf-8";
        request.UserAgent = "GunsawMultiplayer/" + PluginVersion;
        request.Timeout = 10000;
        request.ReadWriteTimeout = 10000;
        if (!string.IsNullOrEmpty(authorization)) request.Headers[HttpRequestHeader.Authorization] = authorization;
        if (body != null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
        }

        try
        {
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }
        catch (WebException exception)
        {
            var response = exception.Response as HttpWebResponse;
            if (response == null) throw new InvalidOperationException("Directory request failed: " + exception.Message, exception);
            using (response)
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                var responseBody = reader.ReadToEnd().Trim();
                var detail = string.IsNullOrEmpty(responseBody) ? response.StatusDescription : responseBody;
                throw new InvalidOperationException("Directory request failed (HTTP " +
                    (int)response.StatusCode + "): " + detail, exception);
            }
        }
    }

    private static string ReleaseRequest(string address)
    {
        Uri uri;
        if (!Uri.TryCreate(address, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Invalid GitHub releases URL.");
        return DirectoryRequest(uri, "GET", null, null);
    }

    private static int CompareVersions(string local, string remote)
    {
        var localParts = ParseVersion(local, out var localSuffix);
        var remoteParts = ParseVersion(remote, out var remoteSuffix);
        var count = Math.Max(localParts.Length, remoteParts.Length);
        for (var index = 0; index < count; index++)
        {
            var left = index < localParts.Length ? localParts[index] : 0;
            var right = index < remoteParts.Length ? remoteParts[index] : 0;
            if (left != right) return left.CompareTo(right);
        }
        if (string.Equals(localSuffix, remoteSuffix, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.IsNullOrEmpty(localSuffix)) return -1;
        if (string.IsNullOrEmpty(remoteSuffix)) return 1;
        return string.Compare(localSuffix, remoteSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static int[] ParseVersion(string value, out string suffix)
    {
        value = (value ?? "").Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1).TrimStart();
        var end = 0;
        while (end < value.Length && (char.IsDigit(value[end]) || value[end] == '.')) end++;
        suffix = value.Substring(end).Trim();
        var numeric = value.Substring(0, end).Split('.');
        var parts = new List<int>();
        foreach (var item in numeric)
        {
            int part;
            if (int.TryParse(item, out part)) parts.Add(part);
        }
        return parts.ToArray();
    }

    private static string JsonString(string json, string name)
    {
        var marker = "\"" + name + "\":\"";
        var start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "";
        start += marker.Length;
        var end = json.IndexOf('"', start);
        return end < 0 ? "" : json.Substring(start, end - start);
    }

    private static ConnectionMode ParseConnectionMode(string value)
    {
        ConnectionMode mode;
        return Enum.TryParse(value, true, out mode) ? mode : ConnectionMode.Relay;
    }

    private static List<LobbyInfo> ParseAndSortLobbies(string json)
    {
        var result = new List<LobbyInfo>();
        var cursor = 0;
        while (true)
        {
            var start = json.IndexOf("{\"id\":", cursor, StringComparison.Ordinal);
            if (start < 0) break;
            var end = json.IndexOf('}', start);
            if (end < 0) break;
            var item = json.Substring(start, end - start + 1);
            var lobby = new LobbyInfo();
            lobby.id = JsonString(item, "id");
            lobby.name = JsonString(item, "name");
            lobby.hostName = JsonString(item, "hostName");
            lobby.map = JsonString(item, "map");
            lobby.players = JsonInt(item, "players");
            lobby.maxPlayers = JsonInt(item, "maxPlayers");
            lobby.pvp = JsonBool(item, "pvp");
            lobby.canGrab = JsonBool(item, "canGrab");
            lobby.grabOnlyUnconscious = JsonBool(item, "grabOnlyUnconscious");
            lobby.allowRespawn = JsonBool(item, "allowRespawn");
            lobby.respawnTime = JsonInt(item, "respawnTime");
            lobby.numberOfLives = JsonInt(item, "numberOfLives");
            lobby.respawnAtStart = JsonBool(item, "respawnAtStart");
            lobby.playerCollisions = JsonBool(item, "playerCollisions");
            lobby.cheats = JsonBool(item, "cheats");
            lobby.allowSwap = !item.Contains("\"allowSwap\":false");
            lobby.hostP2P = JsonBool(item, "HostP2P") || JsonBool(item, "hostP2P");
            lobby.connectionMode = ParseConnectionMode(JsonString(item, "connectionMode"));
            if (!string.IsNullOrEmpty(lobby.id)) result.Add(lobby);
            cursor = end + 1;
        }
        result.Sort((x, y) => x.name.CompareTo(y.name));
        return result;
    }

    private static int JsonInt(string json, string name)
    {
        var marker = "\"" + name + "\":";
        var start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return 0;
        start += marker.Length;
        var end = start;
        while (end < json.Length && char.IsDigit(json[end])) end++;
        int value;
        return int.TryParse(json.Substring(start, end - start), out value) ? value : 0;
    }

    private float ParseInitialScale()
    {
        float scale;
        if (!float.TryParse(createInitialScale, NumberStyles.Float, CultureInfo.InvariantCulture, out scale)) scale = 1f;
        scale = AvatarScaleHandler.Clamp(scale);
        createInitialScale = scale.ToString("0.##", CultureInfo.InvariantCulture);
        return scale;
    }

    private static bool JsonBool(string json, string name)
    {
        var marker = "\"" + name + "\":";
        var start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return false;
        start += marker.Length;
        return json.Substring(start).StartsWith("true", StringComparison.OrdinalIgnoreCase);
    }

    private void SendHeartbeat()
    {
        var scene = SceneManager.GetActiveScene().name;
        var players = MultiplayerSession.PlayerCount;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { Http("PUT", "/v1/lobbies/" + hostedLobbyId, "{\"players\":" + players + ",\"map\":\"" + EscapeJson(scene) + "\"}", "Bearer " + hostRelayKey); }
            catch (Exception exception) { Logger.LogWarning("Lobby heartbeat failed: " + exception.Message); }
        });
    }

    private void RemoveHostedPeer(ushort peerId)
    {
        var lobbyId = hostedLobbyId;
        var relayKey = hostRelayKey;
        if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(relayKey) || peerId == 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { Http("DELETE", "/v1/lobbies/" + lobbyId + "/peers/" + peerId, null, "Bearer " + relayKey); }
            catch (Exception exception) { Logger.LogWarning("Lobby peer removal failed: " + exception.Message); }
        });
    }

    private void UpdateHostedLobbyInDirectory()
    {
        try
        {
            var body = JsonUtility.ToJson(new CreateLobbyRequest
            {
                name = lobbyName,
                hostName = playerName,
                map = SceneManager.GetActiveScene().name,
                players = MultiplayerSession.PlayerCount,
                maxPlayers = MultiplayerSession.MaxPlayers,
                hostPort = 27016,
                pvp = createPvp,
                canGrab = createCanGrab,
                grabOnlyUnconscious = createCanGrab && createGrabOnlyUnconscious,
                allowRespawn = createAllowRespawn,
                respawnTime = MultiplayerSession.RespawnTimeSeconds,
                numberOfLives = MultiplayerSession.NumberOfLives,
                respawnAtStart = createAllowRespawn && createRespawnAtStart,
                playerCollisions = createPlayerCollisions,
                cheats = createCheats,
                allowSwap = createAllowSwap,
                allowScaleChanging = createAllowScaleChanging,
                initialScale = ParseInitialScale(),
                startingWeapon = createStartingWeapon,
                respawnWeapon = createRespawnWeapon,
                startingAmmo = createStartingAmmo,
                respawnAmmo = createRespawnAmmo,
                allowObserver = createAllowObserver,
                teams = createTeams, teamsCfg = createTeamsCfg,
                brutalMode = MultiplayerSession.ReadBrutalMode(),
                hostP2P = createConnectionMode != ConnectionMode.Relay,
                connectionMode = createConnectionMode.ToString(),
                modVersion = PluginVersion
            });
            Http("PUT", "/v1/lobbies/" + hostedLobbyId, body, "Bearer " + hostRelayKey);
        }
        catch (Exception exception) { Logger.LogWarning("Could not update hosted lobby: " + exception.Message); }
    }

    private void DeleteHostedLobby(string lobbyId, string relayKey)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { Http("DELETE", "/v1/lobbies/" + lobbyId, null, "Bearer " + relayKey); }
            catch (Exception exception) { Logger.LogWarning("Could not remove hosted lobby: " + exception.Message); }
        });
    }

    private void OnApplicationQuit()
    {
        ShutdownMultiplayer(true);
    }

    private void OnDestroy()
    {
        ShutdownMultiplayer(false);
    }

    private void ShutdownMultiplayer(bool removeHostedLobby)
    {
        if (headlessKeepAliveTimer != null)
        {
            headlessKeepAliveTimer.Dispose();
            headlessKeepAliveTimer = null;
        }
        if (shuttingDown) return;
        shuttingDown = true;
        MultiplayerSession.Shutdown();
        if (!removeHostedLobby || string.IsNullOrEmpty(hostedLobbyId) || string.IsNullOrEmpty(hostRelayKey)) return;
        var lobbyId = hostedLobbyId;
        var relayKey = hostRelayKey;
        DeleteHostedLobby(lobbyId, relayKey);
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static bool TryNormalizeServerAddress(string value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        var candidate = value.Trim();
        if (!candidate.Contains("://")) candidate = "https://" + candidate;
        Uri uri;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host)) return false;
        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps };
            if (uri.IsDefaultPort) builder.Port = -1;
            uri = builder.Uri;
        }
        if (uri.Host.IndexOf("e621.su", StringComparison.OrdinalIgnoreCase) >= 0)
            uri = new UriBuilder(uri) { Host = "expie.fun" }.Uri;
        normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }

    private static string DisplayServerAddress(string value)
    {
        Uri uri;
        if (Uri.TryCreate(value, UriKind.Absolute, out uri))
            return uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port;
        return value;
    }

    private void SpawnNamedWeapon(string weaponName, string fallbackName, string alternateName = "")
    {
        var player = PlayerScript.player;
        if (player == null || player.bodyScript == null) return;
        var presets = Resources.FindObjectsOfTypeAll<WeaponPreset>();
        WeaponPreset weapon = null;
        foreach (var preset in presets)
            if (preset != null && preset.sprite != null &&
                (string.Equals(preset.name, weaponName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(preset.name, alternateName, StringComparison.OrdinalIgnoreCase)))
            {
                weapon = preset;
                break;
            }
        if (weapon == null)
            foreach (var preset in presets)
                if (preset != null && preset.sprite != null && preset.shootType == 1 &&
                    !string.IsNullOrEmpty(preset.name) &&
                    preset.name.IndexOf(fallbackName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    weapon = preset;
                    break;
                }
        if (weapon == null) { status = weaponName + " preset not found."; return; }
        var prefab = Resources.Load<GameObject>("Spawnables/PickupWeapon");
        if (prefab == null) { status = "Pickup weapon prefab not found."; return; }
        var position = player.bodyScript.transform.position + new Vector3(0f, 2f, 0f);
        var pickup = Instantiate(prefab, position, Quaternion.identity).GetComponent<DroppedWeapon>();
        if (pickup == null) { status = "Pickup component not found."; return; }
        pickup.ChangeWeapon(weapon, weapon.magSize);
        WorldReplication.Instance.weapons.RegisterDroppedWeapon(pickup);
        status = "Spawned " + weapon.name + ".";
    }

    [Serializable]
    internal sealed class ServerInfo
    {
        public string address = "";
        public string location = "";
        [NonSerialized] public int pingMs = -1;
    }

    [Serializable]
    internal sealed class LobbyInfo
    {
        public string id = "";
        public string name = "";
        public string hostName = "";
        public string map = "";
        public int players;
        public int maxPlayers;
        public bool pvp;
        public bool canGrab;
        public bool grabOnlyUnconscious;
        public bool allowRespawn;
        public int respawnTime;
        public int numberOfLives;
        public bool respawnAtStart;
        public bool playerCollisions = true;
        public bool cheats;
        public bool allowSwap = true;
        public bool allowScaleChanging = true;
        public bool allowObserver = true;
        public bool teams;
        public string teamsCfg = "";
        public float initialScale = 1f;
        public string startingWeapon = "Default";
        public string respawnWeapon = "Default";
        public string startingAmmo = LobbyAmmoRules.StartingDefault;
        public string respawnAmmo = LobbyAmmoRules.RespawnDefault;
        public bool hostP2P;
        public ConnectionMode connectionMode = ConnectionMode.Relay;
    }

    [Serializable]
    private sealed class JoinLobbyPayload
    {
        public string playerName = "";
        public string modVersion = "";
    }

    [Serializable]
    private sealed class BanPlayerRequest
    {
        public string playerName = "";
        public int durationMinutes;
    }

    [Serializable]
    private sealed class CreateLobbyRequest
    {
        public string name = "";
        public string hostName = "";
        public string map = "";
        public int players;
        public int maxPlayers;
        public int hostPort;
        public bool pvp;
        public bool canGrab;
        public bool grabOnlyUnconscious;
        public bool allowRespawn;
        public int respawnTime;
        public int numberOfLives;
        public bool respawnAtStart;
        public bool playerCollisions = true;
        public bool cheats;
        public bool brutalMode;
        public bool allowSwap = true;
        public bool allowScaleChanging = true;
        public bool allowObserver = true;
        public bool teams;
        public string teamsCfg = "";
        public float initialScale = 1f;
        public string startingWeapon = "Default";
        public string respawnWeapon = "Default";
        public string startingAmmo = LobbyAmmoRules.StartingDefault;
        public string respawnAmmo = LobbyAmmoRules.RespawnDefault;
        public bool hostP2P;
        public string connectionMode = "Relay";
        public string modVersion = "";
    }
}
