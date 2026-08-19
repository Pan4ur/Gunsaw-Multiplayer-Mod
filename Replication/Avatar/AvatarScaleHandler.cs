using UnityEngine;

internal static class AvatarScaleHandler
{
    private const float Minimum = 0.25f;
    private const float Maximum = 2f;

    internal static float Clamp(float value) => Mathf.Clamp(value, Minimum, Maximum);
    
    internal static bool Incorrect(float value) => float.IsNaN(value) || float.IsInfinity(value) || value < Minimum || value > Maximum;

    internal static bool TrySet(BodyScript body, float value)
    {
        if (body == null || float.IsNaN(value) || float.IsInfinity(value) || body.characterScale <= 0f)
            return false;
        var scale = Clamp(value);
        var factor = scale / body.characterScale;
        if (Mathf.Abs(factor - 1f) < 0.001f) return true;
        body.UpdateScale(factor);
        var tails = body.tails;
        if (tails == null) return true;
        foreach (var tail in tails)
            if (tail != null && !tail.IsChildOf(body.transform)) tail.localScale *= factor;
        return true;
    }
}
