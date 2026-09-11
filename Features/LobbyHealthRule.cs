using UnityEngine;

internal static class LobbyHealthRule
{
    internal const float DefaultFactor = 1f;

    internal static float Clamp(float factor)
    {
        return float.IsNaN(factor) || float.IsInfinity(factor) ? DefaultFactor : Mathf.Clamp(factor, 0.01f, 10f);
    }

    internal static void Apply(BodyScript body, float factor)
    {
        if (body == null) return;
        factor = Clamp(factor);
        if (body.maxHealth <= 0f)
        {
            if (body.health <= 0f)
                return;
            
            body.maxHealth = body.health;
        }
        var state = body.gameObject.GetComponent<LobbyHealthFactorState>();
        var baseHealth = state == null ? body.maxHealth : state.BaseMaxHealth;
        if (state == null) state = body.gameObject.AddComponent<LobbyHealthFactorState>();
        state.BaseMaxHealth = baseHealth;
        if (state.BaseDyingStateThreshold == 0f) state.BaseDyingStateThreshold = body.dyingStateTreshold;
        body.maxHealth = baseHealth * factor;
        body.dyingStateTreshold = state.BaseDyingStateThreshold * factor;
        body.health = Mathf.Min(body.health, body.maxHealth);
    }

    internal static void RestoreFull(BodyScript body)
    {
        Apply(body, MultiplayerSession.HealthFactor);
        body.health = body.maxHealth;
    }
}

internal sealed class LobbyHealthFactorState : MonoBehaviour
{
    internal float BaseMaxHealth;
    internal float BaseDyingStateThreshold;
}
