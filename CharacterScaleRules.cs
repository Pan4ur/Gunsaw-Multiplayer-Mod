using UnityEngine;

internal static class CharacterScaleRules
{
    internal const float Minimum = 0.25f;
    internal const float Maximum = 2f;

    internal static float Clamp(float value) => Mathf.Clamp(value, Minimum, Maximum);

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
