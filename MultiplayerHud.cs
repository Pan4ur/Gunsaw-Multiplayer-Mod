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
    private GUIStyle titleStyle;
    private GUIStyle rowStyle;
    private GUIStyle chatStyle;
    private GUIStyle bubbleStyle;
    private GUIStyle mutedStyle;
    private GUIStyle replicationMarkerStyle;
    private GUIStyle replicationMarkerShadowStyle;
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

    internal static MultiplayerHud Instance { get; private set; }

    internal static bool IsTyping { get; private set; }

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
            networkStatsTextValue = string.Format("PING {0} ms   RX {1:0.0} KB/s   TX {2:0.0} KB/s   PLOSS {3:0.0}%\n" +
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

    private void Submit()
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

    private void CloseChat()
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

    private void OnGUI()
    {
        if (!MultiplayerSession.IsHosting && !MultiplayerSession.IsConnected) return;
        EnsureStyles();
        var previousDepth = GUI.depth;
        GUI.depth = -50;
        if (networkStatsVisible && MultiplayerSession.IsConnected && networkStatsObject == null &&
            !string.IsNullOrEmpty(networkStatsTextValue))
            GUI.Label(new Rect(18f, Screen.height - 348f, 920f, 330f), networkStatsTextValue, mutedStyle);
        if (MultiplayerSession.IsHosting) DrawHostStatus();
        if (replicationDebugOverlayEnabled && MultiplayerSession.IsHost)
        {
            var camera = Camera.main;
            if (camera != null)
            {
                var world = WorldReplication.Instance;
                var npc = NpcReplication.Instance;
                if (world != null)
                    world.DrawReplicationDebugOverlay(camera, replicationMarkerStyle, replicationMarkerShadowStyle);
                if (npc != null)
                    npc.DrawReplicationDebugOverlay(camera, replicationMarkerStyle, replicationMarkerShadowStyle);
            }
        }
        if (Input.GetKey(KeyCode.Tab) && !chatOpen) DrawPlayerList();
        if (MultiplayerSession.IsConnected)
        {
            if (chatOpen) DrawOpenChat();
            else DrawRecentChat();
            DrawChatBubbles();
        }
        GUI.depth = previousDepth;
    }

    private void DrawHostStatus()
    {
        const float width = 300f;
        var height = MultiplayerSession.IsConnected ? 86f : 70f;
        var rect = new Rect(Screen.width - width - 18f, 18f, width, height);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, width - 24f, 22f),
            "HOSTING  " + (string.IsNullOrEmpty(hostedLobbyName) ? "LOBBY" : hostedLobbyName), titleStyle);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 32f, width - 24f, 20f),
            "Players " + MultiplayerSession.PlayerCount + "/" + MultiplayerSession.MaxPlayers +
            (MultiplayerSession.PlayerCount == 1 ? " - waiting for players" : ""), mutedStyle);
        if (MultiplayerSession.PlayerCount > 1)
            GUI.Label(new Rect(rect.x + 12f, rect.y + 55f, width - 24f, 20f),
                (MultiplayerSession.PlayerCount - 1) + " remote player(s) connected", rowStyle);
    }

    private void DrawPlayerList()
    {
        const float width = 430f;
        var remotePlayers = NetworkAvatarReplication.RemotePlayers();
        var rows = 1 + remotePlayers.Length;
        var height = 50f + rows * 34f;
        var rect = new Rect((Screen.width - width) * 0.5f, 54f, width, height);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(rect.x + 14f, rect.y + 8f, width - 28f, 26f), "PLAYERS", titleStyle);
        var y = rect.y + 42f;
        DrawPlayerRow(new Rect(rect.x + 14f, y, width - 28f, 28f), localName,
            MultiplayerSession.IsHost, LocalBody(), -1);
        for (var index = 0; index < remotePlayers.Length; index++)
        {
            var remote = remotePlayers[index];
            DrawPlayerRow(new Rect(rect.x + 14f, y + (index + 1) * 34f, width - 28f, 28f),
                remote.Name, remote.PeerId == 1, remote.Body, remote.PingMs);
        }
    }

    private void DrawPlayerRow(Rect rect, string name, bool host, BodyScript body, int ping)
    {
        GUI.Box(rect, GUIContent.none);
        var state = PlayerState(body);
        var suffix = ping >= 0 ? "  " + ping + " ms" : "";
        GUI.Label(new Rect(rect.x + 9f, rect.y + 3f, rect.width - 18f, rect.height - 6f),
            (host ? "[HOST]  " : "") + SanitizeName(name) + state + suffix, rowStyle);
    }

    private void DrawOpenChat()
    {
        var width = Mathf.Min(560f, Screen.width - 36f);
        const float historyHeight = 230f;
        var x = 18f;
        var inputY = Screen.height - 52f;
        var historyRect = new Rect(x, inputY - historyHeight - 8f, width, historyHeight);
        GUI.Box(historyRect, GUIContent.none);
        GUILayout.BeginArea(new Rect(historyRect.x + 8f, historyRect.y + 8f,
            historyRect.width - 16f, historyRect.height - 16f));
        chatScroll = GUILayout.BeginScrollView(chatScroll);
        foreach (var entry in history)
            GUILayout.Label("[" + entry.Clock + "] " + entry.Sender + ": " + entry.Message, chatStyle);
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        var current = Event.current;
        if (!waitForChatOpenKeyRelease && current.type == EventType.KeyDown &&
            (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter))
        {
            current.Use();
            Submit();
            return;
        }

        GUI.SetNextControlName("GunsawMultiplayerChat");
        input = GUI.TextField(new Rect(x, inputY, width, 30f), input, 160);
        if (focusChat)
        {
            GUI.FocusControl("GunsawMultiplayerChat");
            focusChat = false;
        }
    }

    private void DrawRecentChat()
    {
        var now = Time.unscaledTime;
        var recent = new List<ChatEntry>();
        for (var index = history.Count - 1; index >= 0 && recent.Count < 5; index--)
            if (now - history[index].CreatedAt <= FeedLifetime) recent.Insert(0, history[index]);
        if (recent.Count == 0) return;
        var width = Mathf.Min(560f, Screen.width - 36f);
        var height = recent.Count * 24f + 12f;
        var rect = new Rect(18f, Screen.height - height - 30f, width, height);
        GUI.Box(rect, GUIContent.none);
        for (var index = 0; index < recent.Count; index++)
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f + index * 24f, rect.width - 16f, 22f),
                recent[index].Sender + ": " + recent[index].Message, chatStyle);
    }

    private void DrawChatBubbles()
    {
        ChatEntry local = null;
        var remote = new Dictionary<ushort, ChatEntry>();
        var now = Time.unscaledTime;
        for (var index = history.Count - 1; index >= 0; index--)
        {
            var entry = history[index];
            if (now - entry.CreatedAt > BubbleLifetime) break;
            if (entry.Local && local == null) local = entry;
            if (!entry.Local && !remote.ContainsKey(entry.PeerId)) remote[entry.PeerId] = entry;
        }
        if (local != null) DrawBubble(LocalBody(), local.Message);
        foreach (var pair in remote)
            DrawBubble(NetworkAvatarReplication.RemoteBodyForPeer(pair.Key), pair.Value.Message);
    }

    private void DrawBubble(BodyScript body, string message)
    {
        if (body == null || body.rb == null || Camera.main == null) return;
        var world = (Vector3)body.rb.position + Vector3.down * 1.4f;
        var screen = Camera.main.WorldToScreenPoint(world);
        if (screen.z <= 0f) return;
        var content = new GUIContent(message);
        var size = bubbleStyle.CalcSize(content);
        var width = Mathf.Clamp(size.x + 18f, 70f, 300f);
        var height = Mathf.Clamp(bubbleStyle.CalcHeight(content, width - 12f) + 10f, 28f, 80f);
        var rect = new Rect(Mathf.Clamp(screen.x - width * 0.5f, 6f, Screen.width - width - 6f),
            Mathf.Clamp(Screen.height - screen.y, 6f, Screen.height - height - 6f), width, height);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, rect.height - 8f), content, bubbleStyle);
    }

    private void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        rowStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleLeft
        };
        mutedStyle = new GUIStyle(rowStyle);
        mutedStyle.normal.textColor = new Color(0.72f, 0.76f, 0.8f, 1f);
        chatStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            wordWrap = true,
            richText = false
        };
        bubbleStyle = new GUIStyle(chatStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        replicationMarkerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false,
            clipping = TextClipping.Overflow
        };
        replicationMarkerShadowStyle = new GUIStyle(replicationMarkerStyle);
        replicationMarkerShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.95f);
    }

    internal static void DrawReplicationMarker(Camera camera, Vector3 position, bool sent,
        GUIStyle style, GUIStyle shadowStyle)
    {
        var screenPosition = camera.WorldToScreenPoint(position);
        if (screenPosition.z <= 0f || screenPosition.x < -200f || screenPosition.x > Screen.width + 200f ||
            screenPosition.y < -80f || screenPosition.y > Screen.height + 80f) return;
        var rect = new Rect(screenPosition.x - 12f, Screen.height - screenPosition.y - 12f, 24f, 24f);
        style.normal.textColor = sent ? Color.green : Color.red;
        var previousDepth = GUI.depth;
        GUI.depth = 10;
        GUI.Label(new Rect(rect.x - 1f, rect.y, rect.width, rect.height), sent ? "1" : "0", shadowStyle);
        GUI.Label(new Rect(rect.x + 1f, rect.y, rect.width, rect.height), sent ? "1" : "0", shadowStyle);
        GUI.Label(new Rect(rect.x, rect.y - 1f, rect.width, rect.height), sent ? "1" : "0", shadowStyle);
        GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, rect.height), sent ? "1" : "0", shadowStyle);
        GUI.Label(rect, sent ? "1" : "0", style);
        GUI.depth = previousDepth;
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

    private sealed class ChatEntry
    {
        internal string Sender;
        internal string Message;
        internal bool Local;
        internal ushort PeerId;
        internal float CreatedAt;
        internal string Clock;
    }
}
