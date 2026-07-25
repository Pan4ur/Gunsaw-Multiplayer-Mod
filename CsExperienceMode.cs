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
            if (body != null) GiveSniperRifle(body);
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

    private static void GiveSniperRifle(BodyScript body)
    {
        WeaponPreset sniper = null;
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
        {
            if (preset != null && preset.sprite != null &&
                string.Equals(preset.name, "Sniper Rifle", StringComparison.OrdinalIgnoreCase))
            {
                sniper = preset;
                break;
            }
        }
        if (sniper == null)
            foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
                if (preset != null && preset.sprite != null && !string.IsNullOrEmpty(preset.name) &&
                    preset.name.IndexOf("sniper", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sniper = preset;
                    break;
                }
        if (sniper == null) return;
        NetworkAvatarReplication.EnsureRespawnWeaponSlots(body);
        var slot = 0;
        for (var index = 0; index < body.weapons.Count; index++)
            if (body.weapons[index] == null) { slot = index; break; }
        body.weapons[slot] = sniper;
        body.weaponAmmos[slot] = sniper.magSize;
        if (sniper.ammoType >= 0)
        {
            if (body.ammoAmount == null) body.ammoAmount = new System.Collections.Generic.List<int>();
            while (body.ammoAmount.Count <= sniper.ammoType) body.ammoAmount.Add(0);
            body.ammoAmount[sniper.ammoType] = Mathf.Max(body.ammoAmount[sniper.ammoType], sniper.magSize * 20);
        }
        body.ChangeWeapon(slot);
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
