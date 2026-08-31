using System;
using System.Collections.Generic;
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
                if (part == null || !IsLampPath(part.path) || part.id <= 0) continue;
                pending.Add(new LampLevelData
                {
                    Position = part.pos,
                    ActivationId = part.id,
                    Intensity = part.force.x,
                    Color = LevelLoader.HexToColor(part.team)
                });
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Failed to prepare toggleable lamps: " + exception.Message);
        }
    }

    private static bool IsLampPath(string path)
    {
        return !string.IsNullOrEmpty(path) &&
               (path == LampPath || path.EndsWith("/Lamp", StringComparison.OrdinalIgnoreCase) ||
                path.IndexOf("Lamp", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    internal static void AttachRuntime()
    {
        if (pending.Count == 0) return;
        var lamps = UnityEngine.Object.FindObjectsOfType<ColorLampTag>();
        var used = new HashSet<int>();
        foreach (var definition in pending)
        {
            ColorLampTag best = null;
            var bestDistance = float.MaxValue;
            for (var index = 0; index < lamps.Length; index++)
            {
                var lamp = lamps[index];
                if (lamp == null || used.Contains(lamp.GetInstanceID())) continue;
                var distance = ((Vector2)lamp.transform.position - definition.Position).sqrMagnitude;
                if (distance > 0.01f || distance >= bestDistance) continue;
                best = lamp;
                bestDistance = distance;
            }
            if (best == null) continue;
            used.Add(best.GetInstanceID());
            var runtime = best.GetComponent<ToggleableLampRuntime>();
            if (runtime == null) runtime = best.gameObject.AddComponent<ToggleableLampRuntime>();
            runtime.Configure(definition.ActivationId, definition.Intensity, definition.Color);
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

    private sealed class LampLevelData
    {
        internal Vector2 Position;
        internal int ActivationId;
        internal float Intensity;
        internal Color Color;
    }
}

internal sealed class ToggleableLampRuntime : MonoBehaviour
{
    private int activationId;
    private float onIntensity;
    private Color color;
    private bool powered = true;
    private SpriteRenderer bulbRenderer;
    private SpriteRenderer housingRenderer;

    internal bool Powered => powered;

    internal void Configure(int id, float intensity, Color configuredColor)
    {
        activationId = id;
        onIntensity = Mathf.Max(0f, intensity);
        color = configuredColor;
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

    private void Apply()
    {
        if (gameObject == null) return;
        try
        {
            LevelLoader.UpdateLampColor(gameObject, powered ? onIntensity : 0f, color);
        }
        catch
        {
            var light = GetComponent<Light2D>();
            if (light != null)
            {
                light.color = color;
                light.intensity = powered ? onIntensity : 0f;
            }
        }

        ApplyVisualState();
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

internal sealed class ToggleableLampActivationRelay : MonoBehaviour
{
    private ToggleableLampRuntime runtime;

    internal void Configure(ToggleableLampRuntime value)
    {
        runtime = value;
    }

    private void Activate(int id)
    {
        if (runtime != null) runtime.HandleActivation(id);
    }
}
