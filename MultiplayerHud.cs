using HarmonyLib;
using TMPro;
using UnityEngine;

internal sealed class MultiplayerHud : MonoBehaviour
{
    private static readonly string[] chatCommands = ["/kill", "/spawn", "/tp", "/ban"];
    private readonly List<ChatEntry> history = [];
    private readonly List<string> chatSuggestions = [];
    private string localName = "Player";
    private string input = "";
    private bool chatOpen;
    private bool focusChat;
    private bool waitForChatOpenKeyRelease;
    private bool replicationDebugOverlayEnabled;
    private bool networkStatsVisible;
    private GameObject networkStatsObject;
    private TextMeshProUGUI networkStatsText;
    private TextMeshProUGUI networkStatsTemplate;
    private string networkStatsTextValue = "";
    private float nextNetworkStatsUpdate;
    private MultiplayerHudUi nativeUi;

    internal static MultiplayerHud Instance { get; private set; }

    internal static bool IsTyping { get; private set; }
    internal bool ChatOpen => chatOpen;
    internal IReadOnlyList<ChatEntry> ChatHistory => history;
    internal IReadOnlyList<string> ChatSuggestions => chatSuggestions;
    internal string ChatInput
    {
        get => input;
        set
        {
            input = value ?? "";
            UpdateChatSuggestions();
        }
    }
    internal bool NetworkStatsVisible => networkStatsVisible;
    internal string NetworkStatsText => networkStatsTextValue;

    internal void Configure(string playerName, string lobbyName, bool menuOpen)
    {
        Instance = this;
        localName = SanitizeName(playerName);
    }

    internal void ResetChat()
    {
        history.Clear();
        input = "";
        chatSuggestions.Clear();
        CloseChat();
    }

    internal void ToggleReplicationDebugOverlay()
    {
        SetReplicationDebugOverlay(!replicationDebugOverlayEnabled);
    }

    internal void SetReplicationDebugOverlay(bool enabled)
    {
        if (replicationDebugOverlayEnabled == enabled) return;
        replicationDebugOverlayEnabled = enabled;
        if (!enabled && nativeUi != null) nativeUi.ClearDebugMarkers();
        AddSystemMessage("Replication markers: " + (replicationDebugOverlayEnabled ? "ON" : "OFF"));
    }

    internal void ToggleNetworkStats()
    {
        networkStatsVisible = !networkStatsVisible;
        if (networkStatsVisible) MultiplayerPerformance.Reset();
        if (!networkStatsVisible) DestroyNetworkStatsWidget();
        AddSystemMessage("Network debug: " + (networkStatsVisible ? "ON" : "OFF"));
    }

    private void Update()
    {
        MultiplayerPerformance.AdvancedEnabled = networkStatsVisible;
        MultiplayerPerformance.Sample();
        if (networkStatsVisible && Input.GetKeyDown(KeyCode.C) &&
            (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            GUIUtility.systemCopyBuffer = networkStatsTextValue;
        string sender;
        string message;
        ushort senderId;
        while (MultiplayerSession.TryTakeChat(out senderId, out sender, out message))
        {
            if (GunsawMultiplayerPlugin.Instance.TryHandleLobbyChatCommand(senderId, message)) continue;
            AddMessage(sender, message, false, senderId);
        }

        if (!MultiplayerSession.IsConnected)
        {
            DestroyNetworkStatsWidget();
            if (chatOpen) CloseChat();
            return;
        }
        if (networkStatsVisible) UpdateNetworkStatsWidget();
        else if (networkStatsObject != null) DestroyNetworkStatsWidget();
        bool chatOpenKeyDown = Input.GetKeyDown(Controls.keys[Controls.OPEN_CHAT]);
        if (!chatOpen && chatOpenKeyDown)
        {
            chatOpen = true;
            IsTyping = true;
            focusChat = true;
            waitForChatOpenKeyRelease = true;
            input = "";
            UpdateChatSuggestions();
            return;
        }
        if (!chatOpen) return;
        if (waitForChatOpenKeyRelease && !Input.GetKey(Controls.keys[Controls.OPEN_CHAT]))
            waitForChatOpenKeyRelease = false;
        if (Input.GetKeyDown(Controls.keys[Controls.CLOSE_CHAT]))
        { // Lets hope that bind is not typable
            CloseChat();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            CompleteChatInput();
            return;
        }
        if (!waitForChatOpenKeyRelease && chatOpenKeyDown)
            Submit();
    }

    private void LateUpdate()
    {
        if (nativeUi == null) nativeUi = gameObject.GetComponent<MultiplayerHudUi>() ?? gameObject.AddComponent<MultiplayerHudUi>();
        if (replicationDebugOverlayEnabled && MultiplayerSession.IsHost)
        {
            nativeUi.BeginDebugFrame();
            if (WorldReplication.Instance != null) WorldReplication.Instance.DrawReplicationDebugOverlay();
            if (NpcReplication.Instance != null) NpcReplication.Instance.DrawReplicationDebugOverlay();
        }
        nativeUi.Configure(this);
    }

    private void UpdateNetworkStatsWidget()
    {
        if (Time.unscaledTime >= nextNetworkStatsUpdate)
        {
            nextNetworkStatsUpdate = Time.unscaledTime + 0.25f;
            var stats = MultiplayerSession.DebugStats();
            var npc = NpcReplication.Instance;
            var world = WorldReplication.Instance;
            networkStatsTextValue = "MODE " + MultiplayerSession.ActiveTransport + "\n" + string.Format("PING {0} ms   RX {1:0.0} KB/s   TX {2:0.0} KB/s   PLOSS {3:0.0}%\n" +
                "OUT/s  NPC P:{4} S:{5}   WORLD P:{6} S:{7}\n" +
                "IN/s   NPC P:{8} S:{9}   WORLD P:{10} S:{11}\n" +
                "LAST  NPC {12}/{13}   PROPS {14}/{15}   OTHER {16}/{17}\n" +
                "SLEEP NPC {18}   PROPS {19}   OTHER {20}\n" +
                "CPU/s  NPC {21:0.0}ms  WORLD {22:0.0}ms  AVATAR {23:0.0}ms  DIST {24:0.0}ms\n" +
                "AV S {25:0.0}ms  A {26:0.0}ms\n" +
                "TX MIX  NPC {27:0.0}KB {28:0}%  WORLD {29:0.0}KB {30:0}%\n" +
                "        AVATAR {31:0.0}KB {32:0}%  OTHER {33:0.0}KB {34:0}%\n" +
                "NPC PART  core {35:0.0}  rig {36:0.0}  limbs {37:0.0} KB/s\n" +
                "          tails {38:0.0}  weapon {39:0.0}  fx {40:0.0} KB/s\n" +
                "AV PART   core {41:0.0}  limbs {42:0.0}  rig {43:0.0}  weapon {44:0.0}\n" +
                "          fx {45:0.0}  visual {46:0.0} KB/s",
                stats.PingMs < 0 ? "-" : stats.PingMs.ToString(),
                stats.ReceivedBytesPerSecond / 1024f,
                stats.SentBytesPerSecond / 1024f,
                stats.PacketLossPercent,
                npc == null ? 0 : npc.SentPacketsPerSecond,
                npc == null ? 0 : npc.SentStatesPerSecond,
                world == null ? 0 : world.SentPacketsPerSecond,
                world == null ? 0 : world.SentStatesPerSecond,
                npc == null ? 0 : npc.ReceivedPacketsPerSecond,
                npc == null ? 0 : npc.ReceivedStatesPerSecond,
                world == null ? 0 : world.ReceivedPacketsPerSecond,
                world == null ? 0 : world.ReceivedStatesPerSecond,
                npc == null ? 0 : npc.TotalNpcCount,
                npc == null ? 0 : npc.LastSnapshotNpcCount,
                world == null ? 0 : world.TotalPropCount,
                world == null ? 0 : world.LastSnapshotPropCount,
                world == null ? 0 : world.TotalOtherCount,
                world == null ? 0 : world.LastSnapshotOtherCount,
                npc == null ? 0 : npc.CulledNpcCount,
                world == null ? 0 : world.CulledPropCount,
                world == null ? 0 : world.CulledOtherCount,
                MultiplayerPerformance.NpcMillisecondsPerSecond,
                MultiplayerPerformance.WorldMillisecondsPerSecond,
                MultiplayerPerformance.AvatarMillisecondsPerSecond,
                MultiplayerPerformance.DistanceMillisecondsPerSecond,
                MultiplayerPerformance.AvatarSerializeMillisecondsPerSecond,
                MultiplayerPerformance.AvatarApplyMillisecondsPerSecond,
                stats.SentNpcBytesPerSecond / 1024f,
                TrafficPercent(stats.SentNpcBytesPerSecond, stats.SentBytesPerSecond),
                stats.SentWorldBytesPerSecond / 1024f,
                TrafficPercent(stats.SentWorldBytesPerSecond, stats.SentBytesPerSecond),
                stats.SentAvatarBytesPerSecond / 1024f,
                TrafficPercent(stats.SentAvatarBytesPerSecond, stats.SentBytesPerSecond),
                stats.SentOtherBytesPerSecond / 1024f,
                TrafficPercent(stats.SentOtherBytesPerSecond, stats.SentBytesPerSecond),
                (npc == null ? 0 : npc.CoreBytesPerSecond) / 1024f,
                (npc == null ? 0 : npc.RigBytesPerSecond) / 1024f,
                (npc == null ? 0 : npc.LimbBytesPerSecond) / 1024f,
                (npc == null ? 0 : npc.TailBytesPerSecond) / 1024f,
                (npc == null ? 0 : npc.WeaponBytesPerSecond) / 1024f,
                (npc == null ? 0 : npc.EffectsBytesPerSecond) / 1024f,
                NetworkAvatarReplication.AvatarCoreBytesPerSecond / 1024f,
                NetworkAvatarReplication.AvatarLimbBytesPerSecond / 1024f,
                NetworkAvatarReplication.AvatarRigBytesPerSecond / 1024f,
                NetworkAvatarReplication.AvatarWeaponBytesPerSecond / 1024f,
                NetworkAvatarReplication.AvatarEffectsBytesPerSecond / 1024f,
                NetworkAvatarReplication.AvatarVisualBytesPerSecond / 1024f);
            networkStatsTextValue += string.Format("\nWORLD CPU  disc {0:0.0}  ser {1:0.0}  read {2:0.0}  apply {3:0.0}\n" +
                "           input {4:0.0}  contacts {5:0.0} ms/s\n" +
                "NPC CPU    disc {6:0.0}  anim {7:0.0}  ser {8:0.0}  read {9:0.0}\n" +
                "           apply {10:0.0}  interp {11:0.0} ms/s\n" +
                "WORLD DETAIL bodies {12:0.0}  env {13:0.0}  spawn {14:0.0}  parse {22:0.0}  wire {24:0.0}  decode {25:0.0}  dispatch {26:0.0}  env-ap {23:0.0}\n" +
                "NPC DETAIL   state {15:0.0}  zip {16:0.0}  unzip {17:0.0}  parse {18:0.0}\n" +
                "NPC APPLY    proxy {19:0.0}  pose {20:0.0}  visual {21:0.0} ms/s",
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldDiscovery),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldSerialize),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldSnapshotRead),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldStateApply),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldInput),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldContacts),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcDiscovery),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcAnimation),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcSerialize),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcSnapshotRead),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcStateApply),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcInterpolate),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldSerializeBodies),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldSerializeEnvironment),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldSnapshotObjects),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcSerializeStates),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcCompress),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcDecompress),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcSnapshotParse),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcProxyLookup),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcStatePose),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcVisuals),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldSnapshotParse),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldEnvironmentApply),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldSnapshotWireResolve),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldSnapshotDecode),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldSnapshotDispatch));
            networkStatsTextValue += string.Format("\nWORLD FLOW  fire {0:0.0}  zone {1:0.0}  queue {2:0.0}  saws {3:0.0}  weapons {4:0.0}\n" +
                "            authority {5:0.0}  send {6:0.0}  lod-freeze {7:0.0}  rest {8:0.0} ms/s",
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldFireRefresh),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldZonePrompt),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldSnapshotQueue),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldClientSaws),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldDroppedWeaponIndicators),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldAuthorityMaintenance),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldClientSend),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.WorldClientLodFreeze),
                MultiplayerPerformance.WorldUnaccountedMillisecondsPerSecond);
            networkStatsTextValue += string.Format("\nNPC POSE     core {0:0.0}  rig {1:0.0}  limbs {2:0.0}  tails {3:0.0}\n" +
                "             xform {4:0.0} ms/s\n" +
                "NPC INTERP   bodies {5:0.0}  xform {6:0.0} ms/s\n" +
                "NPC LOD      in {7}  full {8}  root {9}  skip {10}",
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcStateCore),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcStateRig),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcStateLimbs),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcStateTails),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcStateTransforms),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcInterpolateBodies),
                MultiplayerPerformance.PhaseMillisecondsPerSecond(MultiplayerPerformancePhase.NpcInterpolateTransforms),
                npc == null ? 0 : npc.ReceivedStatesPerSecond,
                npc == null ? 0 : npc.ClientFullPoseCount,
                npc == null ? 0 : npc.ClientPoseCulledCount,
                npc == null ? 0 : npc.ClientSkippedPoseCount);
        }
        var manager = GameManager.main;
        if (manager == null) return;
        var template = manager.pauseMenuText;
        if (template == null) return;
        if (networkStatsObject == null || networkStatsTemplate != template)
            CreateNetworkStatsWidget(template);
        if (networkStatsText == null) return;
        networkStatsText.text = networkStatsTextValue;
    }

    private static float TrafficPercent(int bytes, int total)
    {
        return total <= 0 ? 0f : bytes * 100f / total;
    }

    private void CreateNetworkStatsWidget(TextMeshProUGUI template)
    {
        DestroyNetworkStatsWidget();
        var clone = Instantiate(template.gameObject, template.transform.parent, false);
        clone.name = "GunsawMultiplayerNetworkStats";
        clone.transform.SetAsLastSibling();
        networkStatsObject = clone;
        networkStatsTemplate = template;
        networkStatsText = clone.GetComponent<TextMeshProUGUI>();
        var sourceRect = template.transform as RectTransform;
        var cloneRect = clone.transform as RectTransform;
        if (sourceRect != null && cloneRect != null)
        {
            cloneRect.anchorMin = Vector2.zero;
            cloneRect.anchorMax = Vector2.zero;
            cloneRect.pivot = Vector2.zero;
            cloneRect.anchoredPosition = new Vector2(18f, 18f);
            cloneRect.sizeDelta = new Vector2(Mathf.Max(sourceRect.sizeDelta.x, 920f),
                Mathf.Max(sourceRect.sizeDelta.y, 550f));
        }
        ConfigureNetworkStatsTextOverflow();
        clone.SetActive(true);
    }

    private void ConfigureNetworkStatsTextOverflow()
    {
        if (networkStatsText == null) return;
        networkStatsText.enableWordWrapping = false;
        networkStatsText.overflowMode = TextOverflowModes.Overflow;
    }

    private void DestroyNetworkStatsWidget()
    {
        if (networkStatsObject != null) Destroy(networkStatsObject);
        networkStatsObject = null;
        networkStatsText = null;
        networkStatsTemplate = null;
        networkStatsTextValue = "";
        nextNetworkStatsUpdate = 0f;
    }

    internal void Submit()
    {
        var message = SanitizeMessage(input);
        input = "";
        chatSuggestions.Clear();
        if (!string.IsNullOrEmpty(message))
        {
            if (GunsawMultiplayerPlugin.Instance.TryHandleHostCommand(message))
            {
                CloseChat();
                return;
            }
            AddMessage(localName, message, true, MultiplayerSession.LocalPeerId);
            ChatPacket packet;
            if (ChatService.TryCreate(message, false, out packet)) MultiplayerSession.Send(packet);
        }
        CloseChat();
    }

    internal void CloseChat()
    {
        chatOpen = false;
        focusChat = false;
        waitForChatOpenKeyRelease = false;
        IsTyping = false;
        chatSuggestions.Clear();
    }

    private void UpdateChatSuggestions()
    {
        chatSuggestions.Clear();
        if (!input.StartsWith("/", StringComparison.Ordinal)) return;

        var separator = input.IndexOfAny([' ', '\t']);
        if (separator < 0)
        {
            foreach (var command in chatCommands)
                if (command.StartsWith(input, StringComparison.OrdinalIgnoreCase)) chatSuggestions.Add(command);
            return;
        }

        var commandName = input.Substring(0, separator);
        if (!string.Equals(commandName, "/tp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "/ban", StringComparison.OrdinalIgnoreCase)) return;

        var namePrefix = input.Substring(separator).TrimStart();
        AddPlayerSuggestion(MultiplayerSession.LocalPlayerName, commandName, namePrefix);
        foreach (var peerId in MultiplayerSession.PeerIds())
            AddPlayerSuggestion(MultiplayerSession.PlayerName(peerId), commandName, namePrefix);
    }

    private void AddPlayerSuggestion(string playerName, string commandName, string namePrefix)
    {
        if (string.IsNullOrEmpty(playerName) ||
            !playerName.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)) return;
        var suggestion = commandName + " " + playerName;
        if (!chatSuggestions.Contains(suggestion)) chatSuggestions.Add(suggestion);
    }

    private void CompleteChatInput()
    {
        if (chatSuggestions.Count == 0) return;
        input = chatSuggestions[0];
        if (string.Equals(input, "/tp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "/ban", StringComparison.OrdinalIgnoreCase)) input += " ";
        UpdateChatSuggestions();
    }

    private void AddMessage(string sender, string message, bool local, ushort peerId = 0)
    {
        var entry = new ChatEntry
        {
            Sender = SanitizeName(sender),
            Message = SanitizeMessage(message),
            Local = local,
            PeerId = peerId,
            CreatedAt = Time.unscaledTime,
            Clock = DateTime.Now.ToString("HH:mm")
        };
        if (string.IsNullOrEmpty(entry.Message)) return;
        history.Add(entry);
        while (history.Count > 80) history.RemoveAt(0);
    }

    internal static void AddSystemMessage(string message)
    {
        if (Instance != null) Instance.AddMessage("SYSTEM", message, false);
    }

    internal static void DrawReplicationMarker(Vector3 position, bool sent)
    {
        if (Instance != null && Instance.nativeUi != null) Instance.nativeUi.AddDebugMarker(position, sent);
    }

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Player";
        var result = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return result.Length > 32 ? result.Substring(0, 32) : result;
    }

    private static string SanitizeMessage(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var result = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return result.Length > 256 ? result.Substring(0, 256) : result;
    }

    internal sealed class ChatEntry
    {
        internal string Sender;
        internal string Message;
        internal bool Local;
        internal ushort PeerId;
        internal float CreatedAt;
        internal string Clock;
    }

    // Prevents game ui from reading anything when chat is open
    [HarmonyPatch(typeof(Input))]
    internal static class ChatOpenHandler
    {
        [HarmonyPatch(nameof(Input.GetKey), typeof(KeyCode))]
        [HarmonyPrefix]
        private static bool PrefixGetKey(ref bool __result, KeyCode key)
        {
            if (IsChatOpenAndKeyIsTypable(key))
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(nameof(Input.GetKeyDown), typeof(KeyCode))]
        [HarmonyPrefix]
        private static bool PrefixGetKeyDown(ref bool __result, KeyCode key)
        {
            if (IsChatOpenAndKeyIsTypable(key))
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(nameof(Input.GetKeyUp), typeof(KeyCode))]
        [HarmonyPrefix]
        private static bool PrefixGetKeyUp(ref bool __result, KeyCode key)
        {
            if (IsChatOpenAndKeyIsTypable(key))
            {
                __result = false;
                return false;
            }
            return true;
        }

        private static bool IsChatOpenAndKeyIsTypable(KeyCode key)
        {
            return (null != MultiplayerHud.Instance && MultiplayerHud.Instance.ChatOpen) &&
                ((KeyCode.Space == key) || (KeyCode.A <= key && KeyCode.Z >= key) || (KeyCode.Alpha0 <= key && KeyCode.Alpha9 >= key));
        }
    }
}
