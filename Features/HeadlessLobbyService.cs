using BepInEx;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class HeadlessLobbyService : IDisposable
{
    internal static HeadlessLobbyService Instance { get; private set; }
    internal static bool IsHeadlessMode => Instance != null && Instance.IsEnabled;
    internal static bool IsHeadlessServer => IsHeadlessMode && MultiplayerSession.IsHosting;

    private readonly GunsawMultiplayerPlugin plugin;
    private readonly string[] commandLineArgs;
    internal bool IsEnabled { get; }
    private bool startPending;
    private bool warningSkipped;
    private int hiddenAvatarScene = int.MinValue;
    private int fixedTicks;
    private int fixedTicksAtLastSample;
    private float tpsSampleTime = -1f;
    private int tps;
    private float lastFixedTickTime = -1f;
    private float tickIntervalTotalMs;
    private float tickIntervalMaxMs;
    private int tickIntervalCount;
    private int lateTickCount;
    private float tickIntervalAverageMs;
    private float tickJitterMs;
    private int lateTickPercent;
    private Timer keepAliveTimer;
    private int keepAliveInFlight;
    private string defaultMapJson = "";
    private readonly Dictionary<string, HashSet<ushort>> votes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ushort> knownPeers = new();

    internal HeadlessLobbyService(GunsawMultiplayerPlugin plugin)
    {
        this.plugin = plugin;
        commandLineArgs = Environment.GetCommandLineArgs();
        IsEnabled = LobbySettingsSchema.HasCommandLineFlag(commandLineArgs, "-headlessLobby");
        Instance = this;
    }

    internal void Initialize()
    {
        if (!IsEnabled) return;
        HeadlessPresentation.Enable();
        ApplyCommandLineOptions();
        var mapPath = LobbySettingsSchema.CommandLineValue(commandLineArgs, "-headlessMap");
        if (string.IsNullOrEmpty(mapPath)) mapPath = Path.Combine(Paths.GameRootPath, "default_map.txt");
        try
        {
            var code = File.ReadAllText(mapPath).Trim();
            plugin.customLevelJson = CustomLevelCode.DecodeAndValidateLevelCode(code);
            plugin.customLevelCode = code;
            defaultMapJson = plugin.customLevelJson;
            GunsawMultiplayerPlugin.LogInfo("Headless lobby map loaded: " + mapPath);
        }
        catch (Exception exception)
        {
            GunsawMultiplayerPlugin.LogInfo("Headless lobby could not load map: " + exception.Message);
            plugin.customLevelJson = "";
        }
    }

    internal void Start()
    {
        if (!IsEnabled) return;
        if (string.IsNullOrEmpty(plugin.customLevelJson))
        {
            GunsawMultiplayerPlugin.LogInfo("Headless lobby disabled: no valid map.");
            return;
        }
        GunsawMultiplayerPlugin.LogInfo("Starting headless lobby.");
        if (SceneManager.GetActiveScene().name != "LevelSelect") SceneManager.LoadScene("LevelSelect");
        plugin.CreateLobby();
    }

    internal bool KeepRunning()
    {
        if (!IsEnabled) return false;
        var manager = GameManager.main;
        if (manager != null) manager.paused = false;
        Time.timeScale = 1f;
        Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
        return true;
    }

    internal bool UpdateEarly()
    {
        if (!IsEnabled) return false;
        UpdateTps();
        if (warningSkipped) return false;
        var warning = UnityEngine.Object.FindObjectOfType<ViolenceScreen>();
        if (warning == null) return false;
        warning.clicked = true;
        warningSkipped = true;
        return true;
    }

    internal void UpdateLobby()
    {
        if (!IsEnabled) return;
        SendHelpToNewPlayers();
        HideHostAvatar();
        if (!startPending || !MultiplayerSession.IsHosting || SceneLoader.main == null) return;
        startPending = false;
        try
        {
            MultiplayerSession.StartHostCustomLevel(plugin.customLevelJson, plugin.customLevelCode);
            plugin.StartCustomLevelLocally(plugin.customLevelJson);
            GunsawMultiplayerPlugin.LogInfo("Headless lobby custom level started.");
        }
        catch (Exception exception) { GunsawMultiplayerPlugin.LogInfo("Headless lobby could not start map: " + exception.Message); }
    }

    internal void OnLobbyCreated(string lobbyId, string relayKey, string directoryUrl)
    {
        if (!IsEnabled) return;
        StartKeepAlive(lobbyId, relayKey, directoryUrl);
        startPending = true;
    }

    internal void FixedTick()
    {
        if (!IsEnabled) return;
        var now = Time.realtimeSinceStartup;
        if (lastFixedTickTime >= 0f)
        {
            var intervalMs = (now - lastFixedTickTime) * 1000f;
            tickIntervalTotalMs += intervalMs;
            tickIntervalMaxMs = Mathf.Max(tickIntervalMaxMs, intervalMs);
            tickIntervalCount++;
            if (intervalMs > Time.fixedDeltaTime * 1500f) lateTickCount++;
        }
        lastFixedTickTime = now;
        Interlocked.Increment(ref fixedTicks);
    }

    internal bool TryHandleLobbyChatCommand(ushort senderId, string message)
    {
        if (!IsEnabled || !MultiplayerSession.IsHost || string.IsNullOrWhiteSpace(message)) return false;
        var command = message.Trim();
        if (string.Equals(command, "!help", StringComparison.OrdinalIgnoreCase))
        {
            SendHelp(senderId);
            return true;
        }
        if (string.Equals(command, "!tps", StringComparison.OrdinalIgnoreCase))
        {
            UpdateTps();
            var stats = MultiplayerSession.DebugStats();
            SendChat("TPS: " + tps + " | tick interval: avg " + tickIntervalAverageMs.ToString("0.0", CultureInfo.InvariantCulture) +
                " ms | max " + tickIntervalMaxMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms | jitter " +
                tickJitterMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms | late ticks: " + lateTickPercent + "% | RX: " + (stats.ReceivedBytesPerSecond / 1024f).ToString("0.0") +
                " KiB/s | TX: " + (stats.SentBytesPerSecond / 1024f).ToString("0.0") + " KiB/s");
            return true;
        }
        if (string.Equals(command, "!votedefault", StringComparison.OrdinalIgnoreCase))
            return RegisterVote(senderId, "default", "default map");
        if (string.Equals(command, "!vote restart", StringComparison.OrdinalIgnoreCase))
            return RegisterVote(senderId, "restart", "restart");
        const string changePrefix = "!vote change ";
        if (command.StartsWith(changePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var target = command.Substring(changePrefix.Length).Trim();
            if (string.IsNullOrEmpty(target))
            {
                SendChat("Usage: !vote change <map name or scene>", senderId);
                return true;
            }
            return RegisterVote(senderId, "change:" + target, "change to " + target);
        }
        return false;
    }

    private void SendHelpToNewPlayers()
    {
        if (!MultiplayerSession.IsHosting) return;
        var peers = MultiplayerSession.PeerIds();
        foreach (var peerId in peers)
            if (knownPeers.Add(peerId)) SendHelp(peerId);
        knownPeers.RemoveWhere(peerId => Array.IndexOf(peers, peerId) < 0);
    }

    private void StartKeepAlive(string lobbyId, string relayKey, string directoryUrl)
    {
        keepAliveTimer?.Dispose();
        keepAliveTimer = new Timer(_ =>
        {
            if (Interlocked.CompareExchange(ref keepAliveInFlight, 1, 0) != 0) return;
            try
            {
                MultiplayerSession.UpdatePing();
                var players = MultiplayerSession.PlayerCount;
                GunsawMultiplayerPlugin.HttpAt(directoryUrl, "PUT", "/v1/lobbies/" + lobbyId,
                    "{\"players\":" + players + ",\"map\":\"LevelLoader\"}", "Bearer " + relayKey);
            }
            catch (Exception exception) { GunsawMultiplayerPlugin.LogInfo("Headless keep-alive failed: " + exception.Message); }
            finally { Interlocked.Exchange(ref keepAliveInFlight, 0); }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private void UpdateTps()
    {
        var now = Time.realtimeSinceStartup;
        if (tpsSampleTime < 0f)
        {
            tpsSampleTime = now;
            fixedTicksAtLastSample = Interlocked.CompareExchange(ref fixedTicks, 0, 0);
            return;
        }
        var elapsed = now - tpsSampleTime;
        if (elapsed < 0.25f) return;
        var ticks = Interlocked.CompareExchange(ref fixedTicks, 0, 0);
        tps = Mathf.RoundToInt((ticks - fixedTicksAtLastSample) / elapsed);
        if (tickIntervalCount > 0)
        {
            tickIntervalAverageMs = tickIntervalTotalMs / tickIntervalCount;
            tickJitterMs = tickIntervalMaxMs - tickIntervalAverageMs;
            lateTickPercent = Mathf.RoundToInt(lateTickCount * 100f / tickIntervalCount);
        }
        else
        {
            tickIntervalAverageMs = 0f;
            tickJitterMs = 0f;
            lateTickPercent = 0;
        }
        tickIntervalTotalMs = 0f;
        tickIntervalMaxMs = 0f;
        tickIntervalCount = 0;
        lateTickCount = 0;
        fixedTicksAtLastSample = ticks;
        tpsSampleTime = now;
    }

    private void HideHostAvatar()
    {
        if (!IsHeadlessServer || SceneManager.GetActiveScene().name != "LevelLoader") return;
        var sceneHandle = SceneManager.GetActiveScene().handle;
        if (hiddenAvatarScene == sceneHandle) return;
        var player = PlayerScript.player;
        if (player == null || player.bodyScript == null) return;
        hiddenAvatarScene = sceneHandle;
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

    private bool RegisterVote(ushort senderId, string target, string description)
    {
        foreach (var vote in votes.Values) vote.Remove(senderId);
        if (!votes.TryGetValue(target, out var voters))
        {
            voters = new HashSet<ushort>();
            votes[target] = voters;
        }
        voters.Add(senderId);
        var needed = MultiplayerSession.PeerIds().Length / 2 + 1;
        SendChat("Vote " + description + ": " + voters.Count + "/" + needed + ".");
        if (voters.Count < needed) return true;
        votes.Clear();
        SendChat("Vote passed: " + description + ".");
        if (target == "restart") RestartCurrentLevel();
        else if (target == "default") StartCustomLevel(defaultMapJson);
        else StartMapChange(target.Substring("change:".Length));
        return true;
    }

    private void StartMapChange(string mapOrScene)
    {
        if (IsBuiltInScene(mapOrScene))
        {
            try
            {
                MultiplayerSession.EndHostCustomLevel(mapOrScene);
                SceneLoader.main.LoadScene(mapOrScene);
            }
            catch (Exception exception) { SendChat("Could not load scene: " + exception.Message); }
            return;
        }
        SendChat("Looking up map: " + mapOrScene + "...");
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var catalog = new WebClient().DownloadString(GunsawMultiplayerPlugin.CustomLevelsUrl);
                plugin.RunOnMainThread(() => LoadCatalogMap(catalog, mapOrScene));
            }
            catch (Exception exception) { plugin.RunOnMainThread(() => SendChat("Could not load map: " + exception.Message)); }
        });
    }

    private void LoadCatalogMap(string catalog, string requestedName)
    {
        try
        {
            HeadlessLevelEntry match = null;
            var requestedKey = NormalizeLevelName(requestedName);
            var catalogEntries = ParseCatalog(catalog);
            foreach (var entry in catalogEntries)
                if (NormalizeLevelName(entry.name) == requestedKey) { match = entry; break; }
            if (match == null || string.IsNullOrWhiteSpace(match.code))
            {
                var suggestions = new List<string>();
                foreach (var entry in catalogEntries)
                    if (!string.IsNullOrWhiteSpace(entry.name) &&
                        NormalizeLevelName(entry.name).Contains(requestedKey)) suggestions.Add(entry.name);
                throw new InvalidDataException(suggestions.Count == 0 ? "map not found" :
                    "map not found; try: " + string.Join(" | ", suggestions.GetRange(0, Math.Min(3, suggestions.Count)).ToArray()));
            }
            var mapJson = CustomLevelCode.DecodeAndValidateCatalogLevelCode(match.code);
            StartCustomLevel(mapJson);
        }
        catch (Exception exception) { SendChat("Could not load map: " + exception.Message); }
    }

    private void StartCustomLevel(string levelJson)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(levelJson)) throw new InvalidDataException("no default map is loaded");
            plugin.customLevelJson = levelJson;
            MultiplayerSession.StartHostCustomLevel(levelJson, Compression.Compress(levelJson));
            MultiplayerSession.NotifyHostSceneReload("LevelLoader", true);
            plugin.StartCustomLevelLocally(levelJson);
        }
        catch (Exception exception) { SendChat("Could not load map: " + exception.Message); }
    }

    private void RestartCurrentLevel()
    {
        try
        {
            var loader = SceneLoader.main;
            if (loader == null) throw new InvalidOperationException("scene loader is not ready");
            loader.LoadScene(SceneManager.GetActiveScene().name);
        }
        catch (Exception exception) { SendChat("Could not restart level: " + exception.Message); }
    }

    private static bool IsBuiltInScene(string value)
    {
        value = (value ?? "").Trim();
        if (value.StartsWith("actualLevel", StringComparison.OrdinalIgnoreCase)) value = value.Substring("actualLevel".Length);
        else if (value.StartsWith("campaign", StringComparison.OrdinalIgnoreCase)) value = value.Substring("campaign".Length);
        else return false;
        return int.TryParse(value, out _);
    }

    private static string NormalizeLevelName(string value)
    {
        var source = value ?? "";
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
        return builder.ToString();
    }

    private static List<HeadlessLevelEntry> ParseCatalog(string catalog)
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

    private static void SendHelp(ushort targetPeerId)
    {
        SendChat("Maps: !vote change <name>; scenes: actualLevel1/campaign6; !vote restart | !tps | !votedefault | !help", targetPeerId);
    }

    private static void SendChat(string text, ushort targetPeerId = 0)
    {
        ChatPacket packet;
        if (ChatService.TryCreate(text, true, out packet)) MultiplayerSession.Send(packet, targetPeerId);
    }

    private sealed class HeadlessLevelEntry { public string name; public string code; }

    private void ApplyCommandLineOptions()
    {
        var value = LobbySettingsSchema.CommandLineValue(commandLineArgs, "--master");
        if (!string.IsNullOrWhiteSpace(value) && GunsawMultiplayerPlugin.TryNormalizeServerAddress(value, out var normalized))
        {
            plugin.masterUrl.Value = normalized;
            plugin.lobbyServerAddress = GunsawMultiplayerPlugin.DisplayServerAddress(normalized);
        }
        value = LobbySettingsSchema.CommandLineValue(commandLineArgs, "--name");
        if (!string.IsNullOrWhiteSpace(value)) plugin.lobbyName = value.Trim();
        value = LobbySettingsSchema.CommandLineValue(commandLineArgs, "--host");
        if (!string.IsNullOrWhiteSpace(value)) plugin.playerName = value.Trim();
        LobbySettingsSchema.ApplyCommandLine(plugin.lobbySettings, commandLineArgs);
        if (plugin.lobbySettings.GrabOnlyUnconscious) plugin.lobbySettings.CanGrab = true;
        GunsawMultiplayerPlugin.LogInfo("Headless settings: lobby=" + plugin.lobbyName + ", host=" + plugin.playerName + ", max=" + plugin.lobbySettings.MaxPlayers + ".");
    }

    public void Dispose()
    {
        keepAliveTimer?.Dispose();
        keepAliveTimer = null;
        if (ReferenceEquals(Instance, this)) Instance = null;
    }
}
