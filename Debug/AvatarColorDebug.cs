using UnityEngine;

internal sealed class AvatarColorDebug
{
    private bool rainbowScarfEnabled;
    private CharacterColorMode characterColorMode;
    private BodyScript colorizedBody;
    private float nextUpdate;
    private readonly Dictionary<SpriteRenderer, Color> originalCharacterColors = new();
    private LineRenderer scarfLine;
    private Color originalScarfStart;
    private Color originalScarfEnd;

    internal bool IsActive => rainbowScarfEnabled || characterColorMode != CharacterColorMode.None;

    internal void Update(BodyScript body)
    {
        var modifier = Input.GetKey(KeyCode.End) && Input.GetKey(KeyCode.Space);
        if (modifier && Input.GetKeyDown(KeyCode.J))
        {
            rainbowScarfEnabled = !rainbowScarfEnabled;
            nextUpdate = 0f;
            if (!rainbowScarfEnabled) RestoreScarfColor();
        }
        if (modifier && Input.GetKeyDown(KeyCode.K)) ToggleCharacterMode(CharacterColorMode.RainbowGradient);
        if (modifier && Input.GetKeyDown(KeyCode.H)) ToggleCharacterMode(CharacterColorMode.Tricolor);
        if (body == null || !IsActive) return;

        if (colorizedBody != body)
        {
            RestoreCharacterColors();
            RestoreScarfColor();
            colorizedBody = body;
            nextUpdate = 0f;
        }
        if (Time.unscaledTime < nextUpdate) return;
        nextUpdate = Time.unscaledTime + 0.1f;
        if (characterColorMode != CharacterColorMode.None) ApplyCharacterColors(body);
        if (rainbowScarfEnabled) ApplyScarfRainbow(body);
    }

    internal void Restore()
    {
        RestoreCharacterColors();
        RestoreScarfColor();
    }

    private void ToggleCharacterMode(CharacterColorMode mode)
    {
        characterColorMode = characterColorMode == mode ? CharacterColorMode.None : mode;
        nextUpdate = 0f;
        if (characterColorMode == CharacterColorMode.None) RestoreCharacterColors();
    }

    private void ApplyScarfRainbow(BodyScript body)
    {
        var scarf = body.GetComponentInChildren<ScarfPhysics>(true);
        var line = scarf == null ? null : scarf.pointRenderer;
        if (line == null) return;
        if (scarfLine != line)
        {
            scarfLine = line;
            originalScarfStart = line.startColor;
            originalScarfEnd = line.endColor;
        }
        var start = Color.HSVToRGB(Mathf.Repeat(Time.unscaledTime * 0.25f, 1f), 1f, 1f);
        var end = Color.HSVToRGB(Mathf.Repeat(Time.unscaledTime * 0.25f + 0.35f, 1f), 1f, 1f);
        start.a = originalScarfStart.a;
        end.a = originalScarfEnd.a;
        line.startColor = start;
        line.endColor = end;
    }

    private void ApplyCharacterColors(BodyScript body)
    {
        foreach (var renderer in body.GetComponentsInChildren<SpriteRenderer>(true))
            if (renderer != null && !originalCharacterColors.ContainsKey(renderer))
                originalCharacterColors[renderer] = renderer.color;

        var top = body.transform.position.y + 2f;
        var bottom = body.transform.position.y - 2f;
        foreach (var pair in originalCharacterColors)
        {
            if (pair.Key == null) continue;
            Color color;
            if (characterColorMode == CharacterColorMode.Tricolor)
                color = body.headTransform != null && pair.Key.transform.IsChildOf(body.headTransform)
                    ? Color.white : pair.Key.transform.position.y < body.transform.position.y - 0.45f
                        ? Color.red : Color.blue;
            else
                color = Color.HSVToRGB(Mathf.Repeat(Time.unscaledTime * 0.25f +
                    Mathf.InverseLerp(bottom, top, pair.Key.transform.position.y) * 0.55f, 1f), 1f, 1f);
            color.a = pair.Value.a;
            pair.Key.color = color;
        }
    }

    private void RestoreCharacterColors()
    {
        foreach (var pair in originalCharacterColors)
            if (pair.Key != null) pair.Key.color = pair.Value;
        originalCharacterColors.Clear();
        colorizedBody = null;
    }

    private void RestoreScarfColor()
    {
        if (scarfLine != null)
        {
            scarfLine.startColor = originalScarfStart;
            scarfLine.endColor = originalScarfEnd;
        }
        scarfLine = null;
    }

    private enum CharacterColorMode : byte { None, RainbowGradient, Tricolor }
}
