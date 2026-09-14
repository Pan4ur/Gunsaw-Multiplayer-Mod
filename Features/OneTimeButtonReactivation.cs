using UnityEngine;

internal static class OneTimeButtonReactivation
{
    private static readonly HashSet<ButtonScript> used = new();
    private static readonly HashSet<ButtonScript> reactivating = new();
    private static readonly Dictionary<ButtonScript, float> cooldown = new();

    internal static bool IsUsed(ButtonScript button) => button != null && used.Contains(button);

    internal static bool CanReactivate(ButtonScript button) => button != null && (!cooldown.TryGetValue(button, out var time) || Time.unscaledTime >= time);

    internal static void Clear()
    {
        used.Clear();
        reactivating.Clear();
        cooldown.Clear();
    }

    internal static void Reactivate(ButtonScript button)
    {
        if (button == null || !IsUsed(button)) return;
        reactivating.Add(button);
        button.Activated();
    }

    internal static bool TryHandleActivation(ButtonScript button)
    {
        if (!MultiplayerSession.IsConnected || button == null || !button.activateOnce) return false;
        if (!MultiplayerSession.IsHost) return false;
        if (used.Contains(button) && !reactivating.Remove(button)) return true;
        used.Add(button);
        cooldown[button] = Time.unscaledTime + 1f;
        foreach (var target in GameObject.FindGameObjectsWithTag("Activateable"))
            target.SendMessage("Activate", button.id, SendMessageOptions.DontRequireReceiver);
        Sound.Play(button.activateSound, button.transform.position, false, false);
        SetInactive(button);
        GunsawMultiplayerPlugin.World?.NotifyButtonActivated(button);
        return true;
    }

    internal static void SetInactive(ButtonScript button)
    {
        if (button.transform.childCount == 0) return;
        var child = button.transform.GetChild(0);
        var renderer = child.GetComponent<SpriteRenderer>();
        var inactive = Resources.Load<Sprite>("Spawnables/buttonInactive");
        if (renderer != null && inactive != null) renderer.sprite = inactive;
        foreach (var light in child.GetComponents<UnityEngine.Experimental.Rendering.Universal.Light2D>())
            light.color = Color.red;
    }
}