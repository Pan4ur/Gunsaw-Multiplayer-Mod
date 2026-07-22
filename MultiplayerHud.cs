using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

internal sealed class MultiplayerHud : MonoBehaviour
{
    private const float BubbleLifetime = 5f;
    private const float FeedLifetime = 8f;
    private readonly List<ChatEntry> history = new List<ChatEntry>();
    private string localName = "Player";
    private string hostedLobbyName = "";
    private string input = "";
    private bool chatOpen;
    private bool lobbyWindowOpen;
    private bool focusChat;
    private bool waitForChatOpenKeyRelease;
    private Vector2 chatScroll;
    private bool replicationDebugOverlayEnabled;
    private bool networkStatsVisible;
    private GameObject networkStatsObject;
    private Component networkStatsText;
    private Component networkStatsTemplate;
    private PropertyInfo networkStatsTextProperty;
    private string networkStatsTextValue = "";
    private float nextNetworkStatsUpdate;
    private static readonly FieldInfo FpsTextField = AccessTools.Field(typeof(GameManager), "fpsText");
    private static readonly FieldInfo PerformanceTextField = AccessTools.Field(typeof(GameManager), "performanceText");
    private MultiplayerHudUi nativeUi;

    internal static MultiplayerHud Instance { get; private set; }

    internal static bool IsTyping { get; private set; }
    internal bool ChatOpen => chatOpen;
    internal IReadOnlyList<ChatEntry> ChatHistory => history;
    internal string ChatInput { get => input; set => input = SanitizeMessage(value); }
    internal bool NetworkStatsVisible => networkStatsVisible;
    internal string NetworkStatsText => networkStatsTextValue;

    internal void Configure(string playerName, string lobbyName, bool menuOpen)
    {
        Instance = this;
        localName = SanitizeName(playerName);
        hostedLobbyName = lobbyName ?? "";
        lobbyWindowOpen = menuOpen;
    }

    internal void ResetChat()
    {
        history.Clear();
        input = "";
        chatScroll = Vector2.zero;
        CloseChat();
    }

    internal void ToggleReplicationDebugOverlay()
    {
        replicationDebugOverlayEnabled = !replicationDebugOverlayEnabled;
        AddSystemMessage("Replication markers: " + (replicationDebugOverlayEnabled ? "ON" : "OFF"));
    }

    internal void ToggleNetworkStats()
    {
        networkStatsVisible = !networkStatsVisible;
        if (!networkStatsVisible) DestroyNetworkStatsWidget();
        AddSystemMessage("Network debug: " + (networkStatsVisible ? "ON" : "OFF"));
    }

    private void Update()
    {
        string sender;
        string message;
        ushort senderId;
        while (MultiplayerSession.TryTakeChat(out senderId, out sender, out message))
            AddMessage(sender, message, false, senderId);

        if (!MultiplayerSession.IsConnected)
        {
            DestroyNetworkStatsWidget();
            if (chatOpen) CloseChat();
            return;
        }
        if (networkStatsVisible) UpdateNetworkStatsWidget();
        else if (networkStatsObject != null) DestroyNetworkStatsWidget();
        var enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        if (!chatOpen && enter)
        {
            chatOpen = true;
            IsTyping = true;
            focusChat = true;
            waitForChatOpenKeyRelease = true;
            input = "";
            return;
        }
        if (!chatOpen) return;
        if (waitForChatOpenKeyRelease && !Input.GetKey(KeyCode.Return) && !Input.GetKey(KeyCode.KeypadEnter))
            waitForChatOpenKeyRelease = false;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseChat();
            return;
        }
        if (!waitForChatOpenKeyRelease && enter)
            Submit();
    }

    private void LateUpdate()
    {
        if (nativeUi == null) nativeUi = gameObject.GetComponent<MultiplayerHudUi>() ?? gameObject.AddComponent<MultiplayerHudUi>();
        if (replicationDebugOverlayEnabled && MultiplayerSession.IsHost)
        {
            nativeUi.BeginDebugFrame();
            if (WorldReplication.Instance != null) WorldReplication.Instance.DrawReplicationDebugOverlay(null, null, null);
            if (NpcReplication.Instance != null) NpcReplication.Instance.DrawReplicationDebugOverlay(null, null, null);
        }
        nativeUi.Configure(this);
    }

    private void UpdateNetworkStatsWidget()
    {
        if (Time.unscaledTime >= nextNetworkStatsUpdate)
        {
            nextNetworkStatsUpdate = Time.unscaledTime + 0.25f;
            MultiplayerPerformance.Sample();
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
        }
        var manager = GameManager.main;
        if (manager == null) return;
        var template = GetTextComponent(manager, FpsTextField);
        if (template == null) template = GetTextComponent(manager, PerformanceTextField);
        if (template == null) return;
        if (networkStatsObject == null || networkStatsTemplate != template)
            CreateNetworkStatsWidget(template);
        if (networkStatsText == null || networkStatsTextProperty == null) return;
        try { networkStatsTextProperty.SetValue(networkStatsText, networkStatsTextValue, null); }
        catch (Exception) { DestroyNetworkStatsWidget(); }
    }

    private static Component GetTextComponent(GameManager manager, FieldInfo field)
    {
        if (field == null) return null;
        var value = field.GetValue(manager) as Component;
        return value != null && value.GetType().GetProperty("text") != null ? value : null;
    }

    private static float TrafficPercent(int bytes, int total)
    {
        return total <= 0 ? 0f : bytes * 100f / total;
    }

    private void CreateNetworkStatsWidget(Component template)
    {
        DestroyNetworkStatsWidget();
        var clone = Instantiate(template.gameObject, template.transform.parent, false);
        clone.name = "GunsawMultiplayerNetworkStats";
        clone.transform.SetAsLastSibling();
        networkStatsObject = clone;
        networkStatsTemplate = template;
        foreach (var component in clone.GetComponents<Component>())
        {
            if (component != null && component.GetType().GetProperty("text") != null)
            {
                networkStatsText = component;
                networkStatsTextProperty = component.GetType().GetProperty("text");
                break;
            }
        }
        var sourceRect = template.transform as RectTransform;
        var cloneRect = clone.transform as RectTransform;
        if (sourceRect != null && cloneRect != null)
        {
            cloneRect.anchorMin = Vector2.zero;
            cloneRect.anchorMax = Vector2.zero;
            cloneRect.pivot = Vector2.zero;
            cloneRect.anchoredPosition = new Vector2(18f, 18f);
            cloneRect.sizeDelta = new Vector2(Mathf.Max(sourceRect.sizeDelta.x, 920f),
                Mathf.Max(sourceRect.sizeDelta.y, 330f));
        }
        ConfigureNetworkStatsTextOverflow();
        clone.SetActive(true);
    }

    private void ConfigureNetworkStatsTextOverflow()
    {
        if (networkStatsText == null) return;
        var type = networkStatsText.GetType();
        var wrapping = type.GetProperty("enableWordWrapping");
        if (wrapping != null && wrapping.CanWrite) wrapping.SetValue(networkStatsText, false, null);
        var overflow = type.GetProperty("overflowMode");
        if (overflow != null && overflow.CanWrite && overflow.PropertyType.IsEnum)
        {
            try { overflow.SetValue(networkStatsText, Enum.Parse(overflow.PropertyType, "Overflow"), null); }
            catch (ArgumentException) { }
        }
    }

    private void DestroyNetworkStatsWidget()
    {
        if (networkStatsObject != null) Destroy(networkStatsObject);
        networkStatsObject = null;
        networkStatsText = null;
        networkStatsTemplate = null;
        networkStatsTextProperty = null;
        networkStatsTextValue = "";
        nextNetworkStatsUpdate = 0f;
    }

    internal void Submit()
    {
        var message = SanitizeMessage(input);
        input = "";
        if (!string.IsNullOrEmpty(message))
        {
            AddMessage(localName, message, true, MultiplayerSession.LocalPeerId);
            MultiplayerSession.SendChat(message);
        }
        CloseChat();
    }

    internal void CloseChat()
    {
        chatOpen = false;
        focusChat = false;
        waitForChatOpenKeyRelease = false;
        IsTyping = false;
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
        chatScroll.y = float.MaxValue;
    }

    internal static void AddSystemMessage(string message)
    {
        if (Instance != null) Instance.AddMessage("SYSTEM", message, false);
    }

    // Compatibility hook for the existing diagnostic callers. The old IMGUI markers
    // intentionally no longer render; player-facing UI lives on the native Canvas.
    internal static void DrawReplicationMarker(Camera camera, Vector3 position, bool sent,
        GUIStyle style, GUIStyle shadowStyle)
    {
        if (Instance != null && Instance.nativeUi != null) Instance.nativeUi.AddDebugMarker(position, sent);
    }

    private static BodyScript LocalBody()
    {
        var player = PlayerScript.player;
        return player == null ? null : player.bodyScript;
    }

    private static string PlayerState(BodyScript body)
    {
        if (body == null) return "";
        if (!body.isAlive) return "  DEAD";
        if (!body.IsConsc() || !body.CanMove()) return "  unconscious";
        return "";
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
        return result.Length > 160 ? result.Substring(0, 160) : result;
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
}
