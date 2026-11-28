using DiscordIPC.Internal;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GunsawMultiplayer;
using UnityEngine;

internal sealed class HeadlessLobbyWeb : IDisposable
{
    private const int MaxRequestBytes = 12 * 1024 * 1024;
    private const string FontUrl = "https://online-fonts.com/sites/default/files/2021-09/Retro%20Gaming.ttf";
    private readonly HttpListener listener = new();
    private readonly GunsawMultiplayerPlugin plugin;
    private readonly HeadlessLobbyService service;
    private string authority = "";
    private byte[] retroGamingFont = Array.Empty<byte>();
    private DateTime retryFontDownloadAfter;
    private volatile bool disposed;
    private bool quitRequested;

    internal HeadlessLobbyWeb(GunsawMultiplayerPlugin plugin, HeadlessLobbyService service)
    {
        this.plugin = plugin;
        this.service = service;
    }

    internal void Start(int port)
    {
        try
        {
            if (port is < 1 or > 65535) 
                throw new ArgumentException("--web-port must be between 1 and 65535.");
            authority = "127.0.0.1:" + port;
            listener.Prefixes.Add("http://" + authority + "/");
            listener.Start();
            GunsawMultiplayerPlugin.LogInfo("Headless lobby admin: http://" + authority + "/");
            _ = Listen();
        }
        catch (Exception exception) { GunsawMultiplayerPlugin.LogInfo("Could not start headless lobby admin: " + exception.Message); }
    }

    private async Task Listen()
    {
        while (!disposed && !quitRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception exception)
            {
                if (!disposed) GunsawMultiplayerPlugin.LogInfo("Headless lobby admin stopped: " + exception.Message);
                return;
            }

            try { await Handle(context).ConfigureAwait(false); }
            catch (Exception exception)
            {
                try { await Respond(context.Response, 400, "text/plain", Encoding.UTF8.GetBytes(exception.Message)).ConfigureAwait(false); }
                catch (Exception) { context.Response.Abort(); }
            }
        }
        if (quitRequested && !disposed) plugin.RunOnMainThread(() => Application.Quit(0));
    }

    private async Task Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        if (request.UserHostName != authority || request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address))
        {
            await Respond(response, 403, "text/plain", Array.Empty<byte>()).ConfigureAwait(false);
            return;
        }

        var path = request.Url?.AbsolutePath;
        switch (request.HttpMethod)
        {
            case "GET" when path == "/retro-gaming.ttf":
            {
                var font = await GetRetroGamingFont().ConfigureAwait(false);
                if (font.Length > 0) response.Headers["Cache-Control"] = "private, max-age=86400";
                await Respond(response, font.Length > 0 ? 200 : 503, "font/ttf", font).ConfigureAwait(false);
                return;
            }
            case "GET":
            {
                var asset = path switch
                {
                    "/" => ("index.html", "text/html"),
                    "/lobby.css" => ("lobby.css", "text/css"),
                    "/lobby.js" => ("lobby.js", "application/javascript"),
                    _ => ("", "")
                };
                if (asset.Item1.Length != 0)
                {
                    using var stream = typeof(HeadlessLobbyWeb).Assembly.GetManifestResourceStream("GunsawMultiplayer.UI.Headless." + asset.Item1)
                                       ?? throw new InvalidOperationException("Missing web resource: " + asset.Item1);
                    using var bytes = new MemoryStream();
                    await stream.CopyToAsync(bytes).ConfigureAwait(false);
                    await Respond(response, 200, asset.Item2, bytes.ToArray()).ConfigureAwait(false);
                    return;
                }
                switch (path)
                {
                    case "/api/state":
                        await RespondJson(response, await MainThread(() => Snapshot()).ConfigureAwait(false)).ConfigureAwait(false);
                        return;
                    case "/api/weapons":
                        await RespondJson(response, await MainThread(() => MiniJson.Serialize(new Dictionary<string, object>
                        {
                            ["names"] = WeaponPresetProvider.AllWeapons().Select(preset => preset.name)
                        })).ConfigureAwait(false)).ConfigureAwait(false);
                        return;
                    case "/api/tuner":
                        await RespondJson(response, await MainThread(TunerSnapshot).ConfigureAwait(false)).ConfigureAwait(false);
                        return;
                }

                break;
            }
            case "POST" when path == "/api/action":
            {
                if (request.Headers["Origin"] != "http://" + authority || request.Headers["X-Gunsaw-Admin"] != "1")
                {
                    await Respond(response, 403, "text/plain", Encoding.UTF8.GetBytes("Open the panel at http://" + authority + "/.")).ConfigureAwait(false);
                    return;
                }
                if (request.ContentLength64 < 1 || request.ContentLength64 > MaxRequestBytes)
                    throw new ArgumentException("Invalid request size.");
                var bytes = new byte[(int)request.ContentLength64];
                using (var timeout = new CancellationTokenSource())
                {
                    var read = ReadBody(request.InputStream, bytes);
                    if (await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10), timeout.Token)).ConfigureAwait(false) != read)
                    {
                        response.Abort();
                        request.InputStream.Close();
                        return;
                    }
                    timeout.Cancel();
                    await read.ConfigureAwait(false);
                }
                var action = HeadlessWebAction.Parse(Encoding.UTF8.GetString(bytes));
                if (action.action == "close")
                {
                    var (removal, snapshot) = await MainThread(() =>
                    {
                        if (!MultiplayerSession.IsHosting) throw new InvalidOperationException("No hosted lobby is active.");
                        if (plugin.UpdatingLobby) throw new InvalidOperationException("Wait for the lobby update to finish.");
                        var task = plugin.CloseHostedLobby();
                        return (task, Snapshot(stopping: true));
                    }).ConfigureAwait(false);
                    try { await RespondJson(response, snapshot).ConfigureAwait(false); }
                    finally
                    {
                        await removal.ConfigureAwait(false);
                        quitRequested = true;
                    }
                    return;
                }
                await RespondJson(response, await MainThread(() =>
                {
                    Apply(action);
                    return Snapshot();
                }).ConfigureAwait(false)).ConfigureAwait(false);
                return;
            }
        }

        await Respond(response, 404, "text/plain", Array.Empty<byte>()).ConfigureAwait(false);
    }

    private async Task<byte[]> GetRetroGamingFont()
    {
        if (retroGamingFont.Length > 0 || DateTime.UtcNow < retryFontDownloadAfter) return retroGamingFont;
        retryFontDownloadAfter = DateTime.UtcNow.AddMinutes(1);
        try
        {
            var request = (HttpWebRequest)WebRequest.Create(FontUrl);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var abort = timeout.Token.Register(request.Abort);
            using var response = await request.GetResponseAsync().ConfigureAwait(false);
            using var stream = response.GetResponseStream();
            var font = new byte[31024];
            await ReadBody(stream, font).ConfigureAwait(false);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = BitConverter.ToString(sha256.ComputeHash(font)).Replace("-", "");
            if (hash == "DFD827142124C0FAB4B916A4C72DBF4C91A9069A150AA8BE71D0566F6D612066")
                retroGamingFont = font;
        }
        catch (Exception exception) when (exception is WebException or IOException or OperationCanceledException)
        {
            GunsawMultiplayerPlugin.LogInfo("Could not download Retro Gaming font: " + exception.Message);
        }
        return retroGamingFont;
    }

    private static async Task ReadBody(Stream stream, byte[] bytes)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes, offset, bytes.Length - offset).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Incomplete request.");
            offset += read;
        }
    }

    private async Task<T> MainThread<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        plugin.RunOnMainThread(() =>
        {
            if (disposed || result.Task.IsCompleted) return;
            try { result.TrySetResult(action()); }
            catch (Exception exception) { result.TrySetException(exception); }
        });
        using var timeout = new CancellationTokenSource();
        if (await Task.WhenAny(result.Task, Task.Delay(5000, timeout.Token)).ConfigureAwait(false) != result.Task)
        {
            result.TrySetCanceled();
            throw new TimeoutException("The game is busy. Try again after it finishes loading.");
        }
        timeout.Cancel();
        return await result.Task.ConfigureAwait(false);
    }

    private static Task RespondJson(HttpListenerResponse response, string json)
        => Respond(response, 200, "application/json", Encoding.UTF8.GetBytes(json));

    private static async Task Respond(HttpListenerResponse response, int status, string contentType, byte[] body)
    {
        response.StatusCode = status;
        response.ContentType = contentType == "font/ttf" ? contentType : contentType + "; charset=utf-8";
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
        response.Close();
    }

    private string Snapshot(bool stopping = false)
    {
        var hosting = MultiplayerSession.IsHosting;
        var players = new List<Dictionary<string, object>>();
        if (hosting)
        {
            players.Add(new Dictionary<string, object>
            {
                ["id"] = 0, ["name"] = MultiplayerSession.LocalPlayerName, ["ping"] = 0, ["host"] = true
            });
            foreach (var id in MultiplayerSession.PeerIds())
            {
                var peer = MultiplayerSession.GetPeer(id);
                if (peer == null) continue;
                players.Add(new Dictionary<string, object>
                {
                    ["id"] = id, ["name"] = peer.Name,
                    ["ping"] = peer.PingMs, ["isOperator"] = service.IsOperator(id)
                });
            }
        }

        return MiniJson.Serialize(new Dictionary<string, object>
        {
            ["version"] = GunsawMultiplayerPlugin.PluginVersion,
            ["status"] = stopping ? "Lobby closed. Stopping the headless server…" : plugin.status,
            ["map"] = hosting ? MultiplayerSession.GetHostMapName() : "",
            ["hosting"] = hosting,
            ["stopping"] = stopping,
            ["busy"] = plugin.CreatingLobby || plugin.UpdatingLobby,
            ["canBan"] = plugin.CanBanPlayers,
            ["settings"] = GetSettings().Select(setting => setting.ToJson()),
            ["players"] = players,
            ["presets"] = plugin.LobbyPresets.Select(preset => preset.name),
            ["maps"] = CustomLevelBrowserUi.CatalogLevelNames(),
            ["scenes"] = service.BuiltInScenes.Select(scene => new Dictionary<string, object>
            {
                ["id"] = scene, ["name"] = LevelNames.Resolve(scene)
            })
        });
    }

    private void Apply(HeadlessWebAction request)
    {
        var value = request.value ?? "";
        if (plugin.CreatingLobby || plugin.UpdatingLobby)
        {
            if (request.action is "settings" or "create" or "preset-save" or "preset-load" or "reset")
                throw new InvalidOperationException("Wait for the lobby update to finish.");
        }
        switch (request.action)
        {
            case "settings":
                ApplySettings(request.settings);
                if (MultiplayerSession.IsHosting) plugin.UpdateHostedLobby();
                else plugin.status = "Lobby settings saved.";
                return;
            case "create":
                if (MultiplayerSession.IsHosting) throw new InvalidOperationException("A lobby is already running.");
                ApplySettings(request.settings);
                service.CreateLobby();
                return;
            case "reset":
                ApplyPresetSettings(new LobbySettings());
                return;
            case "preset-save":
                ApplySettings(request.settings);
                plugin.SaveLobbyPreset(value);
                if (MultiplayerSession.IsHosting) plugin.UpdateHostedLobby();
                else plugin.status = "Preset saved.";
                return;
            case "preset-load":
            case "preset-delete":
                var preset = plugin.LobbyPresets.FirstOrDefault(item => item.name == value)
                    ?? throw new ArgumentException("Preset not found.");
                if (request.action == "preset-delete") plugin.DeleteLobbyPreset(preset);
                else ApplyPresetSettings(preset.settings.Clone());
                return;
            case "addon":
                var button = GetAddonFields().FirstOrDefault(item => item.Control.key == value && item.Click != null).Click
                    ?? throw new ArgumentException("Unknown add-on button.");
                button();
                return;
        }

        if (!MultiplayerSession.IsHosting) throw new InvalidOperationException("Create a lobby first.");
        switch (request.action)
        {
            case "restart":
            case "default":
            case "level":
            case "change":
            case "scene":
                service.ManageLevel(request.action, value);
                return;
            case "ban":
                BanByName(value);
                return;
            case "kick":
            case "operator":
                Moderate(request);
                return;
            default:
                throw new ArgumentException("Unknown action.");
        }
    }

    private void BanByName(string name)
    {
        if (!plugin.CanBanPlayers) throw new InvalidOperationException("Bans require a lobby directory.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a player name.");
        name = name.Trim();
        var matches = MultiplayerSession.PeerIds()
            .Where(id => string.Equals(MultiplayerSession.GetPeer(id)?.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0) throw new InvalidOperationException("That player is no longer in the lobby.");
        if (matches.Length > 1 || string.Equals(MultiplayerSession.LocalPlayerName, name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The lobby directory bans by name. This name is shared by multiple players.");
        var peer = MultiplayerSession.GetPeer(matches[0]);
        if (peer == null || !string.Equals(peer.Name, name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That player is no longer in the lobby.");
        plugin.BanPlayerFromCommand(peer.Name, matches[0]);
    }

    private void Moderate(HeadlessWebAction request)
    {
        if (request.player < 1 || request.player > ushort.MaxValue || request.player == MultiplayerSession.LocalPeerId)
            throw new ArgumentException("Select a connected player.");
        var id = (ushort)request.player;
        var peer = MultiplayerSession.GetPeer(id);
        if (peer == null || peer.Name != request.value)
            throw new InvalidOperationException("That player is no longer in the lobby.");
        switch (request.action)
        {
            case "kick":
                plugin.status = peer.Name + " was kicked from the lobby.";
                MultiplayerSession.KickPeer(id, plugin.status);
                break;
            case "operator":
                service.SetOperator(service.IsOperator(id) ? null : peer);
                break;
        }
    }

    private void ApplyPresetSettings(LobbySettings settings)
    {
        var hosting = MultiplayerSession.IsHosting;
        settings.Parse(hosting ? MultiplayerSession.MaxPlayers : 4);
        if (hosting) settings.ConnectionMode = plugin.lobbySettings.ConnectionMode;
        plugin.lobbySettings = settings;
        if (hosting) plugin.UpdateHostedLobby();
        else plugin.status = "Lobby settings saved.";
        plugin.SaveLobbyPreferences();
    }

    private void ApplySettings((string Key, string Value)[] changes)
    {
        if (changes.Length == 0) return;
        var next = plugin.lobbySettings.Clone();
        var host = plugin.playerName;
        var lobby = plugin.lobbyName;
        var server = plugin.masterUrl.Value;
        var brutal = MultiplayerSession.ReadBrutalMode();
        var hosting = MultiplayerSession.IsHosting;
        var addonFields = GetAddonFields().ToDictionary(item => item.Control.key, StringComparer.Ordinal);
        var addonChanges = new List<(Func<string, bool> Write, string Value, string Label)>();
        foreach (var (key, value) in changes)
        {
            switch (key)
            {
                case "PlayerName":
                    if (hosting) throw new InvalidOperationException("Close the lobby before changing the host name.");
                    host = ReadName(value, 32, "Player");
                    continue;
                case "LobbyName":
                    lobby = ReadName(value, 48, "Lobby");
                    continue;
                case "Server":
                    if (hosting) throw new InvalidOperationException("Close the lobby before changing the relay server.");
                    if (value.Length > 255 || !GunsawMultiplayerPlugin.TryNormalizeServerAddress(value, out server))
                        throw new ArgumentException("Invalid relay server address.");
                    continue;
                case "Brutal":
                    if (!bool.TryParse(value, out brutal)) throw new ArgumentException("Invalid difficulty.");
                    continue;
            }
            if (addonFields.TryGetValue(key, out var addon) && addon.Write != null)
            {
                if (value.Length > addon.Control.length) throw new ArgumentException(addon.Control.label + " is too long.");
                addonChanges.Add((addon.Write, value, addon.Control.label));
                continue;
            }
            var field = LobbySettingsSchema.Fields.FirstOrDefault(item => item.Field.Name == key).Field;
            if (field == null) throw new ArgumentException("Unknown setting: " + key);
            if (hosting && key == nameof(LobbySettings.ConnectionMode))
                throw new InvalidOperationException("Close the lobby before changing the connection mode.");
            if (field.FieldType == typeof(bool) && bool.TryParse(value, out var enabled)) field.SetValue(next, enabled);
            else if (field.FieldType.IsEnum && Enum.GetNames(field.FieldType).Contains(value))
                field.SetValue(next, Enum.Parse(field.FieldType, value));
            else if (field.FieldType == typeof(string) && value.Length <= Limit(field.Name) &&
                     !value.Any(character => char.IsControl(character) && (field.Name != nameof(LobbySettings.SawTuner) || character != '\n')))
                field.SetValue(next, value);
            else throw new ArgumentException("Invalid value for " + Label(field.Name) + ".");
        }

        next.Parse(hosting ? MultiplayerSession.MaxPlayers : 4);
        foreach (var (write, value, label) in addonChanges)
            if (!write(value)) throw new ArgumentException("Invalid value for " + label + ".");
        plugin.lobbySettings = next;
        plugin.lobbyName = lobby;
        plugin.playerName = host;
        if (plugin.masterUrl.Value != server)
        {
            plugin.masterUrl.Value = server;
            plugin.lobbyServerAddress = GunsawMultiplayerPlugin.DisplayServerAddress(server);
            plugin.Config.Save();
        }
        service.SaveDifficulty(brutal);
        plugin.SaveLobbyPreferences();
    }

    private IEnumerable<HeadlessWebSetting> GetSettings()
    {
        var hosting = MultiplayerSession.IsHosting;
        yield return new HeadlessWebSetting { key = "PlayerName", label = "Host name", value = plugin.playerName, length = 32, locked = hosting };
        yield return new HeadlessWebSetting { key = "LobbyName", label = "Lobby name", value = plugin.lobbyName, length = 48 };
        yield return new HeadlessWebSetting { key = "Server", label = "Relay server", value = plugin.lobbyServerAddress, length = 255, locked = hosting };
        yield return new HeadlessWebSetting { key = "Brutal", label = "Brutal mode", value = MultiplayerSession.ReadBrutalMode().ToString(), kind = "toggle" };

        foreach (var (field, option) in LobbySettingsSchema.Fields)
        {
            yield return new HeadlessWebSetting
            {
                key = field.Name,
                label = Label(field.Name),
                help = option.Description,
                value = field.GetValue(plugin.lobbySettings)?.ToString() ?? "",
                length = Limit(field.Name),
                kind = field.FieldType == typeof(bool) ? "toggle" : field.FieldType.IsEnum ? "choice" : Kind(field.Name),
                options = field.FieldType.IsEnum ? Enum.GetNames(field.FieldType) : Array.Empty<string>(),
                rules = LobbySettingsSchema.Visibility(field.Name),
                locked = hosting && field.Name == nameof(LobbySettings.ConnectionMode)
            };
        }

        foreach (var addon in GetAddonFields()) yield return addon.Control;
    }

    private static IEnumerable<(HeadlessWebSetting Control, Func<string, bool>? Write, Action? Click)> GetAddonFields()
    {
        foreach (var entry in MultiplayerApi.LobbySections.ToArray())
        {
            var guid = entry.Key;
            var section = entry.Value;
            var index = 0;
            HeadlessWebSetting Control(string label, string kind = "text", string value = "", int length = 16)
                => new() { key = "addon:" + guid + ":" + index++, group = section.Title, label = label, kind = kind, value = value, length = length };

            foreach (var (label, read, write) in section.Toggles)
            {
                var value = false;
                MultiplayerApi.Invoke(guid, () => value = read());
                yield return (Control(label, "toggle", value.ToString()), text =>
                {
                    if (!bool.TryParse(text, out var enabled)) return false;
                    var success = false;
                    MultiplayerApi.Invoke(guid, () => { write(enabled); success = true; });
                    return success;
                }, null);
            }
            foreach (var (label, read, write, length, kind, range) in section.Inputs)
            {
                var value = "";
                MultiplayerApi.Invoke(guid, () => value = read() ?? "");
                var control = Control(label, kind == LobbySection.InputKind.Text ? "text" : "number", value, length);
                control.help = range ?? "";
                yield return (control, text =>
                {
                    var success = false;
                    MultiplayerApi.Invoke(guid, () => success = write(text));
                    return success;
                }, null);
            }
            foreach (var (label, options, read, write) in section.Choices)
            {
                var value = -1;
                MultiplayerApi.Invoke(guid, () => value = read());
                var control = Control(label, "choice", value.ToString());
                control.options = options;
                yield return (control, text =>
                {
                    if (!int.TryParse(text, out var choice) || choice < 0 || choice >= options.Length) return false;
                    var success = false;
                    MultiplayerApi.Invoke(guid, () => { write(choice); success = true; });
                    return success;
                }, null);
            }
            foreach (var (label, click, _) in section.Buttons)
                yield return (Control(label, "button"), null, () => MultiplayerApi.Invoke(guid, click));
        }
    }

    private static string ReadName(string value, int limit, string fallback)
    {
        if (value.Length > limit || value.Any(char.IsControl)) throw new ArgumentException("Invalid name.");
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string Label(string name) => name switch
    {
        "GrabOnlyUnconscious" => "Only unconscious",
        "RespawnTime" => "Respawn delay",
        "TeamsCfg" => "Teams cfg",
        "ConnectionMode" => "Connection",
        "GGOnDeath" => "GG on death",
        _ => Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ")
    };

    private static string Kind(string name) => name switch
    {
        "Password" => "text",
        "StartingWeapon" or "RespawnWeapon" => "weapons",
        "StartingAmmo" or "RespawnAmmo" => "ammo",
        "GGSequence" or "TeamsCfg" or "SawTuner" => "editor",
        _ => "number"
    };

    private static int Limit(string name) => name switch
    {
        "Password" => 128,
        "StartingWeapon" or "RespawnWeapon" => 512,
        "GGSequence" or "TeamsCfg" => 4096,
        "SawTuner" => 150000,
        _ => 64
    };

    private static string TunerSnapshot()
    {
        var characters = new List<Dictionary<string, object>>();
        foreach (var path in NetworkAvatarManager.knownCharacterPrefabs)
        {
            var prefab = SawTuner.Prefab(path);
            if (prefab == null) continue;
            characters.Add(new Dictionary<string, object>
            {
                ["path"] = path,
                ["defaults"] = Enumerable.Range(0, SawTunerValues.Fields.Length).Select(index =>
                {
                    var value = SawTuner.DefaultValue(prefab, index);
                    return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
                })
            });
        }
        return MiniJson.Serialize(new Dictionary<string, object>
        {
            ["characters"] = characters,
            ["stats"] = SawTunerValues.Labels.Select((label, index) => new Dictionary<string, object>
            {
                ["label"] = label, ["min"] = SawTunerValues.Mins[index], ["max"] = SawTunerValues.Maxs[index]
            })
        });
    }

    public void Dispose()
    {
        disposed = true;
        listener.Close();
    }
}

internal sealed class HeadlessWebAction
{
    internal string action = "";
    internal string value = "";
    internal int player;
    internal (string Key, string Value)[] settings = [];

    internal static HeadlessWebAction Parse(string json)
    {
        if (MiniJson.Deserialize(json, 4) is not Dictionary<string, object> data || !data.TryGetValue("action", out var action) || action is not string name)
            throw new ArgumentException("Missing action.");
        
        var request = new HeadlessWebAction { action = name };
        
        if (data.TryGetValue("value", out var value))
            request.value = value as string ?? throw new ArgumentException("Invalid action value.");
        
        if (data.TryGetValue("player", out var player))
            request.player = player is long id and >= 0 and <= ushort.MaxValue ? (int)id : throw new ArgumentException("Invalid player.");
        
        if (data.TryGetValue("settings", out var settings))
        {
            if (settings is not List<object> entries) throw new ArgumentException("Invalid settings.");
            request.settings =
            [
                .. entries.Select(entry =>
                {
                    if (entry is Dictionary<string, object> item &&
                        item.TryGetValue("key", out var key) && key is string setting &&
                        item.TryGetValue("value", out var value) && value is string text)
                        return (setting, text);
                    throw new ArgumentException("Invalid setting.");
                })
            ];
        }
        return request;
    }
}

internal sealed class HeadlessWebSetting
{
    internal string key = "";
    internal string label = "";
    internal string help = "";
    internal string group = "";
    internal string value = "";
    internal string kind = "text";
    internal string[] options = [];
    internal IReadOnlyList<LobbySettingVisibleWhenAttribute> rules = [];
    internal int length;
    internal bool locked;

    internal Dictionary<string, object> ToJson() => new()
    {
        ["key"] = key, ["label"] = label, ["help"] = help, ["group"] = group, ["value"] = value,
        ["kind"] = kind, ["options"] = options, ["length"] = length, ["locked"] = locked,
        ["rules"] = rules.Select(rule => new Dictionary<string, object> { ["key"] = rule.Setting, ["value"] = rule.Value })
    };
}
