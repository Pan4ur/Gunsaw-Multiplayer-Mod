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
    private LevelPartGame editingLamp;
    private LevelPartGame editingLampAngle;

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
        WeaponPropRespawnSystem.Prepare(runtimeSourceJson);
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
                GunsawMultiplayerPlugin.LogInfo("Unknown custom prop type: " + instance.TypeId);
                continue;
            }

            var gameObject = new GameObject("Custom Prop: " + definition.DisplayName + " " + instance.Uid);
            gameObject.tag = "Activateable";
            gameObject.transform.position = instance.Position;
            gameObject.transform.rotation = Quaternion.Euler(0f, 0f, instance.Rotation);
            if (instance.Data is NoPvpZoneData)
            {
                var marker = gameObject.AddComponent<CustomPropMarker>();
                marker.Instance = instance;
            }
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
            var instance = ReadInstance(part);
            if (instance != null) result.Add(instance);
        }

        return result;
    }

    private static CustomPropInstance ReadInstance(LevelPart part)
    {
        if (part == null || part.path != RootPath || string.IsNullOrEmpty(part.team)) return null;
        CustomPropPayload payload;
        try { payload = JsonUtility.FromJson<CustomPropPayload>(part.team); }
        catch { return null; }
        if (payload == null || string.IsNullOrEmpty(payload.type)) return null;

        ICustomPropDefinition definition;
        object data = payload.data;
        if (CustomPropRegistry.TryGet(payload.type, out definition))
            data = definition.DeserializeData(payload.data);

        return new CustomPropInstance
        {
            Uid = string.IsNullOrEmpty(payload.uid) ? Guid.NewGuid().ToString("N") : payload.uid,
            TypeId = payload.type,
            Data = data,
            Position = part.pos,
            Rotation = part.rot,
            Size = NoPvpZoneDefinition.Size(part.size)
        };
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
        part.size = instance.Size;
        return part;
    }

    private void RebuildMarkers()
    {
        foreach (var marker in markers)
            if (marker != null)
            {
                marker.gameObject.SetActive(false);
                marker.transform.SetParent(null);
                Destroy(marker.gameObject);
            }
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
        NoPvpZoneDefinition.UpdateMarker(marker);
        markers.Add(marker);
        return marker;
    }

    private void SyncInstances()
    {
        var known = new HashSet<CustomPropMarker>(markers);
        foreach (Transform child in editor.transform)
        {
            var marker = child.GetComponent<CustomPropMarker>();
            if (marker == null || known.Contains(marker)) continue;
            var source = ReadInstance(marker.GetComponent<LevelPartGame>().part) ?? marker.Instance;
            if (source == null) throw new InvalidOperationException("Invalid duplicated custom prop.");
            if (marker.Definition == null) CustomPropRegistry.TryGet(source.TypeId, out marker.Definition);
            marker.Instance = CloneInstance(source, marker.Definition);
            markers.Add(marker);
        }
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
            if (marker.Instance.Data is NoPvpZoneData)
                marker.Instance.Size = NoPvpZoneDefinition.Size(marker.GetComponent<SpriteRenderer>().size);
            marker.GetComponent<LevelPartGame>().part = ToPart(marker.Instance);
            instances.Add(marker.Instance);
        }
    }

    internal void SynchronizeForReplication()
    {
        SyncInstances();
    }

    internal static bool CanReplicate(LevelPart part)
    {
        try
        {
            var payload = JsonUtility.FromJson<CustomPropPayload>(part.team);
            if (payload == null || string.IsNullOrEmpty(payload.uid))
                return false;
                
            var instance = ReadInstance(part);
            return instance != null && CustomPropRegistry.TryGet(instance.TypeId, out _);
        }
        catch { 
            return false; 
        }
    }

    internal LevelPartGame ApplyReplicatedPart(LevelPartGame? existing, LevelPart part)
    {
        var instance = ReadInstance(part);
        if (instance == null)
            throw new InvalidOperationException("Invalid custom prop payload.");
            
        var marker = existing == null ? null : existing.GetComponent<CustomPropMarker>();
     
        if (marker == null)
            marker = CreateMarker(instance);
        else
        {
            marker.Instance = instance;
            CustomPropRegistry.TryGet(instance.TypeId, out marker.Definition);
        }
       
        marker.transform.position = instance.Position;
        marker.transform.rotation = Quaternion.Euler(0f, 0f, instance.Rotation);
        var component = marker.GetComponent<LevelPartGame>();
        component.part = part;
        NoPvpZoneDefinition.UpdateMarker(marker);
        return component;
    }

    internal void RefreshReplicatedInspector(LevelPartGame component)
    {
        var marker = component.GetComponent<CustomPropMarker>();
        if (marker != null)
        {
            if (marker.Definition == null) return;
            shownMarker = marker;
            ShowGenericFields(marker, false);
            return;
        }
        if (!editor.teamField.isFocused) EnableTeamFieldForPlayerSpawn();
        if (!editor.idField.isFocused) EnableIdFieldForColoredLamp();
        if (!editor.sizeXField.isFocused) EnableSizeFieldForLampAngle();
        if (!editor.forceXField.isFocused) EnableRespawnTimeForWeapon();
    }

    internal void ForgetReplicatedPart(LevelPartGame component)
    {
        var marker = component.GetComponent<CustomPropMarker>();
        if (marker == null) 
            return;
            
        markers.Remove(marker);
        instances.Remove(marker.Instance);
        
        if (shownMarker == marker) 
            shownMarker = null;
    }

    internal void DuplicateSelected()
    {
        SyncInstances();
        RefreshInspectorImmediate();
    }

    internal GameObject[] PasteCustomParts(IEnumerable<LevelPart> parts, Vector3 center)
    {
        var sources = parts.Select(part => ReadInstance(part) ?? throw new InvalidOperationException("Invalid pasted custom prop.")).ToArray();
        var created = new List<GameObject>();
        try
        {
            foreach (var source in sources)
            {
                CustomPropRegistry.TryGet(source.TypeId, out var definition);
                var instance = CloneInstance(source, definition);
                instance.Position = (Vector2)center + source.Position;
                created.Add(CreateMarker(instance).gameObject);
            }
            return created.ToArray();
        }
        catch
        {
            foreach (var value in created)
            {
                ForgetReplicatedPart(value.GetComponent<LevelPartGame>());
                value.SetActive(false);
                value.transform.SetParent(null);
                Destroy(value);
            }
            throw;
        }
    }

    internal void PrepareDuplicate()
    {
        if (editor == null || selectedField == null) return;
        var selected = selectedField.GetValue(editor) as GameObject;
        var marker = selected == null ? null : selected.GetComponent<CustomPropMarker>();
        if (marker == null || marker.Instance == null) return;

        if (marker.Instance.Data is NoPvpZoneData)
            marker.Instance.Size = NoPvpZoneDefinition.Size(marker.GetComponent<SpriteRenderer>().size);
        if (shownMarker == marker)
            foreach (var input in GetInputs())
                if (input != null && input.isFocused) CommitField(input);
        marker.GetComponent<LevelPartGame>().part = ToPart(marker.Instance);
    }

    private static CustomPropInstance CloneInstance(CustomPropInstance source, ICustomPropDefinition definition)
    {
        var serializedData = definition == null
            ? source.Data as string ?? string.Empty
            : definition.SerializeData(source.Data);

        return new CustomPropInstance
        {
            Uid = Guid.NewGuid().ToString("N"),
            TypeId = source.TypeId,
            Data = definition == null ? serializedData : definition.DeserializeData(serializedData),
            Position = source.Position,
            Rotation = source.Rotation,
            Size = source.Size
        };
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
            editor.idField.onEndEdit.AddListener(value => CommitField(editor.idField));
            editor.idField.onSelect.AddListener(value => BeginColoredLampIdEdit());
            editor.idField.onEndEdit.AddListener(value => CommitColoredLampId(editingLamp, value));
        }
        if (editor.tarIdField != null) editor.tarIdField.onEndEdit.AddListener(value => CommitField(editor.tarIdField));
        if (editor.teamField != null)
        {
            editor.teamField.onEndEdit.AddListener(value => CommitField(editor.teamField));
            editor.teamField.onEndEdit.AddListener(value => CommitPlayerSpawnTeam());
        }
        if (editor.forceXField != null)
        {
            editor.forceXField.onEndEdit.AddListener(value => CommitField(editor.forceXField));
            editor.forceXField.onEndEdit.AddListener(value => CommitWeaponRespawnTime());
        }
        if (editor.forceYField != null) editor.forceYField.onEndEdit.AddListener(value => CommitField(editor.forceYField));
        if (editor.sizeXField != null)
        {
            editor.sizeXField.onEndEdit.AddListener(value => CommitField(editor.sizeXField));
            editor.sizeXField.onSelect.AddListener(value => BeginLampAngleEdit());
            editor.sizeXField.onEndEdit.AddListener(value => CommitLampAngle(editingLampAngle, value));
        }
        if (editor.sizeYField != null) editor.sizeYField.onEndEdit.AddListener(value => CommitField(editor.sizeYField));
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
        var label = FindNativeFieldLabel(editor.teamField, "Team");
        if (label != null) label.text = "Team";
        SetPlaceholder(editor.teamField, "ID, name or color (empty = any)");
    }

    internal void CommitPlayerSpawnTeam()
    {
        if (!LevelEditorReplication.CanEdit) return;
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
        editor.idField.SetTextWithoutNotify(levelPart.part.id.ToString());
        if (editor.idName != null) editor.idName.text = "Activation ID";
        SetEditable(editor.idField);
    }

    private void BeginColoredLampIdEdit()
    {
        var selected = selectedField == null ? null : selectedField.GetValue(editor) as GameObject;
        var levelPart = selected == null ? null : selected.GetComponent<LevelPartGame>();
        editingLamp = IsColoredLamp(levelPart) ? levelPart : null;
    }

    internal void CommitColoredLampId()
    {
        if (editor != null && editor.idField != null && editor.idField.isFocused && editingLamp != null)
            CommitColoredLampId(editingLamp, editor.idField.text);
    }

    private void CommitColoredLampId(LevelPartGame levelPart, string value)
    {
        if (!LevelEditorReplication.CanEdit) return;
        if (!IsColoredLamp(levelPart)) return;
        int id;
        if (!int.TryParse(value, out id)) return;
        levelPart.part.id = Mathf.Max(0, id);
    }

    internal void EnableSizeFieldForLampAngle()
    {
        if (editor == null || editor.sizeXField == null) return;
        var selected = selectedField == null ? null : selectedField.GetValue(editor) as GameObject;
        var levelPart = selected == null ? null : selected.GetComponent<LevelPartGame>();
        if (!IsColoredLamp(levelPart)) return;
        levelPart.showSize = true;
        editor.sizeXField.SetTextWithoutNotify(LampAngle(levelPart.part.size).ToString());
        SetEditable(editor.sizeXField);
        if (editor.sizeYField != null) editor.sizeYField.gameObject.SetActive(false);
        var sizeName = FindNativeFieldLabel(editor.sizeXField, "Size");
        if (sizeName != null) sizeName.text = "Angle";
    }

    private void BeginLampAngleEdit()
    {
        var selected = selectedField == null ? null : selectedField.GetValue(editor) as GameObject;
        var levelPart = selected == null ? null : selected.GetComponent<LevelPartGame>();
        editingLampAngle = IsColoredLamp(levelPart) ? levelPart : null;
    }

    private void CommitLampAngle(LevelPartGame levelPart, string value)
    {
        if (!LevelEditorReplication.CanEdit) return;
        if (!IsColoredLamp(levelPart)) return;
        float angle;
        if (!float.TryParse(value, out angle)) return;
        levelPart.part.size.x = Mathf.Clamp(angle, 1f, 360f);
    }

    private static float LampAngle(Vector2 size)
    {
        return size.x <= 1f ? 360f : Mathf.Clamp(size.x, 1f, 360f);
    }
    
    internal void EnableRespawnTimeForWeapon()
    {
        if (editor == null || editor.forceXField == null) return;
        var selected = selectedField == null ? null : selectedField.GetValue(editor) as GameObject;
        var levelPart = selected == null ? null : selected.GetComponent<LevelPartGame>();
        if (!WeaponPropRespawnSystem.IsWeaponSpawn(levelPart)) return;
        levelPart.showForce = true;
        editor.forceXField.text = WeaponPropRespawnSystem.EditorTime(levelPart.part).ToString();
        SetEditable(editor.forceXField);
       
        if (editor.forceYField != null)
            editor.forceYField.gameObject.SetActive(false);
            
        if (editor.forceName != null)
            editor.forceName.text = "Respawn time";
    }

    internal void CommitWeaponRespawnTime()
    {
        if (!LevelEditorReplication.CanEdit) return;
        if (editor == null || editor.forceXField == null)
            return;
            
        var selected = selectedField == null ? null : selectedField.GetValue(editor) as GameObject;
        var levelPart = selected == null ? null : selected.GetComponent<LevelPartGame>();
        if (!WeaponPropRespawnSystem.IsWeaponSpawn(levelPart)) 
            return;
            
        float value;
        if (float.TryParse(editor.forceXField.text, out value)) 
            WeaponPropRespawnSystem.SetEditorTime(levelPart.part, value);
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
        if (!LevelEditorReplication.CanEdit)
        {
            EndPlacement();
            return;
        }
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
            ShowGenericFields(marker, selectionChanged);
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
        if (marker.Instance.Data is NoPvpZoneData) return true;
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

    private void ShowGenericFields(CustomPropMarker marker, bool selectionChanged)
    {
        if (marker.Instance.Data is NoPvpZoneData zone)
        {
            RestoreNativeInspector();
            shownMarker = marker;
            foreach (var input in GetInputs())
                if (input != null) input.interactable = false;
            SetEditable(editor.teamField);
            SetEditable(editor.idField);
            var idLabel = FindNativeFieldLabel(editor.idField, "ID");
            if (idLabel != null) idLabel.text = "Activation ID";
            if (!editor.idField.isFocused) editor.idField.SetTextWithoutNotify(zone.activationId.ToString());
            SetPlaceholder(editor.idField, "0 = no activation");
            SetEditable(editor.sizeXField);
            SetEditable(editor.sizeYField);
            if (!editor.teamField.isFocused) editor.teamField.SetTextWithoutNotify(zone.team);
            var size = marker.GetComponent<SpriteRenderer>().size;
            if (!editor.sizeXField.isFocused) editor.sizeXField.SetTextWithoutNotify(size.x.ToString());
            if (!editor.sizeYField.isFocused) editor.sizeYField.SetTextWithoutNotify(size.y.ToString());
            SetPlaceholder(editor.teamField, "ID, name or color (empty = everyone)");
            return;
        }
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
            if (selectionChanged || !inputs[index].isFocused) inputs[index].SetTextWithoutNotify(secondary
                ? field.ReadSecondary(marker.Instance.Data)
                : field.Read(marker.Instance.Data));
            AdvanceField(fields, ref fieldIndex, ref secondary);
        }
    }

    private void CommitField(TMP_InputField input)
    {
        if (!LevelEditorReplication.CanEdit) return;
        if (shownMarker == null || shownMarker.Definition == null) return;

        if (shownMarker.Instance.Data is NoPvpZoneData zone)
        {
            if (input == editor.teamField) zone.team = input.text.Trim();
            if (input == editor.idField && int.TryParse(input.text, out var activationId))
                zone.activationId = Mathf.Max(0, activationId);
            return;
        }
        var fields = shownMarker.Definition.Fields ?? new CustomPropField[0];
        var inputs = GetInputs();
        var fieldIndex = 0;
        var secondary = false;
        for (var index = 0; index < inputs.Length && fieldIndex < fields.Length; index++)
        {
            if (inputs[index] == input)
            {
                if (secondary) fields[fieldIndex].WriteSecondary(shownMarker.Instance.Data, input.text);
                else fields[fieldIndex].Write(shownMarker.Instance.Data, input.text);
                return;
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
        if (!LevelEditorReplication.CanEdit) return;
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
