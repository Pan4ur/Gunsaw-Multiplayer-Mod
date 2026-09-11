using UnityEngine;

internal static class LobbyRegenRule
{
    internal const float DefaultFactor = 1f;

    internal static float Clamp(float factor)
    {
        return float.IsNaN(factor) || float.IsInfinity(factor) ? DefaultFactor : Mathf.Clamp(factor, 0f, 10f);
    }

    internal static void Apply(BodyScript body, float factor)
    {
        if (body == null) return;
        factor = Clamp(factor);
        var state = body.gameObject.GetComponent<LobbyRegenFactorState>();
        if (state == null)
        {
            state = body.gameObject.AddComponent<LobbyRegenFactorState>();
            state.BaseHealthRegen = body.healthRegen;
        }
        else if (!Mathf.Approximately(body.healthRegen, state.BaseHealthRegen * state.AppliedFactor)) state.BaseHealthRegen = body.healthRegen;
        state.AppliedFactor = factor;
        body.healthRegen = state.BaseHealthRegen * factor;
    }
}

internal sealed class LobbyRegenFactorState : MonoBehaviour
{
    internal float BaseHealthRegen;
    internal float AppliedFactor = LobbyRegenRule.DefaultFactor;
}
