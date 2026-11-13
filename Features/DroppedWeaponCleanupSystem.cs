using System.Reflection;
using HarmonyLib;
using UnityEngine;

internal static class DroppedWeaponCleanupSystem
{
    internal static int DropDepth;

    internal static void Register(DroppedWeapon dropped)
    {
        if (!MultiplayerSession.IsHosting || dropped == null || dropped.stats == null)
            return;
        
        var lifetime = dropped.GetComponent<DroppedWeaponLifetime>();
        lifetime ??= dropped.gameObject.AddComponent<DroppedWeaponLifetime>();
        lifetime.ExpiresAt = Time.unscaledTime + 60f;
    }
}

internal sealed class DroppedWeaponLifetime : MonoBehaviour
{
    internal float ExpiresAt;

    private void Update()
    {
        if (!MultiplayerSession.IsHosting)
        {
            Destroy(this);
            return;
        }
        
        if (Time.unscaledTime >= ExpiresAt) 
            Destroy(gameObject);
    }
}
