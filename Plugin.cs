using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class GunsawMultiplayerPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.gunsaw.multiplayer";
    public const string PluginName = "Gunsaw Multiplayer";
    public const string PluginVersion = "0.3.2";

    private readonly List<LobbyInfo> lobbies = new List<LobbyInfo>();
    private ConfigEntry<string> masterUrl;
    private bool visible;
    private string status = "Select an option.";
    private string lobbyServerAddress = "gunsawudp.e621.su";
    private string lobbyName = "Lobby";
    private string playerName = "Player";
    private bool createPvp;
    private bool createCanGrab = true;
    private bool createGrabOnlyUnconscious = true;
    private bool createAllowRespawn = true;
    private bool createRespawnAtStart = true;
    private string createRespawnTime = "5";
    private string createMaxPlayers = "4";
    private string customLevelJson = "";
    private string receivedCustomLevelJson = "";
    private bool waitingForCustomLevel;
    private float customLevelPhysicsRefreshUntil;
    private float nextCustomLevelPhysicsRefresh;
    private Vector2 scroll;
    private NetworkAvatarReplication avatarReplication;
    private WorldReplication worldReplication;
    private NpcReplication npcReplication;
    private MultiplayerHud multiplayerHud;
    private int debugWeaponSequence;
    private bool gameplayTypesLogged;
    private string hostedLobbyId = "";
    private string hostedLobbyDisplayName = "";
    private string hostRelayKey = "";
    private float nextHeartbeat;
    private bool shuttingDown;
    private readonly Queue<Action> mainThreadActions = new Queue<Action>();
    private readonly object mainThreadActionsLock = new object();

    private void Awake()
    {
        KeepMultiplayerRunningInBackground();
        masterUrl = Config.Bind("Network", "MasterUrl", "https://gunsawudp.e621.su", "Lobby directory URL.");
        if (masterUrl.Value == "http://127.0.0.1:18080" ||
            masterUrl.Value == "http://gunsawudp.e621.su") masterUrl.Value = "https://gunsawudp.e621.su";
        lobbyServerAddress = DisplayServerAddress(masterUrl.Value);
        new Harmony(PluginGuid).PatchAll();
        avatarReplication = gameObject.AddComponent<NetworkAvatarReplication>();
        worldReplication = gameObject.AddComponent<WorldReplication>();
        npcReplication = gameObject.AddComponent<NpcReplication>();
        multiplayerHud = gameObject.AddComponent<MultiplayerHud>();
        World = worldReplication;
        Logger.LogInfo("Gunsaw Multiplayer 0.3.2 loaded.");
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

    private void OnGUI()
    {
        if (UnityEngine.Object.FindObjectOfType<MainMenuManager>() == null)
            return;

        if (!visible)
        {
            if (GUI.Button(new Rect(20, Screen.height - 65, 210, 42), "MULTIPLAYER"))
            {
                visible = true;
                RefreshLobbies();
            }
            return;
        }

        var window = new Rect((Screen.width - 680) / 2f, (Screen.height - 590) / 2f, 680, 590);
        GUI.ModalWindow(9191, window, DrawWindow, "GUNSAW MULTIPLAYER");
    }

    private void Update()
    {
        KeepMultiplayerRunningInBackground();
        lock (mainThreadActionsLock)
            while (mainThreadActions.Count > 0) mainThreadActions.Dequeue()();
        MultiplayerSession.UpdateConnection();
        MultiplayerLoadDistance.Apply();
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

        if (!gameplayTypesLogged && UnityEngine.Object.FindObjectOfType<PlayerScript>() != null)
        {
            gameplayTypesLogged = true;
            Logger.LogInfo("Gameplay mapping active: PlayerScript, BodyScript, WeaponScript, LimbScript, SceneLoader.");
        }

        if (!string.IsNullOrEmpty(hostedLobbyId) && Time.unscaledTime >= nextHeartbeat)
        {
            nextHeartbeat = Time.unscaledTime + 10f;
            SendHeartbeat();
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
        if (MultiplayerSession.TryTakeScene(out sceneToLoad))
        {
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
            status = "Loading host scene " + sceneToLoad + "...";
            SceneManager.LoadScene(sceneToLoad);
        }

        if (MultiplayerHud.IsTyping) return;
        if (Input.GetKeyDown(KeyCode.Space)) debugWeaponSequence = 1;
        else if (debugWeaponSequence == 1 && Input.GetKeyDown(KeyCode.End)) debugWeaponSequence = 2;
        else if (debugWeaponSequence == 2 && Input.GetKeyDown(KeyCode.G))
        {
            debugWeaponSequence = 0;
            SpawnRandomWeapon();
        }
        else if (Input.anyKeyDown) debugWeaponSequence = 0;
    }

    private void DrawWindow(int windowId)
    {
        GUI.Label(new Rect(18, 35, 640, 24), "Choose a lobby server, create a lobby, or join one from the list.");
        GUI.Label(new Rect(18, 62, 100, 22), "Name");
        playerName = GUI.TextField(new Rect(120, 60, 190, 24), playerName, 32);
        GUI.Label(new Rect(330, 62, 90, 22), "Lobby");
        lobbyName = GUI.TextField(new Rect(420, 60, 160, 24), lobbyName, 48);
        createPvp = GUI.Toggle(new Rect(590, 61, 72, 24), createPvp, "Pvp");
        createCanGrab = GUI.Toggle(new Rect(18, 92, 115, 24), createCanGrab, "Can grab");
        createGrabOnlyUnconscious = GUI.Toggle(new Rect(145, 92, 235, 24),
            createGrabOnlyUnconscious, "Can grab only unconscious");
        GUI.Label(new Rect(395, 94, 105, 22), "Max players");
        createMaxPlayers = GUI.TextField(new Rect(505, 91, 44, 24), createMaxPlayers, 2);
        createAllowRespawn = GUI.Toggle(new Rect(18, 122, 135, 24), createAllowRespawn, "Allow respawn");
        var controlsEnabled = GUI.enabled;
        GUI.enabled = controlsEnabled && createAllowRespawn;
        GUI.Label(new Rect(165, 124, 105, 22), "Respawn time");
        createRespawnTime = GUI.TextField(new Rect(274, 121, 52, 24), createRespawnTime, 4);
        GUI.Label(new Rect(331, 124, 50, 22), "seconds");
        createRespawnAtStart = GUI.Toggle(new Rect(395, 122, 190, 24), createRespawnAtStart,
            "Respawn at start");
        GUI.enabled = controlsEnabled;

        if (GUI.Button(new Rect(18, 152, 180, 32), "Paste custom level"))
            PasteCustomLevel();
        GUI.Label(new Rect(208, 157, 285, 22), string.IsNullOrEmpty(customLevelJson)
            ? "Custom level: not loaded" : "Custom level: loaded");
        GUI.enabled = controlsEnabled && MultiplayerSession.IsHosting && !string.IsNullOrEmpty(customLevelJson);
        if (GUI.Button(new Rect(500, 152, 162, 32), "Start custom level"))
            StartCustomLevel();

        GUI.enabled = controlsEnabled && !MultiplayerSession.IsHosting;
        if (GUI.Button(new Rect(18, 192, 180, 32), "Create lobby"))
            CreateLobby();
        GUI.enabled = controlsEnabled;
        if (GUI.Button(new Rect(208, 192, 180, 32), "Refresh"))
            RefreshLobbies();
        if (MultiplayerSession.IsHosting)
            GUI.Label(new Rect(405, 194, 185, 28), "HOSTING - " +
                MultiplayerSession.PlayerCount + "/" + MultiplayerSession.MaxPlayers + " PLAYERS");
        if (GUI.Button(new Rect(598, 12, 65, 25), "Close"))
            visible = false;

        GUI.Label(new Rect(18, 237, 100, 22), "Lobby server");
        lobbyServerAddress = GUI.TextField(new Rect(120, 235, 250, 24), lobbyServerAddress, 255);
        GUI.enabled = controlsEnabled && !MultiplayerSession.IsHosting;
        if (GUI.Button(new Rect(380, 234, 110, 27), "Connect"))
            ConnectLobbyServer();
        GUI.enabled = controlsEnabled;

        GUI.Label(new Rect(18, 272, 640, 22), status);
        GUI.Label(new Rect(18, 299, 640, 22), "Public lobbies");
        GUILayout.BeginArea(new Rect(18, 322, 645, 248));
        scroll = GUILayout.BeginScrollView(scroll);
        foreach (var lobby in lobbies)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(lobby.name + "  |  " + lobby.hostName + "  |  " + lobby.map + "  |  " +
                (lobby.pvp ? "Pvp" : "Co-op") + "  |  " +
                (lobby.canGrab ? (lobby.grabOnlyUnconscious ? "Grab down" : "Grab any") : "No grab") +
                "  |  " + (lobby.allowRespawn
                    ? "Respawn " + lobby.respawnTime + "s " + (lobby.respawnAtStart ? "Start" : "Death")
                    : "No respawn") +
                "  |  " + lobby.players + "/" + lobby.maxPlayers, GUILayout.Width(500));
            if (GUILayout.Button("Join", GUILayout.Width(100))) JoinLobby(lobby.id);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void RefreshLobbies()
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

    private void ConnectLobbyServer()
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

    private void PasteCustomLevel()
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

    private void StartCustomLevel()
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

    private void CreateLobby()
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
                respawnAtStart = createRespawnAtStart });
            ThreadPool.QueueUserWorkItem(_ => CreateLobbyInDirectory(body, respawnTime, maxPlayers));
        }
        catch (Exception e) { status = "Could not create lobby: " + e.Message; }
    }

    private void JoinLobby(string id)
    {
        try
        {
            ThreadPool.QueueUserWorkItem(_ => JoinLobbyRequest(id));
        }
        catch (Exception e) { status = "Could not join lobby: " + e.Message; }
    }

    private void ConnectRelay(string address, string lobbyId, string relayKey, ushort peerId, int maxPlayers)
    {
        string error;
        if (!MultiplayerSession.Connect(address, lobbyId, relayKey, playerName, peerId, maxPlayers,
            Logger, out error)) { status = error; return; }
        avatarReplication.Configure(playerName);
        multiplayerHud.ResetChat();
        status = "Connecting through UDP relay " + address + "...";
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
                    playerName, hostPeerId, maxPlayers, Logger);
                avatarReplication.Configure(playerName); multiplayerHud.ResetChat(); hostedLobbyId = lobbyId; hostedLobbyDisplayName = lobbyName; hostRelayKey = relayKey; nextHeartbeat = Time.unscaledTime + 10f; status = "Lobby created, start a level.";
            });
        }
        catch (Exception exception) { RunOnMainThread(() => status = "Could not create lobby: " + exception.Message); }
    }

    private void JoinLobbyRequest(string id)
    {
        try
        {
            var response = Http("POST", "/v1/lobbies/" + id + "/join", "{}", null);
            var lobbyId = JsonString(response, "id");
            var relayKey = JsonString(response, "relayKey");
            var relayAddress = JsonString(response, "relayAddress");
            var peerId = (ushort)Mathf.Clamp(JsonInt(response, "peerId"), 2, 16);
            var maxPlayers = Mathf.Clamp(JsonInt(response, "maxPlayers"), 2, 16);
            if (string.IsNullOrEmpty(relayAddress)) relayAddress = DefaultRelayAddress();
            if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(relayKey)) throw new InvalidDataException("Invalid directory response.");
            RunOnMainThread(() => ConnectRelay(relayAddress, lobbyId, relayKey, peerId, maxPlayers));
        }
        catch (Exception exception) { RunOnMainThread(() => status = "Could not join lobby: " + exception.Message); }
    }

    private void RunOnMainThread(Action action) { lock (mainThreadActionsLock) mainThreadActions.Enqueue(action); }

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
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { Http("DELETE", "/v1/lobbies/" + lobbyId, null, "Bearer " + relayKey); }
            catch (Exception exception) { Logger.LogWarning("Could not remove hosted lobby: " + exception.Message); }
        });
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

    [Serializable] private sealed class LobbyInfo { public string id = ""; public string name = ""; public string hostName = ""; public string map = ""; public int players; public int maxPlayers; public bool pvp; public bool canGrab; public bool grabOnlyUnconscious; public bool allowRespawn; public int respawnTime; public bool respawnAtStart; }
    [Serializable] private sealed class CreateLobbyRequest { public string name = ""; public string hostName = ""; public string map = ""; public int maxPlayers; public int hostPort; public bool pvp; public bool canGrab; public bool grabOnlyUnconscious; public bool allowRespawn; public int respawnTime; public bool respawnAtStart; }
}

[HarmonyPatch(typeof(LimbScript), "OnCollisionStay2D")]
internal static class LimbCrateCollisionPatch
{
    private static void Postfix(LimbScript __instance, Collision2D collision)
    {
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueuePush(__instance, collision);
    }
}

[HarmonyPatch(typeof(LevitatorScript), "FixedUpdate")]
internal static class ClientLevitatorPropPatch
{
    private static void Prefix(LevitatorScript __instance)
    {
        NetworkAvatarReplication.ValidateRemoteGrab(__instance);
    }

    private static void Postfix(LevitatorScript __instance)
    {
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueLevitated(__instance.currentlyLevitating);
        NetworkAvatarReplication.QueueRemoteGrab(__instance);
        NpcReplication.QueueClientCorpseGrab(__instance);
    }
}

[HarmonyPatch(typeof(LevitatorScript), "TryGrab")]
internal static class MultiplayerPlayerGrabPatch
{
    private static void Postfix(LevitatorScript __instance)
    {
        NetworkAvatarReplication.TryGrabRemotePlayer(__instance);
        NpcReplication.TryGrabClientCorpse(__instance);
    }
}

[HarmonyPatch(typeof(PlayerScript), "Update")]
internal static class MultiplayerPlayerSlowmoPatch
{
    private static bool Prefix(PlayerScript __instance, out MultiplayerTimeControl.SlowmoKeyState __state)
    {
        __state = default(MultiplayerTimeControl.SlowmoKeyState);
        if (MultiplayerSession.IsConnected)
        {
            NetworkAvatarReplication.EnsurePlayerSingletonForUpdate();
            if (!NetworkAvatarReplication.PrepareLocalPlayerUpdate(__instance))
                return false;
        }

        MultiplayerTimeControl.KeepMultiplayerActive();
        __state = MultiplayerTimeControl.BeginPlayerUpdate(__instance);
        return !MultiplayerHud.IsTyping;
    }

    private static Exception Finalizer(PlayerScript __instance, Exception __exception,
        MultiplayerTimeControl.SlowmoKeyState __state)
    {
        MultiplayerTimeControl.EndPlayerUpdate(__instance, __state);
        return __exception;
    }
}

[HarmonyPatch(typeof(GameManager), "Update")]
internal static class MultiplayerGameManagerFocusPatch
{
    private static void Prefix()
    {
        MultiplayerLoadDistance.Apply();
        MultiplayerTimeControl.KeepMultiplayerActive();
    }

    private static void Postfix()
    {
        MultiplayerLoadDistance.Apply();
    }
}

[HarmonyPatch(typeof(ResourceManager), "Awake")]
internal static class MultiplayerResourceLoadDistancePatch
{
    private static void Postfix()
    {
        MultiplayerLoadDistance.Apply();
    }
}

[HarmonyPatch(typeof(GameManager), "Switch")]
internal static class MultiplayerVanillaBodySwitchPatch
{
    private static bool Prefix(LimbScript limb)
    {
        if (!MultiplayerSession.IsConnected) return true;
        return !NpcReplication.TryPossessLocalPlayer(limb);
    }
}

[HarmonyPatch(typeof(GameManager), "IsOnscreen", new[] { typeof(BodyScript) })]
internal static class MultiplayerNpcOnScreenPatch
{
    private static void Postfix(BodyScript body, ref bool __result)
    {
        if (body != null && MultiplayerSession.IsHosting && !body.isPlayer)
        {
            body.onScreen = true;
            __result = true;
        }
    }
}

[HarmonyPatch(typeof(LimbScript), "OnWillRenderObject")]
internal static class MultiplayerLimbAnimationPatch
{
    private static bool Prefix(LimbScript __instance)
    {
        var body = __instance == null ? null : __instance.body;
        if (body == null || NpcReplication.IsPossessionRenderGuard(body) ||
            NpcReplication.IsClientProxy(body) || NetworkAvatarReplication.IsRemoteAvatarBody(body))
            return false;
        return true;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var focusedGetter = AccessTools.PropertyGetter(typeof(Application), nameof(Application.isFocused));
        var replacement = AccessTools.Method(typeof(MultiplayerLimbAnimationPatch), nameof(IsAnimationFocused));
        foreach (var instruction in instructions)
        {
            if (focusedGetter != null && instruction.Calls(focusedGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }
            yield return instruction;
        }
    }

    private static bool IsAnimationFocused()
    {
        return Application.isFocused || MultiplayerSession.IsHosting || MultiplayerSession.IsConnected;
    }
}

[HarmonyPatch(typeof(ScreenFXManager), "Update")]
internal static class MultiplayerScreenTimePatch
{
    private static void Prefix(ScreenFXManager __instance)
    {
        MultiplayerTimeControl.SuppressTimeSlowdown(__instance);
    }

    private static void Postfix(ScreenFXManager __instance)
    {
        MultiplayerTimeControl.SuppressTimeSlowdown(__instance);
    }
}

[HarmonyPatch(typeof(CameraFollow), "CreateScreenCrack")]
internal static class MultiplayerPvpScreenCrackPatch
{
    private static bool Prefix()
    {
        return !NetworkAvatarReplication.SuppressLocalShotScreenCrack();
    }
}

[HarmonyPatch(typeof(CameraFollow), "CreateBloodSplat")]
internal static class MultiplayerTargetBloodSplatPatch
{
    private static bool Prefix()
    {
        return !NetworkAvatarReplication.SuppressTargetedScreenEffect();
    }
}

[HarmonyPatch(typeof(CameraFollow), "AddOffset")]
internal static class MultiplayerTargetCameraOffsetPatch
{
    private static bool Prefix()
    {
        return !NetworkAvatarReplication.SuppressTargetedScreenEffect();
    }
}

[HarmonyPatch(typeof(CameraFollow), "AddRot")]
internal static class MultiplayerTargetCameraRotationPatch
{
    private static bool Prefix()
    {
        return !NetworkAvatarReplication.SuppressTargetedScreenEffect();
    }
}

[HarmonyPatch(typeof(CameraFollow), "Update")]
internal static class MultiplayerTargetCameraShakeUpdatePatch
{
    private static void Prefix(CameraFollow __instance)
    {
        NetworkAvatarReplication.ClearSuppressedCameraShake(__instance);
    }
}

internal static class MultiplayerTimeControl
{
    private static readonly FieldInfo PlayerKeys = AccessTools.Field(typeof(PlayerScript), "keys");
    private static readonly FieldInfo PlayerInSlowmo = AccessTools.Field(typeof(PlayerScript), "inSlowmo");
    private static readonly FieldInfo PlayerSlowmoSource = AccessTools.Field(typeof(PlayerScript), "slowmoSource");
    private static readonly FieldInfo PlayerSecondaryBar = AccessTools.Field(typeof(PlayerScript), "secondarySlowmoBarImage");
    private static readonly FieldInfo SlowmoTime = AccessTools.Field(typeof(ScreenFXManager), "slowmoTime");
    private static readonly FieldInfo FullStopTime = AccessTools.Field(typeof(ScreenFXManager), "fullStopTime");
    private static readonly FieldInfo ScreenSlowmo = AccessTools.Field(typeof(ScreenFXManager), "slowmo");
    private static readonly FieldInfo ContrastAmount = AccessTools.Field(typeof(ScreenFXManager), "contrastAmount");
    private static readonly FieldInfo GamePaused = AccessTools.Field(typeof(GameManager), "paused");
    private static readonly FieldInfo FogIntensity = AccessTools.Field(typeof(GameManager), "fogIntensity");

    internal sealed class SlowmoKeyState
    {
        internal Dictionary<string, KeyCode> Keys;
        internal KeyCode Key;
        internal bool Restore;
    }

    internal static SlowmoKeyState BeginPlayerUpdate(PlayerScript player)
    {
        var state = new SlowmoKeyState();
        if (!MultiplayerSession.IsConnected || player == null) return state;
        state.Keys = PlayerKeys == null ? null : PlayerKeys.GetValue(player) as Dictionary<string, KeyCode>;
        if (state.Keys != null && state.Keys.TryGetValue("Slowmo", out state.Key))
        {
            state.Restore = true;
            state.Keys["Slowmo"] = KeyCode.None;
        }
        if (DisablePlayerSlowmo(player)) ResetSlowmoContrast(ScreenFXManager.main);
        ForceNormalTime();
        return state;
    }

    internal static void EndPlayerUpdate(PlayerScript player, SlowmoKeyState state)
    {
        if (state != null && state.Restore && state.Keys != null)
        {
            state.Keys["Slowmo"] = state.Key;
            state.Restore = false;
        }
        if (!MultiplayerSession.IsConnected) return;
        if (DisablePlayerSlowmo(player)) ResetSlowmoContrast(ScreenFXManager.main);
        ForceNormalTime();
    }

    internal static void SuppressTimeSlowdown(ScreenFXManager screen)
    {
        if (!MultiplayerSession.IsConnected) return;
        if (screen != null)
        {
            if (SlowmoTime != null) SlowmoTime.SetValue(screen, 0f);
            if (FullStopTime != null) FullStopTime.SetValue(screen, 0f);
            if (ScreenSlowmo != null) ScreenSlowmo.SetValue(screen, false);
        }
        if (DisablePlayerSlowmo(PlayerScript.player)) ResetSlowmoContrast(screen);
        ForceNormalTime();
    }

    internal static void KeepMultiplayerActive()
    {
        if (!MultiplayerSession.IsConnected || Application.isFocused) return;
        var manager = GameManager.main;
        if (manager != null && GamePaused != null)
            GamePaused.SetValue(manager, false);
        Time.timeScale = 1f;
    }

    private static bool DisablePlayerSlowmo(PlayerScript player)
    {
        if (player == null) return false;
        var wasInSlowmo = PlayerInSlowmo != null && (bool)PlayerInSlowmo.GetValue(player);
        if (PlayerInSlowmo != null) PlayerInSlowmo.SetValue(player, false);
        var source = PlayerSlowmoSource == null ? null : PlayerSlowmoSource.GetValue(player) as AudioSource;
        if (source != null && source.isPlaying) source.Stop();
        var secondaryBar = PlayerSecondaryBar == null ? null : PlayerSecondaryBar.GetValue(player) as Component;
        if (secondaryBar != null && secondaryBar.transform.parent != null)
            secondaryBar.transform.parent.gameObject.SetActive(false);
        return wasInSlowmo;
    }

    private static void ResetSlowmoContrast(ScreenFXManager screen)
    {
        var manager = GameManager.main;
        if (screen != null && ContrastAmount != null && manager != null && FogIntensity != null)
            ContrastAmount.SetValue(screen, -(float)FogIntensity.GetValue(manager) * 45f);
    }

    private static void ForceNormalTime()
    {
        var manager = GameManager.main;
        var paused = manager != null && GamePaused != null && (bool)GamePaused.GetValue(manager);
        if (!paused) Time.timeScale = 1f;
    }
}

[HarmonyPatch(typeof(CrateScript), "Damage")]
internal static class ClientCrateDamagePatch
{
    private static bool Prefix(CrateScript __instance, float dmg)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueDamage(__instance, dmg);
        return false;
    }
}

[HarmonyPatch(typeof(ButtonScript), "Activated")]
internal static class MultiplayerWorldButtonPatch
{
    private static bool Prefix(ButtonScript __instance)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueButtonActivation(__instance);
        return false;
    }

    private static void Postfix(ButtonScript __instance)
    {
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.NotifyButtonActivated(__instance);
    }
}

[HarmonyPatch(typeof(BodyScript), "Damaged")]
internal static class ClientNpcDamagePatch
{
    private static bool Prefix(BodyScript __instance, bool isCrit,
        out NetworkAvatarReplication.TargetScreenEffectState __state)
    {
        __state = NetworkAvatarReplication.BeginTargetScreenEffect(__instance);
        NetworkAvatarReplication.RecordDamageSource(__instance);
        if (NetworkAvatarReplication.HandleHostRemoteDamaged(__instance, isCrit)) return false;
        return !NpcReplication.HandleClientDamaged(__instance, isCrit);
    }

    private static Exception Finalizer(Exception __exception,
        NetworkAvatarReplication.TargetScreenEffectState __state)
    {
        NetworkAvatarReplication.EndTargetScreenEffect(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(BodyScript), "Death")]
internal static class ClientNpcDeathPatch
{
    private static bool Prefix(BodyScript __instance)
    {
        if (MultiplayerSession.IsConnected && __instance != null && __instance.isPlayer)
            __instance.dropWeapon = false;
        if (NetworkAvatarReplication.BlockLocalRespawnDeath(__instance)) return false;
        if (NetworkAvatarReplication.HandleHostRemoteDeath(__instance)) return false;
        NpcReplication.PrepareAuthoritativeNpcDeath(__instance);
        return !NpcReplication.HandleClientDeath(__instance);
    }

    private static void Postfix(BodyScript __instance)
    {
        Announce(__instance);
    }

    internal static void Announce(BodyScript __instance)
    {
        if (!MultiplayerSession.IsHosting || __instance == null ||
            !NetworkAvatarReplication.BeginDeathAnnouncement(__instance)) return;
        var victimName = DeathDisplayName(__instance);
        var killer = NetworkAvatarReplication.DamageSourceFor(__instance);
        var message = killer == null
            ? victimName + " died."
            : DeathDisplayName(killer) + " killed " + victimName + ".";
        MultiplayerHud.AddSystemMessage(message);
        MultiplayerSession.SendChat(message, true);
    }

    private static string DeathDisplayName(BodyScript body)
    {
        if (body == null) return "Environment";
        if (body.isPlayer)
        {
            var localPlayer = PlayerScript.player;
            if (localPlayer != null && body == localPlayer.bodyScript)
                return MultiplayerSession.LocalPlayerName;
            var remoteName = NetworkAvatarReplication.RemoteNameForBody(body);
            return string.IsNullOrEmpty(remoteName) ? "Player" : remoteName;
        }
        var characterName = body.characterName;
        if (!string.IsNullOrWhiteSpace(characterName)) return characterName.Trim();
        var objectName = body.gameObject == null ? "Bot" : body.gameObject.name;
        objectName = objectName.Replace("(Clone)", "").Trim();
        return string.IsNullOrEmpty(objectName) ? "Bot" : objectName;
    }
}

[HarmonyPatch(typeof(BodyScript), "DropWeapon")]
internal static class ClientNpcDropWeaponPatch
{
    private static bool Prefix(BodyScript __instance, out bool __state)
    {
        __state = __instance != null && __instance.dropWeapon && !__instance.unarmed;
        if (NetworkAvatarReplication.BlockNetworkPlayerDrop(__instance, false)) return false;
        return !NpcReplication.BlockClientWeaponDrop(__instance);
    }

    private static void Postfix(BodyScript __instance, bool __state)
    {
        if (__state) NetworkAvatarReplication.ConsumeLocalDeathWeapon(__instance, false);
        WorldReplication.TrackDroppedWeapons();
    }
}

[HarmonyPatch(typeof(BodyScript), "DropWeaponSingle")]
internal static class ClientNpcDropWeaponSinglePatch
{
    private static bool Prefix(BodyScript __instance)
    {
        if (NetworkAvatarReplication.BlockNetworkPlayerDrop(__instance, false)) return false;
        return !NpcReplication.BlockClientWeaponDrop(__instance);
    }

    private static void Postfix()
    {
        WorldReplication.TrackDroppedWeapons();
    }
}

[HarmonyPatch(typeof(BodyScript), "DropAllWeapons")]
internal static class ClientNpcDropAllWeaponsPatch
{
    private static bool Prefix(BodyScript __instance)
    {
        if (NetworkAvatarReplication.BlockNetworkPlayerDrop(__instance, true)) return false;
        return !NpcReplication.BlockClientWeaponDrop(__instance);
    }

    private static void Postfix()
    {
        WorldReplication.TrackDroppedWeapons();
    }
}

[HarmonyPatch(typeof(LimbScript), "OnCollisionEnter2D")]
internal static class ClientNpcLimbCollisionPatch
{
    private static bool Prefix(LimbScript __instance)
    {
        return __instance == null ||
            (!NpcReplication.IsClientProxy(__instance.body) &&
             !NpcReplication.IsLocallyPossessedBody(__instance.body) &&
             !NetworkAvatarReplication.IsRemoteAvatarBody(__instance.body));
    }
}

[HarmonyPatch(typeof(SawScript), "OnCollisionEnter2D")]
internal static class ClientSawCollisionEnterPatch
{
    private static bool Prefix(SawScript __instance, Collision2D collision)
    {
        return ClientSawCollisionPatch.ShouldRun(__instance, collision);
    }
}

[HarmonyPatch(typeof(SawScript), "OnCollisionStay2D")]
internal static class ClientSawCollisionStayPatch
{
    private static bool Prefix(SawScript __instance, Collision2D collision)
    {
        return ClientSawCollisionPatch.ShouldRun(__instance, collision);
    }
}

[HarmonyPatch]
internal static class MultiplayerDamageBarsPatch
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(BloodBars), new string(new[]
        { 'F', 'i', 'x', 'e', 'd', 'U', 'p', 'd', 'a', 't', 'e' }));
    }

    private static void Prefix(BloodBars __instance)
    {
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || __instance == null || player == null ||
            __instance.body != player.bodyScript) return;

        __instance.constTreshold = float.NegativeInfinity;
    }
}

internal static class ClientSawCollisionPatch
{
    internal static bool ShouldRun(SawScript saw, Collision2D collision)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        var player = PlayerScript.player;
        if (saw == null || collision == null || player == null || player.bodyScript == null) return false;
        var collider = collision.collider;
        var hitBody = collider == null ? null : collider.GetComponentInParent<BodyScript>();
        return hitBody == player.bodyScript;
    }
}

[HarmonyPatch(typeof(DroppedWeapon), "PickupWeapon")]
internal static class ClientDroppedWeaponPickupPatch
{
    private static void Prefix(DroppedWeapon __instance, BodyScript body)
    {
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueWeaponInteraction(__instance, body, WorldReplication.WeaponPickup);
    }
}

[HarmonyPatch(typeof(DroppedWeapon), "AmmoGet")]
internal static class ClientDroppedWeaponAmmoPatch
{
    private static void Prefix(DroppedWeapon __instance, BodyScript body)
    {
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueWeaponInteraction(__instance, body, WorldReplication.WeaponAmmoGet);
    }
}

[HarmonyPatch(typeof(WeaponScript), "Shoot")]
internal static class MultiplayerWeaponShotPatch
{
    private static bool Prefix(WeaponScript __instance, out NetworkAvatarReplication.ShotState __state)
    {
        __state = NetworkAvatarReplication.BeginWeaponShot(__instance);
        return !MultiplayerSession.IsConnected || MultiplayerSession.IsHost ||
            __instance == null || (__instance.GetComponentInParent<NpcNetworkReplica>() == null &&
            !NpcReplication.IsClientProxy(__instance.body));
    }

    private static Exception Finalizer(Exception __exception, NetworkAvatarReplication.ShotState __state)
    {
        NetworkAvatarReplication.CompleteWeaponShot(__state, __exception == null);
        return __exception;
    }
}

[HarmonyPatch(typeof(BodyScript), "Kick")]
internal static class MultiplayerPlayerKickPatch
{
    private static void Prefix(BodyScript __instance, out NetworkAvatarReplication.ShotState __state)
    {
        __state = NetworkAvatarReplication.BeginMeleeAttack(__instance);
    }

    private static Exception Finalizer(Exception __exception, NetworkAvatarReplication.ShotState __state)
    {
        NetworkAvatarReplication.EndMeleeAttack(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(AIScript), "FixedUpdate")]
internal static class MultiplayerNpcTargetPatch
{
    private static void Prefix(AIScript __instance)
    {
        NetworkAvatarReplication.PrepareNpcTarget(__instance);
    }
}

[HarmonyPatch(typeof(GrenadeScript), "SetBody")]
internal static class MultiplayerGrenadeOwnerPatch
{
    private static void Postfix(GrenadeScript __instance, BodyScript body)
    {
        NetworkAvatarReplication.ConfigureProjectileCollisions(__instance, body);
    }
}

[HarmonyPatch(typeof(RocketProjectile), "SetBody")]
internal static class MultiplayerRocketOwnerPatch
{
    private static void Postfix(RocketProjectile __instance, BodyScript body)
    {
        NetworkAvatarReplication.ConfigureProjectileCollisions(__instance, body);
    }
}

[HarmonyPatch(typeof(ExplosionHandler), "CreateExplosion")]
internal static class MultiplayerExplosionPatch
{
    private static void Prefix(GameObject explosionObj, out NetworkAvatarReplication.ShotState __state)
    {
        __state = NetworkAvatarReplication.BeginProjectileExplosion(explosionObj);
    }

    private static Exception Finalizer(Exception __exception, NetworkAvatarReplication.ShotState __state)
    {
        NetworkAvatarReplication.EndWeaponShot(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(PlayerScript), "DoBodyMouseOver")]
internal static class RemotePlayerMouseOverPatch
{
    private static bool Prefix(PlayerScript __instance, BodyScript body)
    {
        if (body == null || body.GetComponentInParent<NetworkReplica>() == null) return true;
        var textField = AccessTools.Field(typeof(PlayerScript), "mouseOverText");
        var text = textField == null ? null : textField.GetValue(__instance) as Component;
        var colorProperty = text == null ? null : text.GetType().GetProperty("color");
        if (colorProperty != null) colorProperty.SetValue(text, Color.clear, null);
        return false;
    }
}

[HarmonyPatch(typeof(Chatter), "AllyDied")]
internal static class NetworkChatterAllyDiedPatch
{
    private static readonly FieldInfo BodyField = AccessTools.Field(typeof(Chatter), "body");
    private static readonly FieldInfo AiField = AccessTools.Field(typeof(Chatter), "ai");

    private static bool Prefix(Chatter __instance)
    {
        return __instance != null && BodyField != null && AiField != null &&
            BodyField.GetValue(__instance) != null && AiField.GetValue(__instance) != null &&
            __instance.GetComponentInParent<NetworkReplica>() == null;
    }
}

[HarmonyPatch(typeof(Chatter), "Died")]
internal static class NetworkChatterDiedPatch
{
    private static readonly FieldInfo BodyField = AccessTools.Field(typeof(Chatter), "body");
    private static readonly FieldInfo AiField = AccessTools.Field(typeof(Chatter), "ai");

    private static bool Prefix(Chatter __instance)
    {
        return __instance != null && BodyField != null && AiField != null &&
            BodyField.GetValue(__instance) != null && AiField.GetValue(__instance) != null;
    }
}
