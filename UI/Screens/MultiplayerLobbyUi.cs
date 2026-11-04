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
    private TMP_InputField nameInput, lobbyInput, maxPlayersInput, respawnInput, numberOfLivesInput, healthFactorInput, uhpValueInput, regenFactorInput, initialScaleInput, serverInput;
    private Toggle pvpToggle, grabToggle, downToggle, respawnToggle, autoRestartToggle, respawnAtStartToggle, playerCollisionsToggle, cheatsToggle, allowSwapToggle, allowScaleChangingToggle, allowObserverToggle, teamsToggle, blackoutToggle, restrictLightToggle, gunGameToggle, uniformHPToggle;
    private TMP_InputField[] startingAmmoInputs, respawnAmmoInputs;
    private Button[] startingWeaponButtons, respawnWeaponButtons;
    private string? displayedStartingWeapons, displayedRespawnWeapons;
    private List<WeaponPreset> lobbyWeapons = new();
    private GameObject weaponPicker;
    private Transform weaponPickerRows;
    private TMP_InputField weaponSearch;
    private bool editingRandomWeapons, randomInclude, showSelectedWeapons;
    private List<string> randomWeaponNames = new();
    private Button randomWeaponButton, includeWeaponsButton, excludeWeaponsButton, selectedWeaponsButton, clearWeaponFilterButton, applyWeaponFilterButton;
    private TMP_Text weaponFilterSummary;
    private TMP_Text weaponPickerTitle, weaponPickerError;
    private bool pickingRespawnWeapon;
    private int pickingWeaponSlot;
    private Button ggSequenceButton;
    private string? displayedSequence;
    private GameObject sequenceEditor;
    private Transform sequenceRows;
    private TMP_Text sequenceTitle;
    private List<string> sequenceDraft = new();
    private int sequenceWeaponIndex = -1;
    private bool insertingSequenceWeapon;
    private Button teamsEditorButton;
    private string? displayedTeams;
    private GameObject teamsEditor;
    private Transform teamRows;
    private TMP_Text teamsEditorTitle, teamsEditorHint, teamsEditorError;
    private List<LobbyTeamEntry> teamsDraft = new();
    private static readonly string[] teamColors = { "red", "orange", "yellow", "green", "teal", "cyan", "blue", "purple", "magenta", "white", "grey", "black" };
    private Toggle propHuntToggle;
    private TMP_InputField propHuntTimeInput;
    private TMP_InputField propHuntHuntersInput;
    private TMP_InputField propHuntShotPenaltyInput;
    private TMP_InputField propHuntShotHealingInput;
    private TMP_InputField propHuntTelekinesisPenaltyInput;
    private TMP_InputField propHuntTelekinesisIntervalInput;
    private TMP_InputField propHuntStunCooldownInput;
    private TMP_InputField propHuntChangeCooldownInput;
    private Button ggResetButton, ggRollbackButton, ggNoneButton;
    private string? displayedCustomLevel;
    private CyclingLabel statusLabel;
    private string appliedSettings = "";
    private bool pendingSettings, wasHosting;
    private ColorBlock lobbyActionColors;
    private TMP_Text statusText, customLevelText, tooltipText;
    private GameObject tooltipPanel;
    private TMP_Text lobbyActionText;
    private TMP_Text lobbyGroupTitle;
    private Button lobbyActionButton, p2pButton, relayButton, autoButton;
    private Button presetsButton, resetButton;
    private GameObject resetSettingsDialog;
    private GameObject crossServerDialog;
    private GunsawMultiplayerPlugin.LobbyInfo pendingJoinLobby;
    private Button customLevelActionButton;
    private GameObject updateIndicator;
    private Transform settingsRows;
    private readonly Dictionary<string, Toggle> settingToggles = new();
    private readonly Dictionary<string, string> settingLabels = new();
    private Func<string, bool> readSettingBoolean;
    private string settingsQuery = "";
    private TMP_Text settingsEmpty;
    private Button closeLobbyButton;
    private bool showingLeaveLobbyButton;
    private readonly Dictionary<string, Sprite> characterHeads = new();
    private GameObject serverBrowserPanel;
    private TMP_Text serverBrowserTitle, serverBrowserState;
    private Button serverBrowserRefresh;
    private Transform serverRows;
    private bool serverBrowserOpen;
    private int renderedServerListHash;
    private GameObject customLevelSuggestionDialog;
    private TMP_Text customLevelSuggestionText;
    private CustomLevelSuggestionPacket pendingCustomLevelSuggestion;
    private Transform lobbyRows;
    private TMP_Text publicLobbiesTitle;
    private Button refreshLobbiesButton;
    private CustomLevelBrowserUi customLevelBrowser;
    private int renderedLobbyHash;
    private MainMenuManager menu;
    private GameObject presetsPanel;
    private TMP_InputField presetNameInput;
    private Transform presetRows;

    internal void Configure(GunsawMultiplayerPlugin owner)
    {
        plugin = owner;
        if (wasHosting != MultiplayerSession.IsHosting)
        {
            wasHosting = MultiplayerSession.IsHosting;
            RefreshPendingSettings();
        }
        if (HeadlessLobbyService.IsHeadlessMode) return;
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
        if (root == null || customLevelBrowser == null || !customLevelBrowser.IsReady)
        {
            if (root != null) Destroy(root);
            root = null;
            panel = null;
            customLevelBrowser = null;
            Create(menu);
        }
        if (root == null) return;
        if (!root.activeSelf) root.SetActive(true);
        if (panel.activeSelf != plugin.visible) panel.SetActive(plugin.visible);
        if (!plugin.visible)
        {
            HideTooltip();
            if (presetsPanel != null) presetsPanel.SetActive(false);
            if (weaponPicker != null) weaponPicker.SetActive(false);
            if (sequenceEditor != null) sequenceEditor.SetActive(false);
            if (teamsEditor != null) teamsEditor.SetActive(false);
            if (resetSettingsDialog != null) resetSettingsDialog.SetActive(false);
            if (crossServerDialog != null) crossServerDialog.SetActive(false);
            customLevelBrowser?.SetOpen(false);
            SetServerBrowserOpen(false);
            return;
        }
        var panelRect = panel.GetComponent<RectTransform>();
        var targetPanelX = customLevelBrowser != null && customLevelBrowser.IsOpen ? -300f : 0f;
        panelRect.anchoredPosition = new Vector2(Mathf.Lerp(panelRect.anchoredPosition.x, targetPanelX, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime)), panelRect.anchoredPosition.y);
        FitPanelToScreen();
        customLevelBrowser?.Tick();

        SetInput(nameInput, plugin.playerName);
        var viewingLobbySettings = MultiplayerSession.IsActive && !MultiplayerSession.IsHost;
        SetInput(lobbyInput, viewingLobbySettings ? plugin.JoinedLobbyName : plugin.lobbyName);
        SetInput(maxPlayersInput, viewingLobbySettings ? MultiplayerSession.MaxPlayers.ToString() : plugin.lobbySettings.MaxPlayers);
        SetInput(respawnInput, viewingLobbySettings ? MultiplayerSession.RespawnTimeSeconds.ToString() : plugin.lobbySettings.RespawnTime);
        SetInput(numberOfLivesInput, viewingLobbySettings ? MultiplayerSession.NumberOfLives.ToString() : plugin.lobbySettings.NumberOfLives);
        SetToggle(uniformHPToggle, viewingLobbySettings ? MultiplayerSession.UniformHP : plugin.lobbySettings.UniformHP);
        SetInput(uhpValueInput, viewingLobbySettings ? MultiplayerSession.UHPValue.ToString("0.##") : plugin.lobbySettings.UHPValue);
        SetInput(healthFactorInput, viewingLobbySettings ? MultiplayerSession.HealthFactor.ToString("0.##") : plugin.lobbySettings.HealthFactor);
        SetInput(regenFactorInput, viewingLobbySettings ? MultiplayerSession.RegenFactor.ToString("0.##") : plugin.lobbySettings.RegenFactor);
        SetToggle(blackoutToggle, viewingLobbySettings ? MultiplayerSession.BlackoutEnabled : plugin.lobbySettings.Blackout);
        SetToggle(restrictLightToggle, viewingLobbySettings ? MultiplayerSession.RestrictLightEnabled : plugin.lobbySettings.RestrictLight);
        SetInput(serverInput, plugin.lobbyServerAddress);
        SetToggle(pvpToggle, viewingLobbySettings ? MultiplayerSession.PvpEnabled : plugin.lobbySettings.Pvp);
        SetToggle(propHuntToggle, viewingLobbySettings ? MultiplayerSession.PropHuntEnabled : plugin.lobbySettings.PropHunt);
        SetInput(propHuntTimeInput, viewingLobbySettings ? MultiplayerSession.PropHuntTime.ToString() : plugin.lobbySettings.PropHuntTime);
        SetInput(propHuntHuntersInput, viewingLobbySettings ? MultiplayerSession.PropHuntHunters.ToString() : plugin.lobbySettings.PropHuntHunters);
        SetInput(propHuntShotPenaltyInput, viewingLobbySettings ? MultiplayerSession.PropHuntShotPenalty.ToString("0.##") : plugin.lobbySettings.PropHuntShotPenalty);
        SetInput(propHuntShotHealingInput, viewingLobbySettings ? MultiplayerSession.PropHuntShotHealing.ToString("0.##") : plugin.lobbySettings.PropHuntShotHealing);
        SetInput(propHuntTelekinesisPenaltyInput, viewingLobbySettings ? MultiplayerSession.PropHuntTelekinesisPenalty.ToString("0.##") : plugin.lobbySettings.PropHuntTelekinesisPenalty);
        SetInput(propHuntTelekinesisIntervalInput, viewingLobbySettings ? MultiplayerSession.PropHuntTelekinesisInterval.ToString("0.##") : plugin.lobbySettings.PropHuntTelekinesisInterval);
        SetInput(propHuntStunCooldownInput, viewingLobbySettings ? MultiplayerSession.PropHuntStunCooldown.ToString("0.##") : plugin.lobbySettings.PropHuntStunCooldown);
        SetInput(propHuntChangeCooldownInput, viewingLobbySettings ? MultiplayerSession.PropHuntChangeCooldown.ToString("0.##") : plugin.lobbySettings.PropHuntChangeCooldown);
        SetToggle(gunGameToggle, viewingLobbySettings ? MultiplayerSession.GunGameEnabled : plugin.lobbySettings.GunGame);
        var sequenceValue = viewingLobbySettings ? MultiplayerSession.GGSequence : plugin.lobbySettings.GGSequence;
        if (displayedSequence != sequenceValue)
        {
            displayedSequence = sequenceValue;
            ggSequenceButton.GetComponentInChildren<TMP_Text>(true).text = "EDIT <color=#59616D>//</color> <color=#F29A64>" + LobbyWeaponRules.Sequence(sequenceValue).Count + "</color>";
        }
        var ggOnDeath = viewingLobbySettings ? MultiplayerSession.GGOnDeath : plugin.lobbySettings.GGOnDeath;
        ggResetButton.interactable = !viewingLobbySettings && ggOnDeath != GunGameDeathMode.Reset;
        ggRollbackButton.interactable = !viewingLobbySettings && ggOnDeath != GunGameDeathMode.Rollback;
        ggNoneButton.interactable = !viewingLobbySettings && ggOnDeath != GunGameDeathMode.None;
        SetModeButton(ggResetButton, ggOnDeath == GunGameDeathMode.Reset);
        SetModeButton(ggRollbackButton, ggOnDeath == GunGameDeathMode.Rollback);
        SetModeButton(ggNoneButton, ggOnDeath == GunGameDeathMode.None);
        SetToggle(grabToggle, viewingLobbySettings ? MultiplayerSession.CanGrabPlayers : plugin.lobbySettings.CanGrab);
        SetToggle(downToggle, viewingLobbySettings ? MultiplayerSession.GrabOnlyUnconscious : plugin.lobbySettings.GrabOnlyUnconscious);
        SetToggle(respawnToggle, viewingLobbySettings ? MultiplayerSession.AllowRespawn : plugin.lobbySettings.AllowRespawn);
        SetToggle(autoRestartToggle, viewingLobbySettings ? MultiplayerSession.AutoRestart : plugin.lobbySettings.AutoRestart);
        SetToggle(respawnAtStartToggle, viewingLobbySettings ? MultiplayerSession.RespawnAtStart : plugin.lobbySettings.RespawnAtStart);
        SetToggle(playerCollisionsToggle, viewingLobbySettings ? MultiplayerSession.PlayerCollisions : plugin.lobbySettings.PlayerCollisions);
        SetToggle(cheatsToggle, viewingLobbySettings ? MultiplayerSession.CheatsEnabled : plugin.lobbySettings.Cheats);
        SetToggle(allowSwapToggle, viewingLobbySettings ? MultiplayerSession.AllowSwap : plugin.lobbySettings.AllowSwap);
        SetToggle(allowScaleChangingToggle, viewingLobbySettings ? MultiplayerSession.AllowScaleChanging : plugin.lobbySettings.AllowScaleChanging);
        SetToggle(allowObserverToggle, viewingLobbySettings ? MultiplayerSession.AllowObserver : plugin.lobbySettings.AllowObserver);
        SetToggle(teamsToggle, viewingLobbySettings ? MultiplayerSession.TeamsEnabled : plugin.lobbySettings.Teams);
        var teamValue = viewingLobbySettings ? MultiplayerSession.TeamsCfg : plugin.lobbySettings.TeamsCfg;
        if (displayedTeams != teamValue)
        {
            displayedTeams = teamValue;
            teamsEditorButton.GetComponentInChildren<TMP_Text>(true).text = "EDIT <color=#59616D>//</color> <color=#F29A64>" + LobbyTeamRules.Parse(teamValue).Count + "</color>";
        }
        if (teamsEditor != null && teamsEditor.activeSelf && (viewingLobbySettings || Input.GetKeyDown(KeyCode.Escape))) teamsEditor.SetActive(false);
        SetInput(initialScaleInput, viewingLobbySettings ? MultiplayerSession.InitialScale.ToString("0.##") : plugin.lobbySettings.InitialScale);
        UpdateWeaponSlots(startingWeaponButtons, viewingLobbySettings ? MultiplayerSession.StartingWeapon : plugin.lobbySettings.StartingWeapon, ref displayedStartingWeapons);
        UpdateWeaponSlots(respawnWeaponButtons, viewingLobbySettings ? MultiplayerSession.RespawnWeapon : plugin.lobbySettings.RespawnWeapon, ref displayedRespawnWeapons);
        if (viewingLobbySettings && weaponPicker != null) weaponPicker.SetActive(false);
        if (viewingLobbySettings && sequenceEditor != null) sequenceEditor.SetActive(false);
        if (sequenceEditor != null && sequenceEditor.activeSelf && !weaponPicker.activeSelf && Input.GetKeyDown(KeyCode.Escape)) sequenceEditor.SetActive(false);
        if (weaponPicker != null && weaponPicker.activeSelf && Input.GetKeyDown(KeyCode.Escape)) weaponPicker.SetActive(false);
        SetAmmoInputs(startingAmmoInputs, viewingLobbySettings ? MultiplayerSession.StartingAmmo : plugin.lobbySettings.StartingAmmo);
        SetAmmoInputs(respawnAmmoInputs, viewingLobbySettings ? MultiplayerSession.RespawnAmmo : plugin.lobbySettings.RespawnAmmo);
        SetLobbySettingsInteractable(!viewingLobbySettings, viewingLobbySettings ? false : plugin.lobbySettings.AllowRespawn);
        RefreshSettingsVisibility();
        SetModeButton(p2pButton, plugin.lobbySettings.ConnectionMode == ConnectionMode.P2P);
        SetModeButton(relayButton, plugin.lobbySettings.ConnectionMode == ConnectionMode.Relay);
        SetModeButton(autoButton, plugin.lobbySettings.ConnectionMode == ConnectionMode.Auto);
        statusLabel.SetValue(plugin.status);
        var actionColors = lobbyActionColors;
        if (pendingSettings && MultiplayerSession.IsHosting)
        {
            var pulse = 0.2f + 0.3f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f));
            actionColors.normalColor = Color.Lerp(actionColors.normalColor, new Color(1f, 0.42f, 0.12f), pulse);
            actionColors.highlightedColor = Color.Lerp(actionColors.highlightedColor, new Color(1f, 0.52f, 0.2f), pulse);
            actionColors.selectedColor = actionColors.highlightedColor;
        }
        lobbyActionButton.colors = actionColors;
        updateIndicator.SetActive(plugin.updateStatus.StartsWith("UPDATE AVAILABLE:", StringComparison.Ordinal));
        updateIndicator.GetComponent<CanvasGroup>().alpha = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));
        if (displayedCustomLevel != plugin.customLevelJson)
        {
            displayedCustomLevel = plugin.customLevelJson;
            customLevelText.text = CustomLevelStatus(displayedCustomLevel);
        }
        if (lobbyGroupTitle != null) lobbyGroupTitle.text = viewingLobbySettings ? "CURRENT LOBBY" : "NEW LOBBY";
        if (lobbyActionText != null) lobbyActionText.text = MultiplayerSession.IsHosting ? "APPLY SETTINGS" : "CREATE LOBBY";
        SetLobbyActionButtons(viewingLobbySettings);
        if (lobbyActionButton != null) lobbyActionButton.interactable = !viewingLobbySettings;
        if (presetsButton != null) presetsButton.interactable = !viewingLobbySettings;
        if (resetButton != null) resetButton.interactable = !viewingLobbySettings;
        if (viewingLobbySettings && resetSettingsDialog != null) resetSettingsDialog.SetActive(false);
        if (viewingLobbySettings && presetsPanel != null) presetsPanel.SetActive(false);
        if (p2pButton != null) p2pButton.interactable = !viewingLobbySettings;
        if (relayButton != null) relayButton.interactable = !viewingLobbySettings;
        if (autoButton != null) autoButton.interactable = !viewingLobbySettings;
        customLevelActionButton.interactable = MultiplayerSession.IsActive && !string.IsNullOrEmpty(plugin.customLevelJson);
        if (serverBrowserOpen) RebuildServerRows();
        RebuildLobbyRows();
        plugin.SaveLobbyPreferences();
    }

    private void SetLobbySettingsInteractable(bool interactable, bool allowRespawn)
    {
        lobbyInput.interactable = interactable;
        maxPlayersInput.interactable = interactable;
        respawnInput.interactable = interactable && allowRespawn;
        numberOfLivesInput.interactable = interactable && allowRespawn;
        uniformHPToggle.interactable = interactable;
        uhpValueInput.interactable = interactable && plugin.lobbySettings.UniformHP;
        healthFactorInput.interactable = interactable && !plugin.lobbySettings.UniformHP;
        regenFactorInput.interactable = interactable;
        blackoutToggle.interactable = interactable;
        restrictLightToggle.interactable = interactable && plugin.lobbySettings.Blackout;
        initialScaleInput.interactable = interactable;
        foreach (var button in startingWeaponButtons) button.interactable = interactable;
        foreach (var button in respawnWeaponButtons) button.interactable = interactable;
        foreach (var input in startingAmmoInputs) input.interactable = interactable;
        foreach (var input in respawnAmmoInputs) input.interactable = interactable;
        teamsEditorButton.interactable = interactable;
        pvpToggle.interactable = interactable;
        gunGameToggle.interactable = interactable;
        propHuntToggle.interactable = interactable;
        propHuntTimeInput.interactable = interactable;
        propHuntHuntersInput.interactable = interactable;
        propHuntShotPenaltyInput.interactable = interactable;
        propHuntShotHealingInput.interactable = interactable;
        propHuntTelekinesisPenaltyInput.interactable = interactable;
        propHuntTelekinesisIntervalInput.interactable = interactable;
        propHuntStunCooldownInput.interactable = interactable;
        propHuntChangeCooldownInput.interactable = interactable;
        ggSequenceButton.interactable = interactable;
        grabToggle.interactable = interactable;
        downToggle.interactable = interactable;
        respawnToggle.interactable = interactable;
        autoRestartToggle.interactable = interactable;
        respawnAtStartToggle.interactable = interactable && allowRespawn;
        playerCollisionsToggle.interactable = interactable;
        cheatsToggle.interactable = interactable;
        allowSwapToggle.interactable = interactable;
        allowScaleChangingToggle.interactable = interactable;
        allowObserverToggle.interactable = interactable;
        teamsToggle.interactable = interactable;
    }

    internal bool SaveEditedLocalLevel(string originalCode, string levelName, string replacementCode)
    {
        return customLevelBrowser != null && customLevelBrowser.SaveLocalLevelFromEditor(originalCode, levelName, replacementCode);
    }

    private void SetLobbyActionButtons(bool joinedAsClient)
    {
        if (lobbyActionButton == null || closeLobbyButton == null) return;
        var closeRect = closeLobbyButton.GetComponent<RectTransform>();
        lobbyActionButton.gameObject.SetActive(!joinedAsClient);
        if (joinedAsClient)
        {
            if (closeRect != null)
            {
                closeRect.anchoredPosition = new Vector2(0f, -350f);
                closeRect.sizeDelta = new Vector2(580f, 46f);
            }
            var label = closeLobbyButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = "LEAVE";
            closeLobbyButton.interactable = true;
            if (showingLeaveLobbyButton == joinedAsClient) return;
            showingLeaveLobbyButton = true;
            closeLobbyButton.onClick.RemoveAllListeners();
            closeLobbyButton.onClick.AddListener(plugin.LeaveLobby);
            return;
        }
        if (closeRect != null)
        {
            closeRect.anchoredPosition = new Vector2(155f, -350f);
            closeRect.sizeDelta = new Vector2(280f, 46f);
        }
        var closeLabel = closeLobbyButton.GetComponentInChildren<TMP_Text>(true);
        if (closeLabel != null) closeLabel.text = "CLOSE LOBBY";
        closeLobbyButton.interactable = MultiplayerSession.IsHosting;
        if (showingLeaveLobbyButton == joinedAsClient) return;
        showingLeaveLobbyButton = false;
        closeLobbyButton.onClick.RemoveAllListeners();
        closeLobbyButton.onClick.AddListener(plugin.CloseHostedLobby);
    }

    private static void SetToggle(Toggle toggle, bool value)
    {
        toggle.SetIsOnWithoutNotify(value);
        var state = toggle.transform.Find("Background/State").GetComponent<TMP_Text>();
        state.text = value ? "ON" : "OFF";
        state.color = value ? new Color(0.1f, 0.06f, 0.03f) : new Color(0.62f, 0.65f, 0.69f);
    }

    private void Create(MainMenuManager menu)
    {
        lobbyWeapons = WeaponPresetProvider.AllWeapons();
        displayedStartingWeapons = displayedRespawnWeapons = displayedSequence = displayedTeams = null;
        settingToggles.Clear();
        settingLabels.Clear();
        settingsQuery = "";
        readSettingBoolean = name => settingToggles[name].isOn;
        displayedCustomLevel = null;
        showingLeaveLobbyButton = false;
        renderedLobbyHash = int.MinValue;
        renderedServerListHash = int.MinValue;
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

        panel = CreatePanel(root.transform, Vector2.zero, new Vector2(1320f, 900f));
        var header = CreateText(panel.transform, "GUNSAW <color=#F29A64>MP</color> <color=#59616D>//</color> <color=#A6AFBC>" + GunsawMultiplayerPlugin.PluginVersion + "</color>", new Vector2(-350f, 417f), new Vector2(570f, 44f), 30, TextAlignmentOptions.Left);
        updateIndicator = new GameObject("Update available", typeof(RectTransform), typeof(CanvasGroup), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        updateIndicator.transform.SetParent(panel.transform, false);
        SetRect((RectTransform)updateIndicator.transform, new Vector2(-635f + header.preferredWidth + 18f, 417f), new Vector2(24f, 24f));
        CreateIcon(updateIndicator.transform, LobbyIcon.Warning, new Color(1f, 0.22f, 0.18f));
        var updateHitArea = updateIndicator.GetComponent<Image>();
        updateHitArea.color = Color.clear;
        var updateButton = updateIndicator.GetComponent<Button>();
        updateButton.targetGraphic = updateHitArea;
        updateButton.transition = Selectable.Transition.None;
        updateButton.onClick.AddListener(() => Application.OpenURL("https://github.com/Pan4ur/Gunsaw-Multiplayer-Mod/releases/latest"));
        AddTooltip(updateIndicator, "Your version is out of date. Click to download the latest release.");
        updateIndicator.SetActive(false);
        CreateText(panel.transform, "Welcome back,", new Vector2(178f, 417f), new Vector2(220f, 40f), 22, TextAlignmentOptions.Right);
        nameInput = CreateInput(panel.transform, new Vector2(441.5f, 417f), new Vector2(279f, 40f), 32, value => plugin.playerName = value);
        nameInput.textComponent.fontSize = 24f;
        nameInput.GetComponent<Image>().color = Color.white;
        nameInput.GetComponent<Outline>().enabled = false;
        nameInput.textComponent.margin = new Vector4(4f, 0f, 4f, 0f);
        var nameLine = new GameObject("Name underline", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        nameLine.transform.SetParent(nameInput.transform, false);
        SetRect((RectTransform)nameLine.transform, new Vector2(0f, -19f), new Vector2(279f, 1f));
        nameLine.GetComponent<Image>().color = new Color(0.32f, 0.35f, 0.4f, 0.65f);
        nameLine.GetComponent<Image>().raycastTarget = false;
        var nameColors = nameInput.colors;
        nameColors.normalColor = new Color(0.105f, 0.095f, 0.125f, 0.2f);
        nameColors.highlightedColor = new Color(0.175f, 0.16f, 0.2f, 0.7f);
        nameColors.selectedColor = nameColors.highlightedColor;
        nameColors.pressedColor = nameColors.highlightedColor;
        nameColors.fadeDuration = 0.2f;
        nameInput.colors = nameColors;
        var close = CreateIconButton(panel.transform, LobbyIcon.Close, null, new Vector2(615f, 417f), new Vector2(40f, 36f));
        close.onClick.AddListener(() => plugin.visible = false);

        var lobbyGroup = CreateGroup(panel.transform, "NEW LOBBY", new Vector2(310f, -10f), new Vector2(650f, 790f));
        lobbyGroupTitle = lobbyGroup.GetComponentInChildren<TMP_Text>(true);
        CreateText(lobbyGroup.transform, "LOBBY NAME", new Vector2(-235f, 320f), new Vector2(120f, 32f), 14);
        lobbyInput = CreateInput(lobbyGroup.transform, new Vector2(62.5f, 320f), new Vector2(475f, 42f), 48, value => plugin.lobbyName = value);

        var search = CreateInput(lobbyGroup.transform, new Vector2(-25f, 265f), new Vector2(550f, 38f), 80, FilterSettings);
        var placeholder = CreateText(search.textViewport, "Search lobby settings...", Vector2.zero, search.textComponent.rectTransform.sizeDelta, 15);
        placeholder.color = new Color(0.48f, 0.5f, 0.52f);
        placeholder.margin = search.textComponent.margin;
        search.placeholder = placeholder;
        var clearSearch = CreateIconButton(lobbyGroup.transform, LobbyIcon.Close, null, new Vector2(281f, 265f), new Vector2(38f, 38f));
        clearSearch.onClick.AddListener(() => search.text = "");
        var settings = CreateSettingsScroll(lobbyGroup.transform, new Vector2(0f, -35f), new Vector2(600f, 440f));
        settingsRows = settings;
        settingsEmpty = CreateText(lobbyGroup.transform, "No matching settings", new Vector2(0f, 120f), new Vector2(540f, 36f), 16, TextAlignmentOptions.Center);
        settingsEmpty.gameObject.SetActive(false);
        var presetsRow = CreatePanel(lobbyGroup.transform, new Vector2(0f, 210f), new Vector2(600f, 42f)).transform;
        resetButton = CreateIconButton(presetsRow, LobbyIcon.Delete, "Reset lobby settings", new Vector2(276f, 0f), new Vector2(36f, 30f));
        resetButton.transform.Find("Icon").GetComponent<Image>().color = new Color(0.95f, 0.32f, 0.25f);
        resetButton.onClick.AddListener(OpenResetSettingsDialog);
        presetsButton = CreateButton(presetsRow, "PRESETS", new Vector2(-24f, 0f), new Vector2(540f, 30f));
        presetsButton.onClick.AddListener(OpenPresets);
        foreach (var button in new[] { presetsButton, resetButton })
        {
            var colors = button.colors;
            colors.normalColor = new Color(0.125f, 0.116f, 0.145f);
            colors.highlightedColor = new Color(0.235f, 0.22f, 0.265f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            label.fontSize = 13f;
            label.color = new Color(0.65f, 0.68f, 0.72f);
        }
        pvpToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.Pvp)), "PVP", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.Pvp = value);
        AddTooltip(pvpToggle.gameObject, "PVP: Enables damage between players. Some functions, such as teammate position markers or the /tp command, will not work in this mode.");
        grabToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.CanGrab)), "CAN GRAB", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.CanGrab = value);
        AddTooltip(grabToggle.gameObject, "CAN GRAB: Allows players to grab other players with the gravity laser.");
        downToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.GrabOnlyUnconscious)), "ONLY UNCONSCIOUS", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.GrabOnlyUnconscious = value);
        AddTooltip(downToggle.gameObject, "ONLY UNCONSCIOUS: Applies to CAN GRAB. When enabled, other players can only be grabbed while they are in a ragdoll state.");
        var maxPlayersRow = CreateSettingsRow(settings, nameof(LobbySettings.MaxPlayers));
        CreateText(maxPlayersRow, "MAX PLAYERS", new Vector2(-150f, 0f), new Vector2(220f, 32f), 14);
        maxPlayersInput = CreateInput(maxPlayersRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 2, value => plugin.lobbySettings.MaxPlayers = value, TMP_InputField.CharacterValidation.Integer);
        AddNumericRange(maxPlayersInput, 2f, 64f, false);
        AddTooltip(maxPlayersRow.gameObject, "MAX PLAYERS: The maximum number of players allowed in your lobby.");
        respawnToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.AllowRespawn)), "ALLOW RESPAWN", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.AllowRespawn = value);
        AddTooltip(respawnToggle.gameObject, "ALLOW RESPAWN: When enabled, players can respawn after death. When disabled, they can only spectate living players until the game ends.");
        autoRestartToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.AutoRestart)), "AUTO RESTART", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.AutoRestart = value);
        AddTooltip(autoRestartToggle.gameObject, "AUTO RESTART: The host restarts the level after everyone dies, or after only one team remains alive.");
        var delayRow = CreateSettingsRow(settings, nameof(LobbySettings.RespawnTime));
        CreateText(delayRow, "RESPAWN DELAY", new Vector2(-115f, 0f), new Vector2(290f, 32f), 14);
        respawnInput = CreateInput(delayRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 4, value => plugin.lobbySettings.RespawnTime = value, TMP_InputField.CharacterValidation.Integer);
        AddNumericRange(respawnInput, 0f, 3600f, false);
        AddTooltip(delayRow.gameObject, "RESPAWN DELAY: The time in seconds before a player respawns when ALLOW RESPAWN is enabled.");
        var livesRow = CreateSettingsRow(settings, nameof(LobbySettings.NumberOfLives));
        CreateText(livesRow, "NUMBER OF LIVES", new Vector2(-115f, 0f), new Vector2(290f, 32f), 14);
        numberOfLivesInput = CreateInput(livesRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 5, value => plugin.lobbySettings.NumberOfLives = value, TMP_InputField.CharacterValidation.Integer);
        AddNumericRange(numberOfLivesInput, 0f, ushort.MaxValue, false);
        AddTooltip(livesRow.gameObject, "NUMBER OF LIVES: 0 gives unlimited respawns. With 1 or more, each death uses a life; after the last life, the player spectates until the level reloads.");
        uniformHPToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.UniformHP)), "UNIFORM HP", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.UniformHP = value);
        AddTooltip(uniformHPToggle.gameObject, "UNIFORM HP: Gives every character species the max health set in UHP VALUE. Overrides HEALTH FACTOR.");
        var uhpValueRow = CreateSettingsRow(settings, nameof(LobbySettings.UHPValue));
        CreateText(uhpValueRow, "UHP VALUE", new Vector2(-115f, 0f), new Vector2(290f, 32f), 14);
        uhpValueInput = CreateInput(uhpValueRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 8, value => plugin.lobbySettings.UHPValue = value, TMP_InputField.CharacterValidation.Decimal);
        AddNumericRange(uhpValueInput, LobbyHealthRule.ClampUniformHPValue(float.MinValue), LobbyHealthRule.ClampUniformHPValue(float.MaxValue), false);
        AddTooltip(uhpValueRow.gameObject, "UHP VALUE: Max health for every player when UNIFORM HP is enabled. Allowed range: 1 to 10000.");
        var healthFactorRow = CreateSettingsRow(settings, nameof(LobbySettings.HealthFactor));
        CreateText(healthFactorRow, "HEALTH FACTOR", new Vector2(-115f, 0f), new Vector2(290f, 32f), 14);
        healthFactorInput = CreateInput(healthFactorRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 4, value => plugin.lobbySettings.HealthFactor = value, TMP_InputField.CharacterValidation.Decimal);
        AddNumericRange(healthFactorInput, LobbyHealthRule.Clamp(float.MinValue), LobbyHealthRule.Clamp(float.MaxValue), true);
        AddTooltip(healthFactorRow.gameObject, "HEALTH FACTOR: Multiplies every player's maximum health. Allowed range: 0.01 to 10.");
        var regenFactorRow = CreateSettingsRow(settings, nameof(LobbySettings.RegenFactor));
        CreateText(regenFactorRow, "REGEN FACTOR", new Vector2(-115f, 0f), new Vector2(290f, 32f), 14);
        regenFactorInput = CreateInput(regenFactorRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 4, value => plugin.lobbySettings.RegenFactor = value, TMP_InputField.CharacterValidation.Decimal);
        AddNumericRange(regenFactorInput, LobbyRegenRule.Clamp(float.MinValue), LobbyRegenRule.Clamp(float.MaxValue), true);
        AddTooltip(regenFactorRow.gameObject, "REGEN FACTOR: Multiplies every player's health regeneration speed. 0 disables regeneration. Allowed range: 0 to 10.");
        blackoutToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.Blackout)), "BLACKOUT", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.Blackout = value);
        AddTooltip(blackoutToggle.gameObject, "BLACKOUT: Turns off level lighting and gives every player a headlamp.");
        restrictLightToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.RestrictLight)), "RESTRICT LIGHT", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.RestrictLight = value);
        AddTooltip(restrictLightToggle.gameObject, "RESTRICT LIGHT: Headlamp light is blocked by walls and props.");
        respawnAtStartToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.RespawnAtStart)), "RESPAWN AT START", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.RespawnAtStart = value);
        AddTooltip(respawnAtStartToggle.gameObject, "RESPAWN AT START: Applies to ALLOW RESPAWN. When enabled, players spawn at a player spawn point placed by the map author. If there are several, one is chosen at random. Some custom maps may accidentally contain too many spawn points and become impossible to complete without removing the extra points. When disabled, players respawn at the position of their corpse.");
        playerCollisionsToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.PlayerCollisions)), "PLAYER COLLISIONS", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.PlayerCollisions = value);
        AddTooltip(playerCollisionsToggle.gameObject, "PLAYER COLLISIONS: When disabled, players can pass through each other without blocking one another, which can help on parkour maps.");
        cheatsToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.Cheats)), "CHEATS", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.Cheats = value);
        AddTooltip(cheatsToggle.gameObject, "CHEATS: Allows the cheats opened with SPACE + END.");
        allowSwapToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.AllowSwap)), "ALLOW SWAP", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.AllowSwap = value);
        AddTooltip(allowSwapToggle.gameObject, "ALLOW SWAP: Allows players to use /swap to choose a different character for their next respawn.");
        allowScaleChangingToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.AllowScaleChanging)), "ALLOW SCALE CHANGING", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.AllowScaleChanging = value);
        AddTooltip(allowScaleChangingToggle.gameObject, "ALLOW SCALE CHANGING: Allows players to use /scale between 0.25 and 2.0.");
        allowObserverToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.AllowObserver)), "ALLOW OBSERVER", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.AllowObserver = value);
        AddTooltip(allowObserverToggle.gameObject, "ALLOW OBSERVER: Allows the OBSERVER keyboard easter egg.");
        teamsToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.Teams)), "TEAMS", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.Teams = value);
        var teamsCfgRow = CreateSettingsRow(settings, nameof(LobbySettings.TeamsCfg));
        CreateText(teamsCfgRow, "TEAMS CFG", new Vector2(-185f, 0f), new Vector2(140f, 32f), 14);
        teamsEditorButton = CreateButton(teamsCfgRow, "EDIT", new Vector2(80f, 0f), new Vector2(330f, 36f));
        teamsEditorButton.onClick.AddListener(OpenTeamsEditor);
        AddTooltip(teamsCfgRow.gameObject, "Team spawn IDs follow this order: 0 = first team, 1 = second team, etc.");
        var initialScaleRow = CreateSettingsRow(settings, nameof(LobbySettings.InitialScale));
        CreateText(initialScaleRow, "INITIAL SCALE", new Vector2(-115f, 0f), new Vector2(290f, 32f), 14);
        initialScaleInput = CreateInput(initialScaleRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 4, value => plugin.lobbySettings.InitialScale = value, TMP_InputField.CharacterValidation.Decimal);
        AddNumericRange(initialScaleInput, AvatarScaleHandler.Clamp(float.MinValue), AvatarScaleHandler.Clamp(float.MaxValue), true);
        AddTooltip(initialScaleRow.gameObject, "INITIAL SCALE: The character scale assigned when a player joins or respawns. Allowed range: 0.25 to 2.0.");
        var startingWeaponRow = CreateSettingsRow(settings, nameof(LobbySettings.StartingWeapon));
        CreateText(startingWeaponRow, "STARTING WEAPON", new Vector2(-155f, 0f), new Vector2(210f, 32f), 14);
        startingWeaponButtons = CreateWeaponSlots(startingWeaponRow, false);
        AddTooltip(startingWeaponRow.gameObject, "STARTING WEAPON: Click a slot to choose the weapon assigned when a player joins. Default keeps the normal loadout, None clears the slot, Random selects a random weapon.");
        var respawnWeaponRow = CreateSettingsRow(settings, nameof(LobbySettings.RespawnWeapon));
        CreateText(respawnWeaponRow, "RESPAWN WEAPON", new Vector2(-155f, 0f), new Vector2(210f, 32f), 14);
        respawnWeaponButtons = CreateWeaponSlots(respawnWeaponRow, true);
        AddTooltip(respawnWeaponRow.gameObject, "RESPAWN WEAPON: Click a slot to choose the weapon assigned after respawn. Default keeps the normal loadout, None clears the slot, Random selects a random weapon.");
        var startingAmmoRow = CreateSettingsRow(settings, nameof(LobbySettings.StartingAmmo));
        startingAmmoInputs = CreateAmmoInputs(startingAmmoRow, false);
        AddTooltip(startingAmmoRow.gameObject, "STARTING AMMO: Reserve ammo given to each player when they join, for each of the four ammo types.");
        var respawnAmmoRow = CreateSettingsRow(settings, nameof(LobbySettings.RespawnAmmo));
        respawnAmmoInputs = CreateAmmoInputs(respawnAmmoRow, true);
        AddTooltip(respawnAmmoRow.gameObject, "RESPAWN AMMO: Reserve ammo given to each player after respawn, for each of the four ammo types.");
        propHuntToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.PropHunt)), "PROP HUNT", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.PropHunt = value);
        AddTooltip(propHuntToggle.gameObject, "PROP HUNT: Hunters search for players hiding as props. (LMB on hunter to stun)");
        var propHuntTimeRow = CreateSettingsRow(settings, nameof(LobbySettings.PropHuntTime));
        CreateText(propHuntTimeRow, "PROP HUNT TIME", new Vector2(-115f, 0f), new Vector2(290f, 32f), 14);
        propHuntTimeInput = CreateInput(propHuntTimeRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 4, value => plugin.lobbySettings.PropHuntTime = value, TMP_InputField.CharacterValidation.Integer);
        AddNumericRange(propHuntTimeInput, 1f, 3600f, false);
        var huntersRow = CreateSettingsRow(settings, nameof(LobbySettings.PropHuntHunters));
        CreateText(huntersRow, "PROP HUNT HUNTERS", new Vector2(-115f, 0f), new Vector2(290f, 32f), 14);
        propHuntHuntersInput = CreateInput(huntersRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 2, value => plugin.lobbySettings.PropHuntHunters = value, TMP_InputField.CharacterValidation.Integer);
        AddNumericRange(propHuntHuntersInput, 1f, 63f, false);
        AddTooltip(huntersRow.gameObject, "Number of hunters selected each round.");
        var propHuntShotPenaltyRow = CreateSettingsRow(settings, nameof(LobbySettings.PropHuntShotPenalty));
        CreateText(propHuntShotPenaltyRow, "SHOT PENALTY", new Vector2(-135f, 0f), new Vector2(250f, 32f), 12);
        propHuntShotPenaltyInput = CreateInput(propHuntShotPenaltyRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 7, value => plugin.lobbySettings.PropHuntShotPenalty = value, TMP_InputField.CharacterValidation.Decimal);
        AddNumericRange(propHuntShotPenaltyInput, 0f, 100f, true);
        AddTooltip(propHuntShotPenaltyRow.gameObject, "Maximum health percentage lost when a hunter misses a shot (also applies to missed kicks).");
        var propHuntShotHealingRow = CreateSettingsRow(settings, nameof(LobbySettings.PropHuntShotHealing));
        CreateText(propHuntShotHealingRow, "SHOT HEALING", new Vector2(-135f, 0f), new Vector2(250f, 32f), 12);
        propHuntShotHealingInput = CreateInput(propHuntShotHealingRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 7, value => plugin.lobbySettings.PropHuntShotHealing = value, TMP_InputField.CharacterValidation.Decimal);
        AddNumericRange(propHuntShotHealingInput, 0f, 100f, true);
        AddTooltip(propHuntShotHealingRow.gameObject, "Maximum health percentage restored when a hunter shoots a prop.");
        var propHuntTelekinesisPenaltyRow = CreateSettingsRow(settings, nameof(LobbySettings.PropHuntTelekinesisPenalty));
        CreateText(propHuntTelekinesisPenaltyRow, "TELEKINESIS PENALTY", new Vector2(-135f, 0f), new Vector2(250f, 32f), 12);
        propHuntTelekinesisPenaltyInput = CreateInput(propHuntTelekinesisPenaltyRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 7, value => plugin.lobbySettings.PropHuntTelekinesisPenalty = value, TMP_InputField.CharacterValidation.Decimal);
        AddNumericRange(propHuntTelekinesisPenaltyInput, 0f, 100f, true);
        AddTooltip(propHuntTelekinesisPenaltyRow.gameObject, "Maximum health percentage lost per interval while holding an ordinary object with telekinesis.");
        var propHuntTelekinesisIntervalRow = CreateSettingsRow(settings, nameof(LobbySettings.PropHuntTelekinesisInterval));
        CreateText(propHuntTelekinesisIntervalRow, "TELEKINESIS INTERVAL", new Vector2(-135f, 0f), new Vector2(250f, 32f), 12);
        propHuntTelekinesisIntervalInput = CreateInput(propHuntTelekinesisIntervalRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 7, value => plugin.lobbySettings.PropHuntTelekinesisInterval = value, TMP_InputField.CharacterValidation.Decimal);
        AddNumericRange(propHuntTelekinesisIntervalInput, 0.01f, 3600f, true);
        AddTooltip(propHuntTelekinesisIntervalRow.gameObject, "Seconds between telekinesis penalties. Allowed range: 0.01 to 3600.");
        var propHuntStunCooldownRow = CreateSettingsRow(settings, nameof(LobbySettings.PropHuntStunCooldown));
        CreateText(propHuntStunCooldownRow, "STUN COOLDOWN", new Vector2(-135f, 0f), new Vector2(250f, 32f), 12);
        propHuntStunCooldownInput = CreateInput(propHuntStunCooldownRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 7, value => plugin.lobbySettings.PropHuntStunCooldown = value, TMP_InputField.CharacterValidation.Decimal);
        AddNumericRange(propHuntStunCooldownInput, 0f, 3600f, true);
        AddTooltip(propHuntStunCooldownRow.gameObject, "Seconds before a prop can stun again. Zero disables the cooldown.");
        var propHuntChangeCooldownRow = CreateSettingsRow(settings, nameof(LobbySettings.PropHuntChangeCooldown));
        CreateText(propHuntChangeCooldownRow, "PROP CHANGE COOLDOWN", new Vector2(-135f, 0f), new Vector2(250f, 32f), 12);
        propHuntChangeCooldownInput = CreateInput(propHuntChangeCooldownRow, new Vector2(195f, 0f), new Vector2(120f, 36f), 7, value => plugin.lobbySettings.PropHuntChangeCooldown = value, TMP_InputField.CharacterValidation.Decimal);
        AddNumericRange(propHuntChangeCooldownInput, 0f, 3600f, true);
        AddTooltip(propHuntChangeCooldownRow.gameObject, "Seconds before a prop can change disguise again. Zero disables the cooldown.");
        gunGameToggle = CreateToggle(CreateSettingsRow(settings, nameof(LobbySettings.GunGame)), "GUNGAME", Vector2.zero, new Vector2(520f, 40f), value => plugin.lobbySettings.GunGame = value);
        AddTooltip(gunGameToggle.gameObject, "GUNGAME: Each kill advances to the next weapon in GG SEQUENCE.");
        var ggSequenceRow = CreateSettingsRow(settings, nameof(LobbySettings.GGSequence));
        CreateText(ggSequenceRow, "GG SEQUENCE", new Vector2(-155f, 0f), new Vector2(210f, 32f), 14);
        ggSequenceButton = CreateButton(ggSequenceRow, "EDIT", new Vector2(115f, 0f), new Vector2(280f, 36f));
        ggSequenceButton.onClick.AddListener(OpenSequenceEditor);
        AddTooltip(ggSequenceRow.gameObject, "GG SEQUENCE: Edit the ordered weapon chain. Add, replace, reorder or remove any stage.");
        var ggDeathRow = CreateSettingsRow(settings, nameof(LobbySettings.GGOnDeath));
        CreateText(ggDeathRow, "ON DEATH", new Vector2(-195f, 0f), new Vector2(130f, 32f), 14);
        ggResetButton = CreateButton(ggDeathRow, "RESET", new Vector2(-50f, 0f), new Vector2(100f, 36f));
        ggRollbackButton = CreateButton(ggDeathRow, "ROLLBACK", new Vector2(75f, 0f), new Vector2(120f, 36f));
        ggNoneButton = CreateButton(ggDeathRow, "NONE", new Vector2(200f, 0f), new Vector2(100f, 36f));
        ggResetButton.onClick.AddListener(() => { plugin.lobbySettings.GGOnDeath = GunGameDeathMode.Reset; RefreshPendingSettings(); });
        ggRollbackButton.onClick.AddListener(() => { plugin.lobbySettings.GGOnDeath = GunGameDeathMode.Rollback; RefreshPendingSettings(); });
        ggNoneButton.onClick.AddListener(() => { plugin.lobbySettings.GGOnDeath = GunGameDeathMode.None; RefreshPendingSettings(); });
        AddTooltip(ggDeathRow.gameObject, "ON DEATH: Reset starts the sequence over, Rollback moves back one weapon, None keeps the current weapon.");

        foreach (Transform row in settingsRows)
            settingLabels.Add(row.name, string.Join(" ", row.GetComponentsInChildren<TMP_Text>(true)
                .Where(text => text.name != "State" && text.GetComponentInParent<TMP_InputField>() == null).Select(text => text.text)));

        CreateText(lobbyGroup.transform, "CONNECTION", new Vector2(-235f, -292f), new Vector2(125f, 32f), 14);
        p2pButton = CreateButton(lobbyGroup.transform, "P2P", new Vector2(-70f, -292f), new Vector2(130f, 36f));
        var p2p = p2pButton;
        AddTooltip(p2p.gameObject, "P2P: Experimental direct connection to the host computer. It can reduce ping when you are far from the lobby server, but other players in the lobby can expose your IP address. Do not use it yet unless you are playing with two people and are sure it works correctly.", Color.red);
        p2p.onClick.AddListener(() => { plugin.lobbySettings.ConnectionMode = ConnectionMode.P2P; RefreshPendingSettings(); });
        relayButton = CreateButton(lobbyGroup.transform, "RELAY", new Vector2(70f, -292f), new Vector2(130f, 36f));
        var relay = relayButton;
        AddTooltip(relay.gameObject, "RELAY: Standard connection mode. It uses the server as a proxy and is the recommended mode.");
        relay.onClick.AddListener(() => { plugin.lobbySettings.ConnectionMode = ConnectionMode.Relay; RefreshPendingSettings(); });
        autoButton = CreateButton(lobbyGroup.transform, "AUTO", new Vector2(210f, -292f), new Vector2(130f, 36f));
        var auto = autoButton;
        AddTooltip(auto.gameObject, "AUTO: First tries P2P, then falls back to RELAY if it fails. It supports P2P + RELAY, where players who can connect through P2P use it while others use RELAY. P2P is not reliable yet. Leave RELAY selected if you are not sure.", Color.red);
        auto.onClick.AddListener(() => { plugin.lobbySettings.ConnectionMode = ConnectionMode.Auto; RefreshPendingSettings(); });

        lobbyActionButton = CreateButton(lobbyGroup.transform, "CREATE LOBBY", new Vector2(-155f, -350f), new Vector2(280f, 46f));
        lobbyActionColors = lobbyActionButton.colors;
        var create = lobbyActionButton;
        lobbyActionText = create.GetComponentInChildren<TMP_Text>(true);
        create.onClick.AddListener(() =>
        {
            if (MultiplayerSession.IsHosting) plugin.UpdateHostedLobby();
            else plugin.CreateLobby();
        });
        closeLobbyButton = CreateButton(lobbyGroup.transform, "CLOSE LOBBY", new Vector2(155f, -350f), new Vector2(280f, 46f));
        closeLobbyButton.onClick.AddListener(plugin.CloseHostedLobby);


        var customGroup = CreateGroup(panel.transform, "CUSTOM LEVEL", new Vector2(-335f, 340f), new Vector2(600f, 90f));
        var paste = CreateIconButton(customGroup.transform, LobbyIcon.Paste, "Paste level from clipboard", new Vector2(156f, -12f), new Vector2(44f, 42f));
        paste.onClick.AddListener(() => plugin.PasteCustomLevel());
        customLevelActionButton = CreateIconButton(customGroup.transform, LobbyIcon.Play, "Start level / suggest level to host", new Vector2(208f, -12f), new Vector2(44f, 42f));
        customLevelActionButton.onClick.AddListener(() =>
        {
            if (MultiplayerSession.IsHosting && !string.IsNullOrEmpty(plugin.customLevelJson))
                plugin.StartCustomLevel();
            else if (MultiplayerSession.IsConnected)
                plugin.SuggestCustomLevel();
        });
        var openBrowser = CreateIconButton(customGroup.transform, LobbyIcon.Search, null, new Vector2(260f, -12f), new Vector2(44f, 42f));
        openBrowser.onClick.AddListener(() => customLevelBrowser?.Toggle());
        customLevelText = CreateText(customGroup.transform, "", new Vector2(-75f, -12f), new Vector2(400f, 28f), 13);
        customLevelBrowser = new CustomLevelBrowserUi(plugin, root.transform, template, templateButton);
        CreateCustomLevelSuggestionDialog();

        var connectionGroup = CreateGroup(panel.transform, "RELAY SERVER", new Vector2(-335f, 245f), new Vector2(600f, 80f));
        serverInput = CreateInput(connectionGroup.transform, new Vector2(-54f, -12f), new Vector2(444f, 34f), 255, value => plugin.lobbyServerAddress = value);
        var connect = CreateIconButton(connectionGroup.transform, LobbyIcon.Connect, "Apply relay address", new Vector2(208f, -12f), new Vector2(44f, 34f));
        connect.onClick.AddListener(() => { if (!MultiplayerSession.IsHosting) plugin.ConnectLobbyServer(); });
        var servers = CreateIconButton(connectionGroup.transform, LobbyIcon.List, "Browse relay servers", new Vector2(260f, -12f), new Vector2(44f, 34f));
        servers.onClick.AddListener(() => SetServerBrowserOpen(!serverBrowserOpen));
        CreateServerBrowser();

        var publicGroup = CreateGroup(panel.transform, "PUBLIC LOBBIES", new Vector2(-335f, -107.5f), new Vector2(600f, 595f));
        publicLobbiesTitle = publicGroup.GetComponentInChildren<TMP_Text>(true);
        statusText = CreateText(publicGroup.transform, "", new Vector2(-20f, 239f), new Vector2(510f, 24f), 13);
        statusLabel = statusText.gameObject.AddComponent<CyclingLabel>();
        refreshLobbiesButton = CreateIconButton(publicGroup.transform, LobbyIcon.Refresh, null, new Vector2(260f, 261.25f), new Vector2(34f, 34f));
        refreshLobbiesButton.onClick.AddListener(plugin.RefreshLobbies);
        lobbyRows = CreateScrollArea(publicGroup.transform, new Vector2(0f, -30f), new Vector2(570f, 510f));
        lobbyRows.parent.GetComponent<ScrollRect>().scrollSensitivity = 24f;

        tooltipPanel = CreatePanel(panel.transform, new Vector2(0f, -426f), new Vector2(1250f, 36f));
        tooltipPanel.GetComponent<Image>().color = new Color(0.075f, 0.071f, 0.086f, 0.96f);
        tooltipPanel.GetComponent<Image>().raycastTarget = false;
        var tooltipCanvas = tooltipPanel.AddComponent<Canvas>();
        tooltipCanvas.overrideSorting = true;
        tooltipCanvas.sortingOrder = 550;
        tooltipText = CreateText(tooltipPanel.transform, "", Vector2.zero, new Vector2(1210f, 28f), 12, TextAlignmentOptions.Center);
        tooltipText.enableWordWrapping = true;
        tooltipText.raycastTarget = false;
        tooltipPanel.SetActive(false);
        CreatePresetManager();
        CreateResetSettingsDialog();
        CreateCrossServerDialog();
        CreateWeaponPicker();
        CreateSequenceEditor();
        CreateTeamsEditor();
    }

    private TMP_InputField[] CreateAmmoInputs(Transform parent, bool respawn)
    {
        CreateText(parent, respawn ? "RESPAWN AMMO" : "STARTING AMMO", new Vector2(-178f, 0f), new Vector2(164f, 32f), 14);
        var player = PlayerScript.player;
        if (player == null)
        {
            var prefab = Resources.Load<GameObject>("Base/Player");
            if (prefab != null) player = prefab.GetComponent<PlayerScript>();
        }
        var inputs = new TMP_InputField[4];
        for (var index = 0; index < inputs.Length; index++)
        {
            var ammoIndex = index;
            var x = -78f + index * 88f;
            var icon = new GameObject("Ammo icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            icon.transform.SetParent(parent, false);
            SetRect((RectTransform)icon.transform, new Vector2(x, 0f), new Vector2(20f, 24f));
            var image = icon.GetComponent<Image>();
            var ammoType = LobbyAmmoRules.GetAmmoType(index);
            image.sprite = player != null && player.ammoImages != null && ammoType >= 0 && ammoType < player.ammoImages.Length ? player.ammoImages[ammoType] : null;
            image.enabled = image.sprite != null;
            image.preserveAspect = true;
            image.raycastTarget = false;
            inputs[index] = CreateInput(parent, new Vector2(x + 44f, 0f), new Vector2(56f, 36f), 6, value =>
            {
                if (respawn) plugin.lobbySettings.RespawnAmmo = LobbyAmmoRules.WithAmount(plugin.lobbySettings.RespawnAmmo, ammoIndex, value);
                else plugin.lobbySettings.StartingAmmo = LobbyAmmoRules.WithAmount(plugin.lobbySettings.StartingAmmo, ammoIndex, value);
            }, TMP_InputField.CharacterValidation.Integer);
            inputs[index].textComponent.margin = new Vector4(2f, 0f, 2f, 0f);
        }
        return inputs;
    }

    private static void SetAmmoInputs(TMP_InputField[] inputs, string rule)
    {
        var values = (rule ?? string.Empty).Split(';');
        for (var index = 0; index < inputs.Length; index++)
            SetInput(inputs[index], index < values.Length && !string.IsNullOrWhiteSpace(values[index]) ? values[index].Trim() : "0");
    }

    private Button[] CreateWeaponSlots(Transform parent, bool respawn)
    {
        var buttons = new Button[3];
        for (var slot = 0; slot < buttons.Length; slot++)
        {
            var index = slot;
            var button = CreateButton(parent, "", new Vector2(19f + slot * 96f, 0f), new Vector2(88f, 36f));
            button.GetComponentInChildren<TMP_Text>(true).fontSize = 11f;
            button.GetComponentInChildren<TMP_Text>(true).overflowMode = TextOverflowModes.Ellipsis;
            var icon = new GameObject("Weapon icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            icon.transform.SetParent(button.transform, false);
            SetRect((RectTransform)icon.transform, Vector2.zero, new Vector2(64f, 28f));
            icon.GetComponent<Image>().preserveAspect = true;
            icon.GetComponent<Image>().raycastTarget = false;
            button.onClick.AddListener(() => OpenWeaponPicker(respawn, index));
            AddTooltip(button.gameObject, "Slot " + (slot + 1) + ": click to choose a weapon.");
            buttons[slot] = button;
        }
        return buttons;
    }

    private void UpdateWeaponSlots(Button[] buttons, string rule, ref string? displayed)
    {
        if (displayed == rule) return;
        displayed = rule;
        for (var slot = 0; slot < buttons.Length; slot++)
        {
            var value = LobbyWeaponRules.SlotValue(rule, slot);
            var preset = lobbyWeapons.FirstOrDefault(weapon => string.Equals(weapon.name, value, StringComparison.OrdinalIgnoreCase));
            var icon = buttons[slot].transform.Find("Weapon icon").GetComponent<Image>();
            icon.sprite = preset == null ? null : preset.sprite;
            icon.enabled = icon.sprite != null;
            var label = buttons[slot].GetComponentInChildren<TMP_Text>(true);
            label.text = icon.enabled ? "" : value.StartsWith("Random[", StringComparison.OrdinalIgnoreCase) ? "Random" : value;
            buttons[slot].name = "Slot " + (slot + 1) + ": " + value;
        }
    }

    private void CreateWeaponPicker()
    {
        weaponPicker = CreatePanel(root.transform, Vector2.zero, Vector2.zero);
        weaponPicker.name = "Lobby weapon picker";
        var overlay = (RectTransform)weaponPicker.transform;
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        weaponPicker.GetComponent<Image>().color = Color.clear;
        var dialog = CreatePanel(weaponPicker.transform, Vector2.zero, new Vector2(900f, 720f));
        weaponPickerTitle = CreateText(dialog.transform, "SELECT WEAPON", new Vector2(-35f, 315f), new Vector2(760f, 36f), 22);
        var close = CreateIconButton(dialog.transform, LobbyIcon.Close, "Close weapon picker", new Vector2(400f, 315f), new Vector2(36f, 36f));
        close.onClick.AddListener(() => weaponPicker.SetActive(false));
        weaponSearch = CreateInput(dialog.transform, new Vector2(-25f, 267f), new Vector2(770f, 38f), 80, _ => RebuildWeaponPicker());
        var hint = CreateText(weaponSearch.textViewport, "Search weapons...", Vector2.zero, weaponSearch.textComponent.rectTransform.sizeDelta, 15);
        hint.color = new Color(0.48f, 0.5f, 0.52f);
        hint.margin = weaponSearch.textComponent.margin;
        weaponSearch.placeholder = hint;
        var clear = CreateIconButton(dialog.transform, LobbyIcon.Close, "Clear weapon search", new Vector2(391f, 267f), new Vector2(38f, 38f));
        clear.onClick.AddListener(() => weaponSearch.text = "");
        var choices = new[] { "Default", "None", "Random" };
        for (var index = 0; index < choices.Length; index++)
        {
            var value = choices[index];
            var choice = CreateButton(dialog.transform, value.ToUpperInvariant(), new Vector2(-280f + index * 280f, 215f), new Vector2(260f, 38f));
            if (value == "Random")
            {
                randomWeaponButton = choice;
                choice.onClick.AddListener(() => { editingRandomWeapons = !editingRandomWeapons; showSelectedWeapons = false; RefreshWeaponFilter(); });
            }
            else choice.onClick.AddListener(() => ApplyWeaponChoice(value));
        }
        weaponPickerRows = CreateSettingsScroll(dialog.transform, new Vector2(0f, -25f), new Vector2(840f, 390f));
        weaponPickerRows.GetComponentInParent<ScrollRect>().scrollSensitivity = 28f;
        includeWeaponsButton = CreateButton(dialog.transform, "ONLY SELECTED", new Vector2(-278f, -250f), new Vector2(264f, 36f));
        includeWeaponsButton.onClick.AddListener(() => { editingRandomWeapons = true; randomInclude = true; RefreshWeaponFilter(); });
        excludeWeaponsButton = CreateButton(dialog.transform, "EXCEPT SELECTED", new Vector2(0f, -250f), new Vector2(264f, 36f));
        excludeWeaponsButton.onClick.AddListener(() => { editingRandomWeapons = true; randomInclude = false; RefreshWeaponFilter(); });
        selectedWeaponsButton = CreateButton(dialog.transform, "SELECTED (0)", new Vector2(278f, -250f), new Vector2(264f, 36f));
        selectedWeaponsButton.onClick.AddListener(() => { showSelectedWeapons = !showSelectedWeapons; RefreshWeaponFilter(); });
        weaponFilterSummary = CreateText(dialog.transform, "", new Vector2(-135f, -300f), new Vector2(550f, 40f), 13);
        weaponFilterSummary.enableWordWrapping = true;
        clearWeaponFilterButton = CreateIconButton(dialog.transform, LobbyIcon.Delete, "Clear selected weapons", new Vector2(176f, -300f), new Vector2(38f, 38f));
        clearWeaponFilterButton.onClick.AddListener(() => { randomWeaponNames.Clear(); RefreshWeaponFilter(); });
        applyWeaponFilterButton = CreateButton(dialog.transform, "APPLY RANDOM", new Vector2(310f, -300f), new Vector2(200f, 38f));
        applyWeaponFilterButton.onClick.AddListener(() =>
        {
            try { ApplyWeaponChoice(LobbyWeaponRules.RandomRule(randomInclude, randomWeaponNames)); }
            catch (ArgumentException exception) { weaponPickerError.text = exception.Message; }
        });
        weaponPickerError = CreateText(dialog.transform, "", new Vector2(0f, -337f), new Vector2(820f, 26f), 13, TextAlignmentOptions.Center);
        weaponPickerError.color = new Color(1f, 0.45f, 0.3f);
        weaponPicker.SetActive(false);
    }

    private void OpenWeaponPicker(bool respawn, int slot)
    {
        if (MultiplayerSession.IsActive && !MultiplayerSession.IsHost) return;
        sequenceWeaponIndex = -1;
        pickingRespawnWeapon = respawn;
        pickingWeaponSlot = slot;
        ShowWeaponPicker((respawn ? "RESPAWN WEAPON" : "STARTING WEAPON") + " // SLOT " + (slot + 1),
            LobbyWeaponRules.SlotValue(respawn ? plugin.lobbySettings.RespawnWeapon : plugin.lobbySettings.StartingWeapon, slot));
    }

    private void ShowWeaponPicker(string title, string value)
    {
        lobbyWeapons = WeaponPresetProvider.AllWeapons();
        displayedStartingWeapons = displayedRespawnWeapons = null;
        weaponPickerTitle.text = title;
        weaponSearch.SetTextWithoutNotify("");
        editingRandomWeapons = LobbyWeaponRules.TryParseRandom(value, out randomInclude, out randomWeaponNames);
        showSelectedWeapons = false;
        weaponPicker.transform.SetAsLastSibling();
        weaponPicker.SetActive(true);
        RefreshWeaponFilter();
        HideTooltip();
        weaponSearch.Select();
        weaponSearch.ActivateInputField();
    }

    private void RefreshWeaponFilter()
    {
        SetModeButton(randomWeaponButton, editingRandomWeapons);
        SetModeButton(includeWeaponsButton, editingRandomWeapons && randomInclude);
        SetModeButton(excludeWeaponsButton, editingRandomWeapons && !randomInclude);
        SetModeButton(selectedWeaponsButton, showSelectedWeapons);
        selectedWeaponsButton.interactable = editingRandomWeapons;
        selectedWeaponsButton.GetComponentInChildren<TMP_Text>(true).text = "SELECTED (" + randomWeaponNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() + ")";
        clearWeaponFilterButton.interactable = editingRandomWeapons && randomWeaponNames.Count > 0;
        applyWeaponFilterButton.interactable = editingRandomWeapons && (!randomInclude || randomWeaponNames.Count > 0);
        weaponFilterSummary.text = !editingRandomWeapons ? "Choose a weapon, or select Random to edit its weapon pool." :
            randomWeaponNames.Count == 0 ? (randomInclude ? "Select weapons to include in the random pool." : "No exclusions: Random will use all weapons.") :
            (randomInclude ? "Only marked weapons will be used." : "Marked weapons will be excluded.");
        weaponPickerError.text = "";
        RebuildWeaponPicker();
    }

    private void ToggleRandomWeapon(string name)
    {
        if (randomWeaponNames.Any(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase)))
            randomWeaponNames.RemoveAll(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        else randomWeaponNames.Add(name);
        var scroll = weaponPickerRows.GetComponentInParent<ScrollRect>();
        var position = scroll.verticalNormalizedPosition;
        RefreshWeaponFilter();
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = position;
    }

    private void RebuildWeaponPicker()
    {
        foreach (Transform row in weaponPickerRows)
        {
            row.gameObject.SetActive(false);
            Destroy(row.gameObject);
        }
        var count = 0;
        var query = weaponSearch.text.Trim();
        var rule = pickingRespawnWeapon ? plugin.lobbySettings.RespawnWeapon : plugin.lobbySettings.StartingWeapon;
        var selected = sequenceWeaponIndex >= 0
            ? (!insertingSequenceWeapon ? sequenceDraft[sequenceWeaponIndex] : "")
            : LobbyWeaponRules.SlotValue(rule, pickingWeaponSlot);
        if (editingRandomWeapons)
        {
            foreach (var missing in randomWeaponNames.Distinct(StringComparer.OrdinalIgnoreCase).Where(name => MatchesSettingSearch(name, query) && !lobbyWeapons.Any(weapon => string.Equals(weapon.name, name, StringComparison.OrdinalIgnoreCase))))
            {
                var remove = CreateButton(weaponPickerRows, "REMOVE MISSING WEAPON: " + missing, Vector2.zero, new Vector2(780f, 38f));
                remove.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
                var missingLabel = remove.GetComponentInChildren<TMP_Text>(true);
                missingLabel.richText = false;
                missingLabel.fontSize = 13f;
                missingLabel.overflowMode = TextOverflowModes.Ellipsis;
                EnsureScrollForwarder(remove.gameObject);
                remove.onClick.AddListener(() => ToggleRandomWeapon(missing));
                count++;
            }
        }
        foreach (var group in lobbyWeapons.Where(weapon => MatchesSettingSearch(weapon.name, query) &&
                     (!showSelectedWeapons || randomWeaponNames.Contains(weapon.name, StringComparer.OrdinalIgnoreCase))).GroupBy(weapon => weapon.slot).OrderBy(group => group.Key))
        {
            var header = CreateText(weaponPickerRows, ArsenalMenu.SlotCategoryName(group.Key), Vector2.zero, new Vector2(800f, 30f), 14);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            var weapons = group.ToList();
            Transform row = null;
            for (var index = 0; index < weapons.Count; index++)
            {
                if (index % 3 == 0)
                {
                    var holder = new GameObject("Weapon row", typeof(RectTransform), typeof(LayoutElement));
                    holder.transform.SetParent(weaponPickerRows, false);
                    holder.GetComponent<LayoutElement>().preferredHeight = 82f;
                    row = holder.transform;
                }
                var weapon = weapons[index];
                var tile = CreateButton(row, weapon.name, new Vector2(-262f + index % 3 * 262f, 0f), new Vector2(250f, 74f));
                var marked = editingRandomWeapons ? randomWeaponNames.Contains(weapon.name, StringComparer.OrdinalIgnoreCase) : string.Equals(weapon.name, selected, StringComparison.OrdinalIgnoreCase);
                SetModeButton(tile, marked);
                var label = tile.GetComponentInChildren<TMP_Text>(true);
                SetRect(label.rectTransform, new Vector2(38f, 0f), new Vector2(148f, 58f));
                label.fontSize = 14f;
                label.richText = false;
                label.enableWordWrapping = true;
                label.overflowMode = TextOverflowModes.Ellipsis;
                var icon = new GameObject("Weapon icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                icon.transform.SetParent(tile.transform, false);
                SetRect((RectTransform)icon.transform, new Vector2(-82f, 0f), new Vector2(70f, 52f));
                var image = icon.GetComponent<Image>();
                image.sprite = weapon.sprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                EnsureScrollForwarder(tile.gameObject);
                tile.onClick.AddListener(() =>
                {
                    if (editingRandomWeapons) ToggleRandomWeapon(weapon.name);
                    else ApplyWeaponChoice(weapon.name);
                });
                count++;
            }
        }
        if (count == 0)
            CreateText(weaponPickerRows, "NO WEAPONS FOUND", Vector2.zero, new Vector2(800f, 50f), 16, TextAlignmentOptions.Center).gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;
        weaponPickerRows.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 1f;
    }

    private void ApplyWeaponChoice(string value)
    {
        if (MultiplayerSession.IsActive && !MultiplayerSession.IsHost)
        {
            weaponPicker.SetActive(false);
            return;
        }
        if (sequenceWeaponIndex >= 0)
        {
            var pickingSequenceAtEnd = sequenceWeaponIndex == sequenceDraft.Count;
            if (insertingSequenceWeapon) sequenceDraft.Insert(sequenceWeaponIndex, value);
            else sequenceDraft[sequenceWeaponIndex] = value;
            sequenceWeaponIndex = -1;
            RebuildSequenceEditor();
            if (insertingSequenceWeapon && pickingSequenceAtEnd)
                sequenceRows.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 0f;
            weaponPicker.SetActive(false);
            HideTooltip();
            return;
        }
        try
        {
            var rule = pickingRespawnWeapon ? plugin.lobbySettings.RespawnWeapon : plugin.lobbySettings.StartingWeapon;
            var updated = LobbyWeaponRules.WithSlot(rule, pickingWeaponSlot, value);
            if (pickingRespawnWeapon) plugin.lobbySettings.RespawnWeapon = updated;
            else plugin.lobbySettings.StartingWeapon = updated;
            RefreshPendingSettings();
            weaponPicker.SetActive(false);
            HideTooltip();
        }
        catch (ArgumentException exception) { weaponPickerError.text = exception.Message; }
    }

    private void CreateSequenceEditor()
    {
        sequenceEditor = CreatePanel(root.transform, Vector2.zero, Vector2.zero);
        sequenceEditor.name = "GunGame sequence editor";
        var overlay = (RectTransform)sequenceEditor.transform;
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        sequenceEditor.GetComponent<Image>().color = Color.clear;
        var dialog = CreatePanel(sequenceEditor.transform, Vector2.zero, new Vector2(900f, 720f));
        sequenceTitle = CreateText(dialog.transform, "GUNGAME SEQUENCE", new Vector2(-35f, 315f), new Vector2(760f, 36f), 22);
        var close = CreateIconButton(dialog.transform, LobbyIcon.Close, "Discard chain edits", new Vector2(400f, 315f), new Vector2(36f, 36f));
        close.onClick.AddListener(() => sequenceEditor.SetActive(false));
        sequenceRows = CreateSettingsScroll(dialog.transform, Vector2.zero, new Vector2(840f, 480f));
        sequenceRows.GetComponentInParent<ScrollRect>().scrollSensitivity = 28f;
        var add = CreateButton(dialog.transform, "+ ADD WEAPON", new Vector2(-270f, -310f), new Vector2(280f, 42f));
        add.onClick.AddListener(() => OpenSequenceWeaponPicker(sequenceDraft.Count, true));
        var save = CreateButton(dialog.transform, "SAVE CHAIN", new Vector2(270f, -310f), new Vector2(280f, 42f));
        save.onClick.AddListener(() =>
        {
            if (MultiplayerSession.IsActive && !MultiplayerSession.IsHost) return;
            plugin.lobbySettings.GGSequence = string.Join(";", sequenceDraft);
            RefreshPendingSettings();
            sequenceEditor.SetActive(false);
            HideTooltip();
        });
        sequenceEditor.SetActive(false);
    }

    private void OpenSequenceEditor()
    {
        if (MultiplayerSession.IsActive && !MultiplayerSession.IsHost) return;
        sequenceDraft = LobbyWeaponRules.Sequence(plugin.lobbySettings.GGSequence);
        lobbyWeapons = WeaponPresetProvider.AllWeapons();
        sequenceEditor.transform.SetAsLastSibling();
        sequenceEditor.SetActive(true);
        RebuildSequenceEditor();
        sequenceRows.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 1f;
        HideTooltip();
    }

    private void OpenSequenceWeaponPicker(int index, bool insert)
    {
        if (MultiplayerSession.IsActive && !MultiplayerSession.IsHost) return;
        sequenceWeaponIndex = index;
        insertingSequenceWeapon = insert;
        ShowWeaponPicker("GUNGAME // " + (insert ? "INSERT STAGE " : "REPLACE STAGE ") + (index + 1), insert ? "None" : sequenceDraft[index]);
    }

    private void MoveSequenceStage(int index, int destination)
    {
        LobbyWeaponRules.MoveSequenceEntry(sequenceDraft, index, destination);
        RebuildSequenceEditor();
    }

    private void RebuildSequenceEditor()
    {
        var scroll = sequenceRows.GetComponentInParent<ScrollRect>();
        var scrollPosition = scroll.verticalNormalizedPosition;
        foreach (Transform row in sequenceRows)
        {
            row.gameObject.SetActive(false);
            Destroy(row.gameObject);
        }
        sequenceTitle.text = "GUNGAME SEQUENCE // " + sequenceDraft.Count + " STAGES";
        for (var index = 0; index < sequenceDraft.Count; index++)
        {
            var stage = index;
            var value = sequenceDraft[index];
            var row = CreatePanel(sequenceRows, Vector2.zero, new Vector2(790f, 68f));
            row.AddComponent<LayoutElement>().preferredHeight = 68f;
            var position = CreateInput(row.transform, new Vector2(-362f, 0f), new Vector2(50f, 30f), 9, _ => { }, TMP_InputField.CharacterValidation.Integer);
            position.textComponent.fontSize = 12f;
            position.textComponent.margin = new Vector4(2f, 0f, 2f, 0f);
            position.SetTextWithoutNotify((index + 1).ToString());
            AddTooltip(position.gameObject, "Enter a stage number to move this weapon directly to that position.");
            position.onEndEdit.AddListener(text =>
            {
                if (int.TryParse(text, out var destination) && destination >= 1 && destination <= sequenceDraft.Count && destination != stage + 1)
                {
                    position.onEndEdit.RemoveAllListeners();
                    MoveSequenceStage(stage, destination - 1);
                }
                else position.SetTextWithoutNotify((stage + 1).ToString());
            });
            var change = CreateButton(row.transform, value, new Vector2(-100f, 0f), new Vector2(450f, 52f));
            var label = change.GetComponentInChildren<TMP_Text>(true);
            SetRect(label.rectTransform, new Vector2(40f, 0f), new Vector2(330f, 46f));
            label.fontSize = 15f;
            label.richText = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var preset = lobbyWeapons.FirstOrDefault(weapon => string.Equals(weapon.name, value, StringComparison.OrdinalIgnoreCase));
            var icon = new GameObject("Weapon icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            icon.transform.SetParent(change.transform, false);
            SetRect((RectTransform)icon.transform, new Vector2(-175f, 0f), new Vector2(76f, 42f));
            var image = icon.GetComponent<Image>();
            image.sprite = preset == null ? null : preset.sprite;
            image.enabled = image.sprite != null;
            image.preserveAspect = true;
            image.raycastTarget = false;
            EnsureScrollForwarder(change.gameObject);
            change.onClick.AddListener(() => OpenSequenceWeaponPicker(stage, false));
            var up = CreateIconButton(row.transform, LobbyIcon.Arrow, null, new Vector2(174f, 0f), new Vector2(36f, 36f));
            up.transform.Find("Icon").localRotation = Quaternion.Euler(0f, 0f, 90f);
            up.interactable = index > 0;
            up.onClick.AddListener(() => MoveSequenceStage(stage, stage - 1));
            var down = CreateIconButton(row.transform, LobbyIcon.Arrow, null, new Vector2(220f, 0f), new Vector2(36f, 36f));
            down.transform.Find("Icon").localRotation = Quaternion.Euler(0f, 0f, -90f);
            down.interactable = index + 1 < sequenceDraft.Count;
            down.onClick.AddListener(() => MoveSequenceStage(stage, stage + 1));
            var insert = CreateButton(row.transform, "+", new Vector2(278f, 0f), new Vector2(36f, 36f));
            AddTooltip(insert.gameObject, "Insert a weapon after this stage.");
            insert.onClick.AddListener(() => OpenSequenceWeaponPicker(stage + 1, true));
            var remove = CreateIconButton(row.transform, LobbyIcon.Delete, null, new Vector2(340f, 0f), new Vector2(36f, 36f));
            remove.transform.Find("Icon").GetComponent<Image>().color = new Color(0.95f, 0.32f, 0.25f);
            remove.onClick.AddListener(() => { sequenceDraft.RemoveAt(stage); RebuildSequenceEditor(); });
        }
        if (sequenceDraft.Count == 0)
            CreateText(sequenceRows, "THE CHAIN IS EMPTY. ADD A WEAPON TO BEGIN.", Vector2.zero, new Vector2(790f, 60f), 16, TextAlignmentOptions.Center).gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = scrollPosition;
        HideTooltip();
    }

    private void CreateTeamsEditor()
    {
        teamsEditor = CreatePanel(root.transform, Vector2.zero, Vector2.zero);
        teamsEditor.name = "Lobby teams editor";
        var overlay = (RectTransform)teamsEditor.transform;
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        teamsEditor.GetComponent<Image>().color = Color.clear;
        var dialog = CreatePanel(teamsEditor.transform, Vector2.zero, new Vector2(900f, 720f));
        teamsEditorTitle = CreateText(dialog.transform, "LOBBY TEAMS", new Vector2(-35f, 315f), new Vector2(760f, 36f), 22);
        var close = CreateIconButton(dialog.transform, LobbyIcon.Close, "Discard team edits", new Vector2(400f, 315f), new Vector2(36f, 36f));
        close.onClick.AddListener(() => teamsEditor.SetActive(false));
        teamsEditorHint = CreateText(dialog.transform, "", new Vector2(0f, 270f), new Vector2(820f, 26f), 13, TextAlignmentOptions.Center);
        teamRows = CreateSettingsScroll(dialog.transform, Vector2.zero, new Vector2(840f, 480f));
        teamRows.GetComponentInParent<ScrollRect>().scrollSensitivity = 28f;
        teamsEditorError = CreateText(dialog.transform, "", new Vector2(0f, -265f), new Vector2(820f, 28f), 13, TextAlignmentOptions.Center);
        teamsEditorError.color = new Color(1f, 0.45f, 0.3f);
        var add = CreateButton(dialog.transform, "+ ADD TEAM", new Vector2(-270f, -310f), new Vector2(280f, 42f));
        add.onClick.AddListener(() =>
        {
            var number = teamsDraft.Count + 1;
            while (teamsDraft.Any(team => team.Name.Trim() == "Team " + number)) number++;
            teamsDraft.Add(new LobbyTeamEntry("Team " + number, teamColors[teamsDraft.Count % teamColors.Length]));
            RebuildTeamsEditor();
            teamRows.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 0f;
        });
        var save = CreateButton(dialog.transform, "SAVE TEAMS", new Vector2(270f, -310f), new Vector2(280f, 42f));
        save.onClick.AddListener(() =>
        {
            if (MultiplayerSession.IsActive && !MultiplayerSession.IsHost) return;
            try
            {
                plugin.lobbySettings.TeamsCfg = LobbyTeamRules.Serialize(teamsDraft);
                RefreshPendingSettings();
                teamsEditor.SetActive(false);
                HideTooltip();
            }
            catch (ArgumentException exception) { teamsEditorError.text = exception.Message; }
        });
        teamsEditor.SetActive(false);
    }

    private void OpenTeamsEditor()
    {
        if (MultiplayerSession.IsActive && !MultiplayerSession.IsHost) return;
        teamsDraft = LobbyTeamRules.Parse(plugin.lobbySettings.TeamsCfg);
        teamsEditor.transform.SetAsLastSibling();
        teamsEditor.SetActive(true);
        RebuildTeamsEditor();
        teamRows.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 1f;
        HideTooltip();
    }

    private void RebuildTeamsEditor()
    {
        var scroll = teamRows.GetComponentInParent<ScrollRect>();
        var scrollPosition = scroll.verticalNormalizedPosition;
        foreach (Transform row in teamRows)
        {
            row.gameObject.SetActive(false);
            Destroy(row.gameObject);
        }
        teamsEditorTitle.text = "LOBBY TEAMS // " + teamsDraft.Count;
        teamsEditorHint.text = teamsDraft.Count < 2 ? "Add at least two teams to enable team play." :
            "Edit names, choose a swatch, or enter a HEX color. Team spawn IDs follow the displayed order.";
        teamsEditorError.text = "";
        for (var index = 0; index < teamsDraft.Count; index++)
        {
            var entry = teamsDraft[index];
            var row = CreatePanel(teamRows, Vector2.zero, new Vector2(790f, 112f));
            row.AddComponent<LayoutElement>().preferredHeight = 112f;
            CreateText(row.transform, index.ToString(), new Vector2(-370f, 26f), new Vector2(28f, 30f), 12, TextAlignmentOptions.Center).raycastTarget = false;
            var preview = CreatePanel(row.transform, new Vector2(-320f, 26f), new Vector2(40f, 36f)).GetComponent<Image>();
            preview.color = ColorUtility.TryParseHtmlString(entry.ColorValue.Trim(), out var currentColor) ? currentColor : new Color(0.175f, 0.16f, 0.2f);
            var name = CreateInput(row.transform, new Vector2(-100f, 26f), new Vector2(380f, 36f), 0, value =>
            {
                entry.Name = value;
                teamsEditorError.text = "";
            });
            name.textComponent.richText = false;
            name.SetTextWithoutNotify(entry.Name);
            AddTooltip(name.gameObject, "Team name shown to players.");
            var colorInput = CreateInput(row.transform, new Vector2(200f, 26f), new Vector2(180f, 36f), 0, value =>
            {
                entry.ColorValue = value;
                preview.color = ColorUtility.TryParseHtmlString(value.Trim(), out var color) ? color : new Color(0.175f, 0.16f, 0.2f);
                teamsEditorError.text = "";
            });
            colorInput.textComponent.richText = false;
            colorInput.SetTextWithoutNotify(entry.ColorValue);
            AddTooltip(colorInput.gameObject, "Choose a palette color below, or enter a custom color such as #FF8844.");
            var remove = CreateIconButton(row.transform, LobbyIcon.Delete, "Remove this team", new Vector2(340f, 26f), new Vector2(36f, 36f));
            remove.transform.Find("Icon").GetComponent<Image>().color = new Color(0.95f, 0.32f, 0.25f);
            remove.onClick.AddListener(() => { teamsDraft.Remove(entry); RebuildTeamsEditor(); });
            for (var paletteIndex = 0; paletteIndex < teamColors.Length; paletteIndex++)
            {
                var colorName = teamColors[paletteIndex];
                ColorUtility.TryParseHtmlString(colorName, out var color);
                var swatch = CreateButton(row.transform, "", new Vector2(-340f + paletteIndex * 62f, -25f), new Vector2(28f, 28f));
                swatch.transition = Selectable.Transition.None;
                var image = swatch.GetComponent<Image>();
                image.sprite = null;
                image.material = null;
                image.color = color;
                image.CrossFadeColor(Color.white, 0f, true, true);
                var outline = swatch.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.3f, 0.32f, 0.35f);
                outline.effectDistance = new Vector2(1f, -1f);
                swatch.onClick.AddListener(() => colorInput.text = colorName);
            }
        }
        if (teamsDraft.Count == 0)
            CreateText(teamRows, "NO TEAMS YET. ADD A TEAM TO BEGIN.", Vector2.zero, new Vector2(790f, 60f), 16, TextAlignmentOptions.Center).gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = scrollPosition;
        HideTooltip();
    }

    private void CreateResetSettingsDialog()
    {
        resetSettingsDialog = CreatePanel(root.transform, Vector2.zero, Vector2.zero);
        resetSettingsDialog.name = "Reset Lobby Settings";
        var overlayRect = resetSettingsDialog.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        resetSettingsDialog.GetComponent<Image>().color = Color.clear;
        var dialog = CreatePanel(resetSettingsDialog.transform, Vector2.zero, new Vector2(580f, 240f));
        CreateText(dialog.transform, "RESET LOBBY SETTINGS?", new Vector2(0f, 72f), new Vector2(540f, 40f), 24, TextAlignmentOptions.Center, FontStyles.UpperCase);
        var message = CreateText(dialog.transform, "All settings will be set to their default values", new Vector2(0f, 10f), new Vector2(520f, 60f), 18, TextAlignmentOptions.Center);
        message.enableWordWrapping = true;
        var confirm = CreateButton(dialog.transform, "RESET", new Vector2(-125f, -72f), new Vector2(210f, 44f));
        confirm.onClick.AddListener(() =>
        {
            if (MultiplayerSession.IsActive && !MultiplayerSession.IsHost)
                return;
                
            plugin.lobbySettings = new LobbySettings();
            RefreshPendingSettings();
            plugin.SaveLobbyPreferences();
            resetSettingsDialog.SetActive(false);
        });
        var cancel = CreateButton(dialog.transform, "CANCEL", new Vector2(125f, -72f), new Vector2(210f, 44f));
        cancel.onClick.AddListener(() => resetSettingsDialog.SetActive(false));
        resetSettingsDialog.SetActive(false);
    }

    private void CreateCrossServerDialog()
    {
        crossServerDialog = CreatePanel(root.transform, Vector2.zero, Vector2.zero);
        crossServerDialog.name = "Join Lobby On Another Server";
        var overlay = crossServerDialog.GetComponent<RectTransform>();
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = Vector2.zero;
        overlay.offsetMax = Vector2.zero;
        crossServerDialog.GetComponent<Image>().color = Color.clear;
        var dialog = CreatePanel(crossServerDialog.transform, Vector2.zero, new Vector2(580f, 240f));
        var message = CreateText(dialog.transform, "This lobby is on another server. Would you like to connect?", new Vector2(0f, 30f), new Vector2(520f, 100f), 22, TextAlignmentOptions.Center);
        message.enableWordWrapping = true;
        var no = CreateButton(dialog.transform, "No...", new Vector2(-125f, -72f), new Vector2(210f, 44f));
        no.onClick.AddListener(() => crossServerDialog.SetActive(false));
        var yes = CreateButton(dialog.transform, "Yes", new Vector2(125f, -72f), new Vector2(210f, 44f));
        yes.onClick.AddListener(() =>
        {
            crossServerDialog.SetActive(false);
            plugin.JoinLobby(pendingJoinLobby);
        });
        crossServerDialog.SetActive(false);
    }

    private void RequestJoinLobby(GunsawMultiplayerPlugin.LobbyInfo lobby)
    {
        if (!plugin.CanJoinLobby)
            return;
        
        if (!plugin.IsLobbyOnAnotherServer(lobby))
        {
            plugin.JoinLobby(lobby);
            return;
        }
        
        pendingJoinLobby = lobby;
        crossServerDialog.transform.SetAsLastSibling();
        crossServerDialog.SetActive(true);
    }

    private void OpenResetSettingsDialog()
    {
        if (resetSettingsDialog == null || MultiplayerSession.IsActive && !MultiplayerSession.IsHost) 
            return;
            
        presetsPanel.SetActive(false);
        resetSettingsDialog.transform.SetAsLastSibling();
        resetSettingsDialog.SetActive(true);
    }

    private void CreatePresetManager()
    {
        presetsPanel = CreatePanel(root.transform, Vector2.zero, new Vector2(680f, 610f));
        presetsPanel.name = "Lobby Presets";
        presetsPanel.GetComponent<Image>().color = new Color(0.075f, 0.071f, 0.086f, 0.98f);
        CreateText(presetsPanel.transform, "PRESETS", new Vector2(0f, 255f), new Vector2(600f, 40f), 25, TextAlignmentOptions.Center, FontStyles.UpperCase);
        CreateText(presetsPanel.transform, "PRESET NAME", new Vector2(-210f, 202f), new Vector2(180f, 30f), 14);
        presetNameInput = CreateInput(presetsPanel.transform, new Vector2(20f, 202f), new Vector2(340f, 40f), 32, _ => { });
        var save = CreateButton(presetsPanel.transform, "SAVE", new Vector2(265f, 202f), new Vector2(95f, 40f));
        save.onClick.AddListener(() =>
        {
            try
            {
                plugin.SaveLobbyPreset(presetNameInput.text);
                RebuildPresetRows();
            }
            catch (Exception exception) { plugin.status = exception.Message; }
        });
        var close = CreateIconButton(presetsPanel.transform, LobbyIcon.Close, "Close presets", new Vector2(290f, 255f), new Vector2(40f, 40f));
        close.onClick.AddListener(() => presetsPanel.SetActive(false));
        presetRows = CreateScrollArea(presetsPanel.transform, new Vector2(0f, -50f), new Vector2(620f, 430f));
        presetsPanel.SetActive(false);
    }

    private void OpenPresets()
    {
        if (presetsPanel == null || MultiplayerSession.IsActive && !MultiplayerSession.IsHost) return;
        presetsPanel.SetActive(true);
        RebuildPresetRows();
    }

    private void RebuildPresetRows()
    {
        if (presetRows == null) return;
        for (var index = presetRows.childCount - 1; index >= 0; index--) Destroy(presetRows.GetChild(index).gameObject);
        foreach (var preset in plugin.LobbyPresets)
        {
            var row = new GameObject("Preset", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(presetRows, false);
            row.GetComponent<LayoutElement>().preferredHeight = 46f;
            CreateText(row.transform, preset.name, new Vector2(-175f, 0f), new Vector2(250f, 36f), 16, TextAlignmentOptions.Left, FontStyles.Bold);
            var apply = CreateButton(row.transform, "APPLY", new Vector2(105f, 0f), new Vector2(120f, 38f));
            apply.onClick.AddListener(() =>
            {
                plugin.ApplyLobbyPreset(preset);
                RefreshPendingSettings();
                presetsPanel.SetActive(false);
            });
            var delete = CreateButton(row.transform, "DELETE", new Vector2(245f, 0f), new Vector2(120f, 38f));
            delete.onClick.AddListener(() =>
            {
                plugin.DeleteLobbyPreset(preset);
                RebuildPresetRows();
            });
        }
    }

    private void CreateServerBrowser()
    {
        serverBrowserPanel = CreatePanel(root.transform, Vector2.zero, new Vector2(760f, 650f));
        serverBrowserPanel.name = "Lobby Server Browser";
        serverBrowserPanel.GetComponent<Image>().color = new Color(0.075f, 0.071f, 0.086f, 0.98f);
        serverBrowserTitle = CreateText(serverBrowserPanel.transform, "LOBBY SERVERS", new Vector2(0f, 285f), new Vector2(460f, 44f), 25, TextAlignmentOptions.Center, FontStyles.UpperCase);
        serverBrowserRefresh = CreateIconButton(serverBrowserPanel.transform, LobbyIcon.Refresh, "Refresh relay servers", new Vector2(260f, 285f), new Vector2(40f, 40f));
        serverBrowserRefresh.onClick.AddListener(plugin.RefreshServerList);
        var close = CreateIconButton(serverBrowserPanel.transform, LobbyIcon.Close, "Close relay servers", new Vector2(315f, 285f), new Vector2(40f, 40f));
        close.onClick.AddListener(() => SetServerBrowserOpen(false));
        serverBrowserState = CreateText(serverBrowserPanel.transform, "Loading servers...", new Vector2(0f, 240f), new Vector2(680f, 30f), 14, TextAlignmentOptions.Center);
        serverRows = CreateScrollArea(serverBrowserPanel.transform, new Vector2(0f, -38f), new Vector2(700f, 510f));
        serverBrowserPanel.SetActive(false);
    }

    private void SetServerBrowserOpen(bool value)
    {
        serverBrowserOpen = value;
        if (serverBrowserPanel == null) return;
        serverBrowserPanel.SetActive(value);
        if (value)
        {
            renderedServerListHash = int.MinValue;
            plugin.RefreshServerList();
            RebuildServerRows();
        }
    }

    private void RebuildServerRows()
    {
        if (serverRows == null || serverBrowserState == null) return;
        serverBrowserTitle.text = "LOBBY SERVERS";
        serverBrowserRefresh.gameObject.SetActive(true);
        serverBrowserState.text = plugin.serverListLoading ? "Parsing server list..." :
            !string.IsNullOrEmpty(plugin.serverListError) ? "Could not load servers: " + plugin.serverListError :
            plugin.servers.Count == 0 ? "No servers found." : "";
        var hash = plugin.serverListLoading ? -1 : plugin.serverListError.GetHashCode();
        foreach (var server in plugin.servers) hash = hash * 31 + (server.address + "\n" + server.location + "\n" + server.pingMs).GetHashCode();
        if (hash == renderedServerListHash) return;
        renderedServerListHash = hash;
        for (var index = serverRows.childCount - 1; index >= 0; index--) Destroy(serverRows.GetChild(index).gameObject);
        foreach (var server in plugin.servers)
        {
            var row = CreatePanel(serverRows, Vector2.zero, new Vector2(670f, 76f));
            row.name = server.address;
            row.AddComponent<LayoutElement>().preferredHeight = 76f;
            var addressText = CreateText(row.transform, server.address, new Vector2(-165f, 15f), new Vector2(300f, 28f), 18, TextAlignmentOptions.Left, FontStyles.Bold);
            addressText.richText = false;
            addressText.overflowMode = TextOverflowModes.Ellipsis;
            var locationText = CreateText(row.transform, server.location, new Vector2(-165f, -15f), new Vector2(300f, 24f), 14, TextAlignmentOptions.Left);
            locationText.richText = false;
            locationText.overflowMode = TextOverflowModes.Ellipsis;
            locationText.color = new Color(0.62f, 0.65f, 0.69f);
            var ping = CreateText(row.transform, FormatPing(server.pingMs), new Vector2(105f, 0f), new Vector2(130f, 32f), 17, TextAlignmentOptions.Center, FontStyles.Bold);
            ping.color = PingColor(server.pingMs);
            var connect = CreateIconButton(row.transform, LobbyIcon.Connect, "Use this relay server", new Vector2(280f, 0f), new Vector2(48f, 44f));
            connect.interactable = !MultiplayerSession.IsHosting;
            var address = server.address;
            connect.onClick.AddListener(() =>
            {
                SetServerBrowserOpen(false);
                plugin.SelectLobbyServer(address);
            });
        }
    }

    private static string FormatPing(int pingMs) => pingMs < 0 ? "--" : pingMs + " ms";

    private static Color PingColor(int pingMs)
    {
        if (pingMs < 0) return new Color(0.65f, 0.65f, 0.65f);
        if (pingMs < 80) return new Color(0.3f, 1f, 0.4f);
        if (pingMs < 160) return new Color(1f, 0.82f, 0.2f);
        return new Color(1f, 0.32f, 0.25f);
    }

    internal void ShowCustomLevelSuggestion(string playerName, CustomLevelSuggestionPacket suggestion)
    {
        if (!MultiplayerSession.IsHosting || customLevelSuggestionDialog == null) return;
        pendingCustomLevelSuggestion = suggestion;
        customLevelSuggestionText.text = playerName + " suggests loading their map (" + suggestion.SizeKiB + " KiB).";
        customLevelSuggestionDialog.SetActive(true);
    }

    private void CreateCustomLevelSuggestionDialog()
    {
        customLevelSuggestionDialog = CreatePanel(root.transform, Vector2.zero, new Vector2(580f, 260f));
        customLevelSuggestionDialog.name = "Custom Level Suggestion";
        customLevelSuggestionDialog.GetComponent<Image>().color = new Color(0.075f, 0.071f, 0.086f, 0.98f);
        CreateText(customLevelSuggestionDialog.transform, "CUSTOM LEVEL SUGGESTION", new Vector2(0f, 88f), new Vector2(520f, 38f), 22, TextAlignmentOptions.Center, FontStyles.UpperCase);
        customLevelSuggestionText = CreateText(customLevelSuggestionDialog.transform, "", Vector2.zero, new Vector2(500f, 100f), 16, TextAlignmentOptions.Center);
        customLevelSuggestionText.enableWordWrapping = true;
        var load = CreateButton(customLevelSuggestionDialog.transform, "LOAD", new Vector2(-125f, -88f), new Vector2(210f, 44f));
        load.onClick.AddListener(() =>
        {
            customLevelSuggestionDialog.SetActive(false);
            plugin.AcceptCustomLevelSuggestion(pendingCustomLevelSuggestion);
        });
        var decline = CreateButton(customLevelSuggestionDialog.transform, "DECLINE", new Vector2(125f, -88f), new Vector2(210f, 44f));
        decline.onClick.AddListener(() => customLevelSuggestionDialog.SetActive(false));
        customLevelSuggestionDialog.SetActive(false);
    }

    internal void NoteAppliedLobbySettings(LobbySettings settings, string name)
    {
        appliedSettings = JsonUtility.ToJson(settings) + "\n" + name;
        RefreshPendingSettings();
    }

    private void RefreshPendingSettings()
    {
        pendingSettings = plugin != null && MultiplayerSession.IsHosting && appliedSettings.Length > 0 &&
            appliedSettings != JsonUtility.ToJson(plugin.lobbySettings) + "\n" + plugin.lobbyName;
    }

    internal static string CycleLabel(string value, int offset)
    {
        var cycle = value + "    /    ";
        var characters = System.Globalization.StringInfo.ParseCombiningCharacters(cycle);
        var start = characters[offset % characters.Length];
        return cycle.Substring(start) + cycle.Substring(0, start);
    }

    internal sealed class CyclingLabel : MonoBehaviour
    {
        private TMP_Text label;
        private string source = "";
        private float sourceWidth;
        private float started;
        private int offset = -1;

        private void Awake()
        {
            label = GetComponent<TMP_Text>();
            label.richText = false;
            label.overflowMode = TextOverflowModes.Truncate;
        }

        internal void SetValue(string value)
        {
            if (source == value) return;
            source = value;
            sourceWidth = label.GetPreferredValues(source).x;
            label.text = source;
            started = Time.unscaledTime;
            offset = -1;
        }

        private void OnEnable()
        {
            started = Time.unscaledTime;
            offset = -1;
        }

        private void LateUpdate()
        {
            if (sourceWidth <= label.rectTransform.rect.width) return;
            var elapsed = Mathf.Max(0f, Time.unscaledTime - started - 1.6f);
            var nextOffset = (int)(elapsed / 0.24f);
            if (offset != nextOffset || label.havePropertiesChanged)
            {
                offset = nextOffset;
                label.text = offset == 0 ? source : CycleLabel(source, offset);
                label.ForceMeshUpdate();
            }
            var info = label.textInfo;
            var first = -1;
            var last = -1;
            for (var index = 0; index < info.characterCount; index++)
            {
                if (!info.characterInfo[index].isVisible) continue;
                if (first < 0) first = index;
                last = index;
            }
            if (first < 0) return;
            var phase = elapsed % 0.24f / 0.24f;
            FadeCharacter(first, elapsed == 0f ? 1f : 1f - phase);
            FadeCharacter(last, elapsed == 0f ? 1f : phase);
            label.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }

        private void FadeCharacter(int index, float alpha)
        {
            var character = label.textInfo.characterInfo[index];
            var colors = label.textInfo.meshInfo[character.materialReferenceIndex].colors32;
            for (var corner = 0; corner < 4; corner++)
                colors[character.vertexIndex + corner].a = (byte)(label.color.a * alpha * 255f);
        }
    }

    internal static bool MatchesSettingSearch(string label, string query)
    {
        return label.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void FilterSettings(string query)
    {
        settingsQuery = query;
        if (settingsRows == null) return;
        RefreshSettingsVisibility();
        settingsRows.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 1f;
        HideTooltip();
    }

    private void RefreshSettingsVisibility()
    {
        var visible = 0;
        foreach (Transform row in settingsRows)
        {
            var matches = LobbySettingsSchema.IsVisible(row.name, readSettingBoolean) &&
                MatchesSettingSearch(settingLabels[row.name], settingsQuery);
            if (row.gameObject.activeSelf != matches)
            {
                row.gameObject.SetActive(matches);
                HideTooltip();
            }
            if (matches) visible++;
        }
        settingsEmpty.gameObject.SetActive(visible == 0);
    }

    private Button CreateIconButton(Transform parent, LobbyIcon kind, string? tooltip, Vector2 position, Vector2 size)
    {
        var button = CreateButton(parent, "", position, size);
        button.name = tooltip ?? kind.ToString();
        CreateIcon(button.transform, kind, new Color(0.88f, 0.89f, 0.91f));
        if (tooltip != null) AddTooltip(button.gameObject, tooltip);
        return button;
    }

    internal enum LobbyIcon { Close, Play, Paste, Search, Refresh, List, Connect, Warning, Delete, Arrow }

    internal static void CreateIcon(Transform parent, LobbyIcon kind, Color color)
    {
        var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        SetRect((RectTransform)go.transform, Vector2.zero, new Vector2(32f, 32f));
        var icon = go.GetComponent<Image>();
        icon.sprite = EmbeddedSpriteLoader.Load("GunsawMultiplayer.Assets.UI." + kind.ToString().ToLowerInvariant() + ".png", 16f, new Vector2(0.5f, 0.5f));
        icon.color = color;
        icon.raycastTarget = false;
    }

    private GameObject CreatePanel(Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = new Color(0.075f, 0.071f, 0.086f, 1f);
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0.25f, 0.235f, 0.28f, 1f);
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

        group.GetComponent<Image>().color = new Color(0.095f, 0.089f, 0.107f, 1f);

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

    internal static string CustomLevelStatus(string json)
    {
        return string.IsNullOrEmpty(json) ? "NOT LOADED" :
            "LOADED [" + (System.Text.Encoding.UTF8.GetByteCount(json) / 1024f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KB]";
    }

    internal static ColorBlock MenuButtonColors(ColorBlock colors)
    {
        colors.normalColor = new Color(0.2f, 0.188f, 0.225f);
        colors.highlightedColor = new Color(0.35f, 0.328f, 0.388f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.44f, 0.28f, 0.18f);
        colors.disabledColor = new Color(0.14f, 0.13f, 0.16f, 0.65f);
        colors.fadeDuration = 0.1f;
        return colors;
    }

    internal static void SetModeButton(Button button, bool selected)
    {
        var colors = MenuButtonColors(button.colors);
        if (selected)
        {
            colors.normalColor = new Color(0.48f, 0.22f, 0.095f);
            colors.highlightedColor = new Color(0.66f, 0.31f, 0.13f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.48f, 0.22f, 0.095f);
        }
        button.colors = colors;
        button.GetComponentInChildren<TMP_Text>(true).color = selected ? new Color(1f, 0.72f, 0.46f) : new Color(0.88f, 0.89f, 0.91f);
    }

    private Button CreateButton(Transform parent, string label, Vector2 position, Vector2 size)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        var source = templateButton.targetGraphic as Image;
        image.sprite = source != null ? source.sprite : null;
        image.material = source != null ? source.material : null;
        image.color = Color.white;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.colors = MenuButtonColors(templateButton.colors);
        image.CrossFadeColor(button.colors.normalColor * button.colors.colorMultiplier, 0f, true, true);
        SetRect(go.GetComponent<RectTransform>(), position, size);
        CreateText(go.transform, label, Vector2.zero, size, 16, TextAlignmentOptions.Center).raycastTarget = false;
        return button;
    }

    private void AddNumericRange(TMP_InputField input, float minimum, float maximum, bool floatNum)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var format = floatNum ? "0.0#" : "0";

        var range = "[" + minimum.ToString(format, culture) + ".." + maximum.ToString(format, culture) + "]";
        var label = CreateText(input.transform, range, new Vector2(-input.GetComponent<RectTransform>().sizeDelta.x * 0.5f - 80f, 0f), new Vector2(140f, 24f), 11, TextAlignmentOptions.Right);
        label.color = new Color(0.58f, 0.58f, 0.58f, 0.38f);
        label.raycastTarget = false;
    }

    private TMP_InputField CreateInput(Transform parent, Vector2 position, Vector2 size, int limit, Action<string> changed, TMP_InputField.CharacterValidation validation = TMP_InputField.CharacterValidation.None)
    {
        var go = new GameObject("Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false); SetRect(go.GetComponent<RectTransform>(), position, size);
        var image = go.GetComponent<Image>(); image.color = new Color(0.058f, 0.054f, 0.068f, 1f);
        var outline = go.AddComponent<Outline>(); outline.effectColor = new Color(0.3f, 0.28f, 0.33f, 1f); outline.effectDistance = new Vector2(1f, -1f);
        var field = go.GetComponent<TMP_InputField>(); field.targetGraphic = image; field.characterLimit = limit;
        EnsureScrollForwarder(go);
        var viewport = new GameObject("Text Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(go.transform, false);
        SetRect(viewport.GetComponent<RectTransform>(), Vector2.zero, new Vector2(size.x - 12f, size.y - 4f));
        var text = CreateText(viewport.transform, "", Vector2.zero, new Vector2(size.x - 12f, size.y - 4f), 16, TextAlignmentOptions.Left);
        text.margin = new Vector4(8f, 0f, 8f, 0f); text.enableWordWrapping = false;
        field.textViewport = viewport.GetComponent<RectTransform>(); field.textComponent = text;
        field.caretColor = new Color(1f, 0.56f, 0.3f);
        field.selectionColor = new Color(0.75f, 0.38f, 0.17f, 0.45f);
        field.characterValidation = validation;
        if (validation != TMP_InputField.CharacterValidation.None) text.alignment = TextAlignmentOptions.Center;
        field.onValueChanged.AddListener(value => { changed(value); RefreshPendingSettings(); });
        return field;
    }

    private Toggle CreateToggle(Transform parent, string label, Vector2 position, Vector2 size, Action<bool> changed)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
        go.transform.SetParent(parent, false);
        SetRect((RectTransform)go.transform, position, size);
        go.GetComponent<Image>().color = Color.clear;

        var back = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        back.transform.SetParent(go.transform, false);
        SetRect((RectTransform)back.transform, new Vector2(size.x * 0.5f - 36f, 0f), new Vector2(56f, 26f));
        back.GetComponent<Image>().color = new Color(0.155f, 0.145f, 0.18f, 1f);

        var check = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        check.transform.SetParent(back.transform, false);
        SetRect((RectTransform)check.transform, Vector2.zero, new Vector2(46f, 16f));
        check.GetComponent<Image>().color = new Color(0.95f, 0.48f, 0.23f, 1f);
        var state = CreateText(back.transform, "OFF", Vector2.zero, new Vector2(56f, 26f), 11, TextAlignmentOptions.Center);
        state.name = "State";
        state.raycastTarget = false;

        var toggle = go.GetComponent<Toggle>();
        settingToggles.Add(parent.name, toggle);
        EnsureScrollForwarder(go);
        toggle.targetGraphic = back.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();

        var textRect = CreateText(go.transform, label, new Vector2(-40f, 0f), new Vector2(size.x - 80f, size.y), 14, TextAlignmentOptions.MidlineLeft);
        textRect.raycastTarget = false;

        toggle.onValueChanged.AddListener(value => { SetToggle(toggle, value); changed(value); RefreshPendingSettings(); });
        return toggle;
    }

    private void AddTooltip(GameObject target, string message, Color? color = null)
    {
        if (target == null) return;
        EnsureScrollForwarder(target);
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
        var height = Mathf.Max(36f, tooltipText.GetPreferredValues(message, 1210f, 0f).y + 12f);
        var rect = tooltipPanel.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1250f, height);
        rect.anchoredPosition = new Vector2(0f, -444f + height * 0.5f);
        tooltipText.rectTransform.sizeDelta = new Vector2(1210f, height - 8f);
        tooltipPanel.SetActive(true);
    }

    private void HideTooltip()
    {
        if (tooltipPanel != null) tooltipPanel.SetActive(false);
    }

    private Transform CreateScrollArea(Transform parent, Vector2 position, Vector2 size)
    {
        var view = new GameObject("LobbyScroll", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect)); view.transform.SetParent(parent, false); SetRect((RectTransform)view.transform, position, size);
        view.GetComponent<Image>().color = new Color(0.058f, 0.054f, 0.068f, 1f); view.GetComponent<Mask>().showMaskGraphic = true;
        var outline = view.AddComponent<Outline>(); outline.effectColor = new Color(0.25f, 0.235f, 0.28f, 1f); outline.effectDistance = new Vector2(1f, -1f);
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
        var mask = view.GetComponent<Mask>();
        mask.enabled = false;
        Destroy(mask);
        var viewport = new GameObject("Settings viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(view, false);
        var viewportRect = (RectTransform)viewport.transform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(6f, 6f);
        viewportRect.offsetMax = new Vector2(-26f, -6f);
        content.SetParent(viewport.transform, false);
        ((RectTransform)content).sizeDelta = Vector2.zero;
        scroll.viewport = viewportRect;

        var scrollbar = new GameObject("Scrollbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
        scrollbar.transform.SetParent(view, false);
        var scrollbarRect = (RectTransform)scrollbar.transform;
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.anchoredPosition = new Vector2(-11f, 0f);
        scrollbarRect.sizeDelta = new Vector2(12f, -10f);
        var scrollbarImage = scrollbar.GetComponent<Image>();
        scrollbarImage.color = new Color(0.12f, 0.11f, 0.14f, 0.9f);
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

    private Transform CreateSettingsRow(Transform parent, string setting)
    {
        var row = new GameObject(setting, typeof(RectTransform), typeof(LayoutElement), typeof(CanvasRenderer), typeof(Image));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = 46f;
        row.GetComponent<Image>().color = new Color(0.112f, 0.105f, 0.125f, 1f);
        EnsureScrollForwarder(row);
        return row.transform;
    }

    private static void EnsureScrollForwarder(GameObject target)
    {
        if (target.GetComponent<ScrollForwarder>() == null) target.AddComponent<ScrollForwarder>();
    }

    private sealed class ScrollForwarder : MonoBehaviour, IScrollHandler
    {
        public void OnScroll(PointerEventData eventData)
        {
            var scroll = GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.OnScroll(eventData);
        }
    }

    private TMP_Text CreateText(Transform parent, string value, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false); SetRect((RectTransform)go.transform, position, size);
        var text = go.GetComponent<TextMeshProUGUI>(); text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial; text.spriteAsset = TMP_Settings.defaultSpriteAsset; text.richText = true; text.color = new Color(0.88f, 0.89f, 0.91f); text.fontSize = fontSize; text.alignment = alignment; text.fontStyle = style; text.text = value; text.enableWordWrapping = false; return text;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size) { rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.pivot = new Vector2(0.5f, 0.5f); rect.anchoredPosition = position; rect.sizeDelta = size; }

    private void FitPanelToScreen()
    {
        var canvasRect = root == null ? null : root.GetComponent<RectTransform>();
        if (canvasRect == null || panel == null) return;
        var available = canvasRect.rect.size - new Vector2(48f, 48f);
        if (available.x <= 0f || available.y <= 0f) return;
        var scale = Mathf.Min(1f, Mathf.Min(available.x / 1320f, available.y / 900f));
        panel.transform.localScale = new Vector3(scale, scale, 1f);
    }
    private static void SetInput(TMP_InputField input, string value) { if (input != null && !input.isFocused && input.text != value) input.SetTextWithoutNotify(value); }

    private void RebuildLobbyRows()
    {
        if (MultiplayerSession.IsActive)
        {
            publicLobbiesTitle.text = "LOBBY PLAYERS";
            refreshLobbiesButton.gameObject.SetActive(false);
            var peerIds = MultiplayerSession.PeerIds();
            var localPrefab = LocalPlayerReplication.SelectedCharacterPrefab;
            var playerHash = MultiplayerSession.PeerListRevision * 31 + (MultiplayerSession.IsHost ? 1 : 0);
            playerHash = playerHash * 31 + MultiplayerSession.PingMs;
            playerHash = playerHash * 31 + localPrefab.GetHashCode();
            foreach (var peerId in peerIds)
                playerHash = playerHash * 31 + peerId * 31 + MultiplayerSession.PeerPing(peerId) + MultiplayerSession.PlayerName(peerId).GetHashCode() + MultiplayerSession.PeerCharacterPrefab(peerId).GetHashCode();
            if (playerHash == renderedLobbyHash) return;
            renderedLobbyHash = playerHash;
            for (var index = lobbyRows.childCount - 1; index >= 0; index--) Destroy(lobbyRows.GetChild(index).gameObject);
            AddCurrentLobbyPlayerRow(MultiplayerSession.LocalPlayerName, MultiplayerSession.IsHost ? 0 : MultiplayerSession.PingMs,
                MultiplayerSession.IsHost, localPrefab);
            foreach (var peerId in peerIds)
                AddCurrentLobbyPlayerRow(MultiplayerSession.PlayerName(peerId), peerId == MultiplayerSession.HostPeerId ? 0 : MultiplayerSession.PeerPing(peerId),
                    peerId == MultiplayerSession.HostPeerId, MultiplayerSession.PeerCharacterPrefab(peerId));
            return;
        }
        publicLobbiesTitle.text = "PUBLIC LOBBIES";
        refreshLobbiesButton.gameObject.SetActive(true);
        var canJoin = plugin.CanJoinLobby;
        var hash = plugin.lobbies.Count + (canJoin ? 1 : 0) + (MultiplayerSession.IsActive ? 7 : 0);
        foreach (var lobby in plugin.lobbies)
        {
            hash = hash * 31 + lobby.server.GetHashCode();
            hash = hash * 31 + (lobby.id ?? "").GetHashCode();
            hash = hash * 31 + (lobby.name ?? "").GetHashCode();
            hash = hash * 31 + (lobby.hostName ?? "").GetHashCode();
            hash = hash * 31 + (lobby.map ?? "").GetHashCode();
            hash = hash * 31 + lobby.players;
            hash = hash * 31 + lobby.maxPlayers;
            hash = hash * 31 + (lobby.teams ? 1 : 0);
            hash = hash * 31 + (lobby.pvp ? 1 : 0);
            hash = hash * 31 + (lobby.propHunt ? 1 : 0);
            hash = hash * 31 + (lobby.brutalMode ? 1 : 0);
            hash = hash * 31 + (lobby.blackout ? 1 : 0);
            hash = hash * 31 + (lobby.restrictLight ? 1 : 0);
            hash = hash * 31 + (lobby.gunGame ? 1 : 0);
            hash = hash * 31 + (lobby.modVersion ?? "").GetHashCode();
        }
        if (hash == renderedLobbyHash) return;
        renderedLobbyHash = hash;
        for (var i = lobbyRows.childCount - 1; i >= 0; i--) Destroy(lobbyRows.GetChild(i).gameObject);
        foreach (var lobby in plugin.lobbies)
        {
            var row = CreatePanel(lobbyRows, Vector2.zero, new Vector2(530f, 94f));
            row.name = "Lobby";
            row.AddComponent<LayoutElement>().preferredHeight = 94f;
            var map = lobby.map == "LevelLoader" ? "Custom level" : LevelNames.Resolve(lobby.map ?? "");
            var title = CreateText(row.transform, lobby.name, new Vector2(-48f, 24f), new Vector2(400f, 28f), 18, TextAlignmentOptions.Left, FontStyles.Bold);
            title.richText = false;
            title.gameObject.AddComponent<CyclingLabel>().SetValue(lobby.name ?? "");
            var details = CreateText(row.transform, lobby.hostName + " / " + map, new Vector2(-48f, -4f), new Vector2(400f, 24f), 13);
            details.richText = false;
            details.overflowMode = TextOverflowModes.Ellipsis;
            details.color = new Color(0.62f, 0.65f, 0.69f);
            CreateText(row.transform, LobbyModes(lobby), new Vector2(-48f, -29f), new Vector2(400f, 22f), 12);
            CreateText(row.transform, lobby.players + "/" + lobby.maxPlayers, new Vector2(222f, -34f), new Vector2(64f, 18f), 11, TextAlignmentOptions.Center);
            if (!IsLobbyVersionCompatible(lobby.modVersion))
            {
                var versionBox = CreatePanel(row.transform, new Vector2(222f, 4f), new Vector2(44f, 44f));
                var version = CreateText(versionBox.transform, lobby.modVersion, Vector2.zero, new Vector2(40f, 40f), 10, TextAlignmentOptions.Center);
                version.richText = false;
                version.enableAutoSizing = true;
                version.fontSizeMin = 6f;
                version.fontSizeMax = 10f;
                version.overflowMode = TextOverflowModes.Ellipsis;
                version.color = new Color(1f, 0.72f, 0.46f);
                AddTooltip(versionBox, "Different mod version: " + lobby.modVersion + ". Your version: " + GunsawMultiplayerPlugin.PluginVersion + ".");
            }
            else
            {
                var join = CreateIconButton(row.transform, LobbyIcon.Connect, "Join lobby", new Vector2(222f, 4f), new Vector2(44f, 44f));
                join.interactable = canJoin && lobby.players < lobby.maxPlayers;
                join.onClick.AddListener(() => RequestJoinLobby(lobby));
            }
        }
    }

    internal static string LobbyModes(GunsawMultiplayerPlugin.LobbyInfo lobby)
    {
        var modes = new List<string> { lobby.propHunt ? "PROP HUNT" : lobby.pvp ? "PVP" : "CO-OP" };
        if (lobby.brutalMode) modes.Add("BRUTAL");
        if (lobby.teams) modes.Add("TEAMS");
        if (lobby.blackout) modes.Add(lobby.restrictLight ? "BLACKOUT+" : "BLACKOUT");
        if (lobby.gunGame) modes.Add("GUNGAME");
        return string.Join(" / ", modes);
    }

    internal static bool IsLobbyVersionCompatible(string version) => string.IsNullOrWhiteSpace(version) ||
        string.Equals(version.Trim().TrimStart('v', 'V'), GunsawMultiplayerPlugin.PluginVersion, StringComparison.OrdinalIgnoreCase);

    private Sprite CharacterHead(string path)
    {
        if (string.IsNullOrEmpty(path) || !NetworkAvatarManager.knownCharacterPrefabs.Contains(path)) return null;
        if (characterHeads.TryGetValue(path, out var cached) && cached != null) return cached;
        var prefab = Resources.Load<GameObject>(path);
        var body = prefab == null ? null : prefab.GetComponentInChildren<BodyScript>(true);
        if (body == null || body.headTransform == null) return null;
        var expression = body.headTransform.GetComponentInChildren<FacialExpression>(true);
        var renderer = body.headTransform.GetComponent<SpriteRenderer>() ?? body.headTransform.GetComponentInChildren<SpriteRenderer>(true);
        var head = expression != null && expression.normalFace != null ? expression.normalFace : renderer == null ? null : renderer.sprite;
        if (head != null) characterHeads[path] = head;
        return head;
    }

    private void AddCurrentLobbyPlayerRow(string name, int pingMs, bool host, string characterPrefab)
    {
        var row = CreatePanel(lobbyRows, Vector2.zero, new Vector2(530f, 58f));
        row.name = "Lobby Player";
        row.AddComponent<LayoutElement>().preferredHeight = 58f;
        var portrait = new GameObject("Character head", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        portrait.transform.SetParent(row.transform, false);
        SetRect((RectTransform)portrait.transform, new Vector2(-222f, 0f), new Vector2(40f, 40f));
        var image = portrait.GetComponent<Image>();
        image.sprite = CharacterHead(characterPrefab);
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.enabled = image.sprite != null;
        if (image.sprite == null) CreateText(portrait.transform, "?", Vector2.zero, new Vector2(40f, 40f), 20, TextAlignmentOptions.Center).raycastTarget = false;
        var displayName = (host ? "[HOST] " : "") + (name ?? "Player");
        var pingColor = ColorUtility.ToHtmlStringRGB(PingColor(pingMs));
        var player = CreateText(row.transform, displayName, new Vector2(-30f, 0f), new Vector2(310f, 42f), 17, TextAlignmentOptions.Left, FontStyles.Bold);
        player.richText = false;
        player.overflowMode = TextOverflowModes.Ellipsis;
        CreateText(row.transform, "<color=#" + pingColor + ">" + FormatPing(pingMs) + "</color>", new Vector2(196f, 0f), new Vector2(100f, 32f), 14, TextAlignmentOptions.Right);
    }
}
