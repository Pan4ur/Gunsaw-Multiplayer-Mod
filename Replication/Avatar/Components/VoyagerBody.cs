using UnityEngine;

public class VoyagerBody
{
    private static readonly Dictionary<SpriteRenderer, float> voyagerWeaponBaseAlpha = new();
    private static readonly Dictionary<LineRenderer, Vector2> voyagerScarfBaseAlpha = new();
    private static readonly Dictionary<BodyScript, VoyagerVisualLayout> visualLayouts = new();

    internal static void UpdatePvpVoyagerVisuals(BodyScript body, float deltaTime)
    {
        if (body == null) return;
        var layout = VisualLayout(body);
        if (!layout.HasCamo) return;
        var visibility = PvpVoyagerVisibility(body, true);
        var blend = 1f - Mathf.Exp(-10f * Mathf.Max(0f, deltaTime));
        var isRemote = NetworkAvatarManager.IsRemoteAvatarBody(body);
        foreach (var renderer in layout.AlphaRenderers)
        {
            if (renderer == null) continue;
            if (!voyagerWeaponBaseAlpha.TryGetValue(renderer, out var baseAlpha))
            {
                baseAlpha = isRemote && visibility > 0.001f ? renderer.color.a / visibility : renderer.color.a;
                voyagerWeaponBaseAlpha[renderer] = baseAlpha;
            }
            var color = renderer.color;
            color.a = visibility <= 0.001f ? 0f : Mathf.Lerp(color.a, baseAlpha * visibility, blend);
            renderer.color = color;
        }

        foreach (var line in layout.ScarfLines)
        {
            if (line == null) continue;
            if (!voyagerScarfBaseAlpha.TryGetValue(line, out var baseAlpha))
            {
                baseAlpha = new Vector2(line.startColor.a, line.endColor.a);
                voyagerScarfBaseAlpha[line] = baseAlpha;
            }
            var start = line.startColor;
            var end = line.endColor;
            start.a = visibility <= 0.001f ? 0f : Mathf.Lerp(start.a, baseAlpha.x * visibility, blend);
            end.a = visibility <= 0.001f ? 0f : Mathf.Lerp(end.a, baseAlpha.y * visibility, blend);
            line.startColor = start;
            line.endColor = end;
        }
    }

    internal static float PvpVoyagerVisibility(BodyScript body)
    {
        if (body == null || !MultiplayerSession.PvpEnabled || body.GetComponentInChildren<CarverCamo>(true) == null)
            return 1f;
        
        return PvpVoyagerVisibility(body, true);
    }

    private static float PvpVoyagerVisibility(BodyScript body, bool hasCamo)
    {
        if (body == null || !MultiplayerSession.PvpEnabled || !hasCamo) return 1f;
        return Mathf.InverseLerp(0.25f, 1f, body.susnessMult);
    }

    private static VoyagerVisualLayout VisualLayout(BodyScript body)
    {
        if (visualLayouts.TryGetValue(body, out var layout) && layout.Root == body.transform)
            return layout;

        var renderers = body.GetComponentsInChildren<SpriteRenderer>(true);
        var alphaRenderers = Enumerable.OfType<SpriteRenderer>(renderers)
            .Select(r => new { r, r.gameObject.name })
            .Where(t => t.name is "testGun" or "BackWep1" or "BackWep2" or "ScarfHold" or "MP Remote Scarf Hold")
            .Select(t => t.r).ToList();

        var scarves = body.GetComponentsInChildren<ScarfPhysics>(true);
        var scarfLines = new List<LineRenderer>();
        foreach (var scarf in scarves)
            if (scarf != null && scarf.pointRenderer != null)
                scarfLines.Add(scarf.pointRenderer);

        layout = new VoyagerVisualLayout
        {
            Root = body.transform,
            HasCamo = body.GetComponentInChildren<CarverCamo>(true) != null,
            AlphaRenderers = [.. alphaRenderers],
            ScarfLines = [.. scarfLines]
        };
        visualLayouts[body] = layout;
        return layout;
    }

    private sealed class VoyagerVisualLayout
    {
        internal Transform Root;
        internal bool HasCamo;
        internal SpriteRenderer[] AlphaRenderers = [];
        internal LineRenderer[] ScarfLines = [];
    }
}