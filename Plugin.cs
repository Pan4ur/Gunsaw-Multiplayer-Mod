using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class GunsawMultiplayerPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.gunsaw.multiplayer";
    public const string PluginName = "Gunsaw Multiplayer";
    public const string PluginVersion = "0.4.0";
    internal static GunsawMultiplayerPlugin Instance { get; private set; }

    internal readonly List<LobbyInfo> lobbies = new List<LobbyInfo>();
    private ConfigEntry<string> masterUrl;
    private ConfigEntry<string> savedPlayerName;
    private ConfigEntry<string> savedLobbyName;
    private ConfigEntry<bool> savedCreatePvp;
    private ConfigEntry<bool> savedCreateCanGrab;
    private ConfigEntry<bool> savedCreateGrabOnlyUnconscious;
    private ConfigEntry<bool> savedCreateAllowRespawn;
    private ConfigEntry<bool> savedCreateRespawnAtStart;
    private ConfigEntry<string> savedCreateRespawnTime;
    private ConfigEntry<string> savedCreateMaxPlayers;
    internal bool visible;
    internal string status = "Select an option.";
    internal string lobbyServerAddress = "gunsawudp.e621.su";
    internal string lobbyName = "Lobby";
    internal string playerName = "Player";
    internal bool createPvp;
    internal bool createCanGrab = true;
    internal bool createGrabOnlyUnconscious = true;
    internal bool createAllowRespawn = true;
    internal bool createRespawnAtStart = true;
    internal string createRespawnTime = "5";
    internal string createMaxPlayers = "4";
    internal string customLevelJson = "";
    internal ConnectionMode createConnectionMode = ConnectionMode.Relay;
    private string receivedCustomLevelJson = "";
    private bool waitingForCustomLevel;
    private string requestedHostScene = "";
    private float customLevelPhysicsRefreshUntil;
    private float nextCustomLevelPhysicsRefresh;
    private Vector2 scroll;
    private NetworkAvatarReplication avatarReplication;
    private WorldReplication worldReplication;
    private NpcReplication npcReplication;
    private MultiplayerHud multiplayerHud;
    private MultiplayerLobbyUi multiplayerLobbyUi;
    private int debugWeaponSequence;
    private bool gameplayTypesLogged;
    private string hostedLobbyId = "";
    private string hostedLobbyDisplayName = "";
    private string hostRelayKey = "";
    private float nextHeartbeat;
    private bool shuttingDown;
    private bool joinInProgress;
    private readonly object joinLock = new object();
    private readonly Queue<Action> mainThreadActions = new Queue<Action>();
    private readonly object mainThreadActionsLock = new object();

    private void Awake()
    {
        KeepMultiplayerRunningInBackground();
        Instance = this;
        masterUrl = Config.Bind("Network", "MasterUrl", "https://gunsawudp.e621.su", "Lobby directory URL.");
        if (masterUrl.Value == "http://127.0.0.1:18080" ||
            masterUrl.Value == "http://gunsawudp.e621.su") masterUrl.Value = "https://gunsawudp.e621.su";
        lobbyServerAddress = DisplayServerAddress(masterUrl.Value);
        savedPlayerName = Config.Bind("Lobby", "PlayerName", playerName, "Name shown to other players.");
        savedLobbyName = Config.Bind("Lobby", "LobbyName", lobbyName, "Default name for new lobbies.");
        savedCreatePvp = Config.Bind("Lobby", "Pvp", createPvp, "Enable PvP in new lobbies.");
        savedCreateCanGrab = Config.Bind("Lobby", "CanGrab", createCanGrab, "Allow player grabbing in new lobbies.");
        savedCreateGrabOnlyUnconscious = Config.Bind("Lobby", "GrabOnlyUnconscious", createGrabOnlyUnconscious,
            "Limit grabbing to unconscious players in new lobbies.");
        savedCreateAllowRespawn = Config.Bind("Lobby", "AllowRespawn", createAllowRespawn,
            "Allow respawning in new lobbies.");
        savedCreateRespawnAtStart = Config.Bind("Lobby", "RespawnAtStart", createRespawnAtStart,
            "Respawn players at level start in new lobbies.");
        savedCreateRespawnTime = Config.Bind("Lobby", "RespawnTime", createRespawnTime,
            "Default respawn delay in seconds.");
        savedCreateMaxPlayers = Config.Bind("Lobby", "MaxPlayers", createMaxPlayers,
            "Default maximum player count.");
        playerName = savedPlayerName.Value;
        lobbyName = savedLobbyName.Value;
        createPvp = savedCreatePvp.Value;
        createCanGrab = savedCreateCanGrab.Value;
        createGrabOnlyUnconscious = savedCreateGrabOnlyUnconscious.Value;
        createAllowRespawn = savedCreateAllowRespawn.Value;
        createRespawnAtStart = savedCreateRespawnAtStart.Value;
        createRespawnTime = savedCreateRespawnTime.Value;
        createMaxPlayers = savedCreateMaxPlayers.Value;
        new Harmony(PluginGuid).PatchAll();
        avatarReplication = gameObject.AddComponent<NetworkAvatarReplication>();
        worldReplication = gameObject.AddComponent<WorldReplication>();
        npcReplication = gameObject.AddComponent<NpcReplication>();
        multiplayerHud = gameObject.AddComponent<MultiplayerHud>();
        multiplayerLobbyUi = gameObject.AddComponent<MultiplayerLobbyUi>();
        World = worldReplication;
        Logger.LogInfo("Gunsaw Multiplayer " + PluginVersion + " loaded.");
    }

    private void Start()
    {
        KeepMultiplayerRunningInBackground();
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
        if (MultiplayerSession.IsConnected && !Application.isFocused)
            MultiplayerTimeControl.KeepMultiplayerActive();
    }

    internal static WorldReplication World;

    private void Update()
    {
        KeepMultiplayerRunningInBackground();
        lock (mainThreadActionsLock)
            while (mainThreadActions.Count > 0) mainThreadActions.Dequeue()();
        MultiplayerSession.UpdateConnection();
        MultiplayerLoadDistance.Apply();
        MultiplayerSession.NoteHostSceneHandle(SceneManager.GetActiveScene().handle);
        MultiplayerSession.SetHostScene(SceneManager.GetActiveScene().name);
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

        if (!string.IsNullOrEmpty(hostedLobbyId) && Time.unscaledTime >= nextHeartbeat)
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
            status = "Host closed the lobby.";
            Time.timeScale = 1f;
            if (SceneManager.GetActiveScene().name != "LevelSelect")
                SceneManager.LoadScene("LevelSelect");
            return;
        }

        string incomingCustomLevel;
        if (MultiplayerSession.TryTakeCustomLevel(out incomingCustomLevel))
        {
            receivedCustomLevelJson = incomingCustomLevel;
            if (waitingForCustomLevel)
            {
                waitingForCustomLevel = false;
                StartCustomLevelLocally(receivedCustomLevelJson);
            }
        }

        string sceneToLoad;
        bool sceneReload, sceneEpochAdvanced;
        if (MultiplayerSession.TryTakeScene(out sceneToLoad, out sceneReload, out sceneEpochAdvanced))
        {
            var activeScene = SceneManager.GetActiveScene().name;
            var mustReload = sceneReload || (sceneEpochAdvanced && sceneToLoad == activeScene);
            if (!mustReload && (sceneToLoad == requestedHostScene || sceneToLoad == activeScene))
            {
                status = "Already in host scene " + sceneToLoad + ".";
                return;
            }
            requestedHostScene = sceneToLoad;
            if (sceneToLoad == "LevelLoader")
            {
                if (!string.IsNullOrEmpty(receivedCustomLevelJson))
                    StartCustomLevelLocally(receivedCustomLevelJson);
                else
                {
                    waitingForCustomLevel = true;
                    status = "Receiving custom level from host...";
                }
                return;
            }
            status = mustReload ? "Host restarted the level. Reloading..." :
                "Loading host scene " + sceneToLoad + "...";
            SceneManager.LoadScene(sceneToLoad);
        }

        CsExperienceMode.Tick();
        if (MultiplayerHud.IsTyping || (multiplayerHud != null && multiplayerHud.ChatOpen)) return;
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
        if (Input.GetKey(KeyCode.Space) && Input.GetKey(KeyCode.End) && Input.GetKeyDown(KeyCode.S))
        {
            multiplayerHud.ToggleNetworkStats();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Space)) debugWeaponSequence = 1;
        else if (debugWeaponSequence == 1 && Input.GetKeyDown(KeyCode.End)) debugWeaponSequence = 2;
        else if (debugWeaponSequence == 2 && Input.GetKeyDown(KeyCode.G))
        {
            debugWeaponSequence = 0;
            SpawnRandomWeapon();
        }
        else if (debugWeaponSequence == 2 && Input.GetKeyDown(KeyCode.Alpha1))
        {
            debugWeaponSequence = 0;
            SpawnGrenadeLauncher();
        }
        else if (debugWeaponSequence == 2 && Input.GetKeyDown(KeyCode.Alpha2))
        {
            debugWeaponSequence = 0;
            SpawnRocketLauncher();
        }
        else if (debugWeaponSequence == 2 && Input.GetKeyDown(KeyCode.Alpha3))
        {
            debugWeaponSequence = 0;
            SpawnSniperRifle();
        }
        else if (debugWeaponSequence == 2 && Input.GetKeyDown(KeyCode.Alpha4))
        {
            debugWeaponSequence = 0;
            SpawnMarksmanRifle();
        }
        else if (Input.anyKeyDown) debugWeaponSequence = 0;
    }

    internal void SaveLobbyPreferences()
    {
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
        if (savedCreateRespawnAtStart.Value != createRespawnAtStart)
        {
            savedCreateRespawnAtStart.Value = createRespawnAtStart;
            changed = true;
        }
        if (savedCreateRespawnTime.Value != createRespawnTime) { savedCreateRespawnTime.Value = createRespawnTime; changed = true; }
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
                var refreshed = ParseLobbies(response);
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
            var levelJson = clipboard.StartsWith("{", StringComparison.Ordinal)
                ? clipboard : Compression.Decompress(clipboard);
            var parsed = JsonUtility.FromJson<Level>(levelJson);
            if (parsed == null || string.IsNullOrWhiteSpace(levelJson))
                throw new InvalidDataException("The level JSON is invalid.");
            if (Encoding.UTF8.GetByteCount(levelJson) > 4 * 1024 * 1024)
                throw new InvalidDataException("The level is larger than 4 MB.");
            customLevelJson = levelJson;
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
            MultiplayerSession.StartHostCustomLevel(customLevelJson);
            StartCustomLevelLocally(customLevelJson);
        }
        catch (Exception exception) { status = "Could not start custom level: " + exception.Message; }
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
            int maxPlayers;
            if (!int.TryParse(createMaxPlayers, out maxPlayers)) maxPlayers = 4;
            maxPlayers = Mathf.Clamp(maxPlayers, 2, 16);
            createMaxPlayers = maxPlayers.ToString();
            var body = JsonUtility.ToJson(new CreateLobbyRequest { name = lobbyName, hostName = playerName,
                map = "Host chooses level", maxPlayers = maxPlayers, hostPort = 27016, pvp = createPvp,
                canGrab = createCanGrab, grabOnlyUnconscious = createGrabOnlyUnconscious,
                allowRespawn = createAllowRespawn, respawnTime = respawnTime,
                respawnAtStart = createRespawnAtStart,
                hostP2P = createConnectionMode != ConnectionMode.Relay,
                connectionMode = createConnectionMode.ToString() });
            ThreadPool.QueueUserWorkItem(_ => CreateLobbyInDirectory(body, respawnTime, maxPlayers));
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
        int maxPlayers;
        if (!int.TryParse(createMaxPlayers, out maxPlayers)) maxPlayers = MultiplayerSession.MaxPlayers;
        maxPlayers = Mathf.Clamp(maxPlayers, 2, 16);
        createMaxPlayers = maxPlayers.ToString();
        if (!MultiplayerSession.UpdateHostSettings(createPvp, createCanGrab, createGrabOnlyUnconscious,
            createAllowRespawn, respawnTime, createRespawnAtStart, maxPlayers))
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
        nextHeartbeat = 0f;
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

    internal bool CanJoinLobby
    {
        get
        {
            lock (joinLock)
                return !joinInProgress && !MultiplayerSession.IsHosting && !MultiplayerSession.IsActive;
        }
    }

    internal bool TryHandleHostCommand(string message)
    {
        if (!message.StartsWith("/ban", StringComparison.OrdinalIgnoreCase)) return false;
        if (!MultiplayerSession.IsHosting || string.IsNullOrEmpty(hostedLobbyId) || string.IsNullOrEmpty(hostRelayKey))
        {
            status = "Only the lobby host can use /ban.";
            return true;
        }
        var playerName = message.Length > 4 ? message.Substring(4).Trim() : "";
        if (string.IsNullOrEmpty(playerName))
        {
            status = "Usage: /ban <player name>";
            return true;
        }
        ushort peerId = 0;
        foreach (var id in MultiplayerSession.PeerIds())
            if (string.Equals(MultiplayerSession.PlayerName(id), playerName, StringComparison.OrdinalIgnoreCase))
            {
                peerId = id;
                break;
            }
        if (peerId == 0)
        {
            status = "Player " + playerName + " is not in the lobby.";
            return true;
        }
        var lobbyId = hostedLobbyId;
        var relayKey = hostRelayKey;
        status = "Banning " + playerName + "...";
        ThreadPool.QueueUserWorkItem(_ => BanPlayerInDirectory(lobbyId, relayKey, playerName, peerId));
        return true;
    }

    private void ConnectRelay(string address, string lobbyId, string relayKey, ushort peerId, ushort hostPeerId,
        int maxPlayers, ConnectionMode mode)
    {
        string error;
        if (!MultiplayerSession.Connect(address, lobbyId, relayKey, playerName, peerId, hostPeerId, maxPlayers,
            mode, Logger, out error))
        {
            SetJoinInProgress(false);
            status = error;
            return;
        }
        SetJoinInProgress(false);
        requestedHostScene = "";
        avatarReplication.Configure(playerName);
        multiplayerHud.ResetChat();
        status = "Connecting via " + mode + " through UDP relay " + address + "...";
    }

    private void CreateLobbyInDirectory(string body, int respawnTime, int maxPlayers)
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
                    createGrabOnlyUnconscious, createAllowRespawn, respawnTime, createRespawnAtStart,
                    playerName, hostPeerId, maxPlayers, createConnectionMode, Logger);
                avatarReplication.Configure(playerName); multiplayerHud.ResetChat(); hostedLobbyId = lobbyId; hostedLobbyDisplayName = lobbyName; hostRelayKey = relayKey; nextHeartbeat = Time.unscaledTime + 10f; status = "Lobby created, start a level.";
            });
        }
        catch (Exception exception) { RunOnMainThread(() => status = "Could not create lobby: " + exception.Message); }
    }

    private void JoinLobbyRequest(string id, ConnectionMode listedMode)
    {
        try
        {
            var response = Http("POST", "/v1/lobbies/" + id + "/join",
                JsonUtility.ToJson(new JoinLobbyPayload { playerName = playerName }), null);
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
            RunOnMainThread(() => ConnectRelay(relayAddress, lobbyId, relayKey, peerId, hostPeerId, maxPlayers, mode));
        }
        catch (Exception exception)
        {
            RunOnMainThread(() =>
            {
                SetJoinInProgress(false);
                status = "Could not join lobby: " + exception.Message;
            });
        }
    }

    private void SetJoinInProgress(bool value)
    {
        lock (joinLock) joinInProgress = value;
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
        return "udp://gunsawudp.e621.su:27015";
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

    private static List<LobbyInfo> ParseLobbies(string json)
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
            lobby.respawnAtStart = JsonBool(item, "respawnAtStart");
            lobby.hostP2P = JsonBool(item, "HostP2P") || JsonBool(item, "hostP2P");
            lobby.connectionMode = ParseConnectionMode(JsonString(item, "connectionMode"));
            if (!string.IsNullOrEmpty(lobby.id)) result.Add(lobby);
            cursor = end + 1;
        }
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

    private void UpdateHostedLobbyInDirectory()
    {
        try
        {
            var body = JsonUtility.ToJson(new CreateLobbyRequest
            {
                name = lobbyName,
                hostName = playerName,
                map = SceneManager.GetActiveScene().name,
                maxPlayers = MultiplayerSession.MaxPlayers,
                hostPort = 27016,
                pvp = createPvp,
                canGrab = createCanGrab,
                grabOnlyUnconscious = createCanGrab && createGrabOnlyUnconscious,
                allowRespawn = createAllowRespawn,
                respawnTime = MultiplayerSession.RespawnTimeSeconds,
                respawnAtStart = createAllowRespawn && createRespawnAtStart,
                hostP2P = createConnectionMode != ConnectionMode.Relay,
                connectionMode = createConnectionMode.ToString()
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

    private void SpawnRandomWeapon()
    {
        var player = PlayerScript.player;
        if (player == null || player.bodyScript == null) return;
        var presets = Resources.FindObjectsOfTypeAll<WeaponPreset>();
        if (presets == null || presets.Length == 0) { status = "No weapon presets loaded."; return; }
        var choices = new List<WeaponPreset>();
        foreach (var preset in presets)
            if (preset != null && preset.sprite != null) choices.Add(preset);
        if (choices.Count == 0) { status = "No usable weapon presets loaded."; return; }
        var prefab = Resources.Load<GameObject>("Spawnables/PickupWeapon");
        if (prefab == null) { status = "Pickup weapon prefab not found."; return; }
        var position = player.bodyScript.transform.position + new Vector3(0f, 2f, 0f);
        var pickup = Instantiate(prefab, position, Quaternion.identity).GetComponent<DroppedWeapon>();
        if (pickup == null) { status = "Pickup component not found."; return; }
        var weapon = choices[UnityEngine.Random.Range(0, choices.Count)];
        pickup.ChangeWeapon(weapon, weapon.magSize);
        WorldReplication.TrackDroppedWeapons();
        status = "Spawned " + weapon.name + ".";
    }

    private void SpawnGrenadeLauncher()
    {
        SpawnNamedWeapon("Grenade Launcher", "Grenade launcher");
    }

    private void SpawnRocketLauncher()
    {
        SpawnNamedWeapon("Rocket Launcher", "Rocket", "RPG");
    }

   private void SpawnSniperRifle()
   {
        SpawnNamedWeapon("Sniper Rifle", "Sniper rifle");
   }

   private void SpawnMarksmanRifle()
   {
       SpawnNamedWeapon("Marksman Rifle", "Marksman rifle");
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
        WorldReplication.TrackDroppedWeapons();
        status = "Spawned " + weapon.name + ".";
    }

    [Serializable] internal sealed class LobbyInfo { public string id = ""; public string name = ""; public string hostName = ""; public string map = ""; public int players; public int maxPlayers; public bool pvp; public bool canGrab; public bool grabOnlyUnconscious; public bool allowRespawn; public int respawnTime; public bool respawnAtStart; public bool hostP2P; public ConnectionMode connectionMode = ConnectionMode.Relay; }
    [Serializable] private sealed class JoinLobbyPayload { public string playerName = ""; }
    [Serializable] private sealed class BanPlayerRequest { public string playerName = ""; public int durationMinutes; }
    [Serializable] private sealed class CreateLobbyRequest { public string name = ""; public string hostName = ""; public string map = ""; public int maxPlayers; public int hostPort; public bool pvp; public bool canGrab; public bool grabOnlyUnconscious; public bool allowRespawn; public int respawnTime; public bool respawnAtStart; public bool hostP2P; public string connectionMode = "Relay"; }
}
