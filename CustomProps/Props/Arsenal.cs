using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[Serializable]
internal sealed class ArsenalData
{
    public float radius = 2.5f;
    public string weapons = "All";
    public string ammo = "30;60;15;5";
}

internal sealed class ArsenalPropDefinition : CustomPropDefinition<ArsenalData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/Arsenal";
    public override string DisplayName => "Arsenal";
    public override string Description => "Lets players choose a weapon.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Misc;

    public override Sprite Icon => EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.arsenal.png", 28f,
        new Vector2(0.5f, 0.15f));

    public override CustomPropField[] Fields => fields ??= new[]
    {
        Float("Radius", "Units", value => value.radius, (value, number) => value.radius = number, 0.5f),
        Text("Weapons", "All or comma-separated weapon names", value => value.weapons,
            (value, text) => value.weapons = text),
        Text("Ammo", "Pistol;rifle;heavy;grenades", value => value.ammo, (value, text) => value.ammo = text)
    };

    public override void CreateRuntime(GameObject gameObject, ArsenalData data)
    {
        var renderer = gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = Icon;
        renderer.sortingLayerName = "Background";
        renderer.sortingOrder = 999;
        gameObject.AddComponent<ArsenalRuntime>().Configure(data);
    }
}

internal sealed class ArsenalRuntime : MonoBehaviour
{
    private ArsenalData data;
    internal float Radius => data == null ? 2.5f : Mathf.Max(0.5f, data.radius);

    internal void Configure(ArsenalData value)
    {
        data = value ?? new ArsenalData();
    }

    internal List<WeaponPreset> Weapons()
    {
        var all = new List<WeaponPreset>();
        var seen = new HashSet<int>();
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (preset != null && preset.sprite != null && seen.Add(preset.GetInstanceID())) all.Add(preset);
        all.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
        var filter = data == null ? "All" : (data.weapons ?? "All").Trim();
        if (string.IsNullOrEmpty(filter) ||
            string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase)) return all;
        var allowed = new HashSet<string>(filter.Split(','), StringComparer.OrdinalIgnoreCase);
        all.RemoveAll(value => !allowed.Contains(value.name));
        return all;
    }

    internal int[] AmmoAmounts()
    {
        var result = new int[4];
        var values = (data == null ? "" : data.ammo ?? "").Split(';');
        for (var index = 0; index < result.Length && index < values.Length; index++)
            if (int.TryParse(values[index].Trim(), out var amount))
                result[index] = Mathf.Max(0, amount);
        return result;
    }

    private void Update()
    {
        var body = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
        if (body == null || !body.isAlive) return;
        if (((Vector2)(body.transform.position - transform.position)).sqrMagnitude > Radius * Radius) return;
        ArsenalMenu.NotifyNearby(this);
        if (Input.GetKeyDown(KeyCode.B) && !MultiplayerHud.IsTyping) ArsenalMenu.Open(this);
    }
}

internal sealed class ArsenalMenu : MonoBehaviour
{
    private static ArsenalMenu instance;
    private GameObject menu, content;
    private RectTransform characterPreview;
    private Image weaponPreview;
    private TMP_Text weaponName, weaponInfo;
    private readonly List<Button> tiles = [];
    private ArsenalRuntime arsenal;
    private List<WeaponPreset> weapons = [];
    private WeaponPreset selected;
    private float nearbyUntil;
    private float maxScroll;
    private bool cursorVisible;
    private CursorLockMode cursorLock;
    private readonly Dictionary<SpriteRenderer, Image> characterSprites = [];
    private Vector2 previewCenterOffset;
    private float previewScale;
    internal static bool IsOpen => instance != null && instance.menu != null && instance.menu.activeSelf;

    internal static string Prompt => instance != null && !instance.menu.activeSelf &&
                                     Time.unscaledTime <= instance.nearbyUntil
        ? "PRESS [B] TO OPEN ARSENAL"
        : "";

    internal static void NotifyNearby(ArsenalRuntime value)
    {
        var current = Ensure();
        if (current.menu.activeSelf) return;
        current.arsenal = value;
        current.nearbyUntil = Time.unscaledTime + 0.2f;
    }

    internal static void Open(ArsenalRuntime value)
    {
        var current = Ensure();
        current.arsenal = value;
        current.weapons = value == null ? new List<WeaponPreset>() : value.Weapons();
        if (current.weapons.Count == 0) return;
        current.selected = current.weapons[0];
        current.previewScale = 0f;
        current.RebuildTiles();
        current.menu.SetActive(true);
        current.UpdatePreview();
        current.cursorVisible = Cursor.visible;
        current.cursorLock = Cursor.lockState;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private static ArsenalMenu Ensure()
    {
        if (instance != null) return instance;
        var root = new GameObject("MP Arsenal UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        DontDestroyOnLoad(root);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        if (EventSystem.current == null)
        {
            var eventSystem = new GameObject("MP Arsenal Event System", typeof(EventSystem),
                typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
        }

        instance = root.AddComponent<ArsenalMenu>();
        instance.Build(root.transform);
        return instance;
    }

    private void Build(Transform root)
    {
        menu = Panel(root, Vector2.zero, new Vector2(1280f, 760f), new Color(0.035f, 0.045f, 0.055f, 0.98f));
        var title = Text(menu.transform, "ARSENAL", new Vector2(0f, 325f), new Vector2(1180f, 56f), 36f,
            TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        var listPanel = Panel(menu.transform, new Vector2(-320f, -5f), new Vector2(590f, 550f),
            new Color(0.08f, 0.1f, 0.12f, 1f));
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(listPanel.transform, false);
        Stretch(viewport.GetComponent<RectTransform>(), new Vector2(12f, 12f), new Vector2(-12f, -12f));
        content = new GameObject("Weapons", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        var preview = Panel(menu.transform, new Vector2(320f, -5f), new Vector2(590f, 550f),
            new Color(0.08f, 0.1f, 0.12f, 1f));
        var character = new GameObject("CharacterPreview", typeof(RectTransform), typeof(RectMask2D));
        character.transform.SetParent(preview.transform, false);
        Rect(character.GetComponent<RectTransform>(), new Vector2(0f, 30f), new Vector2(500f, 420f));
        characterPreview = character.GetComponent<RectTransform>();
        weaponPreview = Image(preview.transform, new Vector2(110f, 5f), new Vector2(300f, 190f));
        weaponPreview.color = Color.white;
        weaponPreview.gameObject.SetActive(false);
        weaponName = Text(preview.transform, "", new Vector2(0f, -190f), new Vector2(530f, 42f), 29f,
            TextAlignmentOptions.Center);
        weaponName.fontStyle = FontStyles.Bold;
        weaponInfo = Text(preview.transform, "", new Vector2(0f, -230f), new Vector2(530f, 30f), 17f,
            TextAlignmentOptions.Center);
        var exit = Button(menu.transform, "CLOSE", new Vector2(465f, -325f), new Vector2(220f, 54f));
        exit.onClick.AddListener(CloseAndEquip);
        menu.SetActive(false);
    }

    private void Update()
    {
        if (menu == null) return;
        if (!menu.activeSelf)
        {
            return;
        }

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        RefreshCharacterPreview();
        if (Input.GetKeyDown(KeyCode.Escape)) CloseAndEquip();
        var wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f)
        {
            var rect = content.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0f, Mathf.Clamp(rect.anchoredPosition.y - wheel * 70f, 0f, maxScroll));
        }
    }

    private void RebuildTiles()
    {
        foreach (var tile in tiles)
            if (tile != null)
                Destroy(tile.gameObject);
        tiles.Clear();
        for (var index = 0; index < weapons.Count; index++)
        {
            var weapon = weapons[index];
            var row = index / 3;
            var column = index % 3;
            var tile = Button(content.transform, weapon.name, Vector2.zero, new Vector2(178f, 94f));
            TopRect(tile.GetComponent<RectTransform>(), new Vector2(-190f + column * 190f, -8f - row * 112f));
            var icon = Image(tile.transform, new Vector2(-54f, 0f), new Vector2(52f, 52f));
            icon.sprite = weapon.sprite;
            icon.color = weapon.sprite == null ? Color.clear : Color.white;
            var label = tile.GetComponentInChildren<TextMeshProUGUI>();
            label.rectTransform.anchoredPosition = new Vector2(33f, 0f);
            label.rectTransform.sizeDelta = new Vector2(108f, 70f);
            label.fontSize = 13f;
            label.alignment = TextAlignmentOptions.Left;
            var captured = weapon;
            tile.onClick.AddListener(() =>
            {
                selected = captured;
                EquipSelected();
                UpdatePreview();
            });
            tiles.Add(tile);
        }

        var height = Mathf.Max(526f, ((weapons.Count + 2) / 3) * 112f + 20f);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.sizeDelta = new Vector2(0f, height);
        contentRect.anchoredPosition = Vector2.zero;
        maxScroll = Mathf.Max(0f, height - 526f);
    }

    private void UpdatePreview()
    {
        if (selected == null) return;
        weaponName.text = selected.name;
        weaponInfo.text = "MAG " + selected.magSize + "   •   SLOT " + (selected.slot + 1);
        UpdateCharacterPreview();
    }

    private void UpdateCharacterPreview()
    {
        RefreshCharacterPreview();
    }

    private void RefreshCharacterPreview()
    {
        var body = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
        if (body == null || characterPreview == null) return;
        var sources = CollectPreviewSprites(body);
        if (sources.Count == 0) return;
        sources.Sort((left, right) => PreviewOrder(body, left).CompareTo(PreviewOrder(body, right)));
        foreach (var image in characterSprites.Values) image.gameObject.SetActive(false);
        if (previewScale <= 0f)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var source in sources)
            {
                if (source.sprite == null || !source.enabled || IsTailRenderer(body, source) ||
                    source.GetComponentInParent<WeaponScript>() != null) continue;
                min = Vector2.Min(min, source.bounds.min);
                max = Vector2.Max(max, source.bounds.max);
            }

            if (min.x == float.MaxValue) return;
            previewScale = Mathf.Min(characterPreview.rect.width / Mathf.Max(0.01f, max.x - min.x),
                characterPreview.rect.height / Mathf.Max(0.01f, max.y - min.y)) * 0.9f;
            previewCenterOffset = (min + max) * 0.5f - (Vector2)body.transform.position;
        }

        var center = (Vector2)body.transform.position + previewCenterOffset;
        var live = new HashSet<SpriteRenderer>(sources);
        var stale = new List<SpriteRenderer>();
        foreach (var pair in characterSprites)
            if (!live.Contains(pair.Key))
            {
                Destroy(pair.Value.gameObject);
                stale.Add(pair.Key);
            }

        foreach (var source in stale) characterSprites.Remove(source);
        foreach (var source in sources)
        {
            if (source.sprite == null || !source.enabled) continue;
            if (!characterSprites.TryGetValue(source, out var image) || image == null)
            {
                var item = new GameObject("Sprite", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                item.transform.SetParent(characterPreview, false);
                image = item.GetComponent<Image>();
                image.preserveAspect = true;
                characterSprites[source] = image;
            }

            image.gameObject.SetActive(true);
            image.transform.SetAsLastSibling();
            var rect = image.rectTransform;
            image.sprite = source.sprite;
            image.color = source.color;
            rect.anchoredPosition = ((Vector2)source.bounds.center - center) * previewScale;
            rect.localRotation = Quaternion.Euler(0f, 0f, source.transform.eulerAngles.z);
            rect.sizeDelta = Vector2.Scale(source.sprite.bounds.size, Abs(source.transform.lossyScale)) * previewScale;
            var transformScale = source.transform.lossyScale;
            rect.localScale = new Vector3(Sign(transformScale.x) * (source.flipX ? -1f : 1f),
                Sign(transformScale.y) * (source.flipY ? -1f : 1f), 1f);
        }
    }

    private static List<SpriteRenderer> CollectPreviewSprites(BodyScript body)
    {
        var result = new List<SpriteRenderer>();
        var known = new HashSet<SpriteRenderer>();

        void Add(Transform root)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                if (renderer != null && known.Add(renderer))
                    result.Add(renderer);
        }

        Add(body.transform);
        foreach (var limb in body.limbs)
            if (limb != null)
                Add(limb.transform);
        Add(body.gunTransform);
        if (body.tails != null)
            foreach (var tail in body.tails)
                Add(tail);
        return result;
    }

    private static Vector2 Abs(Vector3 value) => new(Mathf.Abs(value.x), Mathf.Abs(value.y));
    private static float Sign(float value) => value < 0f ? -1f : 1f;

    private static bool IsTailRenderer(BodyScript body, SpriteRenderer renderer)
    {
        if (body.tails == null) return false;
        foreach (var tail in body.tails)
            if (tail != null && renderer.transform.IsChildOf(tail))
                return true;
        return false;
    }

    private static int PreviewOrder(BodyScript body, SpriteRenderer renderer)
    {
        return (IsTailRenderer(body, renderer) ? -10000 : 0) + renderer.sortingOrder;
    }

    private void CloseAndEquip()
    {
        menu.SetActive(false);
        var body = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
        if (body != null)
        {
            var amounts = arsenal == null ? new[] { 60, 60, 60, 60 } : arsenal.AmmoAmounts();
            for (var index = 0; index < amounts.Length; index++)
            {
                var ammoType = LobbyAmmoRules.GetAmmoType(index);
                if (ammoType >= 0 && ammoType < body.ammoAmount.Count)
                    body.ammoAmount[ammoType] += amounts[index];
            }
            PlayerScript.player.BodyAmmoChanged();
        }

        foreach (var image in characterSprites.Values) Destroy(image.gameObject);
        characterSprites.Clear();
        previewScale = 0f;
        Cursor.visible = cursorVisible;
        Cursor.lockState = cursorLock;
    }

    private void EquipSelected()
    {
        if (selected == null) return;
        var body = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
        if (body == null || !body.isAlive) return;
        if (body.weapons.Count > selected.slot && body.weapons[selected.slot] == selected)
        {
            body.weapons[selected.slot] = null;
            body.weaponAmmos[selected.slot] = 0;
            if (body.currentWeapon == selected.slot) body.ChangeToUnarmed();
            return;
        }

        while (body.weapons.Count <= selected.slot) body.weapons.Add(null);
        while (body.weaponAmmos.Count <= selected.slot) body.weaponAmmos.Add(0);
        for (var index = 0; index < body.weapons.Count; index++)
            if (index != selected.slot && body.weapons[index] != null && body.weapons[index].slot == selected.slot)
            {
                body.weapons[index] = null;
                if (index < body.weaponAmmos.Count) body.weaponAmmos[index] = 0;
            }

        body.weapons[selected.slot] = selected;
        body.weaponAmmos[selected.slot] = selected.magSize;
        body.ChangeWeapon(selected.slot);
    }

    private static GameObject Panel(Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Rect(go.GetComponent<RectTransform>(), position, size);
        go.GetComponent<Image>().color = color;
        return go;
    }

    private static Image Image(Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Rect(go.GetComponent<RectTransform>(), position, size);
        var image = go.GetComponent<Image>();
        image.preserveAspect = true;
        return image;
    }

    private static Button Button(Transform parent, string label, Vector2 position, Vector2 size)
    {
        var go = Panel(parent, position, size, new Color(0.14f, 0.18f, 0.2f, 1f));
        var button = go.AddComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.8f, 0.32f);
        colors.pressedColor = new Color(0.7f, 0.52f, 0.15f);
        button.colors = colors;
        var text = Text(go.transform, label, Vector2.zero, size - new Vector2(14f, 12f), 17f,
            TextAlignmentOptions.Center);
        text.fontStyle = FontStyles.Bold;
        return button;
    }

    private static TMP_Text Text(Transform parent, string value, Vector2 position, Vector2 size, float fontSize,
        TextAlignmentOptions alignment)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Rect(go.GetComponent<RectTransform>(), position, size);
        var text = go.GetComponent<TextMeshProUGUI>();
        TMP_Text source = PlayerScript.player == null ? null : PlayerScript.player.ammoText;
        if (source == null)
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TMP_Text>())
                if (candidate != null && candidate.font != null)
                {
                    source = candidate;
                    break;
                }

        if (source != null)
        {
            text.font = source.font;
            text.fontSharedMaterial = source.fontSharedMaterial;
            text.color = source.color;
        }

        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        return text;
    }

    private static void Rect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void TopRect(RectTransform rect, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }
}
