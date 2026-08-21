using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal sealed class MultiplayerLobbyUi : MonoBehaviour
{
    private GunsawMultiplayerPlugin plugin;
    private GameObject root;
    private GameObject panel;
    private TMP_Text template;
    private Button templateButton;
    private TMP_InputField nameInput, lobbyInput, maxPlayersInput, respawnInput, initialScaleInput, startingWeaponInput, respawnWeaponInput, startingAmmoInput, respawnAmmoInput, serverInput, teamsCfgInput;
    private Toggle pvpToggle, grabToggle, downToggle, respawnToggle, respawnAtStartToggle, playerCollisionsToggle, cheatsToggle, allowSwapToggle, allowScaleChangingToggle, allowObserverToggle, teamsToggle;
    private TMP_Text statusText, customLevelText, connectionModeText, updateText, tooltipText;
    private GameObject tooltipPanel;
    private TMP_Text lobbyActionText;
    private Button closeLobbyButton;
    private Transform lobbyRows;
    private CustomLevelBrowserUi customLevelBrowser;
    private int renderedLobbyHash;
    private MainMenuManager menu;

    internal void Configure(GunsawMultiplayerPlugin owner)
    {
        plugin = owner;
        if (SceneManager.GetActiveScene().name != "LevelSelect")
        {
            if (root != null && root.activeSelf) root.SetActive(false);
            return;
        }
        if (menu == null) menu = FindObjectOfType<MainMenuManager>();
        if (menu == null)
        {
            if (root != null && root.activeSelf) root.SetActive(false);
            return;
        }
        if (root == null) Create(menu);
        if (root == null) return;
        if (!root.activeSelf) root.SetActive(true);
        if (panel.activeSelf != plugin.visible) panel.SetActive(plugin.visible);
        if (!plugin.visible)
        {
            customLevelBrowser?.SetOpen(false);
            return;
        }
        var panelRect = panel.GetComponent<RectTransform>();
        var targetPanelX = customLevelBrowser != null && customLevelBrowser.IsOpen ? -300f : 0f;
        panelRect.anchoredPosition = new Vector2(Mathf.Lerp(panelRect.anchoredPosition.x, targetPanelX, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime)), panelRect.anchoredPosition.y);
        FitPanelToScreen();
        customLevelBrowser?.Tick();

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
        playerCollisionsToggle.isOn = plugin.createPlayerCollisions;
        cheatsToggle.isOn = plugin.createCheats;
        allowSwapToggle.isOn = plugin.createAllowSwap;
        allowScaleChangingToggle.isOn = plugin.createAllowScaleChanging;
        allowObserverToggle.isOn = plugin.createAllowObserver;
        teamsToggle.isOn = plugin.createTeams;
        SetInput(teamsCfgInput, plugin.createTeamsCfg);
        SetInput(initialScaleInput, plugin.createInitialScale);
        SetInput(startingWeaponInput, plugin.createStartingWeapon);
        SetInput(respawnWeaponInput, plugin.createRespawnWeapon);
        SetInput(startingAmmoInput, plugin.createStartingAmmo);
        SetInput(respawnAmmoInput, plugin.createRespawnAmmo);
        respawnInput.interactable = plugin.createAllowRespawn;
        respawnAtStartToggle.interactable = plugin.createAllowRespawn;
        connectionModeText.text = plugin.createConnectionMode.ToString();
        statusText.text = plugin.status;
        if (updateText != null) updateText.text = plugin.updateStatus;
        customLevelText.text = string.IsNullOrEmpty(plugin.customLevelJson) ? "CUSTOM LEVEL: NOT LOADED" : "CUSTOM LEVEL: LOADED";
        if (lobbyActionText != null) lobbyActionText.text = MultiplayerSession.IsHosting ? "APPLY SETTINGS" : "CREATE LOBBY";
        if (closeLobbyButton != null) closeLobbyButton.interactable = MultiplayerSession.IsHosting;
        RebuildLobbyRows();
        plugin.SaveLobbyPreferences();
    }

    private void Create(MainMenuManager menu)
    {
        template = menu.startText != null ? menu.startText : menu.curName;
        templateButton = FindNativeMenuButton(menu);
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

        CreateNativeOpenButton(menu, templateButton);

        panel = CreatePanel(root.transform, Vector2.zero, new Vector2(1320f, 920f));
        CreateText(panel.transform, "GUNSAW MULTIPLAYER v" + GunsawMultiplayerPlugin.PluginVersion, new Vector2(0f, 412f), new Vector2(1160f, 48f), 28, TextAlignmentOptions.Center, FontStyles.UpperCase);

        var checkUpdates = CreateButton(panel.transform, "CHECK UPDATES", new Vector2(-515f, 412f), new Vector2(240f, 42f));
        checkUpdates.onClick.AddListener(() => plugin.CheckForUpdates(true));
        updateText = CreateText(panel.transform, "", new Vector2(0f, 376f), new Vector2(1000f, 26f), 14, TextAlignmentOptions.Center);

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

        var settings = CreateSettingsScroll(lobbyGroup.transform, new Vector2(0f, 0f), new Vector2(580f, 195f));
        pvpToggle = CreateToggle(CreateSettingsRow(settings), "PVP", Vector2.zero, new Vector2(520f, 40f), value => plugin.createPvp = value);
        AddTooltip(pvpToggle.gameObject, "PVP: Enables damage between players. Some functions, such as teammate position markers or the /tp command, will not work in this mode.");
        grabToggle = CreateToggle(CreateSettingsRow(settings), "CAN GRAB", Vector2.zero, new Vector2(520f, 40f), value => plugin.createCanGrab = value);
        AddTooltip(grabToggle.gameObject, "CAN GRAB: Allows players to grab other players with the gravity laser.");
        downToggle = CreateToggle(CreateSettingsRow(settings), "ONLY UNCONSCIOUS", Vector2.zero, new Vector2(520f, 40f), value => plugin.createGrabOnlyUnconscious = value);
        AddTooltip(downToggle.gameObject, "ONLY UNCONSCIOUS: Applies to CAN GRAB. When enabled, other players can only be grabbed while they are in a ragdoll state.");
        var maxPlayersRow = CreateSettingsRow(settings);
        CreateText(maxPlayersRow, "MAX PLAYERS", new Vector2(-150f, 0f), new Vector2(220f, 32f), 14);
        maxPlayersInput = CreateInput(maxPlayersRow, new Vector2(170f, 0f), new Vector2(80f, 40f), 2, value => plugin.createMaxPlayers = value);
        AddTooltip(maxPlayersRow.gameObject, "MAX PLAYERS: The maximum number of players allowed in your lobby.");
        respawnToggle = CreateToggle(CreateSettingsRow(settings), "ALLOW RESPAWN", Vector2.zero, new Vector2(520f, 40f), value => plugin.createAllowRespawn = value);
        AddTooltip(respawnToggle.gameObject, "ALLOW RESPAWN: When enabled, players can respawn after death. When disabled, they can only spectate living players until the game ends.");
        var delayRow = CreateSettingsRow(settings);
        CreateText(delayRow, "RESPAWN DELAY", new Vector2(-115f, 0f), new Vector2(290f, 32f), 14);
        respawnInput = CreateInput(delayRow, new Vector2(170f, 0f), new Vector2(80f, 40f), 4, value => plugin.createRespawnTime = value);
        AddTooltip(delayRow.gameObject, "RESPAWN DELAY: The time in seconds before a player respawns when ALLOW RESPAWN is enabled.");
        respawnAtStartToggle = CreateToggle(CreateSettingsRow(settings), "RESPAWN AT START", Vector2.zero, new Vector2(520f, 40f), value => plugin.createRespawnAtStart = value);
        AddTooltip(respawnAtStartToggle.gameObject, "RESPAWN AT START: Applies to ALLOW RESPAWN. When enabled, players spawn at a player spawn point placed by the map author. If there are several, one is chosen at random. Some custom maps may accidentally contain too many spawn points and become impossible to complete without removing the extra points. When disabled, players respawn at the position of their corpse.");
        playerCollisionsToggle = CreateToggle(CreateSettingsRow(settings), "PLAYER COLLISIONS", Vector2.zero, new Vector2(520f, 40f), value => plugin.createPlayerCollisions = value);
        AddTooltip(playerCollisionsToggle.gameObject, "PLAYER COLLISIONS: When disabled, players can pass through each other without blocking one another, which can help on parkour maps.");
        cheatsToggle = CreateToggle(CreateSettingsRow(settings), "CHEATS", Vector2.zero, new Vector2(520f, 40f), value => plugin.createCheats = value);
        AddTooltip(cheatsToggle.gameObject, "CHEATS: Allows the cheats opened with SPACE + END.");
        allowSwapToggle = CreateToggle(CreateSettingsRow(settings), "ALLOW SWAP", Vector2.zero, new Vector2(520f, 40f), value => plugin.createAllowSwap = value);
        AddTooltip(allowSwapToggle.gameObject, "ALLOW SWAP: Allows players to use /swap to choose a different character for their next respawn.");
        allowScaleChangingToggle = CreateToggle(CreateSettingsRow(settings), "ALLOW SCALE CHANGING", Vector2.zero, new Vector2(520f, 40f), value => plugin.createAllowScaleChanging = value);
        AddTooltip(allowScaleChangingToggle.gameObject, "ALLOW SCALE CHANGING: Allows players to use /scale between 0.25 and 2.0.");
        allowObserverToggle = CreateToggle(CreateSettingsRow(settings), "ALLOW OBSERVER", Vector2.zero, new Vector2(520f, 40f), value => plugin.createAllowObserver = value);
        AddTooltip(allowObserverToggle.gameObject, "ALLOW OBSERVER: Allows the OBSERVER keyboard easter egg.");
        teamsToggle = CreateToggle(CreateSettingsRow(settings), "TEAMS", Vector2.zero, new Vector2(520f, 40f), value => plugin.createTeams = value);
        var teamsCfgRow = CreateSettingsRow(settings);
        CreateText(teamsCfgRow, "TEAMS CFG", new Vector2(-185f, 0f), new Vector2(140f, 32f), 14);
        teamsCfgInput = CreateInput(teamsCfgRow, new Vector2(80f, 0f), new Vector2(330f, 36f), 512, value => plugin.createTeamsCfg = value);
        var initialScaleRow = CreateSettingsRow(settings);
        CreateText(initialScaleRow, "INITIAL SCALE", new Vector2(-115f, 0f), new Vector2(290f, 32f), 14);
        initialScaleInput = CreateInput(initialScaleRow, new Vector2(170f, 0f), new Vector2(80f, 40f), 4, value => plugin.createInitialScale = value);
        AddTooltip(initialScaleRow.gameObject, "INITIAL SCALE: The character scale assigned when a player joins or respawns. Allowed range: 0.25 to 2.0.");
        var startingWeaponRow = CreateSettingsRow(settings);
        CreateText(startingWeaponRow, "STARTING WEAPON", new Vector2(-145f, 0f), new Vector2(210f, 32f), 14);
        startingWeaponInput = CreateInput(startingWeaponRow, new Vector2(115f, 0f), new Vector2(280f, 36f), 512, value => plugin.createStartingWeapon = value);
        AddTooltip(startingWeaponRow.gameObject, "STARTING WEAPON: Weapons assigned when a player joins. Use Default, None;None;None, or three names separated by semicolons.");
        var respawnWeaponRow = CreateSettingsRow(settings);
        CreateText(respawnWeaponRow, "RESPAWN WEAPON", new Vector2(-145f, 0f), new Vector2(210f, 32f), 14);
        respawnWeaponInput = CreateInput(respawnWeaponRow, new Vector2(115f, 0f), new Vector2(280f, 36f), 512, value => plugin.createRespawnWeapon = value);
        AddTooltip(respawnWeaponRow.gameObject, "RESPAWN WEAPON: Weapons assigned after respawn. Use Default, None;None;None, or three names separated by semicolons.");
        var startingAmmoRow = CreateSettingsRow(settings);
        CreateText(startingAmmoRow, "STARTING AMMO", new Vector2(-145f, 0f), new Vector2(210f, 32f), 14);
        startingAmmoInput = CreateInput(startingAmmoRow, new Vector2(115f, 0f), new Vector2(280f, 36f), 32, value => plugin.createStartingAmmo = value);
        AddTooltip(startingAmmoRow.gameObject, "STARTING AMMO: Pistol;Rifle;Heavy;Grenade ammo assigned when a player joins.");
        var respawnAmmoRow = CreateSettingsRow(settings);
        CreateText(respawnAmmoRow, "RESPAWN AMMO", new Vector2(-145f, 0f), new Vector2(210f, 32f), 14);
        respawnAmmoInput = CreateInput(respawnAmmoRow, new Vector2(115f, 0f), new Vector2(280f, 36f), 32, value => plugin.createRespawnAmmo = value);
        AddTooltip(respawnAmmoRow.gameObject, "RESPAWN AMMO: Pistol;Rifle;Heavy;Grenade ammo assigned after respawn.");

        CreateText(lobbyGroup.transform, "CONNECTION", new Vector2(-235f, -120f), new Vector2(125f, 32f), 14);
        connectionModeText = CreateText(lobbyGroup.transform, "AUTO", new Vector2(-105f, -120f), new Vector2(105f, 32f), 14, TextAlignmentOptions.Center);
        var p2p = CreateButton(lobbyGroup.transform, "P2P", new Vector2(0f, -120f), new Vector2(95f, 36f));
        AddTooltip(p2p.gameObject, "P2P: Experimental direct connection to the host computer. It can reduce ping when you are far from the lobby server, but other players in the lobby can expose your IP address. Do not use it yet unless you are playing with two people and are sure it works correctly.", Color.red);
        p2p.onClick.AddListener(() => plugin.createConnectionMode = ConnectionMode.P2P);
        var relay = CreateButton(lobbyGroup.transform, "RELAY", new Vector2(105f, -120f), new Vector2(95f, 36f));
        AddTooltip(relay.gameObject, "RELAY: Standard connection mode. It uses the server as a proxy and is the recommended mode.");
        relay.onClick.AddListener(() => plugin.createConnectionMode = ConnectionMode.Relay);
        var auto = CreateButton(lobbyGroup.transform, "AUTO", new Vector2(210f, -120f), new Vector2(95f, 36f));
        AddTooltip(auto.gameObject, "AUTO: First tries P2P, then falls back to RELAY if it fails. It supports P2P + RELAY, where players who can connect through P2P use it while others use RELAY. P2P is not reliable yet. Leave RELAY selected if you are not sure.", Color.red);
        auto.onClick.AddListener(() => plugin.createConnectionMode = ConnectionMode.Auto);

        var create = CreateButton(lobbyGroup.transform, "CREATE LOBBY", new Vector2(-155f, -172.5f), new Vector2(280f, 46f));
        lobbyActionText = create.GetComponentInChildren<TMP_Text>();
        create.onClick.AddListener(() =>
        {
            if (MultiplayerSession.IsHosting) plugin.UpdateHostedLobby();
            else plugin.CreateLobby();
        });
        closeLobbyButton = CreateButton(lobbyGroup.transform, "CLOSE LOBBY", new Vector2(155f, -172.5f), new Vector2(280f, 46f));
        closeLobbyButton.onClick.AddListener(plugin.CloseHostedLobby);


        // CUSTOM LEVEL
        var customGroup = CreateGroup(panel.transform, "CUSTOM LEVEL", new Vector2(-325f, 145f), new Vector2(620f, 150f));
        var paste = CreateButton(customGroup.transform, "PASTE", new Vector2(-190f, -42f), new Vector2(180f, 46f));
        paste.onClick.AddListener(() => plugin.PasteCustomLevel());
        var startCustom = CreateButton(customGroup.transform, "START", new Vector2(0f, -42f), new Vector2(180f, 46f));
        startCustom.onClick.AddListener(() =>
        {
            if (MultiplayerSession.IsHosting && !string.IsNullOrEmpty(plugin.customLevelJson))
                plugin.StartCustomLevel();
        });
        var openBrowser = CreateButton(customGroup.transform, "OPEN BROWSER", new Vector2(190f, -42f), new Vector2(180f, 46f));
        openBrowser.onClick.AddListener(() => customLevelBrowser?.Toggle());
        customLevelText = CreateText(customGroup.transform, "", new Vector2(0f, 17f), new Vector2(570f, 28f), 13, TextAlignmentOptions.Center);
        customLevelBrowser = new CustomLevelBrowserUi(plugin, root.transform, template, templateButton);
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

        tooltipPanel = CreatePanel(panel.transform, new Vector2(0f, -412f), new Vector2(1160f, 66f));
        tooltipPanel.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.04f, 0.96f);
        tooltipText = CreateText(tooltipPanel.transform, "", Vector2.zero, new Vector2(1120f, 58f), 13, TextAlignmentOptions.Center);
        tooltipText.enableWordWrapping = true;
        tooltipText.raycastTarget = false;
        tooltipPanel.SetActive(false);
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

    private Button FindNativeMenuButton(MainMenuManager menu)
    {
        var fallback = menu.GetComponentInChildren<Button>(true);
        foreach (var candidate in menu.GetComponentsInChildren<Button>(true))
        {
            var text = candidate.GetComponentInChildren<TMP_Text>(true);
            if (text != null && string.Equals(text.text.Trim(), "PLAY", StringComparison.OrdinalIgnoreCase)) return candidate;
        }
        return fallback;
    }

    private void CreateNativeOpenButton(MainMenuManager menu, Button source)
    {
        var clone = Instantiate(source.gameObject, source.transform.parent);
        clone.name = "Multiplayer Button";
        var button = clone.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => { plugin.visible = true; plugin.RefreshLobbies(); });
        var text = clone.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = "MULTIPLAYER";
        var rect = clone.GetComponent<RectTransform>();
        var sourceRect = source.GetComponent<RectTransform>();
        var extension = sourceRect.rect.height;
        foreach (var candidate in source.transform.parent.GetComponentsInChildren<Button>(true))
        {
            var candidateText = candidate.GetComponentInChildren<TMP_Text>(true);
            if (candidateText == null || !string.Equals(candidateText.text.Trim(), "SETTINGS", StringComparison.OrdinalIgnoreCase)) continue;
            var candidateRect = candidate.GetComponent<RectTransform>();
            if (candidateRect != null) extension = Mathf.Abs(sourceRect.position.y - candidateRect.position.y);
            break;
        }
        rect.position = sourceRect.position + Vector3.up * extension;
        StartCoroutine(ExpandNativeButtonFrame(sourceRect, extension));
    }

    private System.Collections.IEnumerator ExpandNativeButtonFrame(RectTransform source, float extension)
    {
        yield return new WaitForEndOfFrame();
        var frame = FindNativeButtonFrame(source);
        if (frame != null) frame.offsetMax += Vector2.up * (extension + 2f);
    }

    private static RectTransform FindNativeButtonFrame(RectTransform source)
    {
        var title = source.root.Find("Title");
        return title != null && title.childCount > 3 ? title.GetChild(3) as RectTransform : null;
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
        back.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.16f, 0.95f);

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

    private void AddTooltip(GameObject target, string message, Color? color = null)
    {
        if (target == null) return;
        var trigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
        trigger.triggers ??= new List<EventTrigger.Entry>();

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => ShowTooltip(message, color ?? Color.white));
        trigger.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => HideTooltip());
        trigger.triggers.Add(exit);
    }

    private void ShowTooltip(string message, Color color)
    {
        if (tooltipPanel == null || tooltipText == null) return;
        tooltipText.text = message;
        tooltipText.color = color;
        tooltipPanel.SetActive(true);
    }

    private void HideTooltip()
    {
        if (tooltipPanel != null) tooltipPanel.SetActive(false);
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

    private Transform CreateSettingsScroll(Transform parent, Vector2 position, Vector2 size)
    {
        var content = CreateScrollArea(parent, position, size);
        var view = content.parent;
        view.name = "Game Rules Scroll";
        var scroll = view.GetComponent<ScrollRect>();
        scroll.scrollSensitivity = 24f;

        var scrollbar = new GameObject("Scrollbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
        scrollbar.transform.SetParent(view, false);
        var scrollbarRect = (RectTransform)scrollbar.transform;
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.anchoredPosition = new Vector2(-5f, 0f);
        scrollbarRect.sizeDelta = new Vector2(12f, -10f);
        var scrollbarImage = scrollbar.GetComponent<Image>();
        scrollbarImage.color = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        handle.transform.SetParent(scrollbar.transform, false);
        var handleRect = (RectTransform)handle.transform;
        handleRect.anchorMin = new Vector2(0f, 0f);
        handleRect.anchorMax = new Vector2(1f, 1f);
        handleRect.offsetMin = new Vector2(2f, 2f);
        handleRect.offsetMax = new Vector2(-2f, -2f);
        handle.GetComponent<Image>().color = new Color(0.65f, 0.65f, 0.65f, 0.95f);
        var bar = scrollbar.GetComponent<Scrollbar>();
        bar.targetGraphic = handle.GetComponent<Image>();
        bar.handleRect = handleRect;
        bar.direction = Scrollbar.Direction.BottomToTop;

        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        return content;
    }

    private Transform CreateSettingsRow(Transform parent)
    {
        var row = new GameObject("Rule", typeof(RectTransform), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = 40f;
        return row.transform;
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
        var canJoin = plugin.CanJoinLobby;
        var hash = plugin.lobbies.Count + (canJoin ? 1 : 0) + (MultiplayerSession.IsActive ? 7 : 0);
        foreach (var lobby in plugin.lobbies)
            hash = hash * 31 + (lobby.id ?? "").GetHashCode() + lobby.players + (plugin.IsJoinedLobby(lobby.id) ? 13 : 0);
        if (hash == renderedLobbyHash) return;
        renderedLobbyHash = hash;
        for (var i = lobbyRows.childCount - 1; i >= 0; i--) Destroy(lobbyRows.GetChild(i).gameObject);
        foreach (var lobby in plugin.lobbies)
        {
            var row = new GameObject("Lobby", typeof(RectTransform), typeof(LayoutElement)); row.transform.SetParent(lobbyRows, false); row.GetComponent<LayoutElement>().preferredHeight = 46f;
            var info = CreateText(row.transform, lobby.name + "  |  " + lobby.hostName + "  |  " + lobby.map + "  |  " + (lobby.teams ? "TEAMS" : lobby.pvp ? "PVP" : "CO-OP") + "  |  " + lobby.players + "/" + lobby.maxPlayers, new Vector2(-135f, 0f), new Vector2(810f, 42f), 14); info.enableWordWrapping = false;
            var id = lobby.id;
            var joined = plugin.IsJoinedLobby(id);
            var join = CreateButton(row.transform, joined ? "LEAVE" : "JOIN", new Vector2(450f, 0f), new Vector2(140f, 40f));
            join.interactable = joined || canJoin;
            if (joined) join.onClick.AddListener(plugin.LeaveLobby);
            else join.onClick.AddListener(() => plugin.JoinLobby(id));
        }
    }
}
