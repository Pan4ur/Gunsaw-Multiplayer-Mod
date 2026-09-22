using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;

internal static class ToggleableLampSystem
{
    private const string LampPath = "Building/Lamp";
    private static readonly List<LampLevelData> pending = new List<LampLevelData>();

    internal static void PrepareRuntime(string json)
    {
        pending.Clear();
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var level = JsonUtility.FromJson<Level>(json);
            if (level == null || level.parts == null) return;
            foreach (var part in level.parts)
            {
                if (part == null || !IsLampPath(part.path)) continue;
                pending.Add(new LampLevelData
                {
                    Position = part.pos,
                    ActivationId = part.id,
                    Intensity = part.force.x,
                    Angle = LampAngle(part.size),
                    Color = LevelLoader.HexToColor(part.team),
                    IsColored = IsColoredLampPath(part.path)
                });
            }
        }
        catch (Exception e)
        {
            GunsawMultiplayerPlugin.LogInfo("Failed to prepare toggleable lamps: " + e.Message);
        }
    }

    private static bool IsLampPath(string path)
    {
        return !string.IsNullOrEmpty(path) &&
               (path == LampPath || path.EndsWith("/Lamp", StringComparison.OrdinalIgnoreCase) ||
                path.IndexOf("Lamp", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsColoredLampPath(string path)
    {
        return !string.IsNullOrEmpty(path) &&
               (path.Equals("Building/ColorLamp", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("/ColorLamp", StringComparison.OrdinalIgnoreCase));
    }

    private static float LampAngle(Vector2 size)
    {
        return size.x <= 1f ? 360f : Mathf.Clamp(size.x, 1f, 360f);
    }

    internal static void AttachRuntime()
    {
        if (pending.Count == 0) return;
        var coloredLamps = UnityEngine.Object.FindObjectsOfType<ColorLampTag>();
        var regularLamps = UnityEngine.Object.FindObjectsOfType<Light2D>();
        var used = new HashSet<int>();
        foreach (var definition in pending)
        {
            Component best = null;
            var bestDistance = float.MaxValue;
            if (definition.IsColored)
            {
                for (var index = 0; index < coloredLamps.Length; index++)
                {
                    var lamp = coloredLamps[index];
                    if (lamp == null || used.Contains(lamp.GetInstanceID())) continue;
                    var distance = ((Vector2)lamp.transform.position - definition.Position).sqrMagnitude;
                    if (distance > 0.01f || distance >= bestDistance) continue;
                    best = lamp;
                    bestDistance = distance;
                }
            }
            else
            {
                for (var index = 0; index < regularLamps.Length; index++)
                {
                    var lamp = regularLamps[index];
                    if (lamp == null || used.Contains(lamp.GetInstanceID())) continue;
                    var distance = ((Vector2)lamp.transform.position - definition.Position).sqrMagnitude;
                    if (distance > 0.01f || distance >= bestDistance) continue;
                    best = lamp;
                    bestDistance = distance;
                }
            }
            if (best == null) continue;
            used.Add(best.GetInstanceID());
            var runtime = best.GetComponent<ToggleableLampRuntime>();
            if (runtime == null) runtime = best.gameObject.AddComponent<ToggleableLampRuntime>();
            runtime.Configure(definition.ActivationId, definition.Intensity, definition.Angle, definition.Color, definition.IsColored);
            EnsureActivationRelay(best.transform, runtime);
        }
        pending.Clear();
    }

    private static void EnsureActivationRelay(Transform parent, ToggleableLampRuntime runtime)
    {
        if (parent == null || runtime == null) return;
        var existing = parent.GetComponentInChildren<ToggleableLampActivationRelay>(true);
        if (existing != null)
        {
            existing.Configure(runtime);
            return;
        }

        var relayObject = new GameObject("MP Lamp Activation Relay");
        relayObject.tag = "Activateable";
        relayObject.transform.SetParent(parent, false);
        relayObject.AddComponent<ToggleableLampActivationRelay>().Configure(runtime);
    }

    internal static ToggleableLampRuntime RuntimeForLamp(WorldReplication.LampState lamp)
    {
        if (lamp == null || lamp.Object == null) return null;
        var runtime = lamp.Object.GetComponent<ToggleableLampRuntime>();
        if (runtime != null) return runtime;
        runtime = lamp.Object.GetComponentInParent<ToggleableLampRuntime>();
        if (runtime != null) return runtime;
        return lamp.Object.GetComponentInChildren<ToggleableLampRuntime>(true);
    }

    internal static ToggleableLampRuntime RuntimeForId(int id)
    {
        foreach (var r in UnityEngine.Object.FindObjectsOfType<ToggleableLampRuntime>())
            if (r != null && r.ActivationId == id)
                return r;
        
        return null;
    }

    private sealed class LampLevelData
    {
        internal Vector2 Position;
        internal int ActivationId;
        internal float Intensity;
        internal float Angle;
        internal Color Color;
        internal bool IsColored;
    }
}

internal sealed class ToggleableLampRuntime : MonoBehaviour
{
    private int activationId;
    private float onIntensity;
    private float angle;
    private Color color;
    private bool powered = true;
    private SpriteRenderer bulbRenderer;
    private SpriteRenderer housingRenderer;

    internal bool Powered => powered;
    internal int ActivationId => activationId;
    internal float Intensity => onIntensity;
    internal Color Color => color;

    internal void Configure(int id, float intensity, float angle, Color configuredColor, bool isColored)
    {
        activationId = id;
        var lights = GetComponentsInChildren<Light2D>(true);
        var light = lights.Length > 0 ? lights[0] : null;
        onIntensity = isColored || light == null ? Mathf.Max(0f, intensity) : light.intensity;
        color = isColored || light == null ? configuredColor : light.color;
        this.angle = angle;
        ApplyPointLightShape(lights);
        powered = true;
        FindVisualRenderers();
        Apply();
    }

    internal void HandleActivation(int id)
    {
        if (id != activationId || activationId <= 0) return;
        if (MultiplayerSession.IsActive && !MultiplayerSession.IsHost) return;
        SetPowered(!powered);
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.nextSnapshot = 0f;
    }

    internal void SetPowered(bool value)
    {
        if (powered == value) return;
        powered = value;
        Apply();
    }

    internal void SetColor(Color value)
    {
        color = value;
        Apply();
    }

    internal void SetIntensity(float value)
    {
        onIntensity = Mathf.Max(0f, value);
        Apply();
    }

    private void Apply()
    {
        if (gameObject == null) return;
        try
        {
            LevelLoader.UpdateLampColor(gameObject, powered ? onIntensity : 0f, color);
        }
        catch
        {
            foreach (var light in GetComponentsInChildren<Light2D>(true))
                if (light != null)
                {
                    light.color = color;
                    light.intensity = powered ? onIntensity : 0f;
                }
        }

        ApplyPointLightShape(GetComponentsInChildren<Light2D>(true));

        ApplyVisualState();
    }

    private void ApplyPointLightShape(Light2D[] lights)
    {
        var innerAngle = Mathf.Clamp(angle, 1f, 360f);
        var outerAngle = Mathf.Min(360f, innerAngle + 26f);
        foreach (var light in lights)
        {
            if (light == null) continue;
            light.lightType = Light2D.LightType.Point;
            light.pointLightInnerAngle = innerAngle;
            light.pointLightOuterAngle = outerAngle;
        }
    }

    private void FindVisualRenderers()
    {
        if (transform.childCount == 0) return;
        var visualRoot = transform.GetChild(0);
        housingRenderer = visualRoot.GetComponent<SpriteRenderer>();
        if (visualRoot.childCount > 0)
            bulbRenderer = visualRoot.GetChild(0).GetComponent<SpriteRenderer>();
    }

    private void ApplyVisualState()
    {
        if (bulbRenderer != null)
            bulbRenderer.color = powered
                ? new Color(color.r, color.g, color.b, 1f)
                : new Color(0.04f, 0.04f, 0.04f, 0.7f);
        if (housingRenderer != null)
            housingRenderer.color = powered
                ? new Color(color.r, color.g, color.b, 0.45f)
                : new Color(0.12f, 0.12f, 0.12f, 0.6f);
    }
}

internal sealed class ToggleableLampActivationRelay : MonoBehaviour, IActivationIdReceiver
{
    private ToggleableLampRuntime runtime;

    public int ActivationId => runtime != null ? runtime.ActivationId : -1;

    internal void Configure(ToggleableLampRuntime value)
    {
        runtime = value;
    }

    private void Activate(int id)
    {
        if (runtime != null) runtime.HandleActivation(id);
    }
}
