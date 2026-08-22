using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal sealed class MultiplayerHudUi : MonoBehaviour
{
    private GameObject root, hostPanel, playersPanel, chatPanel, finalLeaderboardPanel;
    private TMP_Text template, hostText, playersText, chatText, chatHint, commandHints, statsText, spectatorText, spectatorHint, respawnText, activationText, finalLeaderboardHeader;
    private TMP_InputField input;
    private ScrollRect chatScroll;
    private RectTransform chatContent;
    private float nextChatRefresh;
    private int renderedChatEntryCount = -1;
    private bool chatWasOpen;
    private readonly List<TMP_Text> debugMarkers = new();
    private readonly Dictionary<BodyScript, TMP_Text> nameTags = new();
    private readonly Dictionary<BodyScript, TMP_Text> chatBubbles = new();
    private readonly Dictionary<BodyScript, CoopMarker> coopMarkers = new();
    private readonly Dictionary<ushort, FinalLeaderboardRow> finalLeaderboardRows = new();
    private bool coopMarkersVisible = true;

    internal void Configure(MultiplayerHud hud)
    {
        if (!MultiplayerSession.IsHosting && !MultiplayerSession.IsConnected)
        {
            if (root != null) root.SetActive(false);
            return;
        }

        if (root == null)
        {
            Create();
            return;
        }
        
        if (!root.activeSelf) root.SetActive(true);
        SetActive(hostPanel, MultiplayerSession.IsHosting);
        SetActive(playersPanel, Input.GetKey(Controls.keys[Controls.SEE_PLAYER]) && !hud.ChatOpen);
        SetActive(chatPanel, MultiplayerSession.IsConnected);
        hostText.text = "HOSTING  " + MultiplayerSession.PlayerCount + "/" + MultiplayerSession.MaxPlayers + " PLAYERS";
        hostText.gameObject.SetActive(null == PlayerScript.player || PlayerScript.player.canvasVisible);
        if (playersPanel.activeSelf) UpdatePlayers();
        UpdateNameTags();
        UpdateCoopMarkers();
        UpdateChatBubbles(hud);
        UpdateSpectator();
        UpdateStatusPrompts();
        
        if (!string.IsNullOrEmpty(ArsenalMenu.Prompt))
        {
            activationText.text = ArsenalMenu.Prompt;
            activationText.gameObject.SetActive(true);
        }
        else if ((WorldReplication.Instance == null || !WorldReplication.Instance.HasActivationPrompt) && !string.IsNullOrEmpty(PlayerCarrySystem.Prompt))
        {
            activationText.text = PlayerCarrySystem.Prompt;
            activationText.gameObject.SetActive(true);
        }
        
        UpdateFinalLeaderboard();
        statsText.gameObject.SetActive(hud.NetworkStatsVisible && !string.IsNullOrEmpty(hud.NetworkStatsText));
        if (statsText.gameObject.activeSelf) statsText.text = hud.NetworkStatsText;
        if (Time.unscaledTime >= nextChatRefresh || hud.ChatOpen)
        {
            nextChatRefresh = Time.unscaledTime + 0.15f;
            UpdateChat(hud);
        }
        if (!hud.ChatOpen) chatWasOpen = false;
        input.gameObject.SetActive(hud.ChatOpen);
        chatHint.gameObject.SetActive(!hud.ChatOpen && SceneManager.GetActiveScene().name != "LevelSelect" && (null == PlayerScript.player || PlayerScript.player.canvasVisible) && MultiplayerSession.IsConnected);
        commandHints.gameObject.SetActive(hud.ChatOpen && hud.ChatSuggestions.Count > 0);
        if (!hud.ChatOpen) chatHint.text = "Press " + Controls.keys[Controls.OPEN_CHAT] + " to open the chat";
        if (hud.ChatOpen && hud.ChatSuggestions.Count > 0)
            commandHints.text = string.Join("    ", hud.ChatSuggestions);
        if (hud.ChatOpen)
        {
            if (!input.isFocused) input.ActivateInputField();
            if (input.text != hud.ChatInput) input.SetTextWithoutNotify(hud.ChatInput);
        }
    }

    internal void BeginDebugFrame()
    {
        ClearDebugMarkers();
    }

    internal void ClearDebugMarkers()
    {
        foreach (var marker in debugMarkers) if (marker != null) marker.gameObject.SetActive(false);
    }

    internal void AddDebugMarker(Vector3 worldPosition, bool sent)
    {
        if (root == null || Camera.main == null) return;
        var screen = Camera.main.WorldToScreenPoint(worldPosition);
        if (screen.z <= 0f) return;
        TMP_Text marker = null;
        foreach (var candidate in debugMarkers) if (candidate != null && !candidate.gameObject.activeSelf) { marker = candidate; break; }
        if (marker == null) { marker = Text(root.transform, "", Vector2.zero, new Vector2(32f, 32f), 18, TextAlignmentOptions.Center); debugMarkers.Add(marker); }
        marker.text = sent ? "1" : "0"; marker.color = sent ? Color.green : Color.red; marker.rectTransform.anchoredPosition = CanvasPosition(screen); marker.gameObject.SetActive(true);
    }

    private void Create()
    {
        var player = PlayerScript.player;
        template = player != null ? player.ammoText : null;
        if (template == null)
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TMP_Text>())
                if (candidate != null && candidate.font != null)
                {
                    template = candidate;
                    break;
                }
        if (template == null && TMP_Settings.defaultFontAsset != null)
        {
            var fallback = new GameObject("MultiplayerChatFont", typeof(TextMeshProUGUI));
            template = fallback.GetComponent<TextMeshProUGUI>();
            template.font = TMP_Settings.defaultFontAsset;
            template.color = Color.white;
            fallback.hideFlags = HideFlags.HideAndDontSave;
        }
        if (template == null) return;
        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            var eventSystem = new GameObject("MultiplayerChatEventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
            eventSystem.hideFlags = HideFlags.HideAndDontSave;
        }
        root = new GameObject("GunsawMultiplayerNativeHud", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = short.MaxValue;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f;

        hostPanel = Panel(root.transform, Vector2.zero, new Vector2(480f, 66f));
        ScreenAnchor(hostPanel.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -20f));
        hostText = Text(hostPanel.transform, "", Vector2.zero, new Vector2(450f, 48f), 21, TextAlignmentOptions.Center);

        playersPanel = Panel(root.transform, Vector2.zero, new Vector2(1080f, 320f));
        ScreenAnchor(playersPanel.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f));
        playersText = Text(playersPanel.transform, "", Vector2.zero, new Vector2(1040f, 290f), 18, TextAlignmentOptions.Top);
        playersText.enableWordWrapping = false;
        playersText.overflowMode = TextOverflowModes.Overflow;

        chatPanel = Panel(root.transform, Vector2.zero, new Vector2(620f, 250f));
        ScreenAnchor(chatPanel.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 80f));
        var chatViewport = new GameObject("ChatViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        chatViewport.transform.SetParent(chatPanel.transform, false);
        Rect(chatViewport.GetComponent<RectTransform>(), new Vector2(0f, 24f), new Vector2(580f, 185f));
        chatViewport.GetComponent<Image>().color = Color.clear;
        chatScroll = chatViewport.GetComponent<ScrollRect>();
        chatScroll.horizontal = false;
        chatScroll.movementType = ScrollRect.MovementType.Clamped;
        chatScroll.scrollSensitivity = 32f;
        chatScroll.viewport = chatViewport.GetComponent<RectTransform>();
        var contentObject = new GameObject("ChatContent", typeof(RectTransform));
        contentObject.transform.SetParent(chatViewport.transform, false);
        chatContent = contentObject.GetComponent<RectTransform>();
        chatContent.anchorMin = new Vector2(0f, 1f);
        chatContent.anchorMax = new Vector2(1f, 1f);
        chatContent.pivot = new Vector2(0.5f, 1f);
        chatContent.anchoredPosition = Vector2.zero;
        chatContent.sizeDelta = new Vector2(0f, 185f);
        chatScroll.content = chatContent;
        chatText = Text(chatContent, "", Vector2.zero, Vector2.zero, 19, TextAlignmentOptions.TopLeft);
        chatText.rectTransform.anchorMin = new Vector2(0f, 1f);
        chatText.rectTransform.anchorMax = new Vector2(1f, 1f);
        chatText.rectTransform.pivot = new Vector2(0.5f, 1f);
        chatText.rectTransform.anchoredPosition = Vector2.zero;
        chatText.rectTransform.sizeDelta = Vector2.zero;
        chatText.enableWordWrapping = true;
        chatHint = Text(root.transform, "", Vector2.zero, new Vector2(620f, 42f), 14, TextAlignmentOptions.Center);
        ScreenAnchor(chatHint.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 25f));
        chatHint.color = new Color(template.color.r, template.color.g, template.color.b, 0.45f);
        chatHint.raycastTarget = false;
        commandHints = Text(root.transform, "", Vector2.zero, new Vector2(620f, 28f), 14, TextAlignmentOptions.BottomLeft);
        ScreenAnchor(commandHints.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 70f));
        commandHints.color = new Color(template.color.r, template.color.g, template.color.b, 0.68f);

        input = CreateInput(root.transform, Vector2.zero, new Vector2(620f, 42f));
        ScreenAnchor(input.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 25f));
        input.onValueChanged.AddListener(value => { if (MultiplayerHud.Instance != null) MultiplayerHud.Instance.ChatInput = value; });
        input.onSubmit.AddListener(_ => { if (MultiplayerHud.Instance != null) MultiplayerHud.Instance.Submit(); });
        statsText = Text(root.transform, "", Vector2.zero, new Vector2(920f, 330f), 13, TextAlignmentOptions.TopLeft);
        ScreenAnchor(statsText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -20f));
        statsText.enableWordWrapping = false;
        statsText.gameObject.SetActive(false);

        spectatorText = Text(root.transform, "", Vector2.zero, new Vector2(560f, 38f), 24, TextAlignmentOptions.Center);
        ScreenAnchor(spectatorText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -125f));
        spectatorText.fontStyle = FontStyles.Bold;
        spectatorText.gameObject.SetActive(false);
        spectatorHint = Text(root.transform, "A/D or mouse wheel to switch player", Vector2.zero, new Vector2(620f, 30f), 16, TextAlignmentOptions.Center);
        ScreenAnchor(spectatorHint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -158f));
        spectatorHint.gameObject.SetActive(false);
        respawnText = Text(root.transform, "", Vector2.zero, new Vector2(360f, 40f), 24, TextAlignmentOptions.Center);
        ScreenAnchor(respawnText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -378f));
        respawnText.fontStyle = FontStyles.Bold;
        respawnText.gameObject.SetActive(false);
        activationText = Text(root.transform, "PRESS [USE] TO ACTIVATE", Vector2.zero, new Vector2(360f, 32f), 18, TextAlignmentOptions.Center);
        ScreenAnchor(activationText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -778f));
        activationText.fontStyle = FontStyles.Bold;
        activationText.gameObject.SetActive(false);

        finalLeaderboardPanel = Panel(root.transform, Vector2.zero, new Vector2(940f, 720f));
        ScreenAnchor(finalLeaderboardPanel.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -32f));
        finalLeaderboardPanel.GetComponent<Image>().color = Color.clear;
        finalLeaderboardPanel.GetComponent<Outline>().effectColor = Color.clear;
        var finalTitle = Text(finalLeaderboardPanel.transform, "MISSION LEADERBOARD", new Vector2(0f, 328f), new Vector2(880f, 42f), 26f, TextAlignmentOptions.Center);
        finalTitle.fontStyle = FontStyles.Bold;
        finalLeaderboardHeader = Text(finalLeaderboardPanel.transform, Monospace("PLAYER                 PING    K    D     AIM      HS     DMG     RANK"), new Vector2(37f, 280f), new Vector2(820f, 30f), 16f, TextAlignmentOptions.Left);
        finalLeaderboardHeader.enableWordWrapping = false;
        finalLeaderboardPanel.SetActive(false);
    }

    private void UpdateSpectator()
    {
        var active = NetworkAvatarReplication.IsSpectating;
        spectatorText.gameObject.SetActive(active);
        spectatorHint.gameObject.SetActive(active && NetworkAvatarReplication.SpectatorTargetName() != "NO ALIVE PLAYERS");
        if (active) spectatorText.text = NetworkAvatarReplication.SpectatorTargetName();
    }

    private void UpdateStatusPrompts()
    {
        var countdown = NetworkAvatarReplication.RespawnCountdownText();
        respawnText.gameObject.SetActive(!string.IsNullOrEmpty(countdown));
        if (!string.IsNullOrEmpty(countdown)) respawnText.text = countdown;
        activationText.text = "PRESS [USE] TO ACTIVATE";
        activationText.gameObject.SetActive((WorldReplication.Instance != null && WorldReplication.Instance.HasActivationPrompt) ||
            !string.IsNullOrEmpty(PlayerCarrySystem.Prompt) || !string.IsNullOrEmpty(ArsenalMenu.Prompt));
    }

    private void UpdatePlayers()
    {
        var header = Monospace("PLAYER                 PING    K    D     AIM      HS     DMG     RANK");
        if (!TeamSystem.Enabled)
        {
            var text = header + "\n\n" + PlayerScoreLine(MultiplayerSession.LocalPeerId, MultiplayerSession.LocalPlayerName,
                MultiplayerSession.IsHost ? 0 : MultiplayerSession.PingMs, MultiplayerSession.IsHost);
            foreach (var remote in NetworkAvatarRegistry.RemotePlayers()) text += "\n" + PlayerScoreLine(remote.PeerId, remote.Name, remote.PingMs, remote.PeerId == 1);
            playersText.text = text;
            return;
        }
        var grouped = header;
        foreach (var team in TeamSystem.Names())
        {
            grouped += "\n\n<color=#" + TeamSystem.Hex(team) + ">" + team.ToUpperInvariant() + "  " + TeamKills(team) + "</color>";
            if (TeamSystem.Name(MultiplayerSession.LocalPeerId) == team)
                grouped += "\n" + PlayerScoreLine(MultiplayerSession.LocalPeerId, MultiplayerSession.LocalPlayerName,
                    MultiplayerSession.IsHost ? 0 : MultiplayerSession.PingMs, MultiplayerSession.IsHost);
            foreach (var remote in NetworkAvatarRegistry.RemotePlayers())
                if (TeamSystem.Name(remote.PeerId) == team) grouped += "\n" + PlayerScoreLine(remote.PeerId, remote.Name, remote.PingMs, remote.PeerId == 1);
        }
        playersText.text = grouped;
    }

    private static int TeamKills(string team)
    {
        var kills = 0;
        if (TeamSystem.Name(MultiplayerSession.LocalPeerId) == team)
            kills += ScoreboardSystem.ForPlayer(MultiplayerSession.LocalPeerId).Kills;
        foreach (var remote in NetworkAvatarRegistry.RemotePlayers())
            if (TeamSystem.Name(remote.PeerId) == team) kills += ScoreboardSystem.ForPlayer(remote.PeerId).Kills;
        return kills;
    }

    private static string PlayerScoreLine(ushort peerId, string name, int ping, bool host)
    {
        var score = ScoreboardSystem.ForPlayer(peerId);
        var rank = ScoreboardSystem.Rank(score);
        var displayName = (host ? "[HOST] " : "") + name;
        if (displayName.Length > 21) displayName = displayName.Substring(0, 21);
        return Monospace(displayName.PadRight(22) + (ping >= 0 ? ping.ToString().PadLeft(4) : "   -") + "  " +
            score.Kills.ToString().PadLeft(3) + "  " + score.Deaths.ToString().PadLeft(3) + "  " +
            score.Accuracy.ToString("0.00").PadLeft(6) + "  " + score.HeadshotRatio.ToString("0.00").PadLeft(6) + "  " +
            score.DamageRatio.ToString("0.00").PadLeft(6) + "     <color=" + ScoreboardSystem.RankColor(rank) + ">" + rank + "</color>");
    }

    private static string Monospace(string value) => "<mspace=0.72em>" + value + "</mspace>";

    private void UpdateFinalLeaderboard()
    {
        if (finalLeaderboardPanel == null) return;
        var mission = MissionManager.main;
        var visible = mission != null && mission.finished;
        finalLeaderboardPanel.SetActive(visible);
        if (!visible) return;

        var entries = new List<FinalLeaderboardEntry>();
        var player = PlayerScript.player;
        entries.Add(new FinalLeaderboardEntry(MultiplayerSession.LocalPeerId, MultiplayerSession.LocalPlayerName,
            MultiplayerSession.IsHost ? 0 : MultiplayerSession.PingMs, MultiplayerSession.IsHost,
            player == null ? null : player.bodyScript));
        foreach (var remote in NetworkAvatarRegistry.RemotePlayers())
            entries.Add(new FinalLeaderboardEntry(remote.PeerId, remote.Name, remote.PingMs, remote.PeerId == 1, remote.Body));
        entries.Sort((left, right) =>
        {
            var result = ScoreboardSystem.PerformanceValue(ScoreboardSystem.ForPlayer(right.PeerId)).CompareTo(
                ScoreboardSystem.PerformanceValue(ScoreboardSystem.ForPlayer(left.PeerId)));
            return result != 0 ? result : left.PeerId.CompareTo(right.PeerId);
        });

        foreach (var pair in finalLeaderboardRows)
            if (pair.Value != null && pair.Value.Root != null) pair.Value.Root.SetActive(false);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            FinalLeaderboardRow row;
            if (!finalLeaderboardRows.TryGetValue(entry.PeerId, out row) || row == null || row.Root == null ||
                row.Background == null || row.Visual == null || row.Stats == null)
            {
                row = CreateFinalLeaderboardRow();
                finalLeaderboardRows[entry.PeerId] = row;
            }
            var rowRect = row.Root.GetComponent<RectTransform>();
            if (rowRect == null) continue;
            rowRect.anchoredPosition = new Vector2(0f, 230f - index * 70f);
            var score = ScoreboardSystem.ForPlayer(entry.PeerId);
            var rank = ScoreboardSystem.Rank(score);
            var name = (entry.Host ? "[HOST] " : "") + entry.Name;
            if (ScoreboardSystem.IsMvp(entry.PeerId)) name = "[MVP] " + name;
            if (name.Length > 21) name = name.Substring(0, 21);
            var line = name.PadRight(22) + (entry.Ping >= 0 ? entry.Ping.ToString().PadLeft(4) : "   -") + "  " +
                score.Kills.ToString().PadLeft(3) + "  " + score.Deaths.ToString().PadLeft(3) + "  " +
                score.Accuracy.ToString("0.00").PadLeft(6) + "  " + score.HeadshotRatio.ToString("0.00").PadLeft(6) + "  " +
                score.DamageRatio.ToString("0.00").PadLeft(6) + "     " + rank;
            var mvp = ScoreboardSystem.IsMvp(entry.PeerId);
            row.Background.color = mvp ? new Color(0.75f, 0.55f, 0.08f, 0.84f) : Color.clear;
            row.Stats.text = "<color=" + ScoreboardSystem.RankColor(rank) + ">" + Monospace(line) + "</color>";
            row.Visual.localScale = entry.Body != null && !entry.Body.isRight ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            UpdateHeadVisual(row.HeadParts, row.Visual, entry.Body, 58f);
            row.Root.SetActive(true);
        }
    }

    private FinalLeaderboardRow CreateFinalLeaderboardRow()
    {
        var rootObject = new GameObject("FinalLeaderboardRow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rootObject.transform.SetParent(finalLeaderboardPanel.transform, false);
        Rect(rootObject.GetComponent<RectTransform>(), Vector2.zero, new Vector2(900f, 62f));
        var visualObject = new GameObject("HeadVisual", typeof(RectTransform));
        visualObject.transform.SetParent(rootObject.transform, false);
        Rect(visualObject.GetComponent<RectTransform>(), new Vector2(-415f, 0f), new Vector2(58f, 58f));
        var stats = Text(rootObject.transform, "", new Vector2(37f, 0f), new Vector2(820f, 56f), 16f, TextAlignmentOptions.Left);
        stats.enableWordWrapping = false;
        stats.overflowMode = TextOverflowModes.Overflow;
        return new FinalLeaderboardRow { Root = rootObject, Background = rootObject.GetComponent<Image>(), Visual = visualObject.transform, Stats = stats };
    }

    private void UpdateNameTags()
    {
        var camera = Camera.main;
        if (camera == null) return;
        var active = new HashSet<BodyScript>();
        foreach (var remote in NetworkAvatarRegistry.RemotePlayers())
        {
            var body = remote.Body;
            if (body == null || body.rb == null) continue;
            var visibility = VoyagerBody.PvpVoyagerVisibility(body);
            if (visibility <= 0.01f)
            {
                active.Add(body);
                TMP_Text hiddenTag;
                if (nameTags.TryGetValue(body, out hiddenTag) && hiddenTag != null)
                    hiddenTag.gameObject.SetActive(false);
                continue;
            }
            var scale = Mathf.Clamp(Mathf.Abs(body.characterScale), 0.7f, 1.8f);
            Vector3 position = body.inVehicle ? body.transform.position : (Vector3)body.rb.position;
            var screen = camera.WorldToScreenPoint(position + Vector3.up * (1.35f * scale));
            if (screen.z <= 0f) continue;
            active.Add(body);
            TMP_Text tag;
            if (!nameTags.TryGetValue(body, out tag) || tag == null || tag is TextMeshPro)
            {
                if (tag != null) Destroy(tag.gameObject);
                tag = Text(root.transform, "", Vector2.zero, new Vector2(420f, 32f), 15, TextAlignmentOptions.Center);
                tag.fontStyle = FontStyles.Bold;
                nameTags[body] = tag;
            }
            var name = NetworkAvatarReplication.RemoteNameTag(body);
            tag.text = ScoreboardSystem.IsMvp(remote.PeerId) ? "[MVP] " + name : name;
            var color = TeamSystem.Enabled ? TeamSystem.Color(remote.PeerId) :
                !body.isAlive ? new Color(1f, 0.28f, 0.28f) : !body.IsConsc() ? new Color(1f, 0.72f, 0.22f) : Color.white;
            color.a = visibility;
            tag.color = color;
            tag.rectTransform.anchoredPosition = CanvasPosition(screen + new Vector3(0f, 12f, 0f));
            tag.gameObject.SetActive(true);
        }
        var stale = new List<BodyScript>();
        foreach (var pair in nameTags)
            if (!active.Contains(pair.Key)) { if (pair.Value != null) pair.Value.gameObject.SetActive(false); stale.Add(pair.Key); }
        foreach (var body in stale) nameTags.Remove(body);
    }

    private void UpdateChatBubbles(MultiplayerHud hud)
    {
        var camera = Camera.main;
        if (camera == null) return;
        var latest = new Dictionary<BodyScript, MultiplayerHud.ChatEntry>();
        var now = Time.unscaledTime;
        var entries = hud.ChatHistory;
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            if (now - entry.CreatedAt > 5f) break;
            BodyScript body;
            if (entry.Local)
            {
                var player = PlayerScript.player;
                body = player == null ? null : player.bodyScript;
            }
            else body = NetworkAvatarRegistry.RemoteBodyForPeer(entry.PeerId);
            if (body != null && !latest.ContainsKey(body)) latest.Add(body, entry);
        }

        var stale = new List<BodyScript>();
        foreach (var pair in chatBubbles)
        {
            MultiplayerHud.ChatEntry entry;
            if (!latest.TryGetValue(pair.Key, out entry) || pair.Key == null || pair.Key.rb == null)
            {
                if (pair.Value != null) pair.Value.gameObject.SetActive(false);
                stale.Add(pair.Key);
                continue;
            }
            var position = pair.Key.inVehicle ? pair.Key.transform.position : (Vector3)pair.Key.rb.position;
            var screen = camera.WorldToScreenPoint(position + Vector3.down * 1.4f);
            if (screen.z <= 0f) { pair.Value.gameObject.SetActive(false); continue; }
            pair.Value.text = entry.Message;
            pair.Value.rectTransform.anchoredPosition = CanvasPosition(screen);
            pair.Value.gameObject.SetActive(true);
            latest.Remove(pair.Key);
        }
        foreach (var body in stale) chatBubbles.Remove(body);
        foreach (var pair in latest)
        {
            var position = pair.Key.inVehicle ? pair.Key.transform.position : (Vector3)pair.Key.rb.position;
            var screen = camera.WorldToScreenPoint(position + Vector3.down * 1.4f);
            if (screen.z <= 0f) continue;
            var bubble = Text(root.transform, pair.Value.Message, CanvasPosition(screen), new Vector2(300f, 54f), 14, TextAlignmentOptions.Center);
            bubble.fontStyle = FontStyles.Bold;
            bubble.enableWordWrapping = true;
            chatBubbles[pair.Key] = bubble;
        }
    }

    private void UpdateCoopMarkers()
    {
        var active = new HashSet<BodyScript>();
        var camera = Camera.main;

        if (Input.GetKeyDown(Controls.keys[Controls.TOGGLE_PLAYER_MARKERS]))
            coopMarkersVisible = !coopMarkersVisible;

        if (camera == null || MultiplayerSession.PvpEnabled || !coopMarkersVisible)
        {
            HideCoopMarkers(active);
            return;
        }

        const float size = 0.75f;

        foreach (var remote in NetworkAvatarRegistry.RemotePlayers())
        {
            var body = remote.Body;

            if (body == null)
                continue;

            if (!body.inVehicle && body.rb == null)
                continue;

            Vector3 bodyPosition = body.inVehicle
                ? body.transform.position
                : (Vector3)body.rb.position;

            var screen = camera.WorldToScreenPoint(bodyPosition);

            if (screen.z > 0f &&
                screen.x >= 0f && screen.x <= Screen.width &&
                screen.y >= 0f && screen.y <= Screen.height)
            {
                continue;
            }

            active.Add(body);

            CoopMarker marker;
            if (!coopMarkers.TryGetValue(body, out marker))
            {
                marker = CreateCoopMarker();
                coopMarkers[body] = marker;
            }

            if (marker == null)
                continue;

            if (screen.z <= 0f)
            {
                screen.x = Screen.width - screen.x;
                screen.y = Screen.height - screen.y;
            }

            var center = new Vector2(
                Screen.width * 0.5f,
                Screen.height * 0.5f
            );

            var direction = new Vector2(screen.x, screen.y) - center;

            if (direction.sqrMagnitude < 0.01f)
                direction = Vector2.up;

            direction.Normalize();

            var margin = 72f * size;
            var edgePosition = center + new Vector2(
                direction.x * Mathf.Max(0f, center.x - margin),
                direction.y * Mathf.Max(0f, center.y - margin)
            );

            marker.Rect.anchoredPosition = CanvasPosition(
                new Vector3(edgePosition.x, edgePosition.y, 0f)
            );

            marker.Rect.sizeDelta = new Vector2(82f, 96f) * size;

            var player = PlayerScript.player;
            var localBody = player == null ? null : player.bodyScript;

            float distance = 0f;

            if (localBody != null &&
                (localBody.inVehicle || localBody.rb != null))
            {
                Vector3 localPosition = localBody.inVehicle
                    ? localBody.transform.position
                    : (Vector3)localBody.rb.position;

                distance = Vector2.Distance(
                    (Vector2)localPosition,
                    (Vector2)bodyPosition
                );
            }

            marker.Name.text =
                remote.Name + " (" + Mathf.RoundToInt(distance) + " m)";

            marker.Name.fontSize = 16f * size;

            UpdateHeadVisual(marker.HeadParts, marker.Visual, body, 60f);

            marker.Visual.localScale = new Vector3(
                body.isRight ? 1f : -1f,
                1f,
                1f
            );

            marker.Root.SetActive(true);
        }

        HideCoopMarkers(active);
    }

    private void HideCoopMarkers(HashSet<BodyScript> active)
    {
        foreach (var pair in coopMarkers)
            if (!active.Contains(pair.Key) && pair.Value != null && pair.Value.Root != null) pair.Value.Root.SetActive(false);
    }

    private CoopMarker CreateCoopMarker()
    {
        var go = new GameObject("CoopPlayerMarker", typeof(RectTransform)); go.transform.SetParent(root.transform, false);
        var rect = go.GetComponent<RectTransform>(); Rect(rect, Vector2.zero, new Vector2(82f, 96f));
        var visualGo = new GameObject("HeadVisual", typeof(RectTransform)); visualGo.transform.SetParent(go.transform, false);
        Rect(visualGo.GetComponent<RectTransform>(), new Vector2(0f, -13f), new Vector2(60f, 60f));
        var name = Text(go.transform, "", new Vector2(0f, 34f), new Vector2(190f, 30f), 16f, TextAlignmentOptions.Center); name.fontStyle = FontStyles.Bold; name.outlineWidth = 0.22f;
        return new CoopMarker { Root = go, Rect = rect, Visual = visualGo.transform, Name = name };
    }

    private static void UpdateHeadVisual(List<Image> parts, Transform visual, BodyScript body, float width)
    {
        if (body == null)
        {
            for (var index = 0; index < parts.Count; index++) parts[index].gameObject.SetActive(false);
            return;
        }
        var head = body.headTransform;
        if (head == null)
        {
            for (var index = 0; index < parts.Count; index++) parts[index].gameObject.SetActive(false);
            return;
        }
        var renderers = head.GetComponentsInChildren<SpriteRenderer>(true);
        var main = head.GetComponent<SpriteRenderer>();
        if (main == null || main.sprite == null)
        {
            foreach (var renderer in renderers)
                if (renderer != null && renderer.sprite != null) { main = renderer; break; }
        }
        if (main == null || main.sprite == null) return;
        var pixelsPerUnit = width / Mathf.Max(0.01f, main.sprite.bounds.size.x);
        var count = 0;
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer.sprite == null || !renderer.enabled) continue;
            Image image;
            if (count < parts.Count) image = parts[count];
            else
            {
                var part = new GameObject("HeadPart", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); part.transform.SetParent(visual, false);
                image = part.GetComponent<Image>(); image.preserveAspect = true; parts.Add(image);
            }
            image.sprite = renderer.sprite;
            image.color = renderer.color;
            var offset = (Vector2)head.InverseTransformPoint(renderer.transform.position) * pixelsPerUnit;
            var size = renderer.sprite.bounds.size * pixelsPerUnit;
            Rect(image.rectTransform, offset, size);
            image.gameObject.SetActive(true);
            count++;
        }
        for (var index = count; index < parts.Count; index++) parts[index].gameObject.SetActive(false);
    }

    private void UpdateChat(MultiplayerHud hud)
    {
        chatText.richText = true;
        var entries = hud.ChatHistory;
        var opened = hud.ChatOpen && !chatWasOpen;
        var newEntry = entries.Count != renderedChatEntryCount;
        var keepAtBottom = opened || (newEntry && chatScroll.verticalNormalizedPosition <= 0.01f);
        var start = hud.ChatOpen ? 0 : Mathf.Max(0, entries.Count - 5);
        var text = "";
        var now = Time.unscaledTime;
        for (var i = start; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!hud.ChatOpen && now - entry.CreatedAt > 8f) continue;
            text += "[" + entry.Clock + "] " + entry.Sender + ": " + entry.Message + "\n";
        }
        chatText.text = text;
        var preferredHeight = chatText.GetPreferredValues(text, 580f, 0f).y;
        chatContent.sizeDelta = new Vector2(0f, Mathf.Max(185f, preferredHeight + 8f));
        Canvas.ForceUpdateCanvases();
        if (keepAtBottom) chatScroll.verticalNormalizedPosition = 0f;
        renderedChatEntryCount = entries.Count;
        chatWasOpen = hud.ChatOpen;
    }

    private static void SetActive(GameObject gameObject, bool active)
    {
        if (gameObject != null && gameObject.activeSelf != active) gameObject.SetActive(active);
    }

    private GameObject Panel(Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline)); go.transform.SetParent(parent, false); Rect(go.GetComponent<RectTransform>(), position, size); go.GetComponent<Image>().color = new Color(0.19f, 0.19f, 0.19f, 0.0f); var outline = go.GetComponent<Outline>(); outline.effectColor = new Color(0.58f, 0.58f, 0.58f, 0.0f); outline.effectDistance = new Vector2(1f, -1f); return go;
    }

    private TMP_Text Text(Transform parent, string value, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false); Rect(go.GetComponent<RectTransform>(), position, size);
        var text = go.GetComponent<TextMeshProUGUI>(); text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial; text.color = template.color; text.fontSize = fontSize; text.alignment = alignment; text.text = value; return text;
    }


    private TMP_InputField CreateInput(Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject("ChatInput", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(TMP_InputField)); go.transform.SetParent(parent, false); Rect(go.GetComponent<RectTransform>(), position, size); var image = go.GetComponent<Image>(); image.color = new Color(0.17f, 0.17f, 0.17f, 0.86f); var outline = go.GetComponent<Outline>(); outline.effectColor = new Color(0.58f, 0.58f, 0.58f, 0.78f); outline.effectDistance = new Vector2(1f, -1f);
        var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportObject.transform.SetParent(go.transform, false);
        var viewport = viewportObject.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(8f, 0f);
        viewport.offsetMax = new Vector2(-8f, 0f);
        var field = go.GetComponent<TMP_InputField>(); field.targetGraphic = image; field.characterLimit = 160;
        field.lineType = TMP_InputField.LineType.SingleLine;
        var text = Text(viewport, "", Vector2.zero, Vector2.zero, 19, TextAlignmentOptions.Left);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        text.margin = Vector4.zero;
        text.enableWordWrapping = false;
        field.textViewport = viewport;
        field.textComponent = text;
        return field;
    }

    private static void Rect(RectTransform rect, Vector2 position, Vector2 size) { rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.pivot = new Vector2(0.5f, 0.5f); rect.anchoredPosition = position; rect.sizeDelta = size; }
    private static void ScreenAnchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
    }

    private Vector2 CanvasPosition(Vector3 screen)
    {
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)root.transform,
            screen, null, out local);
        return local;
    }
    private sealed class CoopMarker { internal GameObject Root; internal RectTransform Rect; internal Transform Visual; internal readonly List<Image> HeadParts = new List<Image>(); internal TMP_Text Name; }
    private sealed class FinalLeaderboardRow { internal GameObject Root; internal Image Background; internal Transform Visual; internal readonly List<Image> HeadParts = new List<Image>(); internal TMP_Text Stats; }
    private readonly struct FinalLeaderboardEntry
    {
        internal readonly ushort PeerId;
        internal readonly string Name;
        internal readonly int Ping;
        internal readonly bool Host;
        internal readonly BodyScript Body;
        internal FinalLeaderboardEntry(ushort peerId, string name, int ping, bool host, BodyScript body)
        { PeerId = peerId; Name = name ?? "Player"; Ping = ping; Host = host; Body = body; }
    }
}
