using UnityEngine;

internal static class LobbyHealthRule
{
    internal const float DefaultFactor = 1f;
    internal const float DefaultUniformHPValue = 100f;

    internal static float ClampUniformHPValue(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? DefaultUniformHPValue : Mathf.Clamp(value, 1f, 10000f);

    internal static float Clamp(float factor) =>
        float.IsNaN(factor) || float.IsInfinity(factor) ? DefaultFactor : Mathf.Clamp(factor, 0.01f, 10f);

    internal static float ClampDamage(BodyScript body, float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount)) return 0f;
        var limit = body == null ? 1000f : Mathf.Max(1000f, Mathf.Max(body.maxHealth, body.health) + 1f);
        return Mathf.Clamp(amount, 0f, limit);
    }

    internal static void Apply(BodyScript body, float factor, float baseMaxHealth = 0f)
    {
        if (body == null) return;
        factor = Clamp(factor);
        var state = body.gameObject.GetComponent<LobbyHealthFactorState>();
        if (state == null) state = body.gameObject.AddComponent<LobbyHealthFactorState>();
        if (state.BaseMaxHealth <= 0f)
            state.BaseMaxHealth = baseMaxHealth > 0f ? baseMaxHealth : body.maxHealth > 0f ? body.maxHealth : body.health;
        if (state.BaseDyingStateThreshold == 0f) state.BaseDyingStateThreshold = body.dyingStateTreshold;
        var uniformHP = MultiplayerSession.IsActive && MultiplayerSession.UniformHP;
        body.maxHealth = uniformHP ? ClampUniformHPValue(MultiplayerSession.UHPValue) : state.BaseMaxHealth * factor;
        var thresholdFactor = uniformHP && state.BaseMaxHealth > 0f ? body.maxHealth / state.BaseMaxHealth : factor;
        body.dyingStateTreshold = state.BaseDyingStateThreshold * thresholdFactor;
        body.health = Mathf.Min(body.health, body.maxHealth);
    }

    internal static void RestoreFull(BodyScript body)
    {
        Apply(body, MultiplayerSession.HealthFactor);
        body.health = body.maxHealth;
        body.stamina = body.maxHealth;
        var player = PlayerScript.player;
        if (player != null && player.bodyScript == body)
        {
            player.curHealthShow = body.maxHealth;
            player.curStaminaShow = body.maxHealth;
        }
    }
}

internal sealed class LobbyHealthFactorState : MonoBehaviour
{
    internal float BaseMaxHealth;
    internal float BaseDyingStateThreshold;

    private void Update()
    {
        if (MultiplayerSession.IsActive) return;
        LobbyHealthRule.Apply(GetComponent<BodyScript>(), LobbyHealthRule.DefaultFactor);
        Destroy(this);
    }
}
