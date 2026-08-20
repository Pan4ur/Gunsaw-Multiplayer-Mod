using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

internal sealed class CustomLevelBrowserUi
{
    private const string CoversUrl = "https://raw.githubusercontent.com/jimmyking9999999/gunsaw-level-editor-plus/main/Images/";
    private readonly GunsawMultiplayerPlugin plugin;
    private readonly TMP_Text template;
    private readonly Button buttonTemplate;
    private readonly GameObject panel;
    private readonly RectTransform panelRect;
    private readonly TMP_InputField search;
    private readonly TMP_Text sortText;
    private readonly TMP_Text stateText;
    private readonly Transform rows;
    private readonly Sprite playIcon;
    private readonly Dictionary<string, Sprite> covers = new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> coverFiles = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    private static CatalogEntry[] cachedLevels = new CatalogEntry[0];
    private CatalogEntry[] levels = new CatalogEntry[0];
    private SortMode sortMode;
    private bool open;
    private bool loading;
    private string renderedSearch = "\u0000";
    private SortMode renderedSort = (SortMode)(-1);

    private enum SortMode { Date, Size, Difficulty, Length, Type }

    private sealed class CatalogEntry
    {
        public string name;
        public string author;
        public string code;
        public string length;
        public string difficulty;
        public string type;
        public string info;
        public string date;
    }

    internal static void CacheCatalog(string source)
    {
        var parsed = ParseCatalog(source);
        if (parsed.Length == 0) throw new System.InvalidOperationException("The catalog contains no levels.");
        cachedLevels = parsed;
    }

    internal CustomLevelBrowserUi(GunsawMultiplayerPlugin owner, Transform parent, TMP_Text textTemplate, Button sourceButton)
    {
        plugin = owner;
        template = textTemplate;
        buttonTemplate = sourceButton;
        playIcon = EmbeddedSpriteLoader.Load("GunsawMultiplayer.Assets.play.png", 100f, new Vector2(0.5f, 0.5f));
        panel = CreatePanel(parent, new Vector2(100f, 0f), new Vector2(600f, 920f));
        panel.transform.SetSiblingIndex(Mathf.Max(0, parent.childCount - 2));
        panel.name = "Custom Level Browser";
        panelRect = panel.GetComponent<RectTransform>();
        CreateText(panel.transform, "CUSTOM LEVEL BROWSER", new Vector2(0f, 410f), new Vector2(540f, 38f), 23, TextAlignmentOptions.Center, FontStyles.UpperCase);
        var close = CreateButton(panel.transform, "CLOSE", new Vector2(240f, 410f), new Vector2(100f, 38f));
        close.onClick.AddListener(() => SetOpen(false));
        search = CreateInput(panel.transform, new Vector2(-105f, 360f), new Vector2(330f, 40f), "Search levels");
        search.onValueChanged.AddListener(_ => Rebuild());
        var sort = CreateButton(panel.transform, "SORT: DATE", new Vector2(169f, 360f), new Vector2(202f, 40f));
        sortText = sort.GetComponentInChildren<TMP_Text>();
        sort.onClick.AddListener(NextSort);
        stateText = CreateText(panel.transform, "Open the browser to load levels.", new Vector2(0f, 318f), new Vector2(540f, 28f), 13, TextAlignmentOptions.Center);
        rows = CreateScroll(panel.transform, new Vector2(0f, -36f), new Vector2(550f, 670f));
        panel.SetActive(false);
    }

    internal void Toggle() => SetOpen(!open);

    internal bool IsOpen => open;

    internal void SetOpen(bool value)
    {
        open = value;
        if (value)
        {
            panel.SetActive(true);
            if (!loading && levels.Length == 0) plugin.StartCoroutine(LoadCatalog());
        }
    }

    internal void Tick()
    {
        if (!panel.activeSelf) return;
        var target = open ? 650f : 100f;
        panelRect.anchoredPosition = new Vector2(Mathf.Lerp(panelRect.anchoredPosition.x, target, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime)), 0f);
        if (!open && Mathf.Abs(panelRect.anchoredPosition.x - target) < 1f) panel.SetActive(false);
    }

    private IEnumerator LoadCatalog()
    {
        loading = true;
        stateText.text = "Loading custom levels...";
        while (!plugin.customLevelCatalogReady && string.IsNullOrEmpty(plugin.customLevelCatalogError)) yield return null;
        if (!plugin.customLevelCatalogReady)
        {
            stateText.text = "Could not load levels: " + plugin.customLevelCatalogError;
            loading = false;
            yield break;
        }
        levels = cachedLevels;
        yield return LoadCoverManifest();
        stateText.text = levels.Length == 0 ? "No levels found." : levels.Length + " levels loaded.";
        Rebuild(true);
        loading = false;
    }

    private static CatalogEntry[] ParseCatalog(string source)
    {
        var matches = Regex.Matches(source ?? "", "\\{\\s*\\\"name\\\"\\s*:\\s*\\\"(?<name>(?:\\\\.|[^\\\"])*)\\\"\\s*,\\s*\\\"author\\\"\\s*:\\s*\\\"(?<author>(?:\\\\.|[^\\\"])*)\\\"\\s*,\\s*\\\"code\\\"\\s*:\\s*\\\"(?<code>(?:\\\\.|[^\\\"])*)\\\"\\s*,\\s*\\\"length\\\"\\s*:\\s*\\\"(?<length>(?:\\\\.|[^\\\"])*)\\\"\\s*,\\s*\\\"difficulty\\\"\\s*:\\s*\\\"(?<difficulty>(?:\\\\.|[^\\\"])*)\\\"\\s*,\\s*\\\"type\\\"\\s*:\\s*\\\"(?<type>(?:\\\\.|[^\\\"])*)\\\"\\s*,\\s*\\\"info\\\"\\s*:\\s*\\\"(?<info>(?:\\\\.|[^\\\"])*)\\\"\\s*,\\s*\\\"date\\\"\\s*:\\s*\\\"(?<date>(?:\\\\.|[^\\\"])*)\\\"", RegexOptions.Singleline);
        var result = new List<CatalogEntry>(matches.Count);
        foreach (Match match in matches)
            result.Add(new CatalogEntry
            {
                name = Decode(match, "name"), author = Decode(match, "author"), code = Decode(match, "code"),
                length = Decode(match, "length"), difficulty = Decode(match, "difficulty"), type = Decode(match, "type"),
                info = Decode(match, "info"), date = Decode(match, "date")
            });
        return result.ToArray();
    }

    private static string Decode(Match match, string name) => Regex.Unescape(match.Groups[name].Value);

    private IEnumerator LoadCoverManifest()
    {
        using (var request = UnityWebRequest.Get("https://api.github.com/repos/jimmyking9999999/gunsaw-level-editor-plus/git/trees/main?recursive=1"))
        {
            yield return request.SendWebRequest();
            if (request.isNetworkError || request.isHttpError) yield break;
            var matches = Regex.Matches(request.downloadHandler.text ?? "", "\\\"path\\\"\\s*:\\s*\\\"Images/(?<name>(?:\\\\.|[^\\\"])*)\\.png\\\"", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var name = Regex.Unescape(match.Groups["name"].Value);
                var key = NormalizeName(name);
                if (!string.IsNullOrEmpty(key) && !coverFiles.ContainsKey(key)) coverFiles.Add(key, name);
            }
        }
    }

    private string FindCoverName(string levelName)
    {
        var key = NormalizeName(levelName);
        string fileName;
        if (coverFiles.TryGetValue(key, out fileName)) return fileName;
        foreach (var pair in coverFiles)
            if (pair.Key.Contains(key) || key.Contains(pair.Key)) return pair.Value;
        return levelName;
    }

    private static string NormalizeName(string value)
    {
        var source = Regex.Replace(value ?? "", "\\s*\\([^)]*\\)", "");
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
        return builder.ToString();
    }

    private void NextSort()
    {
        sortMode = (SortMode)(((int)sortMode + 1) % 5);
        sortText.text = "SORT: " + sortMode.ToString().ToUpperInvariant();
        Rebuild();
    }

    private void Rebuild(bool force = false)
    {
        if (rows == null) return;
        var query = search == null ? "" : search.text.Trim();
        if (!force && query == renderedSearch && sortMode == renderedSort) return;
        renderedSearch = query;
        renderedSort = sortMode;
        for (var index = rows.childCount - 1; index >= 0; index--) UnityEngine.Object.Destroy(rows.GetChild(index).gameObject);
        var filtered = new List<CatalogEntry>();
        foreach (var entry in levels)
            if (entry != null && Matches(entry, query)) filtered.Add(entry);
        filtered.Sort(Compare);
        foreach (var entry in filtered) CreateLevelCard(entry);
        if (!loading) stateText.text = filtered.Count + " of " + levels.Length + " levels";
    }

    private bool Matches(CatalogEntry entry, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        return Contains(entry.name, query) || Contains(entry.author, query) || Contains(entry.type, query) || Contains(entry.difficulty, query) || Contains(entry.length, query);
    }

    private static bool Contains(string value, string query) => (value ?? "").IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;

    private int Compare(CatalogEntry left, CatalogEntry right)
    {
        var comparison = sortMode == SortMode.Size ? CodeSize(right).CompareTo(CodeSize(left)) :
            sortMode == SortMode.Date ? string.Compare(right.date, left.date, System.StringComparison.OrdinalIgnoreCase) :
            string.Compare(SortValue(left), SortValue(right), System.StringComparison.OrdinalIgnoreCase);
        return comparison != 0 ? comparison : string.Compare(left.name, right.name, System.StringComparison.OrdinalIgnoreCase);
    }

    private string SortValue(CatalogEntry entry)
    {
        if (sortMode == SortMode.Difficulty) return entry.difficulty ?? "";
        if (sortMode == SortMode.Length) return entry.length ?? "";
        return entry.type ?? "";
    }

    private static int CodeSize(CatalogEntry entry) => Encoding.UTF8.GetByteCount(entry.code ?? "");

    private void CreateLevelCard(CatalogEntry entry)
    {
        var card = new GameObject("Level", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Outline));
        card.transform.SetParent(rows, false);
        card.GetComponent<LayoutElement>().preferredHeight = 118f;
        var image = card.GetComponent<Image>();
        image.color = CoverColor(entry.type);
        var outline = card.GetComponent<Outline>();
        outline.effectColor = new Color(0.78f, 0.78f, 0.78f, 0.7f);
        outline.effectDistance = new Vector2(1f, -1f);
        var shade = new GameObject("Shade", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); shade.transform.SetParent(card.transform, false); var shadeRect = shade.GetComponent<RectTransform>(); shadeRect.anchorMin = Vector2.zero; shadeRect.anchorMax = Vector2.one; shadeRect.offsetMin = Vector2.zero; shadeRect.offsetMax = Vector2.zero; shade.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.48f);
        var title = CreateText(card.transform, entry.name ?? "Untitled", new Vector2(-50f, 35f), new Vector2(390f, 30f), 18, TextAlignmentOptions.Left, FontStyles.Bold);
        title.margin = new Vector4(12f, 0f, 0f, 0f);
        var author = CreateText(card.transform, "BY " + (entry.author ?? "Unknown"), new Vector2(-50f, 10f), new Vector2(390f, 22f), 13, TextAlignmentOptions.Left);
        author.margin = new Vector4(12f, 0f, 0f, 0f);
        var data = (entry.difficulty ?? "?").ToUpperInvariant() + "  |  " + (entry.length ?? "?").ToUpperInvariant() + "  |  " + (entry.type ?? "?").ToUpperInvariant() + "\n" + (entry.date ?? "Unknown date") + "  |  " + FormatSize(CodeSize(entry));
        var details = CreateText(card.transform, data, new Vector2(-50f, -32f), new Vector2(390f, 42f), 12, TextAlignmentOptions.Left);
        details.margin = new Vector4(12f, 0f, 0f, 0f);
        details.enableWordWrapping = true;
        var rank = CustomLevelProgress.Rank(entry.code);
        if (!string.IsNullOrEmpty(rank))
        {
            var rankText = CreateText(card.transform, rank, new Vector2(145f, 0f), new Vector2(42f, 50f), 42, TextAlignmentOptions.Center, FontStyles.Bold);
            rankText.color = CustomLevelProgress.RankColor(entry.code);
        }
        var play = CreatePlayButton(card.transform, new Vector2(220f, 0f), new Vector2(50f, 50f));
        play.onClick.AddListener(() => plugin.StartCatalogCustomLevel(entry.code, entry.name ?? "Untitled"));
        plugin.StartCoroutine(LoadCover(entry.name, image));
    }

    private IEnumerator LoadCover(string levelName, Image image)
    {
        if (image == null || string.IsNullOrWhiteSpace(levelName)) yield break;
        Sprite cover;
        if (covers.TryGetValue(levelName, out cover))
        {
            image.sprite = cover;
            image.color = Color.white;
            yield break;
        }
        var fileName = FindCoverName(levelName);
        using (var request = UnityWebRequest.Get(CoversUrl + Uri.EscapeDataString(fileName) + ".png"))
        {
            yield return request.SendWebRequest();
            if (request.isNetworkError || request.isHttpError) yield break;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, request.downloadHandler.data)) { UnityEngine.Object.Destroy(texture); yield break; }
            cover = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            covers[levelName] = cover;
            if (image != null)
            {
                image.sprite = cover;
                image.color = Color.white;
            }
        }
    }

    private static string FormatSize(int bytes) => bytes < 1024 ? bytes + " B" : (bytes / 1024f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KiB";

    private static Color CoverColor(string type)
    {
        var value = (type ?? "").ToLowerInvariant();
        if (value.Contains("elimination")) return new Color(0.36f, 0.12f, 0.12f, 0.96f);
        if (value.Contains("parkour")) return new Color(0.12f, 0.28f, 0.36f, 0.96f);
        if (value.Contains("hybrid")) return new Color(0.28f, 0.2f, 0.36f, 0.96f);
        return new Color(0.18f, 0.28f, 0.18f, 0.96f);
    }

    private GameObject CreatePanel(Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.98f);
        var outline = go.GetComponent<Outline>(); outline.effectColor = new Color(0.58f, 0.58f, 0.58f, 0.92f); outline.effectDistance = new Vector2(1f, -1f);
        Rect(go.GetComponent<RectTransform>(), position, size);
        return go;
    }

    private Button CreateButton(Transform parent, string label, Vector2 position, Vector2 size)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        var source = buttonTemplate.targetGraphic as Image;
        image.sprite = source == null ? null : source.sprite; image.material = source == null ? null : source.material; image.color = source == null ? new Color(0.16f, 0.2f, 0.2f, 1f) : source.color;
        var button = go.GetComponent<Button>(); button.targetGraphic = image; button.colors = buttonTemplate.colors;
        Rect(go.GetComponent<RectTransform>(), position, size);
        CreateText(go.transform, label, Vector2.zero, size, label == "▶" ? 28 : 14, TextAlignmentOptions.Center).raycastTarget = false;
        return button;
    }

    private Button CreatePlayButton(Transform parent, Vector2 position, Vector2 size)
    {
        var button = CreateButton(parent, "", position, size);
        var background = button.GetComponent<Image>();
        background.sprite = null;
        background.material = null;
        background.color = new Color(0.1f, 0.45f, 0.1f, 1f);
        if (playIcon == null) return button;
        var icon = new GameObject("Play Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        icon.transform.SetParent(button.transform, false);
        Rect(icon.GetComponent<RectTransform>(), Vector2.zero, size);
        var iconImage = icon.GetComponent<Image>();
        iconImage.sprite = playIcon;
        iconImage.color = Color.white;
        iconImage.raycastTarget = false;
        return button;
    }

    private TMP_InputField CreateInput(Transform parent, Vector2 position, Vector2 size, string placeholder)
    {
        var go = new GameObject("Search", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(TMP_InputField));
        go.transform.SetParent(parent, false); Rect(go.GetComponent<RectTransform>(), position, size);
        var image = go.GetComponent<Image>(); image.color = new Color(0.17f, 0.17f, 0.17f, 0.7f);
        var outline = go.GetComponent<Outline>(); outline.effectColor = new Color(0.58f, 0.58f, 0.58f, 0.95f); outline.effectDistance = new Vector2(1f, -1f);
        var field = go.GetComponent<TMP_InputField>(); field.targetGraphic = image; field.characterLimit = 80;
        var text = CreateText(go.transform, "", Vector2.zero, new Vector2(size.x - 16f, size.y), 15, TextAlignmentOptions.Left); text.margin = new Vector4(8f, 0f, 8f, 0f); field.textViewport = text.rectTransform; field.textComponent = text;
        var hint = CreateText(go.transform, placeholder, Vector2.zero, new Vector2(size.x - 16f, size.y), 15, TextAlignmentOptions.Left); hint.margin = new Vector4(8f, 0f, 8f, 0f); hint.color = new Color(0.7f, 0.7f, 0.7f, 0.55f); field.placeholder = hint;
        return field;
    }

    private Transform CreateScroll(Transform parent, Vector2 position, Vector2 size)
    {
        var view = new GameObject("Level Scroll", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect)); view.transform.SetParent(parent, false); Rect(view.GetComponent<RectTransform>(), position, size);
        view.GetComponent<Image>().color = new Color(0.13f, 0.13f, 0.13f, 0.5f); view.GetComponent<Mask>().showMaskGraphic = true;
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)); content.transform.SetParent(view.transform, false);
        var contentRect = content.GetComponent<RectTransform>(); contentRect.anchorMin = new Vector2(0f, 1f); contentRect.anchorMax = new Vector2(1f, 1f); contentRect.pivot = new Vector2(0.5f, 1f); contentRect.anchoredPosition = new Vector2(0f, -5f); contentRect.sizeDelta = new Vector2(-18f, 0f);
        var layout = content.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(7, 7, 7, 7); layout.spacing = 7; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = view.GetComponent<ScrollRect>(); scroll.viewport = view.GetComponent<RectTransform>(); scroll.content = contentRect; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28f;
        var barObject = new GameObject("Scrollbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar)); barObject.transform.SetParent(view.transform, false);
        var barRect = barObject.GetComponent<RectTransform>(); barRect.anchorMin = new Vector2(1f, 0f); barRect.anchorMax = new Vector2(1f, 1f); barRect.pivot = new Vector2(1f, 0.5f); barRect.anchoredPosition = new Vector2(-4f, 0f); barRect.sizeDelta = new Vector2(11f, -10f); barObject.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); handle.transform.SetParent(barObject.transform, false); var handleRect = handle.GetComponent<RectTransform>(); handleRect.anchorMin = Vector2.zero; handleRect.anchorMax = Vector2.one; handleRect.offsetMin = new Vector2(2f, 2f); handleRect.offsetMax = new Vector2(-2f, -2f); handle.GetComponent<Image>().color = new Color(0.65f, 0.65f, 0.65f, 0.95f);
        var bar = barObject.GetComponent<Scrollbar>(); bar.targetGraphic = handle.GetComponent<Image>(); bar.handleRect = handleRect; bar.direction = Scrollbar.Direction.BottomToTop; scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        return content.transform;
    }

    private TMP_Text CreateText(Transform parent, string value, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false); Rect(go.GetComponent<RectTransform>(), position, size);
        var text = go.GetComponent<TextMeshProUGUI>(); text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial; text.color = template.color; text.fontSize = fontSize; text.alignment = alignment; text.fontStyle = style; text.text = value; text.enableWordWrapping = false; return text;
    }

    private static void Rect(RectTransform rect, Vector2 position, Vector2 size) { rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.pivot = new Vector2(0.5f, 0.5f); rect.anchoredPosition = position; rect.sizeDelta = size; }
}
