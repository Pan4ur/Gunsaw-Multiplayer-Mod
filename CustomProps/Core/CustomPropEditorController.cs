using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed class CustomPropEditorController : MonoBehaviour
{
    private const string RootPath = "MP/CustomProp";
    private static readonly FieldInfo loadStringField = AccessTools.Field(typeof(LevelEditor), "loadString");
    private static readonly FieldInfo selectedField = AccessTools.Field(typeof(LevelEditor), "currentlySelected");
    private static readonly List<CustomPropInstance> runtimeInstances = new List<CustomPropInstance>();
    private static string runtimeSourceJson = string.Empty;

    private readonly List<CustomPropInstance> instances = new List<CustomPropInstance>();
    private readonly List<CustomPropMarker> markers = new List<CustomPropMarker>();

    private LevelEditor editor;
    private GameObject ghost;
    private ICustomPropDefinition placingDefinition;
    private CustomPropMarker shownMarker;
    private bool listenersInstalled;
    private bool nativeInspectorCaptured;
    private readonly List<NativeInputState> nativeInputs = new List<NativeInputState>();
    private readonly List<NativeLabelState> nativeLabels = new List<NativeLabelState>();

    internal static CustomPropEditorController Ensure(LevelEditor value)
    {
        CustomPropBootstrap.EnsureRegistered();
        if (value == null) return null;
        var controller = value.GetComponent<CustomPropEditorController>();
        if (controller == null) controller = value.gameObject.AddComponent<CustomPropEditorController>();
        controller.editor = value;
        return controller;
    }

    internal static void ReadLevel(LevelEditor value)
    {
        var controller = Ensure(value);
        if (controller == null) return;
        var json = loadStringField == null ? string.Empty : loadStringField.GetValue(value) as string;
        controller.Load(json ?? string.Empty);
        if (loadStringField != null) loadStringField.SetValue(value, RemoveCustomParts(json ?? string.Empty));
    }

    internal static void FinishLevelLoad(LevelEditor value)
    {
        var controller = Ensure(value);
        if (controller != null) controller.RebuildMarkers();
    }

    internal static string WriteLevel(LevelEditor value, string vanilla)
    {
        var controller = Ensure(value);
        return controller == null ? vanilla : controller.Append(vanilla);
    }

    internal static void PrepareRuntime(LevelLoader loader)
    {
        CustomPropBootstrap.EnsureRegistered();
        LogicTickService.ResetRuntime();
        runtimeInstances.Clear();
        var json = SceneLoader.main == null
            ? (loader == null ? string.Empty : loader.levelCode)
            : SceneLoader.main.levelEditString;
        runtimeSourceJson = json ?? string.Empty;
        ToggleableLampSystem.PrepareRuntime(runtimeSourceJson);
        runtimeInstances.AddRange(ReadInstances(runtimeSourceJson));
        var cleaned = RemoveCustomParts(runtimeSourceJson);
        if (SceneLoader.main != null) SceneLoader.main.levelEditString = cleaned;
        if (loader != null) loader.levelCode = cleaned;
    }

    internal static void CreateRuntime()
    {
        foreach (var instance in runtimeInstances)
        {
            if (instance == null) continue;
            ICustomPropDefinition definition;
            if (!CustomPropRegistry.TryGet(instance.TypeId, out definition))
            {
                Debug.LogWarning("Unknown custom prop type: " + instance.TypeId);
                continue;
            }

            var gameObject = new GameObject("Custom Prop: " + definition.DisplayName + " " + instance.Uid);
            gameObject.tag = "Activateable";
            gameObject.transform.position = instance.Position;
            gameObject.transform.rotation = Quaternion.Euler(0f, 0f, instance.Rotation);
            definition.CreateRuntime(gameObject, instance.Data);
        }

        if (SceneLoader.main != null && !string.IsNullOrEmpty(runtimeSourceJson))
            SceneLoader.main.levelEditString = runtimeSourceJson;
    }

    private void Start()
    {
        if (editor == null) editor = GetComponent<LevelEditor>();
        if (editor == null || editor.spawnButtonPrefab == null) return;
        InstallFieldListeners();
        CaptureNativeInspector();
        CreateButtons();
    }

    private void Load(string json)
    {
        instances.Clear();
        instances.AddRange(ReadInstances(json));
    }

    private string Append(string vanilla)
    {
        SyncInstances();
        Level level;
        try { level = JsonUtility.FromJson<Level>(vanilla); }
        catch { return vanilla; }
        if (level == null) return vanilla;

        var parts = new List<LevelPart>();
        if (level.parts != null)
        {
            foreach (var part in level.parts)
                if (part != null && part.path != RootPath) parts.Add(part);
        }

        foreach (var instance in instances)
            parts.Add(ToPart(instance));

        level.parts = parts.ToArray();
        return JsonUtility.ToJson(level);
    }

    private static List<CustomPropInstance> ReadInstances(string json)
    {
        var result = new List<CustomPropInstance>();
        Level level;
        try { level = JsonUtility.FromJson<Level>(json); }
        catch { return result; }
        if (level == null || level.parts == null) return result;

        foreach (var part in level.parts)
        {
            if (part == null) continue;
            if (part.path != RootPath || string.IsNullOrEmpty(part.team)) continue;
            CustomPropPayload payload;
            try { payload = JsonUtility.FromJson<CustomPropPayload>(part.team); }
            catch { continue; }
            if (payload == null || string.IsNullOrEmpty(payload.type)) continue;

            ICustomPropDefinition definition;
            object data = payload.data;
            if (CustomPropRegistry.TryGet(payload.type, out definition))
            {
                var serializedData = payload.data;
                data = definition.DeserializeData(serializedData);
            }

            result.Add(new CustomPropInstance
            {
                Uid = string.IsNullOrEmpty(payload.uid) ? Guid.NewGuid().ToString("N") : payload.uid,
                TypeId = payload.type,
                Data = data,
                Position = part.pos,
                Rotation = part.rot
            });
        }

        return result;
    }

    private static string RemoveCustomParts(string json)
    {
        Level level;
        try { level = JsonUtility.FromJson<Level>(json); }
        catch { return json; }
        if (level == null || level.parts == null) return json;

        var parts = new List<LevelPart>();
        foreach (var part in level.parts)
            if (part != null && part.path != RootPath) parts.Add(part);
        level.parts = parts.ToArray();
        return JsonUtility.ToJson(level);
    }

    private static LevelPart ToPart(CustomPropInstance instance)
    {
        ICustomPropDefinition definition;
        string serializedData;
        if (CustomPropRegistry.TryGet(instance.TypeId, out definition))
            serializedData = definition.SerializeData(instance.Data);
        else
            serializedData = instance.Data as string ?? string.Empty;

        var payload = new CustomPropPayload
        {
            version = 1,
            uid = instance.Uid,
            type = instance.TypeId,
            data = serializedData
        };

        var part = new LevelPart(instance.Position, instance.Rotation, RootPath);
        part.team = JsonUtility.ToJson(payload);
        return part;
    }

    private void RebuildMarkers()
    {
        foreach (var marker in markers)
            if (marker != null) Destroy(marker.gameObject);
        markers.Clear();
        foreach (var instance in instances) CreateMarker(instance);
    }

    private CustomPropMarker CreateMarker(CustomPropInstance instance)
    {
        ICustomPropDefinition definition;
        CustomPropRegistry.TryGet(instance.TypeId, out definition);

        var gameObject = new GameObject(definition == null ? "Unknown Custom Prop" : definition.DisplayName);
        gameObject.transform.SetParent(editor == null ? null : editor.transform);
        gameObject.transform.position = instance.Position;
        gameObject.transform.rotation = Quaternion.Euler(0f, 0f, instance.Rotation);

        var renderer = gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = definition == null ? null : definition.Icon;
        renderer.sortingLayerName = "Foreground";
        renderer.sortingOrder = 250;

        var collider = gameObject.AddComponent<CircleCollider2D>();
        collider.radius = 0.7f;

        var levelPart = gameObject.AddComponent<LevelPartGame>();
        levelPart.part = ToPart(instance);
        levelPart.showId = true;
        levelPart.showActiveId = true;
        levelPart.showTeam = true;
        levelPart.showForce = true;
        levelPart.showSize = true;
        levelPart.fullName = definition == null ? "Unknown Custom Prop" : definition.DisplayName;
        levelPart.description = definition == null
            ? "This custom prop type is not installed: " + instance.TypeId
            : definition.Description;

        var marker = gameObject.AddComponent<CustomPropMarker>();
        marker.Instance = instance;
        marker.Definition = definition;
        markers.Add(marker);
        return marker;
    }

    private void SyncInstances()
    {
        instances.Clear();
        for (var index = markers.Count - 1; index >= 0; index--)
        {
            var marker = markers[index];
            if (marker == null)
            {
                markers.RemoveAt(index);
                continue;
            }

            marker.Instance.Position = marker.transform.position;
            marker.Instance.Rotation = marker.transform.eulerAngles.z;
            marker.GetComponent<LevelPartGame>().part = ToPart(marker.Instance);
            instances.Add(marker.Instance);
        }
    }

    private void CreateButtons()
    {
        var previousByCategory = new Dictionary<CustomPropCategory, RectTransform>();

        foreach (var definition in CustomPropRegistry.All)
        {
            var captured = definition;
            var category = captured.EditorCategory;
            var menu = CategoryMenu(category);
            if (menu == null) continue;
            RectTransform previous;
            if (!previousByCategory.TryGetValue(category, out previous))
                previous = LastSpawnButton(menu);

            var buttonObject = Instantiate(editor.spawnButtonPrefab, menu.transform);
            var rect = buttonObject.GetComponent<RectTransform>();
            if (rect != null && previous != null)
            {
                rect.anchoredPosition = Mathf.Abs(previous.anchoredPosition.y) < 1f
                    ? previous.anchoredPosition + Vector2.down * 102f
                    : new Vector2(previous.anchoredPosition.x + 121f, 0f);
                previous = rect;
            }
            previousByCategory[category] = previous;

            var label = buttonObject.GetComponent<TextMeshProUGUI>();
            if (label != null) label.text = captured.DisplayName;
            var image = buttonObject.transform.childCount > 1
                ? buttonObject.transform.GetChild(1).GetComponent<Image>()
                : null;
            if (image != null)
            {
                image.sprite = captured.Icon;
                image.preserveAspect = true;
            }

            var click = buttonObject.GetComponent<Button>();
            if (click != null)
            {
                click.onClick.RemoveAllListeners();
                click.onClick.AddListener(() => BeginPlacement(captured));
            }

            var icon = buttonObject.GetComponent<SpawnIcon>();
            if (icon != null) icon.descrption = captured.Description;
        }
    }

    private GameObject CategoryMenu(CustomPropCategory category)
    {
        switch (category)
        {
            case CustomPropCategory.Basic: return editor.basicMenu;
            case CustomPropCategory.Obstacle: return editor.obstacleMenu;
            case CustomPropCategory.Enemy: return editor.enemyMenu;
            case CustomPropCategory.Decor: return editor.decorMenu;
            case CustomPropCategory.Trigger: return editor.triggerMenu;
            default: return editor.miscMenu;
        }
    }

    private static RectTransform LastSpawnButton(GameObject menu)
    {
        if (menu == null) return null;
        RectTransform previous = null;
        foreach (var existing in menu.GetComponentsInChildren<SpawnIcon>(true))
            if (existing != null) previous = existing.GetComponent<RectTransform>();
        return previous;
    }

    private void InstallFieldListeners()
    {
        if (listenersInstalled) return;
        listenersInstalled = true;
        SetEditable(editor.idField);
        SetEditable(editor.tarIdField);
        SetEditable(editor.teamField);
        SetEditable(editor.forceXField);
        SetEditable(editor.forceYField);
        SetEditable(editor.sizeXField);
        SetEditable(editor.sizeYField);

        if (editor.idField != null)
        {
            editor.idField.onEndEdit.AddListener(value => CommitFields());
            editor.idField.onEndEdit.AddListener(value => CommitColoredLampId());
        }
        if (editor.tarIdField != null) editor.tarIdField.onEndEdit.AddListener(value => CommitFields());
        if (editor.teamField != null)
        {
            editor.teamField.onEndEdit.AddListener(value => CommitFields());
            editor.teamField.onEndEdit.AddListener(value => CommitPlayerSpawnTeam());
        }
        if (editor.forceXField != null) editor.forceXField.onEndEdit.AddListener(value => CommitFields());
        if (editor.forceYField != null) editor.forceYField.onEndEdit.AddListener(value => CommitFields());
        if (editor.sizeXField != null) editor.sizeXField.onEndEdit.AddListener(value => CommitFields());
        if (editor.sizeYField != null) editor.sizeYField.onEndEdit.AddListener(value => CommitFields());
    }

    private static void SetEditable(TMP_InputField field)
    {
        if (field == null) return;
        field.interactable = true;
        field.readOnly = false;
        field.contentType = TMP_InputField.ContentType.Standard;
        field.inputType = TMP_InputField.InputType.Standard;
    }

    internal void EnableTeamFieldForPlayerSpawn()
    {
        if (editor == null || editor.teamField == null) return;
        var selected = selectedField == null ? null : selectedField.GetValue(editor) as GameObject;
        var levelPart = selected == null ? null : selected.GetComponent<LevelPartGame>();
        if (levelPart == null || levelPart.part == null || levelPart.part.path != "Building/PlayerSpawn") return;
        levelPart.showTeam = true;
        editor.teamField.text = levelPart.part.team ?? "";
        SetEditable(editor.teamField);
    }

    internal void CommitPlayerSpawnTeam()
    {
        if (editor == null || editor.teamField == null) return;
        var selected = selectedField == null ? null : selectedField.GetValue(editor) as GameObject;
        var levelPart = selected == null ? null : selected.GetComponent<LevelPartGame>();
        if (levelPart == null || levelPart.part == null || levelPart.part.path != "Building/PlayerSpawn") return;
        levelPart.part.team = editor.teamField.text.Trim();
    }

    internal void EnableIdFieldForColoredLamp()
    {
        if (editor == null || editor.idField == null) return;
        var selected = selectedField == null ? null : selectedField.GetValue(editor) as GameObject;
        var levelPart = selected == null ? null : selected.GetComponent<LevelPartGame>();
        if (!IsColoredLamp(levelPart)) return;
        levelPart.showId = true;
        levelPart.idNameOverride = "Activation ID";
        editor.idField.text = levelPart.part.id.ToString();
        if (editor.idName != null) editor.idName.text = "Activation ID";
        SetEditable(editor.idField);
    }

    internal void CommitColoredLampId()
    {
        if (editor == null || editor.idField == null) return;
        var selected = selectedField == null ? null : selectedField.GetValue(editor) as GameObject;
        var levelPart = selected == null ? null : selected.GetComponent<LevelPartGame>();
        if (!IsColoredLamp(levelPart)) return;
        int id;
        if (int.TryParse(editor.idField.text, out id)) levelPart.part.id = Mathf.Max(0, id);
    }

    private static bool IsColoredLamp(LevelPartGame levelPart)
    {
        return levelPart != null && levelPart.part != null &&
               (levelPart.fullName == "Colored Lamp" ||
                (!string.IsNullOrEmpty(levelPart.part.path) &&
                 levelPart.part.path.IndexOf("Lamp", StringComparison.OrdinalIgnoreCase) >= 0));
    }

    private void Update()
    {
        UpdateInspector();
        if (placingDefinition == null || editor == null || Camera.main == null) return;
        var position = editor.alignToGrid(Camera.main.ScreenToWorldPoint(Input.mousePosition));
        if (ghost != null) ghost.transform.position = position;
        if (!Input.GetMouseButtonDown(0) || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) return;

        var instance = new CustomPropInstance
        {
            Uid = Guid.NewGuid().ToString("N"),
            TypeId = placingDefinition.TypeId,
            Data = placingDefinition.CreateDefaultData(),
            Position = position,
            Rotation = 0f
        };
        instances.Add(instance);
        var marker = CreateMarker(instance);
        editor.SelectPart(marker.GetComponent<Collider2D>());
        EndPlacement();
    }

    internal void RefreshInspectorImmediate()
    {
        shownMarker = null;
        UpdateInspector();
    }

    internal void RestoreNativeInspector()
    {
        if (!nativeInspectorCaptured) CaptureNativeInspector();
        foreach (var state in nativeInputs)
        {
            if (state.Input == null) continue;
            state.Input.gameObject.SetActive(state.Active);
            state.Input.interactable = state.Interactable;
            state.Input.readOnly = state.ReadOnly;
            state.Input.contentType = state.ContentType;
            state.Input.inputType = state.InputType;
            SetPlaceholder(state.Input, state.Placeholder);
        }
        foreach (var state in nativeLabels)
        {
            if (state.Label == null) continue;
            state.Label.gameObject.SetActive(state.Active);
            state.Label.text = state.Text;
        }
        shownMarker = null;
    }

    private void UpdateInspector()
    {
        if (editor == null || selectedField == null) return;
        var selected = selectedField.GetValue(editor) as GameObject;
        var marker = selected == null ? null : selected.GetComponent<CustomPropMarker>();

        if (marker == null || marker.Definition == null)
        {
            shownMarker = null;
            return;
        }

        var selectionChanged = marker != shownMarker;
        shownMarker = marker;
        if (selectionChanged || !HasGenericInspectorLayout(marker))
            ShowGenericFields(marker, selectionChanged || !AnyInputFocused());
    }

    private void CaptureNativeInspector()
    {
        if (nativeInspectorCaptured || editor == null) return;
        nativeInspectorCaptured = true;
        foreach (var input in GetInputs())
        {
            if (input == null) continue;
            nativeInputs.Add(new NativeInputState
            {
                Input = input,
                Active = input.gameObject.activeSelf,
                Interactable = input.interactable,
                ReadOnly = input.readOnly,
                ContentType = input.contentType,
                InputType = input.inputType,
                Placeholder = PlaceholderText(input)
            });
        }
        var seen = new HashSet<TMP_Text>();
        foreach (var label in GetLabels())
        {
            if (label == null || !seen.Add(label)) continue;
            nativeLabels.Add(new NativeLabelState
            {
                Label = label,
                Active = label.gameObject.activeSelf,
                Text = label.text
            });
        }
    }

    private bool HasGenericInspectorLayout(CustomPropMarker marker)
    {
        var inputs = GetInputs();
        var labels = GetLabels();
        var fields = marker.Definition.Fields ?? new CustomPropField[0];
        var fieldIndex = 0;
        var secondary = false;

        for (var index = 0; index < inputs.Length; index++)
        {
            var shouldBeVisible = fieldIndex < fields.Length;
            if (inputs[index] != null && inputs[index].gameObject.activeSelf != shouldBeVisible) return false;
            if (shouldBeVisible && !secondary && labels[index] != null && labels[index].text != fields[fieldIndex].Label) return false;
            AdvanceField(fields, ref fieldIndex, ref secondary);
        }
        return true;
    }

    private bool AnyInputFocused()
    {
        foreach (var input in GetInputs())
            if (input != null && input.isFocused) return true;
        return false;
    }

    private void ShowGenericFields(CustomPropMarker marker, bool refreshValues)
    {
        var inputs = GetInputs();
        var labels = GetLabels();
        var fields = marker.Definition.Fields ?? new CustomPropField[0];
        var fieldIndex = 0;
        var secondary = false;

        for (var index = 0; index < inputs.Length; index++)
        {
            var visible = fieldIndex < fields.Length;
            if (inputs[index] != null) inputs[index].gameObject.SetActive(visible);
            if (!visible) continue;

            var field = fields[fieldIndex];
            SetEditable(inputs[index]);
            if (!secondary && labels[index] != null)
            {
                labels[index].gameObject.SetActive(true);
                labels[index].text = field.Label;
            }
            SetPlaceholder(inputs[index], secondary ? field.SecondaryPlaceholder : field.Placeholder);
            if (refreshValues) inputs[index].text = secondary
                ? field.ReadSecondary(marker.Instance.Data)
                : field.Read(marker.Instance.Data);
            AdvanceField(fields, ref fieldIndex, ref secondary);
        }
    }

    private void CommitFields()
    {
        if (shownMarker == null || shownMarker.Definition == null) return;

        var fields = shownMarker.Definition.Fields ?? new CustomPropField[0];
        var inputs = GetInputs();
        var fieldIndex = 0;
        var secondary = false;
        for (var index = 0; index < inputs.Length && fieldIndex < fields.Length; index++)
        {
            if (inputs[index] != null)
            {
                if (secondary) fields[fieldIndex].WriteSecondary(shownMarker.Instance.Data, inputs[index].text);
                else fields[fieldIndex].Write(shownMarker.Instance.Data, inputs[index].text);
            }
            AdvanceField(fields, ref fieldIndex, ref secondary);
        }
    }

    private static void AdvanceField(CustomPropField[] fields, ref int fieldIndex, ref bool secondary)
    {
        if (fieldIndex >= fields.Length) return;
        if (!secondary && fields[fieldIndex].ReadSecondary != null)
        {
            secondary = true;
            return;
        }
        secondary = false;
        fieldIndex++;
    }

    private TMP_InputField[] GetInputs()
    {
        return new[]
        {
            editor.idField,
            editor.tarIdField,
            editor.teamField,
            editor.forceXField,
            editor.forceYField,
            editor.sizeXField,
            editor.sizeYField
        };
    }

    private TMP_Text[] GetLabels()
    {
        return new TMP_Text[]
        {
            editor.idName,
            editor.tarIdName,
            FindNativeFieldLabel(editor.teamField, "Team"),
            editor.forceName,
            null,
            FindNativeFieldLabel(editor.sizeXField, "Size"),
            null
        };
    }

    private TMP_Text FindNativeFieldLabel(TMP_InputField input, string nativeName)
    {
        if (input == null || editor == null || editor.objMenu == null) return null;

        var inputTransform = input.transform;
        TMP_Text closest = null;
        var closestScore = float.MaxValue;
        var inputPosition = inputTransform.position;
        foreach (var candidate in editor.objMenu.GetComponentsInChildren<TMP_Text>(true))
        {
            if (candidate == null || candidate.transform.IsChildOf(inputTransform)) continue;
            if (candidate.GetComponentInParent<TMP_InputField>() != null) continue;
            if (!string.IsNullOrEmpty(nativeName) && candidate.text == nativeName) return candidate;

            var delta = candidate.transform.position - inputPosition;
            if (delta.x > 1f || Mathf.Abs(delta.y) > 35f) continue;
            var score = Mathf.Abs(delta.y) * 10f + Mathf.Abs(delta.x);
            if (score >= closestScore) continue;
            closest = candidate;
            closestScore = score;
        }
        return closest;
    }

    private static void SetPlaceholder(TMP_InputField input, string text)
    {
        if (input == null || input.placeholder == null) return;
        var label = input.placeholder.GetComponent<TextMeshProUGUI>();
        if (label != null) label.text = text ?? string.Empty;
    }

    private static string PlaceholderText(TMP_InputField input)
    {
        if (input == null || input.placeholder == null) return string.Empty;
        var label = input.placeholder.GetComponent<TextMeshProUGUI>();
        return label == null ? string.Empty : label.text;
    }

    private sealed class NativeInputState
    {
        internal TMP_InputField Input;
        internal bool Active;
        internal bool Interactable;
        internal bool ReadOnly;
        internal TMP_InputField.ContentType ContentType;
        internal TMP_InputField.InputType InputType;
        internal string Placeholder;
    }

    private sealed class NativeLabelState
    {
        internal TMP_Text Label;
        internal bool Active;
        internal string Text;
    }

    private void BeginPlacement(ICustomPropDefinition definition)
    {
        EndPlacement();
        editor.curSpawnPath = string.Empty;
        editor.miniHeldObj.sprite = null;
        placingDefinition = definition;
        ghost = new GameObject(definition.DisplayName + " Preview");
        var renderer = ghost.AddComponent<SpriteRenderer>();
        renderer.sprite = definition.Icon;
        renderer.color = new Color(1f, 1f, 1f, 0.55f);
        renderer.sortingLayerName = "Foreground";
        renderer.sortingOrder = 300;
    }

    private void EndPlacement()
    {
        placingDefinition = null;
        if (ghost != null) Destroy(ghost);
        ghost = null;
    }

    private void OnDestroy()
    {
        EndPlacement();
    }
}
