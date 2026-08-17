using System.Diagnostics;
using System.Globalization;

namespace DiscordIPC.Internal
{
    internal sealed class DiscordIpcClient
    {
        private static readonly string[] UnixTempVariables = { "XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP" };
        private readonly object _sync = new();
        private Connection _connection;
        private Activity _queuedActivity;
        private bool _ready;
        private Thread _updateThread;
        private int _generation;
        private IPCUser _user = new IPCUser();

        public Action Tick;
        public Action<string> Log;
        public Action<string> Join;
        public Action<IPCUser> JoinRequest;

        public bool IsConnected
        {
            get { lock (_sync) return _connection != null && _connection.IsOpen; }
        }

        public IPCUser User
        {
            get { lock (_sync) return _user; }
        }

        public void Start(long appId)
        {
            Connection old;
            int generation;
            lock (_sync)
            {
                old = _connection;
                _connection = null;
                _ready = false;
                _user = new IPCUser();
                _generation++;
                generation = _generation;
            }
            if (old != null) old.Close();

            Connection connection = Open();
            if (connection == null)
            {
                WriteLog("Discord IPC: Discord pipe/socket was not found.");
                return;
            }

            lock (_sync)
            {
                if (generation != _generation)
                {
                    connection.Close();
                    return;
                }
                _connection = connection;
            }

            string clientId = appId.ToString(CultureInfo.InvariantCulture);
            string nonce = Guid.NewGuid().ToString();
            string handshakeJson = "{\"v\":1,\"client_id\":\"" + clientId + "\",\"nonce\":\"" + nonce + "\"}";
            WriteLog("Discord IPC: sending HANDSHAKE v=1, client_id=" + clientId +
                     ", nonce=" + nonce + ", payload_bytes=" + System.Text.Encoding.UTF8.GetByteCount(handshakeJson));
            connection.WriteJson(Opcode.Handshake, handshakeJson);
            WriteLog("Discord IPC: HANDSHAKE frame written to pipe");

            _updateThread = new Thread(delegate() { UpdateLoop(generation); });
            _updateThread.IsBackground = true;
            _updateThread.Name = "Discord IPC - Update thread";
            _updateThread.Start();
        }

        public void QueueActivity(Activity activity)
        {
            lock (_sync) _queuedActivity = activity;
        }

        public bool AcceptJoinRequest(string userId)
        {
            return SendUserCommand("SEND_ACTIVITY_JOIN_INVITE", userId);
        }

        public bool RejectJoinRequest(string userId)
        {
            return SendUserCommand("CLOSE_ACTIVITY_REQUEST", userId);
        }

        public void Stop()
        {
            Connection old;
            lock (_sync)
            {
                old = _connection;
                _connection = null;
                _ready = false;
                _queuedActivity = null;
                _user = new IPCUser();
                _generation++;
            }
            if (old != null) old.Close();
        }

        private Connection Open()
        {
            PlatformID platform = Environment.OSVersion.Platform;
            bool windows = platform == PlatformID.Win32NT || platform == PlatformID.Win32Windows ||
                           platform == PlatformID.Win32S || platform == PlatformID.WinCE;

            if (windows)
            {
                Exception lastError = null;
                for (int i = 0; i < 10; i++)
                {
                    string pipeName = "discord-ipc-" + i;
                    try
                    {
                        Connection connection = new WinConnection(pipeName, HandlePacket, HandleDisconnected);
                        WriteLog("Discord IPC: connected to named pipe " + pipeName);
                        return connection;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }
                }
                if (lastError != null)
                    WriteLog("Discord IPC: no Windows IPC pipe accepted the connection. Last error: " + lastError.Message);
                return null;
            }

            List<string> directories = new List<string>();
            for (int i = 0; i < UnixTempVariables.Length; i++)
            {
                string directory = Environment.GetEnvironmentVariable(UnixTempVariables[i]);
                if (!string.IsNullOrEmpty(directory) && !directories.Contains(directory))
                    directories.Add(directory);
            }
            if (!directories.Contains("/tmp")) directories.Add("/tmp");

            for (int d = 0; d < directories.Count; d++)
            {
                for (int i = 0; i < 10; i++)
                {
                    string path = Path.Combine(directories[d], "discord-ipc-" + i);
                    try
                    {
                        Connection connection = new UnixConnection(path, HandlePacket, HandleDisconnected);
                        WriteLog("Discord IPC: connected to unix socket " + path);
                        return connection;
                    }
                    catch { }
                }
            }
            return null;
        }

        private void UpdateLoop(int generation)
        {
            while (IsConnected && IsGeneration(generation))
            {
                try
                {
                    if (IsReady())
                    {
                        SendActivity();
                        Action tick = Tick;
                        if (tick != null) tick();
                    }
                }
                catch (Exception ex)
                {
                    WriteLog("Discord IPC update error: " + ex.Message);
                }
                Thread.Sleep(5000);
            }
        }

        private bool IsReady()
        {
            lock (_sync) return _ready;
        }

        private bool IsGeneration(int generation)
        {
            lock (_sync) return _generation == generation;
        }

        private void SendActivity()
        {
            Connection connection;
            Activity activity;
            lock (_sync)
            {
                connection = _connection;
                activity = _queuedActivity;
                _queuedActivity = null;
            }
            if (connection == null || activity == null) return;

            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["cmd"] = "SET_ACTIVITY";

            Dictionary<string, object> args = new Dictionary<string, object>();
            args["pid"] = Process.GetCurrentProcess().Id;
            args["activity"] = ActivityToJson(activity);
            payload["args"] = args;
            connection.Write(Opcode.Frame, payload);
        }

        private bool SendUserCommand(string command, string userId)
        {
            if (string.IsNullOrEmpty(userId) || userId == "none") return false;

            Connection connection;
            bool ready;
            lock (_sync)
            {
                connection = _connection;
                ready = _ready;
            }
            if (connection == null || !connection.IsOpen || !ready) return false;

            Dictionary<string, object> args = new Dictionary<string, object>();
            args["user_id"] = userId;

            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["cmd"] = command;
            payload["args"] = args;
            connection.Write(Opcode.Frame, payload);
            return true;
        }

        private void Subscribe(string eventName)
        {
            Connection connection;
            lock (_sync) connection = _connection;
            if (connection == null || !connection.IsOpen) return;

            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["cmd"] = "SUBSCRIBE";
            payload["evt"] = eventName;
            connection.Write(Opcode.Frame, payload);
        }

        private static Dictionary<string, object> ActivityToJson(Activity activity)
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            if (activity.Details != null) obj["details"] = activity.Details;
            if (activity.State != null) obj["state"] = activity.State;

            if (activity.Timestamps != null)
            {
                Dictionary<string, object> timestamps = new Dictionary<string, object>();
                timestamps["start"] = activity.Timestamps.Start;
                if (activity.Timestamps.End.HasValue) timestamps["end"] = activity.Timestamps.End.Value;
                obj["timestamps"] = timestamps;
            }

            if (activity.Assets != null)
            {
                Dictionary<string, object> assets = new Dictionary<string, object>();
                if (activity.Assets.LargeImage != null) assets["large_image"] = activity.Assets.LargeImage;
                if (activity.Assets.LargeText != null) assets["large_text"] = activity.Assets.LargeText;
                if (activity.Assets.SmallImage != null) assets["small_image"] = activity.Assets.SmallImage;
                if (activity.Assets.SmallText != null) assets["small_text"] = activity.Assets.SmallText;
                obj["assets"] = assets;
            }

            if (activity.Buttons != null &&
                activity.Buttons.Count > 0 &&
                string.IsNullOrEmpty(activity.JoinSecret))
            {
                List<object> buttons = new List<object>();

                for (int i = 0; i < activity.Buttons.Count; i++)
                {
                    Dictionary<string, object> button =
                        new Dictionary<string, object>();

                    button["label"] = activity.Buttons[i].Label;
                    button["url"] = activity.Buttons[i].Url;

                    buttons.Add(button);
                }

                obj["buttons"] = buttons;
            }

            if (activity.Party != null && !string.IsNullOrEmpty(activity.Party.Id))
            {
                Dictionary<string, object> party = new Dictionary<string, object>();
                party["id"] = activity.Party.Id;
                party["size"] = new object[] { activity.Party.Size, activity.Party.MaxSize };
                obj["party"] = party;
            }

            if (!string.IsNullOrEmpty(activity.JoinSecret))
            {
                Dictionary<string, object> secrets = new Dictionary<string, object>();
                secrets["join"] = activity.JoinSecret;
                obj["secrets"] = secrets;
                obj["instance"] = activity.Instance;
            }

            return obj;
        }

        private void HandlePacket(Packet packet)
        {
            if (packet.Opcode == Opcode.Ping)
            {
                Connection conn;
                lock (_sync) conn = _connection;
                if (conn != null) conn.WriteJson(Opcode.Pong, packet.RawJson);
                return;
            }

            if (packet.Opcode == Opcode.Close)
            {
                WritePacketError(packet, "Discord IPC closed");
                Stop();
                return;
            }

            if (packet.Opcode != Opcode.Frame) return;

            string evt = GetString(packet.Data, "evt");
            string cmd = GetString(packet.Data, "cmd");

            if (string.Equals(evt, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                WritePacketError(packet, "Discord IPC error");
                return;
            }

            if (!string.Equals(cmd, "DISPATCH", StringComparison.OrdinalIgnoreCase)) return;

            if (string.Equals(evt, "READY", StringComparison.OrdinalIgnoreCase))
            {
                Dictionary<string, object> data = GetObject(packet.Data, "data");
                Dictionary<string, object> user = data == null ? null : GetObject(data, "user");
                IPCUser parsed = ParseUser(user);

                lock (_sync)
                {
                    _ready = true;
                    _user = parsed;
                }

                WriteLog("Discord IPC: READY" + (string.IsNullOrEmpty(parsed.Username) ? "" : " as " + parsed.Username));
                Subscribe("ACTIVITY_JOIN");
                Subscribe("ACTIVITY_JOIN_REQUEST");
                SendActivity();
                return;
            }

            if (string.Equals(evt, "ACTIVITY_JOIN", StringComparison.OrdinalIgnoreCase))
            {
                Dictionary<string, object> data = GetObject(packet.Data, "data");
                string secret = GetString(data, "secret");
                Action<string> handler = Join;
                if (handler != null && secret != null) handler(secret);
                return;
            }

            if (string.Equals(evt, "ACTIVITY_JOIN_REQUEST", StringComparison.OrdinalIgnoreCase))
            {
                Dictionary<string, object> data = GetObject(packet.Data, "data");
                Dictionary<string, object> user = data == null ? null : GetObject(data, "user");
                Action<IPCUser> handler = JoinRequest;
                if (handler != null && user != null) handler(ParseUser(user));
            }
        }

        private static IPCUser ParseUser(Dictionary<string, object> user)
        {
            return user == null
                ? new IPCUser()
                : new IPCUser(GetString(user, "id"), GetString(user, "username"), GetString(user, "avatar"));
        }

        private void WritePacketError(Packet packet, string prefix)
        {
            Dictionary<string, object> data = GetObject(packet.Data, "data");
            if (data == null) data = packet.Data;
            string code = GetString(data, "code") ?? "unknown";
            string message = GetString(data, "message") ?? "unknown";
            WriteLog(prefix + " " + code + ": " + message);
        }

        private void HandleDisconnected()
        {
            Stop();
        }

        private void WriteLog(string message)
        {
            Action<string> log = Log;
            if (log != null) log(message);
            else Console.WriteLine(message);
        }

        private static Dictionary<string, object> GetObject(Dictionary<string, object> source, string key)
        {
            if (source == null) return null;
            object value;
            return source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            if (source == null) return null;
            object value;
            if (!source.TryGetValue(key, out value) || value == null) return null;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
