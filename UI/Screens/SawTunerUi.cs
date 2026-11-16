using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class MultiplayerLobbyUi
{
    private GameObject? sawTunerWindow;
    private Image sawTunerPreview = null!;
    private readonly Dictionary<string, (string Name, Sprite Sprite)> sawTunerPortraits = new();
    private TMP_Text sawTunerName = null!, sawTunerCounter = null!;
    private Slider[] sawTunerSliders = Array.Empty<Slider>();
    private TMP_InputField[] sawTunerInputs = Array.Empty<TMP_InputField>();
    private Button sawTunerSave = null!, sawTunerReset = null!;
    private Dictionary<string, float> sawTunerDraft = new();
    private List<string> sawTunerCharacters = new();
    private int sawTunerIndex, sawTunerGeneration;
    private static bool SawTunerReadOnly => MultiplayerSession.IsActive && !MultiplayerSession.IsHost;

    private void CaptureSawTunerPortraits(MainMenuManager menu)
    {
        foreach (var character in menu.characters)
            if (character != null && !string.IsNullOrEmpty(character.prefabPath) && character.charSprite != null)
                sawTunerPortraits[character.prefabPath] = (character.name, character.charSprite);
        if (menu.albinoSprite != null) sawTunerPortraits["Enemies/AlbinoEnemy"] = ("Albino", menu.albinoSprite);
    }

    private void OpenSawTuner()
    {
        sawTunerCharacters = NetworkAvatarManager.knownCharacterPrefabs.Where(path => SawTuner.Prefab(path) != null).ToList();
        if (sawTunerCharacters.Count == 0) return;
        if (sawTunerWindow == null) CreateSawTuner();
        sawTunerDraft = SawTunerReadOnly ? new Dictionary<string, float>(SawTuner.Values) : SawTunerValues.Parse(plugin.lobbySettings.SawTuner, clampToRange: true);
        sawTunerGeneration = SawTuner.Generation;
        sawTunerIndex = Mathf.Clamp(sawTunerIndex, 0, sawTunerCharacters.Count - 1);
        sawTunerWindow!.transform.SetAsLastSibling();
        sawTunerWindow.SetActive(true);
        ShowSawTunerCharacter();
        HideTooltip();
    }

    private void CreateSawTuner()
    {
        sawTunerWindow = CreatePanel(panel.transform, Vector2.zero, new Vector2(1320f, 900f));
        sawTunerWindow.name = "SawTuner";
        sawTunerWindow.GetComponent<Image>().color = Color.clear;
        sawTunerWindow.GetComponent<Outline>().enabled = false;
        var dialog = CreatePanel(sawTunerWindow.transform, Vector2.zero, new Vector2(1000f, 720f));
        CreateText(dialog.transform, "SAWTUNER", new Vector2(-25f, 315f), new Vector2(870f, 36f), 24);
        var close = CreateIconButton(dialog.transform, LobbyIcon.Close, "Discard tuner edits", new Vector2(450f, 315f), new Vector2(36f, 36f));
        close.onClick.AddListener(() => sawTunerWindow.SetActive(false));
        var preview = CreatePanel(dialog.transform, new Vector2(-320f, 47f), new Vector2(280f, 420f));
        preview.GetComponent<Image>().color = new Color(0.112f, 0.105f, 0.125f);
        var portrait = new GameObject("Character portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        portrait.transform.SetParent(preview.transform, false);
        SetRect((RectTransform)portrait.transform, new Vector2(0f, 25f), new Vector2(240f, 340f));
        sawTunerPreview = portrait.GetComponent<Image>();
        sawTunerPreview.preserveAspect = true;
        sawTunerPreview.raycastTarget = false;
        sawTunerName = CreateText(preview.transform, "", new Vector2(0f, -175f), new Vector2(260f, 34f), 19, TextAlignmentOptions.Center);
        var previous = CreateButton(dialog.transform, "<", new Vector2(-420f, -200f), new Vector2(70f, 38f));
        previous.onClick.AddListener(() => { sawTunerIndex = (sawTunerIndex + sawTunerCharacters.Count - 1) % sawTunerCharacters.Count; ShowSawTunerCharacter(); });
        var next = CreateButton(dialog.transform, ">", new Vector2(-220f, -200f), new Vector2(70f, 38f));
        next.onClick.AddListener(() => { sawTunerIndex = (sawTunerIndex + 1) % sawTunerCharacters.Count; ShowSawTunerCharacter(); });
        sawTunerCounter = CreateText(dialog.transform, "", new Vector2(-320f, -200f), new Vector2(110f, 38f), 18, TextAlignmentOptions.Center);
        sawTunerSliders = new Slider[SawTunerValues.Fields.Length];
        sawTunerInputs = new TMP_InputField[SawTunerValues.Fields.Length];
        for (var i = 0; i < sawTunerInputs.Length; i++)
        {
            var stat = i;
            var row = CreatePanel(dialog.transform, new Vector2(145f, 235f - i * 48f), new Vector2(620f, 44f));
            row.GetComponent<Image>().color = new Color(0.112f, 0.105f, 0.125f);
            CreateText(row.transform, SawTunerValues.Labels[i], new Vector2(-210f, 0f), new Vector2(180f, 32f), 13);
            var track = CreatePanel(row.transform, new Vector2(20f, 0f), new Vector2(230f, 10f));
            track.GetComponent<Image>().color = new Color(0.25f, 0.235f, 0.28f);
            var slider = track.AddComponent<Slider>();
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = new Color(1f, 0.42f, 0.12f);
            fill.GetComponent<Image>().raycastTarget = false;
            slider.fillRect = fillRect;
            var handle = CreatePanel(track.transform, Vector2.zero, new Vector2(14f, 26f));
            handle.GetComponent<Image>().color = new Color(1f, 0.56f, 0.3f);
            slider.handleRect = (RectTransform)handle.transform;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = SawTunerValues.Mins[i];
            slider.maxValue = SawTunerValues.Maxs[i];
            sawTunerSliders[i] = slider;
            var input = CreateInput(row.transform, new Vector2(240f, 0f), new Vector2(110f, 34f), 12, _ => { }, TMP_InputField.CharacterValidation.Decimal);
            sawTunerInputs[i] = input;
            slider.onValueChanged.AddListener(value => SetSawTunerValue(stat, Mathf.Round(value * 100f) / 100f));
            input.onEndEdit.AddListener(text =>
            {
                if (float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && !float.IsNaN(value) && !float.IsInfinity(value))
                    SetSawTunerValue(stat, Mathf.Clamp(value, SawTunerValues.Mins[stat], SawTunerValues.Maxs[stat]));
                else RefreshSawTunerValues();
            });
        }
        sawTunerReset = CreateButton(dialog.transform, "RESET CHARACTER", new Vector2(-240f, -320f), new Vector2(280f, 40f));
        sawTunerReset.onClick.AddListener(() =>
        {
            if (SawTunerReadOnly) return;
            for (var i = 0; i < sawTunerInputs.Length; i++) sawTunerDraft.Remove(SawTunerValues.Key(sawTunerCharacters[sawTunerIndex], i));
            RefreshSawTunerValues();
        });
        sawTunerSave = CreateButton(dialog.transform, "APPLY", new Vector2(240f, -320f), new Vector2(280f, 40f));
        sawTunerSave.onClick.AddListener(() =>
        {
            if (SawTunerReadOnly) return;
            plugin.lobbySettings.SawTuner = SawTuner.Normalize(SawTunerValues.Serialize(sawTunerDraft));
            RefreshPendingSettings();
            if (MultiplayerSession.IsHosting) plugin.UpdateHostedLobby();
            sawTunerWindow.SetActive(false);
        });
    }

    private void RefreshSawTuner()
    {
        if (sawTunerWindow == null || !sawTunerWindow.activeSelf) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { sawTunerWindow.SetActive(false); return; }
        if (SawTunerReadOnly && sawTunerGeneration != SawTuner.Generation)
        {
            sawTunerGeneration = SawTuner.Generation;
            sawTunerDraft = new Dictionary<string, float>(SawTuner.Values);
            RefreshSawTunerValues();
        }
        sawTunerSave.interactable = sawTunerReset.interactable = !SawTunerReadOnly;
        foreach (var input in sawTunerInputs) input.interactable = !SawTunerReadOnly;
        foreach (var slider in sawTunerSliders) slider.interactable = !SawTunerReadOnly;
    }

    private void SetSawTunerValue(int stat, float value)
    {
        if (SawTunerReadOnly) return;
        var path = sawTunerCharacters[sawTunerIndex];
        var baseline = SawTuner.DefaultValue(SawTuner.Prefab(path)!, stat);
        var key = SawTunerValues.Key(path, stat);
        if (Mathf.Approximately(value, baseline)) sawTunerDraft.Remove(key);
        else sawTunerDraft[key] = value;
        RefreshSawTunerValues();
    }

    private void RefreshSawTunerValues()
    {
        var path = sawTunerCharacters[sawTunerIndex];
        var body = SawTuner.Prefab(path)!;
        for (var i = 0; i < sawTunerInputs.Length; i++)
        {
            var value = sawTunerDraft.TryGetValue(SawTunerValues.Key(path, i), out var tuned) ? tuned : SawTuner.DefaultValue(body, i);
            sawTunerInputs[i].SetTextWithoutNotify(value.ToString("0.##", CultureInfo.InvariantCulture));
            sawTunerSliders[i].SetValueWithoutNotify(value);
        }
    }

    private void ShowSawTunerCharacter()
    {
        var path = sawTunerCharacters[sawTunerIndex];
        var body = SawTuner.Prefab(path)!;
        var hasPortrait = sawTunerPortraits.TryGetValue(path, out var portrait);
        sawTunerName.text = (hasPortrait ? portrait.Name : body.speciesName).ToUpperInvariant();
        sawTunerCounter.text = (sawTunerIndex + 1) + " / " + sawTunerCharacters.Count;
        sawTunerPreview.sprite = hasPortrait ? portrait.Sprite : CharacterHead(path);
        sawTunerPreview.enabled = sawTunerPreview.sprite != null;
        RefreshSawTunerValues();
        RefreshSawTuner();
    }
}
