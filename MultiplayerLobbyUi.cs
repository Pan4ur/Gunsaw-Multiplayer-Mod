using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class MultiplayerLobbyUi : MonoBehaviour
{
    private GunsawMultiplayerPlugin plugin;
    private GameObject root;
    private GameObject panel;
    private TMP_Text template;
    private Button templateButton;
    private TMP_InputField nameInput, lobbyInput, maxPlayersInput, respawnInput, serverInput;
    private Toggle pvpToggle, grabToggle, downToggle, respawnToggle, respawnAtStartToggle;
    private TMP_Text statusText, customLevelText, hostingText, connectionModeText;
    private TMP_Text lobbyActionText;
    private Button closeLobbyButton;
    private Transform lobbyRows;
    private int renderedLobbyHash;

    internal void Configure(GunsawMultiplayerPlugin owner)
    {
        plugin = owner;
        var menu = FindObjectOfType<MainMenuManager>();
        if (menu == null)
        {
            if (root != null) root.SetActive(false);
            return;
        }
        if (root == null) Create(menu);
        if (root == null) return;
        root.SetActive(true);
        panel.SetActive(plugin.visible);
        if (!plugin.visible) return;
        FitPanelToScreen();

        SetInput(nameInput, plugin.playerName);
        SetInput(lobbyInput, plugin.lobbyName);
        SetInput(maxPlayersInput, plugin.createMaxPlayers);
        SetInput(respawnInput, plugin.createRespawnTime);
        SetInput(serverInput, plugin.lobbyServerAddress);
        pvpToggle.isOn = plugin.createPvp;
        grabToggle.isOn = plugin.createCanGrab;
        downToggle.isOn = plugin.createGrabOnlyUnconscious;
        respawnToggle.isOn = plugin.createAllowRespawn;
        respawnAtStartToggle.isOn = plugin.createRespawnAtStart;
        respawnInput.interactable = plugin.createAllowRespawn;
        respawnAtStartToggle.interactable = plugin.createAllowRespawn;
        connectionModeText.text = plugin.createConnectionMode.ToString();
        statusText.text = plugin.status;
        customLevelText.text = string.IsNullOrEmpty(plugin.customLevelJson) ? "CUSTOM LEVEL: NOT LOADED" : "CUSTOM LEVEL: LOADED";
        hostingText.text = MultiplayerSession.IsHosting
            ? "HOSTING  " + MultiplayerSession.PlayerCount + "/" + MultiplayerSession.MaxPlayers + " PLAYERS"
            : "";
        if (lobbyActionText != null) lobbyActionText.text = MultiplayerSession.IsHosting ? "APPLY SETTINGS" : "CREATE LOBBY";
        if (closeLobbyButton != null) closeLobbyButton.interactable = MultiplayerSession.IsHosting;
        RebuildLobbyRows();
        plugin.SaveLobbyPreferences();
    }

    private void Create(MainMenuManager menu)
    {
        template = menu.startText != null ? menu.startText : menu.curName;
        templateButton = menu.GetComponentInChildren<Button>(true);
        if (template == null || templateButton == null) return;

        root = new GameObject("GunsawMultiplayerNativeMenu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var open = CreateButton(root.transform, "MULTIPLAYER", new Vector2(-780f, -464f), new Vector2(250f, 52f));
        ScreenAnchor(open.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 20f));
        open.onClick.AddListener(() => { plugin.visible = true; plugin.RefreshLobbies(); });

        panel = CreatePanel(root.transform, Vector2.zero, new Vector2(1320f, 920f));
        CreateText(panel.transform, "GUNSAW MULTIPLAYER", new Vector2(0f, 412f), new Vector2(1160f, 48f), 28, TextAlignmentOptions.Center, FontStyles.UpperCase);

        var close = CreateButton(panel.transform, "CLOSE", new Vector2(575f, 412f), new Vector2(120f, 42f));
        close.onClick.AddListener(() => plugin.visible = false);

        // PLAYER
        var playerGroup = CreateGroup(panel.transform, "PLAYER", new Vector2(-325f, 285f), new Vector2(620f, 120f));
        CreateText(playerGroup.transform, "NAME", new Vector2(-240f, -10f), new Vector2(100f, 32f), 14);
        nameInput = CreateInput(playerGroup.transform, new Vector2(-55f, -10f), new Vector2(320f, 42f), 32, value => plugin.playerName = value);

        // NEW LOBBY
        var lobbyGroup = CreateGroup(panel.transform, "NEW LOBBY", new Vector2(315f, 142.5f), new Vector2(630f, 405f));
        CreateText(lobbyGroup.transform, "LOBBY NAME", new Vector2(-235f, 127.5f), new Vector2(120f, 32f), 14);
        lobbyInput = CreateInput(lobbyGroup.transform, new Vector2(55f, 127.5f), new Vector2(460f, 42f), 48, value => plugin.lobbyName = value);

        pvpToggle = CreateToggle(lobbyGroup.transform, "PVP", new Vector2(-255f, 62.5f), new Vector2(90f, 40f), value => plugin.createPvp = value);
        grabToggle = CreateToggle(lobbyGroup.transform, "CAN GRAB", new Vector2(-145f, 62.5f), new Vector2(140f, 40f), value => plugin.createCanGrab = value);
        downToggle = CreateToggle(lobbyGroup.transform, "ONLY UNCONSCIOUS", new Vector2(10f, 62.5f), new Vector2(185f, 40f), value => plugin.createGrabOnlyUnconscious = value);
        CreateText(lobbyGroup.transform, "MAX PLAYERS", new Vector2(186f, 62.5f), new Vector2(110f, 32f), 14);
        maxPlayersInput = CreateInput(lobbyGroup.transform, new Vector2(285f, 62.5f), new Vector2(40f, 40f), 2, value => plugin.createMaxPlayers = value);

        respawnToggle = CreateToggle(lobbyGroup.transform, "ALLOW RESPAWN", new Vector2(-200f, 2.5f), new Vector2(200f, 40f), value => plugin.createAllowRespawn = value);
        CreateText(lobbyGroup.transform, "DELAY", new Vector2(-60f, 2.5f), new Vector2(70f, 32f), 14);
        respawnInput = CreateInput(lobbyGroup.transform, new Vector2(10f, 2.5f), new Vector2(60f, 40f), 4, value => plugin.createRespawnTime = value);
        CreateText(lobbyGroup.transform, "SEC", new Vector2(80f, 2.5f), new Vector2(50f, 32f), 14);
        respawnAtStartToggle = CreateToggle(lobbyGroup.transform, "RESPAWN AT START", new Vector2(205f, 2.5f), new Vector2(190f, 40f), value => plugin.createRespawnAtStart = value);

        CreateText(lobbyGroup.transform, "CONNECTION", new Vector2(-235f, -57.5f), new Vector2(125f, 32f), 14);
        connectionModeText = CreateText(lobbyGroup.transform, "AUTO", new Vector2(-105f, -57.5f), new Vector2(105f, 32f), 14, TextAlignmentOptions.Center);
        var p2p = CreateButton(lobbyGroup.transform, "P2P", new Vector2(0f, -57.5f), new Vector2(95f, 36f));
        p2p.onClick.AddListener(() => plugin.createConnectionMode = ConnectionMode.P2P);
        var relay = CreateButton(lobbyGroup.transform, "RELAY", new Vector2(105f, -57.5f), new Vector2(95f, 36f));
        relay.onClick.AddListener(() => plugin.createConnectionMode = ConnectionMode.Relay);
        var auto = CreateButton(lobbyGroup.transform, "AUTO", new Vector2(210f, -57.5f), new Vector2(95f, 36f));
        auto.onClick.AddListener(() => plugin.createConnectionMode = ConnectionMode.Auto);

        var create = CreateButton(lobbyGroup.transform, "CREATE LOBBY", new Vector2(-155f, -159.5f), new Vector2(280f, 46f));
        lobbyActionText = create.GetComponentInChildren<TMP_Text>();
        create.onClick.AddListener(() =>
        {
            if (MultiplayerSession.IsHosting) plugin.UpdateHostedLobby();
            else plugin.CreateLobby();
        });
        closeLobbyButton = CreateButton(lobbyGroup.transform, "CLOSE LOBBY", new Vector2(155f, -159.5f), new Vector2(280f, 46f));
        closeLobbyButton.onClick.AddListener(plugin.CloseHostedLobby);
        hostingText = CreateText(lobbyGroup.transform, "", new Vector2(0f, -112f), new Vector2(560f, 30f), 14, TextAlignmentOptions.Center);


        // CUSTOM LEVEL
        var customGroup = CreateGroup(panel.transform, "CUSTOM LEVEL", new Vector2(-325f, 145f), new Vector2(620f, 150f));
        var paste = CreateButton(customGroup.transform, "PASTE CUSTOM LEVEL", new Vector2(-152.5f, -42f), new Vector2(295f, 46f));
        paste.onClick.AddListener(() => plugin.PasteCustomLevel());
        var startCustom = CreateButton(customGroup.transform, "START CUSTOM LEVEL", new Vector2(152.5f, -42f), new Vector2(295f, 46f));
        startCustom.onClick.AddListener(() =>
        {
            if (MultiplayerSession.IsHosting && !string.IsNullOrEmpty(plugin.customLevelJson))
                plugin.StartCustomLevel();
        });
        customLevelText = CreateText(customGroup.transform, "", new Vector2(0f, 17f), new Vector2(570f, 28f), 13, TextAlignmentOptions.Center);
        //


        // CONNECTION
        var connectionGroup = CreateGroup(panel.transform, "CONNECTION", new Vector2(-325f, 2.5f), new Vector2(620f, 125f));
        CreateText(connectionGroup.transform, "SERVER", new Vector2(-255f, -8f), new Vector2(90f, 32f), 14);
        serverInput = CreateInput(connectionGroup.transform, new Vector2(-40f, -8f), new Vector2(310f, 42f), 255, value => plugin.lobbyServerAddress = value);
        var connect = CreateButton(connectionGroup.transform, "CONNECT", new Vector2(220f, -8f), new Vector2(130f, 42f));
        connect.onClick.AddListener(() => { if (!MultiplayerSession.IsHosting) plugin.ConnectLobbyServer(); });

        // PUBLIC LOBBIES
        var publicGroup = CreateGroup(panel.transform, "PUBLIC LOBBIES", new Vector2(-2.5f, -251.25f), new Vector2(1265f, 372.5f));
        statusText = CreateText(
            publicGroup.transform,
            "",
            new Vector2(0f, 160f),
            new Vector2(900f, 34f),
            14,
            TextAlignmentOptions.Center);

        var refresh = CreateButton(publicGroup.transform, "REFRESH", new Vector2(540f, 160f), new Vector2(150f, 38f));
        refresh.onClick.AddListener(plugin.RefreshLobbies);

        lobbyRows = CreateScrollArea(publicGroup.transform, new Vector2(0f, -20f), new Vector2(1240f, 300f));
    }

    private GameObject CreatePanel(Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = new Color(0.1f, 0.1f, 0.1f, 1f);
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0.58f, 0.58f, 0.58f, 0.92f);
        outline.effectDistance = new Vector2(1f, -1f);
        var rect = (RectTransform)go.transform;
        rect.anchoredPosition = position; rect.sizeDelta = size;
        return go;
    }

    private GameObject CreateGroup(Transform parent, string title, Vector2 position, Vector2 size)
    {
        var group = CreatePanel(parent, position, size);
        group.name = title + " Group";

        group.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

        var titleText = CreateText(
            group.transform,
            title,
            new Vector2(0f, size.y * 0.5f - 18f),
            new Vector2(size.x - 32f, 28f),
            13,
            TextAlignmentOptions.Left,
            FontStyles.Bold);
        titleText.margin = new Vector4(8f, 0f, 0f, 0f);
        titleText.raycastTarget = false;
        return group;
    }

    private Button CreateButton(Transform parent, string label, Vector2 position, Vector2 size)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        var source = templateButton.targetGraphic as Image;
        image.sprite = source != null ? source.sprite : null;
        image.material = source != null ? source.material : null;
        image.color = source != null ? source.color : new Color(0.16f, 0.2f, 0.2f, 1f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.colors = templateButton.colors;
        SetRect(go.GetComponent<RectTransform>(), position, size);
        CreateText(go.transform, label, Vector2.zero, size, 16, TextAlignmentOptions.Center).raycastTarget = false;
        return button;
    }

    private TMP_InputField CreateInput(Transform parent, Vector2 position, Vector2 size, int limit, Action<string> changed)
    {
        var go = new GameObject("Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false); SetRect(go.GetComponent<RectTransform>(), position, size);
        var image = go.GetComponent<Image>(); image.color = new Color(0.17f, 0.17f, 0.17f, 0.215f);
        var outline = go.AddComponent<Outline>(); outline.effectColor = new Color(0.58f, 0.58f, 0.58f, 0.95f); outline.effectDistance = new Vector2(1f, -1f);
        var field = go.GetComponent<TMP_InputField>(); field.targetGraphic = image; field.characterLimit = limit;
        var text = CreateText(go.transform, "", Vector2.zero, new Vector2(size.x - 16f, size.y), 16, TextAlignmentOptions.Left);
        text.margin = new Vector4(8f, 0f, 8f, 0f); text.enableWordWrapping = false;
        field.textViewport = text.rectTransform; field.textComponent = text;
        field.onValueChanged.AddListener(value => changed(value));
        return field;
    }

    private Toggle CreateToggle(Transform parent, string label, Vector2 position, Vector2 size, Action<bool> changed)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Toggle));
        go.transform.SetParent(parent, false);
        SetRect((RectTransform)go.transform, position, size);

        var back = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        back.transform.SetParent(go.transform, false);
        SetRect((RectTransform)back.transform, new Vector2(-size.x * 0.5f + 16f, 0f), new Vector2(28f, 28f));
        back.GetComponent<Image>().color = new Color(0.27f, 0.27f, 0.27f, 0.415f);

        var check = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        check.transform.SetParent(back.transform, false);
        SetRect((RectTransform)check.transform, Vector2.zero, new Vector2(18f, 18f));
        check.GetComponent<Image>().color = new Color(0.46f, 0.4f, 0.4f, 1f);

        var toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = back.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();

        var textRect = CreateText(go.transform, label, new Vector2(18f, 0f), new Vector2(size.x - 36f, size.y), 14, TextAlignmentOptions.MidlineLeft);
        textRect.raycastTarget = false;

        toggle.onValueChanged.AddListener(value => changed(value));
        return toggle;
    }

    private Transform CreateScrollArea(Transform parent, Vector2 position, Vector2 size)
    {
        var view = new GameObject("LobbyScroll", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect)); view.transform.SetParent(parent, false); SetRect((RectTransform)view.transform, position, size);
        view.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.16f, 0.1375f); view.GetComponent<Mask>().showMaskGraphic = true;
        var outline = view.AddComponent<Outline>(); outline.effectColor = new Color(0.58f, 0.58f, 0.58f, 0.92f); outline.effectDistance = new Vector2(1f, -1f);
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)); content.transform.SetParent(view.transform, false);
        var contentRect = (RectTransform)content.transform; contentRect.anchorMin = new Vector2(0f, 1f); contentRect.anchorMax = new Vector2(1f, 1f); contentRect.pivot = new Vector2(0.5f, 1f); contentRect.anchoredPosition = new Vector2(0f, -4f); contentRect.sizeDelta = new Vector2(-10f, 0f);
        var layout = content.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(8, 8, 6, 6); layout.spacing = 6; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = view.GetComponent<ScrollRect>(); scroll.viewport = (RectTransform)view.transform; scroll.content = contentRect; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        return content.transform;
    }

    private TMP_Text CreateText(Transform parent, string value, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false); SetRect((RectTransform)go.transform, position, size);
        var text = go.GetComponent<TextMeshProUGUI>(); text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial; text.color = template.color; text.fontSize = fontSize; text.alignment = alignment; text.fontStyle = style; text.text = value; text.enableWordWrapping = false; return text;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size) { rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.pivot = new Vector2(0.5f, 0.5f); rect.anchoredPosition = position; rect.sizeDelta = size; }
    private static void ScreenAnchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
    }

    private void FitPanelToScreen()
    {
        var canvasRect = root == null ? null : root.GetComponent<RectTransform>();
        if (canvasRect == null || panel == null) return;
        var available = canvasRect.rect.size - new Vector2(48f, 48f);
        if (available.x <= 0f || available.y <= 0f) return;
        var scale = Mathf.Min(1f, Mathf.Min(available.x / 1320f, available.y / 920f));
        panel.transform.localScale = new Vector3(scale, scale, 1f);
    }
    private static void SetInput(TMP_InputField input, string value) { if (input != null && !input.isFocused && input.text != value) input.SetTextWithoutNotify(value); }

    private void RebuildLobbyRows()
    {
        var hash = plugin.lobbies.Count;
        foreach (var lobby in plugin.lobbies) hash = hash * 31 + (lobby.id ?? "").GetHashCode() + lobby.players;
        if (hash == renderedLobbyHash) return;
        renderedLobbyHash = hash;
        for (var i = lobbyRows.childCount - 1; i >= 0; i--) Destroy(lobbyRows.GetChild(i).gameObject);
        foreach (var lobby in plugin.lobbies)
        {
            var row = new GameObject("Lobby", typeof(RectTransform), typeof(LayoutElement)); row.transform.SetParent(lobbyRows, false); row.GetComponent<LayoutElement>().preferredHeight = 46f;
            var info = CreateText(row.transform, lobby.name + "  |  " + lobby.hostName + "  |  " + lobby.map + "  |  " + (lobby.pvp ? "PVP" : "CO-OP") + "  |  " + lobby.players + "/" + lobby.maxPlayers, new Vector2(-135f, 0f), new Vector2(810f, 42f), 14); info.enableWordWrapping = false;
            var join = CreateButton(row.transform, "JOIN", new Vector2(450f, 0f), new Vector2(140f, 40f)); var id = lobby.id; join.onClick.AddListener(() => { if (!MultiplayerSession.IsHosting) plugin.JoinLobby(id); });
        }
    }
}
