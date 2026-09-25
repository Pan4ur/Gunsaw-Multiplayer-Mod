using BepInEx;
using BepInEx.Configuration;
using DiscordIPC.Internal;
using HarmonyLib;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

[BepInPlugin(PluginGuid, PluginName, PluginMetadataVersion)]
public sealed class GunsawMultiplayerPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.gunsaw.multiplayer";
    public const string PluginName = "Gunsaw Multiplayer";
    public const string PluginVersion = "0.4.9HF";
    private const string PluginMetadataVersion = "0.4.9.1"; // TODO remove on 0.5.0
    private const string ReleasesApiUrl = "https://api.github.com/repos/Pan4ur/Gunsaw-Multiplayer-Mod/releases/latest";
    internal const string CustomLevelsUrl = "https://github.com/jimmyking9999999/gunsaw-level-editor-plus/raw/refs/heads/main/Levels.json";
    private const string ServersUrl = "https://raw.githubusercontent.com/Pan4ur/Gunsaw-Multiplayer-Mod/main/Assets/servers.json";
    private const string DefaultRelayServer = "udp://expie.fun:27015";

    internal static GunsawMultiplayerPlugin Instance { get; private set; }

    internal readonly List<LobbyInfo> lobbies = [];
    internal readonly List<ServerInfo> servers = [];
    internal bool customLevelCatalogReady;
    internal string customLevelCatalogError = "";
    internal bool serverListLoading;
    internal string serverListError = "";
    internal ConfigEntry<string> masterUrl;
    private ConfigEntry<string> savedPlayerName;
    private ConfigEntry<string> savedLobbyName;
    private readonly List<Func<bool>> lobbySettingSavers = [];
    private readonly List<LobbyPreset> lobbyPresets = [];
    internal bool visible;
    internal string status = "Select an option.";
    internal string updateStatus = "Checking for updates..."; 
    internal string lobbyServerAddress = "expie.fun";
    internal string lobbyName = "Lobby";
    internal string playerName = "Player";
    internal LobbySettings lobbySettings = new();
    internal string customLevelJson = "";
    internal string customLevelCode = "";
    private string receivedCustomLevelJson = "";
    private int receivedCustomLevelTransferId;
    private bool waitingForCustomLevel;
    private int waitingForCustomLevelTransferId;
    private string requestedHostScene = "";
    private float customLevelPhysicsRefreshUntil;
    private float nextCustomLevelPhysicsRefresh;
    private LocalPlayerReplication avatarReplication;
    private WorldReplication worldReplication;
    private NpcReplication npcReplication;
    private MultiplayerHud multiplayerHud;
    private ChatCommandSystem _chatCommandSystem;
    private MultiplayerLobbyUi multiplayerLobbyUi;
    private MultiplayerReplicationDebugMode replicationDebugMode;
    private HeadlessLobbyService headlessLobbyService;
    private string hostedLobbyId = "";
    private string hostedLobbyDisplayName = "";
    private string editedLocalLevelCode = "";
    private string editedLocalLevelName = "";
    private string hostRelayKey = "";
    private float nextHeartbeat;
    private int lastHostedPeerListRevision = -1;
    private bool shuttingDown;
    private bool joinInProgress;
    private string joinedLobbyId = "";
    private int updateCheckInProgress;
    private readonly object joinLock = new ();
    private readonly Queue<Action> mainThreadActions = new ();
    private readonly object mainThreadActionsLock = new ();

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
        lobbySettingSavers.AddRange(LobbySettingsSchema.BindConfig(Config, () => lobbySettings));
        playerName = savedPlayerName.Value;
        lobbyName = savedLobbyName.Value;
        LoadLobbyPresets();
        headlessLobbyService = new HeadlessLobbyService(this);
        GraffitiSystem.Initialize(headlessLobbyService.IsEnabled);
        headlessLobbyService.Initialize();
        new Harmony(PluginGuid).PatchAll();
        avatarReplication = gameObject.AddComponent<LocalPlayerReplication>();
        gameObject.AddComponent<NetworkAvatarManager>();
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
        headlessLobbyService.Start();
    }

    internal static void LogInfo(string m)
    {
        Instance?.Logger.LogInfo(m);
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
        if (headlessLobbyService != null && headlessLobbyService.KeepRunning()) return;
        
        MultiplayerTimeControl.KeepMultiplayerActive();
    }

    internal static WorldReplication World;

    private void Update()
    {
        KeepMultiplayerRunningInBackground();
        if (headlessLobbyService.UpdateEarly()) return;
        lock (mainThreadActionsLock)
            while (mainThreadActions.Count > 0) mainThreadActions.Dequeue()();
        MultiplayerSession.UpdateConnection();
        ushort suggestingPeer;
        CustomLevelSuggestionPacket suggestion;
        while (MultiplayerSession.TryTakeCustomLevelSuggestion(out suggestingPeer, out suggestion))
            multiplayerLobbyUi?.ShowCustomLevelSuggestion(MultiplayerSession.PlayerName(suggestingPeer), suggestion);
        TeamSystem.Tick();
        ScoreboardSystem.Tick();
        GunGameRule.Tick();
        AutoRestartSystem.Tick(lobbySettings.AutoRestart);
        MultiplayerSession.SyncBrutalMode();
        BlackoutRule.Tick();
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
        headlessLobbyService.UpdateLobby();
        if (Time.unscaledTime < customLevelPhysicsRefreshUntil &&
            Time.unscaledTime >= nextCustomLevelPhysicsRefresh)
        {
            nextCustomLevelPhysicsRefresh = Time.unscaledTime + 0.25f;
            NetworkAvatarManager.ForceRefreshRemotePhysics();
        }
        var sessionName = MultiplayerSession.IsHosting || MultiplayerSession.IsConnected ? MultiplayerSession.LocalPlayerName : playerName;
        multiplayerHud.Configure(sessionName, MultiplayerSession.IsHosting ? hostedLobbyDisplayName : lobbyName, visible);
        multiplayerLobbyUi.Configure(this);

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
            try
            {
                var levelJson = CustomLevelCode.DecodeAndValidateLevelCode(incomingCustomLevel, 8 * 1024 * 1024);
                RPCManager.CheckInstance();
                RPCManager.instance?.UpdateCustomLevel(incomingCustomLevel);
                CustomLevelProgress.SetActive(incomingCustomLevel);
                receivedCustomLevelJson = levelJson;
                receivedCustomLevelTransferId = incomingCustomLevelTransferId;
                if (waitingForCustomLevel && (waitingForCustomLevelTransferId == 0 ||
                    waitingForCustomLevelTransferId == incomingCustomLevelTransferId))
                {
                    waitingForCustomLevel = false;
                    waitingForCustomLevelTransferId = 0;
                    StartCustomLevelLocally(receivedCustomLevelJson);
                }
            }
            catch (Exception e) { status = "Could not load custom level from host: " + e.Message; }
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
            LocalPlayerReplication.ResetExhaustedLives();
            ObserverSystem.ResetForLevelChange(mustReload);
            if (sceneToLoad == "LevelLoader")
            {
                if (sceneCustomLevelTransferId != 0 && receivedCustomLevelTransferId != sceneCustomLevelTransferId)
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
            status = mustReload ? "Host restarted the level. Reloading..." : "Loading host scene " + sceneToLoad + "...";
            SceneManager.LoadScene(sceneToLoad);
        }

        if (MultiplayerHud.IsTyping || (multiplayerHud != null && multiplayerHud.ChatOpen)) return;
        if (Input.GetKeyDown(Controls.keys[Controls.PAIN_SOUND]) && !ArsenalMenu.ConsumesWhineKey(Controls.keys[Controls.PAIN_SOUND]))
        {
            var body = PlayerScript.player?.bodyScript;
            if (body != null)
            {
                body.screamTime = -1;
                body.DoGrunt();
            }
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
        var changed = LobbySettingsSchema.SetIfChanged(savedPlayerName, playerName);
        changed |= LobbySettingsSchema.SetIfChanged(savedLobbyName, lobbyName);
        foreach (var save in lobbySettingSavers) changed |= save();
        if (changed) Config.Save();
    }

    internal IReadOnlyList<LobbyPreset> LobbyPresets => lobbyPresets;

    internal void SaveLobbyPreset(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new InvalidOperationException("Preset name is empty.");
        if (name.Length > 32) name = name.Substring(0, 32);
        var preset = CreateLobbyPreset(name);
        var index = lobbyPresets.FindIndex(item => string.Equals(item.name, name, StringComparison.OrdinalIgnoreCase));
        var updated = new List<LobbyPreset>(lobbyPresets);
        if (index >= 0) updated[index] = preset;
        else updated.Add(preset);
        LobbyPresetStore.Save(updated);
        lobbyPresets.Clear();
        lobbyPresets.AddRange(updated);
    }

    internal void ApplyLobbyPreset(LobbyPreset preset)
    {
        if (preset?.settings == null) return;
        lobbySettings = preset.settings.Clone();
        SaveLobbyPreferences();
    }

    internal void DeleteLobbyPreset(LobbyPreset preset)
    {
        if (preset == null || !lobbyPresets.Contains(preset)) return;
        var updated = new List<LobbyPreset>(lobbyPresets);
        updated.Remove(preset);
        LobbyPresetStore.Save(updated);
        lobbyPresets.Clear();
        lobbyPresets.AddRange(updated);
    }

    private LobbyPreset CreateLobbyPreset(string name)
    {
        return new LobbyPreset { name = name, settings = lobbySettings.Clone() };
    }

    private void LoadLobbyPresets()
    {
        lobbyPresets.Clear();
        try { lobbyPresets.AddRange(LobbyPresetStore.Load()); }
        catch (Exception e) { Logger.LogInfo("Could not load lobby presets: " + e.Message); }
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
                return reply != null && reply.Status == System.Net.NetworkInformation.IPStatus.Success && reply.RoundtripTime <= int.MaxValue ? (int)reply.RoundtripTime : -1;
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
            var levelJson = CustomLevelCode.DecodeAndValidateLevelCode(clipboard, 8 * 1024 * 1024);
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
        catch (Exception e) { status = "Could not start custom level: " + e.Message; }
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
        var hosting = MultiplayerSession.IsHosting;
        try
        {
            var levelJson = CustomLevelCode.DecodeAndValidateCatalogLevelCode(code, hosting ? 4 * 1024 * 1024 : int.MaxValue);
            if (!hosting)
            {
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
                return;
            }
            CustomLevelProgress.SetActive(code);
            customLevelJson = levelJson;
            customLevelCode = code;
            status = "Starting custom level: " + levelName;
            StartCustomLevel();
        }
        catch (Exception e) { status = (hosting ? "Could not load custom level: " : "Could not suggest custom level: ") + e.Message; }
    }

    internal void OpenCustomLevelEditor(string code, string levelName)
    {
        try
        {
            var levelJson = CustomLevelCode.DecodeAndValidateLevelCode(code);
            var loader = SceneLoader.main;
            if (loader == null)
                throw new InvalidOperationException("Scene loader is not ready.");
            loader.levelEditString = levelJson;
            editedLocalLevelCode = code;
            editedLocalLevelName = levelName ?? "Untitled";
            status = "Opening level editor: " + levelName;
            loader.LoadScene("LevelEditor");
        }
        catch (Exception exception)
        {
            status = "Could not open level editor: " + exception.Message;
        }
    }

    internal bool HasLocalLevelEditorTarget => !string.IsNullOrWhiteSpace(editedLocalLevelCode);

    internal void SaveEditedLocalLevel(LevelEditor editor)
    {
        if (editor == null || !HasLocalLevelEditorTarget) return;
        try
        {
            var replacementCode = Compression.Compress(editor.GetLevelCode());
            if (multiplayerLobbyUi == null || !multiplayerLobbyUi.SaveEditedLocalLevel(editedLocalLevelCode,editedLocalLevelName, replacementCode))
                throw new InvalidOperationException("The original local level was not found.");
            editedLocalLevelCode = replacementCode;
            editor.SetInfoText("Saved changes to " + editedLocalLevelName + ".");
        }
        catch (Exception exception)
        {
            editor.SetInfoText("Could not save changes: " + exception.Message);
        }
    }

    internal void EndLocalLevelEditing()
    {
        editedLocalLevelCode = "";
        editedLocalLevelName = "";
    }

    internal void StartCustomLevelLocally(string levelJson)
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
            var parsed = lobbySettings.Parse(4);
            var settings = lobbySettings.Clone();
            var requestedLobbyName = lobbyName;
            var requestedPlayerName = playerName;
            var body = JsonUtility.ToJson(BuildLobbyRequest(settings, parsed, requestedLobbyName, requestedPlayerName, "Host chooses level", 0, false));
            ThreadPool.QueueUserWorkItem(_ => CreateLobbyInDirectory(body, settings, parsed, requestedLobbyName, requestedPlayerName));
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
        var parsed = lobbySettings.Parse(MultiplayerSession.MaxPlayers);
        if (!MultiplayerSession.UpdateHostSettings(lobbySettings, parsed))
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
                if (lobby.id == id)
                {
                    mode = lobby.connectionMode;
                    break;
                }
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
                result = comparison < 0 ? "UPDATE AVAILABLE: " + tag : comparison > 0 ? "INSTALLED BUILD IS NEWER THAN (HOW??)" + tag : "YOU ARE UP TO DATE";
            }
            catch (Exception exception)
            {
                result = "UPDATE CHECK FAILED: " + exception.Message;
                Logger.LogInfo(result);
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

    internal bool CanBanPlayers => MultiplayerSession.IsHosting && !string.IsNullOrEmpty(hostedLobbyId) && !string.IsNullOrEmpty(hostRelayKey);

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
            mode, out error))
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

    private void CreateLobbyInDirectory(string body, LobbySettings settings, ParsedLobbySettings parsed, string requestedLobbyName, string requestedPlayerName)
    {
        try
        {
            var response = Http("POST", "/v1/lobbies", body, null);
            var lobbyId = JsonString(response, "id");
            var relayKey = JsonString(response, "hostRelayKey");
            var relayAddress = JsonString(response, "relayAddress");
            var hostPeerId = (ushort)Mathf.Clamp(JsonInt(response, "hostPeerId"), 1, 64);
            if (string.IsNullOrEmpty(relayAddress)) relayAddress = DefaultRelayServer;
            if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(relayKey)) throw new InvalidDataException("Invalid directory response.");
            RunOnMainThread(() =>
            {
                MultiplayerSession.StartHost(lobbyId, relayKey, relayAddress, settings, parsed, requestedPlayerName, hostPeerId);
                avatarReplication.Configure(requestedPlayerName); multiplayerHud.ResetChat(); hostedLobbyId = lobbyId; hostedLobbyDisplayName = requestedLobbyName; hostRelayKey = relayKey; nextHeartbeat = Time.unscaledTime + 10f; status = "Lobby created, start a level.";
                headlessLobbyService.OnLobbyCreated(lobbyId, relayKey, masterUrl.Value.TrimEnd('/'));
            });
        }
        catch (Exception exception) { RunOnMainThread(() => status = "Could not create lobby: " + exception.Message); }
    }

    private void FixedUpdate()
    {
        PlayerCarrySystem.FixedTick();
        headlessLobbyService.FixedTick();
    }

    public string GetCurrentLobbyId()
    {
        if (string.IsNullOrEmpty(joinedLobbyId))
            return hostedLobbyId;

        return joinedLobbyId;
    }

    private void JoinLobbyRequest(string id, ConnectionMode listedMode)
    {
        try
        {
            var response = Http("POST", "/v1/lobbies/" + id + "/join", JsonUtility.ToJson(new JoinLobbyPayload { playerName = playerName, modVersion = PluginVersion }), null);
            var lobbyId = JsonString(response, "id");
            var relayKey = JsonString(response, "relayKey");
            var relayAddress = JsonString(response, "relayAddress");
            var peerId = (ushort)Mathf.Clamp(JsonInt(response, "peerId"), 2, 64);
            var hostPeerId = (ushort)Mathf.Clamp(JsonInt(response, "hostPeerId"), 1, 64);
            var maxPlayers = Mathf.Clamp(JsonInt(response, "maxPlayers"), 2, 64);
            var modeText = JsonString(response, "connectionMode");
            var mode = string.IsNullOrEmpty(modeText) ? listedMode : ParseConnectionMode(modeText);
            if (string.IsNullOrEmpty(relayAddress)) relayAddress = DefaultRelayServer;
            if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(relayKey)) 
                throw new InvalidDataException("Invalid directory response.");
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
                status = "Could not join lobby: " + GetDirectoryErrorMessage(exception.Message);
            });
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
    
    internal void RunOnMainThread(Action action) { lock (mainThreadActionsLock) mainThreadActions.Enqueue(action); }

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
    
    internal static string HttpAt(string server, string method, string path, string body, string authorization)
    {
        Uri uri;
        if (!Uri.TryCreate(server.TrimEnd('/') + path, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
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
        catch (WebException e)
        {
            var response = e.Response as HttpWebResponse;
            if (response == null) throw new InvalidOperationException("Directory request failed: " + e.Message, e);
            using (response)
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                var responseBody = reader.ReadToEnd().Trim();
                var detail = string.IsNullOrEmpty(responseBody) ? response.StatusDescription : responseBody;
                throw new InvalidOperationException("Directory request failed (HTTP " + (int) response.StatusCode + "): " + detail, e);
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
        return JsonString(MiniJson.Deserialize(json) as Dictionary<string, object>, name);
    }

    private static ConnectionMode ParseConnectionMode(string value)
    {
        ConnectionMode mode;
        return Enum.TryParse(value, true, out mode) ? mode : ConnectionMode.Relay;
    }

    private static List<LobbyInfo> ParseAndSortLobbies(string json)
    {
        var result = new List<LobbyInfo>();
        var entries = MiniJson.Deserialize(json) as List<object>;
        if (entries == null)
        {
            var response = MiniJson.Deserialize(json) as Dictionary<string, object>;
            object value = null;
            if (response != null) response.TryGetValue("lobbies", out value);
            entries = value as List<object>;
        }
        if (entries == null) return result;

        foreach (var entry in entries)
        {
            var item = entry as Dictionary<string, object>;
            if (item == null) continue;
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
            lobby.allowSwap = JsonBool(item, "allowSwap", true);
            lobby.hostP2P = JsonBool(item, "HostP2P") || JsonBool(item, "hostP2P");
            lobby.connectionMode = ParseConnectionMode(JsonString(item, "connectionMode"));
            if (!string.IsNullOrEmpty(lobby.id)) result.Add(lobby);
        }
        result.Sort((x, y) => x.name.CompareTo(y.name));
        return result;
    }

    private static int JsonInt(string json, string name)
    {
        return JsonInt(MiniJson.Deserialize(json) as Dictionary<string, object>, name);
    }

    private static bool JsonBool(string json, string name)
    {
        return JsonBool(MiniJson.Deserialize(json) as Dictionary<string, object>, name);
    }

    private static string JsonString(Dictionary<string, object> values, string name)
    {
        return values != null && values.TryGetValue(name, out var value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";
    }

    private static int JsonInt(Dictionary<string, object> values, string name)
    {
        if (values == null || !values.TryGetValue(name, out var value) || value == null) return 0;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch (Exception) { return 0; }
    }

    private static bool JsonBool(Dictionary<string, object> values, string name, bool defaultValue = false)
    {
        if (values == null || !values.TryGetValue(name, out var value) || value == null) return defaultValue;
        if (value is bool result) return result;
        return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out result) ? result : defaultValue;
    }

    private void SendHeartbeat()
    {
        var scene = SceneManager.GetActiveScene().name;
        var players = MultiplayerSession.PlayerCount;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { Http("PUT", "/v1/lobbies/" + hostedLobbyId, "{\"players\":" + players + ",\"map\":\"" + EscapeJson(scene) + "\"}", "Bearer " + hostRelayKey); }
            catch (Exception exception) { Logger.LogInfo("Lobby heartbeat failed: " + exception.Message); }
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
            catch (Exception exception) { Logger.LogInfo("Lobby peer removal failed: " + exception.Message); }
        });
    }

    private static CreateLobbyRequest BuildLobbyRequest(LobbySettings settings, ParsedLobbySettings parsed, string requestedLobbyName, string requestedPlayerName, string map, int players, bool live)
    {
        return new CreateLobbyRequest
        {
            name = requestedLobbyName,
            hostName = requestedPlayerName,
            map = map,
            players = players,
            maxPlayers = live ? MultiplayerSession.MaxPlayers : parsed.MaxPlayers,
            hostPort = 27016,
            pvp = settings.Pvp,
            gunGame = live ? MultiplayerSession.GunGameEnabled : settings.GunGame,
            ggSequence = live ? MultiplayerSession.GGSequence : settings.GGSequence,
            ggOnDeath = (live ? MultiplayerSession.GGOnDeath : settings.GGOnDeath).ToString(),
            canGrab = settings.CanGrab,
            grabOnlyUnconscious = live ? settings.CanGrab && settings.GrabOnlyUnconscious : settings.GrabOnlyUnconscious,
            allowRespawn = settings.AllowRespawn,
            respawnTime = live ? MultiplayerSession.RespawnTimeSeconds : parsed.RespawnTime,
            numberOfLives = live ? MultiplayerSession.NumberOfLives : parsed.NumberOfLives,
            healthFactor = live ? MultiplayerSession.HealthFactor : parsed.HealthFactor,
            regenFactor = live ? MultiplayerSession.RegenFactor : parsed.RegenFactor,
            blackout = live ? MultiplayerSession.BlackoutEnabled : settings.Blackout,
            restrictLight = live ? MultiplayerSession.RestrictLightEnabled : settings.RestrictLight,
            respawnAtStart = live ? settings.AllowRespawn && settings.RespawnAtStart : settings.RespawnAtStart,
            playerCollisions = settings.PlayerCollisions,
            cheats = settings.Cheats,
            allowSwap = settings.AllowSwap,
            allowScaleChanging = settings.AllowScaleChanging,
            initialScale = live ? MultiplayerSession.InitialScale : parsed.InitialScale,
            startingWeapon = settings.StartingWeapon,
            respawnWeapon = settings.RespawnWeapon,
            startingAmmo = settings.StartingAmmo,
            respawnAmmo = settings.RespawnAmmo,
            allowObserver = settings.AllowObserver,
            teams = settings.Teams,
            teamsCfg = settings.TeamsCfg,
            brutalMode = MultiplayerSession.ReadBrutalMode(),
            hostP2P = settings.ConnectionMode != ConnectionMode.Relay,
            connectionMode = settings.ConnectionMode.ToString(),
            modVersion = PluginVersion
        };
    }

    private void UpdateHostedLobbyInDirectory()
    {
        try
        {
            var body = JsonUtility.ToJson(BuildLobbyRequest(lobbySettings, default, lobbyName, playerName, SceneManager.GetActiveScene().name, MultiplayerSession.PlayerCount, true));
            Http("PUT", "/v1/lobbies/" + hostedLobbyId, body, "Bearer " + hostRelayKey);
        }
        catch (Exception e) { Logger.LogInfo("Could not update hosted lobby: " + e.Message); }
    }

    private void DeleteHostedLobby(string lobbyId, string relayKey)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { Http("DELETE", "/v1/lobbies/" + lobbyId, null, "Bearer " + relayKey); }
            catch (Exception e) { Logger.LogInfo("Could not remove hosted lobby: " + e.Message); }
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
        headlessLobbyService?.Dispose();
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

    internal static bool TryNormalizeServerAddress(string value, out string normalized)
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
            var builder = new UriBuilder(uri) { Scheme = uri.Scheme };
            if (uri.IsDefaultPort) builder.Port = -1;
            uri = builder.Uri;
        }
        if (uri.Host.IndexOf("e621.su", StringComparison.OrdinalIgnoreCase) >= 0)
            uri = new UriBuilder(uri) { Host = "expie.fun" }.Uri;
        normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }

    internal static string DisplayServerAddress(string value)
    {
        Uri uri;
        if (Uri.TryCreate(value, UriKind.Absolute, out uri))
        {
            var prefix = uri.Scheme == "https" ? "" : uri.Scheme + "://";
            var suffix = uri.IsDefaultPort ? "" : ":" + uri.Port;
            return $"{prefix}{uri.Host}{suffix}";
        }
        return value;
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
        public bool gunGame;
        public string ggSequence = "";
        public string ggOnDeath = "Reset";
        public bool canGrab;
        public bool grabOnlyUnconscious;
        public bool allowRespawn;
        public int respawnTime;
        public int numberOfLives;
        public float healthFactor = LobbyHealthRule.DefaultFactor;
        public float regenFactor = LobbyRegenRule.DefaultFactor;
        public bool blackout;
        public bool restrictLight;
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
        public bool gunGame;
        public string ggSequence = "";
        public string ggOnDeath = "Reset";
        public bool canGrab;
        public bool grabOnlyUnconscious;
        public bool allowRespawn;
        public int respawnTime;
        public int numberOfLives;
        public float healthFactor = LobbyHealthRule.DefaultFactor;
        public float regenFactor = LobbyRegenRule.DefaultFactor;
        public bool blackout;
        public bool restrictLight;
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