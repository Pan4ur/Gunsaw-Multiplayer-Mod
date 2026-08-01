using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class MultiplayerHudUi : MonoBehaviour
{
    private GameObject root, hostPanel, playersPanel, chatPanel;
    private TMP_Text template, hostText, playersText, chatText, statsText, spectatorText, spectatorHint, respawnText, activationText;
    private TMP_InputField input;
    private float nextChatRefresh;
    private readonly List<TMP_Text> debugMarkers = new List<TMP_Text>();
    private readonly Dictionary<BodyScript, TMP_Text> nameTags = new Dictionary<BodyScript, TMP_Text>();
    private readonly Dictionary<BodyScript, TMP_Text> chatBubbles = new Dictionary<BodyScript, TMP_Text>();
    private readonly Dictionary<BodyScript, CoopMarker> coopMarkers = new Dictionary<BodyScript, CoopMarker>();
    private bool coopMarkersVisible = true;

    internal void Configure(MultiplayerHud hud)
    {
        if (!MultiplayerSession.IsHosting && !MultiplayerSession.IsConnected)
        {
            if (root != null) root.SetActive(false);
            return;
        }
        if (root == null) Create();
        if (root == null) return;
        root.SetActive(true);
        hostPanel.SetActive(MultiplayerSession.IsHosting);
        playersPanel.SetActive(Input.GetKey(Controls.keys[Controls.SEE_PLAYER]) && !hud.ChatOpen);
        chatPanel.SetActive(MultiplayerSession.IsConnected);
        hostText.text = "HOSTING  " + MultiplayerSession.PlayerCount + "/" + MultiplayerSession.MaxPlayers + " PLAYERS";
        if (playersPanel.activeSelf) UpdatePlayers();
        UpdateNameTags();
        UpdateCoopMarkers();
        UpdateChatBubbles(hud);
        UpdateSpectator();
        UpdateStatusPrompts();
        statsText.gameObject.SetActive(hud.NetworkStatsVisible && !string.IsNullOrEmpty(hud.NetworkStatsText));
        if (statsText.gameObject.activeSelf) statsText.text = hud.NetworkStatsText;
        if (Time.unscaledTime >= nextChatRefresh || hud.ChatOpen)
        {
            nextChatRefresh = Time.unscaledTime + 0.15f;
            UpdateChat(hud);
        }
        input.gameObject.SetActive(hud.ChatOpen);
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

        playersPanel = Panel(root.transform, Vector2.zero, new Vector2(650f, 320f));
        ScreenAnchor(playersPanel.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f));
        playersText = Text(playersPanel.transform, "", Vector2.zero, new Vector2(610f, 290f), 22, TextAlignmentOptions.TopLeft);

        chatPanel = Panel(root.transform, Vector2.zero, new Vector2(620f, 250f));
        ScreenAnchor(chatPanel.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 80f));
        chatText = Text(chatPanel.transform, "", new Vector2(0f, 24f), new Vector2(580f, 185f), 19, TextAlignmentOptions.BottomLeft); chatText.enableWordWrapping = true;

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
        activationText.gameObject.SetActive(WorldReplication.Instance != null && WorldReplication.Instance.HasActivationPrompt);
    }

    private void UpdatePlayers()
    {
        var text = "PLAYERS\n\n" + (MultiplayerSession.IsHost ? "[HOST]  " : "") + "YOU";
        foreach (var remote in NetworkAvatarReplication.RemotePlayers())
            text += "\n" + (remote.PeerId == 1 ? "[HOST]  " : "") + remote.Name + (remote.PingMs >= 0 ? "   " + remote.PingMs + " ms" : "");
        playersText.text = text;
    }

    private void UpdateNameTags()
    {
        var camera = Camera.main;
        if (camera == null) return;
        var active = new HashSet<BodyScript>();
        foreach (var remote in NetworkAvatarReplication.RemotePlayers())
        {
            var body = remote.Body;
            if (body == null || body.rb == null) continue;
            var scale = Mathf.Clamp(Mathf.Abs(body.characterScale), 0.7f, 1.8f);
            var screen = camera.WorldToScreenPoint((Vector3)body.rb.position + Vector3.up * (1.35f * scale));
            if (screen.z <= 0f) continue;
            active.Add(body);
            TMP_Text tag;
            if (!nameTags.TryGetValue(body, out tag) || tag == null)
            {
                tag = Text(root.transform, "", Vector2.zero, new Vector2(420f, 32f), 15, TextAlignmentOptions.Center);
                tag.fontStyle = FontStyles.Bold;
                nameTags[body] = tag;
            }
            tag.text = NetworkAvatarReplication.RemoteNameTag(body);
            tag.color = !body.isAlive ? new Color(1f, 0.28f, 0.28f) : !body.IsConsc() ? new Color(1f, 0.72f, 0.22f) : Color.white;
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
            else body = NetworkAvatarReplication.RemoteBodyForPeer(entry.PeerId);
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
            var screen = camera.WorldToScreenPoint((Vector3)pair.Key.rb.position + Vector3.down * 1.4f);
            if (screen.z <= 0f) { pair.Value.gameObject.SetActive(false); continue; }
            pair.Value.text = entry.Message;
            pair.Value.rectTransform.anchoredPosition = CanvasPosition(screen);
            pair.Value.gameObject.SetActive(true);
            latest.Remove(pair.Key);
        }
        foreach (var body in stale) chatBubbles.Remove(body);
        foreach (var pair in latest)
        {
            var screen = camera.WorldToScreenPoint((Vector3)pair.Key.rb.position + Vector3.down * 1.4f);
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
        if (Input.GetKeyDown(Controls.keys[Controls.TOGGLE_PLAYER_MARKERS])) coopMarkersVisible = !coopMarkersVisible;
        if (camera == null || MultiplayerSession.PvpEnabled || !coopMarkersVisible) { HideCoopMarkers(active); return; }
        const float size = 0.75f;
        foreach (var remote in NetworkAvatarReplication.RemotePlayers())
        {
            var body = remote.Body;
            if (body == null || body.rb == null) continue;
            var screen = camera.WorldToScreenPoint(body.rb.position);
            if (screen.z > 0f && screen.x >= 0f && screen.x <= Screen.width && screen.y >= 0f && screen.y <= Screen.height) continue;
            active.Add(body);
            CoopMarker marker;
            if (!coopMarkers.TryGetValue(body, out marker)) { marker = CreateCoopMarker(); coopMarkers[body] = marker; }
            if (marker == null) continue;
            if (screen.z <= 0f) { screen.x = Screen.width - screen.x; screen.y = Screen.height - screen.y; }
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var direction = new Vector2(screen.x, screen.y) - center;
            if (direction.sqrMagnitude < 0.01f) direction = Vector2.up;
            direction.Normalize();
            var margin = 72f * size;
            var edgePosition = center + new Vector2(direction.x * Mathf.Max(0f, center.x - margin), direction.y * Mathf.Max(0f, center.y - margin));
            marker.Rect.anchoredPosition = CanvasPosition(new Vector3(edgePosition.x, edgePosition.y, 0f));
            marker.Rect.sizeDelta = new Vector2(82f, 96f) * size;
            var player = PlayerScript.player;
            var localBody = player == null ? null : player.bodyScript;
            var distance = localBody == null || localBody.rb == null ? 0f : Vector2.Distance(localBody.rb.position, body.rb.position);
            marker.Name.text = remote.Name + " (" + Mathf.RoundToInt(distance) + " m)";
            marker.Name.fontSize = 16f * size;
            UpdateHeadVisual(marker, body);
            marker.Visual.localScale = new Vector3(body.isRight ? 1f : -1f, 1f, 1f);
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

    private static void UpdateHeadVisual(CoopMarker marker, BodyScript body)
    {
        var head = body.headTransform;
        if (head == null) return;
        var renderers = head.GetComponentsInChildren<SpriteRenderer>(true);
        var main = head.GetComponent<SpriteRenderer>();
        if (main == null || main.sprite == null)
        {
            foreach (var renderer in renderers)
                if (renderer != null && renderer.sprite != null) { main = renderer; break; }
        }
        if (main == null || main.sprite == null) return;
        var pixelsPerUnit = 60f / Mathf.Max(0.01f, main.sprite.bounds.size.x);
        var count = 0;
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer.sprite == null || !renderer.enabled) continue;
            Image image;
            if (count < marker.HeadParts.Count) image = marker.HeadParts[count];
            else
            {
                var part = new GameObject("HeadPart", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); part.transform.SetParent(marker.Visual, false);
                image = part.GetComponent<Image>(); image.preserveAspect = true; marker.HeadParts.Add(image);
            }
            image.sprite = renderer.sprite;
            image.color = renderer.color;
            var offset = (Vector2)head.InverseTransformPoint(renderer.transform.position) * pixelsPerUnit;
            var size = renderer.sprite.bounds.size * pixelsPerUnit;
            Rect(image.rectTransform, offset, size);
            image.gameObject.SetActive(true);
            count++;
        }
        for (var index = count; index < marker.HeadParts.Count; index++) marker.HeadParts[index].gameObject.SetActive(false);
    }

    private void UpdateChat(MultiplayerHud hud)
    {
        chatText.richText = true;
        var entries = hud.ChatHistory;
        var start = hud.ChatOpen ? Mathf.Max(0, entries.Count - 9) : Mathf.Max(0, entries.Count - 5);
        var text = "";
        var now = Time.unscaledTime;
        for (var i = start; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!hud.ChatOpen && now - entry.CreatedAt > 8f) continue;
            text += "[" + entry.Clock + "] " + entry.Sender + ": " + entry.Message + "\n";
        }
        chatText.text = text;
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
}
