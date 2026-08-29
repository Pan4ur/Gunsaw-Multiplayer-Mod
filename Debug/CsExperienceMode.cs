using System;
using HarmonyLib;
using UnityEngine;

// unhitable AA
internal static class CsExperienceMode
{
    private const float FlickInterval = 0.1f;
    private const float ShotPauseSeconds = 0.25f;

    private static bool active;
    private static bool internalSwitch;
    private static float nextFlick;
    private static float pauseFlickUntil;
    private static int lastAmmo = -1;

    internal static void Toggle()
    {
        SetActive(!active);
    }

    private static void SetActive(bool value)
    {
        if (active == value) return;
        active = value;
        var body = LocalBody();
        if (active)
        {
            nextFlick = 0f;
            pauseFlickUntil = 0f;
            lastAmmo = -1;
        }
        else if (body != null)
        {
            body.customLook = false;
        }
        MultiplayerHud.AddSystemMessage("CS Experience " + (active ? "enabled." : "disabled."));
    }

    internal static void Tick()
    {
        if (!active) return;
        var body = LocalBody();
        if (body == null || !body.isAlive)
        {
            SetActive(false);
            return;
        }

        body.customLook = true;
        body.customLookPos = (Vector2)body.transform.position + Vector2.down * 5f;
        var weapon = body.weapon;
        if (weapon == null)
        {
            lastAmmo = -1;
            return;
        }
        if (lastAmmo >= 0 && weapon.ammo < lastAmmo)
            pauseFlickUntil = Time.unscaledTime + ShotPauseSeconds;
        lastAmmo = weapon.ammo;
        if (Time.unscaledTime < pauseFlickUntil || Time.unscaledTime < nextFlick) return;
        nextFlick = Time.unscaledTime + FlickInterval;
        internalSwitch = true;
        body.SwitchDir(true);
        internalSwitch = false;
    }

    internal static bool BlockSwitchDir(BodyScript body)
    {
        return active && !internalSwitch && body != null && body == LocalBody();
    }

    private static BodyScript LocalBody()
    {
        var player = PlayerScript.player;
        return player == null ? null : player.bodyScript;
    }
}

[HarmonyPatch(typeof(BodyScript), "SwitchDir")]
internal static class CsExperienceSwitchDirPatch
{
    private static bool Prefix(BodyScript __instance)
    {
        return !CsExperienceMode.BlockSwitchDir(__instance);
    }
}
