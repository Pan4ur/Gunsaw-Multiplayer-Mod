namespace DiscordIPC
{
    public static class RichPresence
    {
        private static readonly object Sync = new();
        private static readonly Internal.DiscordIpcClient Client = new();
        private static readonly DraftService Drafts = new();
        private static readonly long InitTime = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        private static long? _appId;
        private static string _details;
        private static string _state;
        private static PresencePair _largeImage;
        private static PresencePair _smallImage;
        private static PresencePair _button1;
        private static PresencePair _button2;
        private static PresenceParty _party;
        private static string _joinSecret;
        private static Action<string> _onJoin;
        private static Action<IPCUser> _onJoinRequest;

        static RichPresence()
        {
            Client.Tick = HandleRoll;
            Client.Join = HandleJoin;
            Client.JoinRequest = HandleJoinRequest;
        }

        public static Action<string> Logger
        {
            get { return Client.Log; }
            set { Client.Log = value; }
        }

        public static bool IsConnected { get { return Client.IsConnected; } }
        public static IPCUser User { get { return Client.User; } }

        public static event Action<string> OnJoin
        {
            add { lock (Sync) _onJoin += value; }
            remove { lock (Sync) _onJoin -= value; }
        }

        public static event Action<IPCUser> OnJoinRequest
        {
            add { lock (Sync) _onJoinRequest += value; }
            remove { lock (Sync) _onJoinRequest -= value; }
        }

        public static long? AppId
        {
            get { lock (Sync) return _appId; }
            set
            {
                bool changed;
                lock (Sync)
                {
                    changed = _appId != value;
                    _appId = value;
                }
                if (!changed && !(value.HasValue && !Client.IsConnected)) return;
                if (value.HasValue) Client.Start(value.Value);
                else Client.Stop();
            }
        }

        public static string Details
        {
            get { lock (Sync) return _details; }
            set { lock (Sync) { if (_details == value) return; _details = value; } QueueCurrentActivity(); }
        }

        public static string State
        {
            get { lock (Sync) return _state; }
            set { lock (Sync) { if (_state == value) return; _state = value; } QueueCurrentActivity(); }
        }

        public static PresencePair LargeImage
        {
            get { lock (Sync) return _largeImage; }
            set { lock (Sync) { if (Same(_largeImage, value)) return; _largeImage = value; } QueueCurrentActivity(); }
        }

        public static PresencePair SmallImage
        {
            get { lock (Sync) return _smallImage; }
            set { lock (Sync) { if (Same(_smallImage, value)) return; _smallImage = value; } QueueCurrentActivity(); }
        }

        public static PresencePair Button1
        {
            get { lock (Sync) return _button1; }
            set { lock (Sync) { if (Same(_button1, value)) return; _button1 = value; } QueueCurrentActivity(); }
        }

        public static PresencePair Button2
        {
            get { lock (Sync) return _button2; }
            set { lock (Sync) { if (Same(_button2, value)) return; _button2 = value; } QueueCurrentActivity(); }
        }

        public static PresenceParty Party
        {
            get { lock (Sync) return _party; }
            set { lock (Sync) { if (Same(_party, value)) return; _party = value; } QueueCurrentActivity(); }
        }

        public static string JoinSecret
        {
            get { lock (Sync) return _joinSecret; }
            set { lock (Sync) { if (_joinSecret == value) return; _joinSecret = value; } QueueCurrentActivity(); }
        }

        public static bool AcceptJoinRequest(string userId)
        {
            return Client.AcceptJoinRequest(userId);
        }

        public static bool AcceptJoinRequest(IPCUser user)
        {
            return user != null && Client.AcceptJoinRequest(user.Id);
        }

        public static bool RejectJoinRequest(string userId)
        {
            return Client.RejectJoinRequest(userId);
        }

        public static bool RejectJoinRequest(IPCUser user)
        {
            return user != null && Client.RejectJoinRequest(user.Id);
        }

        public static void SaveDraft(Action<Draft> configure)
        {
            if (configure == null) throw new ArgumentNullException("configure");
            Draft draft = new Draft();
            configure(draft);
            Drafts.Save(draft);
        }

        public static void SaveDraft(object draftId, Action<Draft> configure)
        {
            if (configure == null) throw new ArgumentNullException("configure");
            Draft draft = new Draft();
            draft.DraftId = draftId;
            configure(draft);
            Drafts.Save(draft);
        }

        public static bool SetDraft(object draftId)
        {
            Draft draft = Drafts.Find(draftId);
            if (draft == null)
            {
                Log("Discord IPC - Draft with id " + draftId + " not found");
                return false;
            }
            ApplyDraft(draft);
            return true;
        }

        public static void StartRolling()
        {
            Drafts.StartRolling();
        }

        public static void StopRolling()
        {
            Drafts.StopRolling();
        }

        public static void Stop()
        {
            Client.Stop();
        }

        private static void HandleRoll()
        {
            Draft next = Drafts.NextRollingDraft();
            if (next != null) ApplyDraft(next);
        }

        private static void HandleJoin(string secret)
        {
            Action<string> handler;
            lock (Sync) handler = _onJoin;
            if (handler != null) handler(secret);
        }

        private static void HandleJoinRequest(IPCUser user)
        {
            Action<IPCUser> handler;
            lock (Sync) handler = _onJoinRequest;
            if (handler != null) handler(user);
        }

        private static void ApplyDraft(Draft draft)
        {
            lock (Sync)
            {
                _details = draft.Details;
                _state = draft.State;
                _largeImage = draft.LargeImage;
                _smallImage = draft.SmallImage;
                _button1 = draft.Button1;
                _button2 = draft.Button2;
                _party = draft.Party;
                _joinSecret = draft.JoinSecret;
            }
            QueueCurrentActivity();
        }

        private static void QueueCurrentActivity()
        {
            Client.QueueActivity(BuildActivity());
        }

        private static Activity BuildActivity()
        {
            lock (Sync)
            {
                List<ActivityButton> buttons = new List<ActivityButton>();
                if (_button1 != null) buttons.Add(new ActivityButton(_button1.First, _button1.Second));
                if (_button2 != null) buttons.Add(new ActivityButton(_button2.First, _button2.Second));

                ActivityAsset assets = null;
                if (_largeImage != null || _smallImage != null)
                {
                    assets = new ActivityAsset
                    {
                        LargeImage = _largeImage == null ? null : _largeImage.First,
                        LargeText = _largeImage == null ? null : _largeImage.Second,
                        SmallImage = _smallImage == null ? null : _smallImage.First,
                        SmallText = _smallImage == null ? null : _smallImage.Second
                    };
                }

                return new Activity
                {
                    Details = _details,
                    State = _state,
                    Assets = assets,
                    Buttons = buttons,
                    Party = _party == null ? null : new PresenceParty(_party.Id, _party.Size, _party.MaxSize),
                    JoinSecret = _joinSecret,
                    Instance = !string.IsNullOrEmpty(_joinSecret),
                    Timestamps = new ActivityTimestamp { Start = InitTime }
                };
            }
        }

        private static bool Same(PresencePair a, PresencePair b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.First == b.First && a.Second == b.Second;
        }

        private static bool Same(PresenceParty a, PresenceParty b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.Id == b.Id && a.Size == b.Size && a.MaxSize == b.MaxSize;
        }

        private static void Log(string message)
        {
            Action<string> logger = Logger;
            if (logger != null) logger(message);
            else Console.WriteLine(message);
        }
    }
}
