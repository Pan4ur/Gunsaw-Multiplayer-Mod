using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class NetworkReplica : MonoBehaviour { }

[HarmonyPatch(typeof(MainMenuManager), "UpdateCharacter")]
internal static class CharacterSelectionReplicationPatch
{
    private static void Postfix(MainMenuManager __instance)
    {
        NetworkAvatarReplication.CaptureCharacterMenu(__instance);
    }
}

internal sealed class RemotePlayerInfo
{
    internal ushort PeerId;
    internal string Name = "Player";
    internal BodyScript Body;
    internal Vector2 AuthoritativePosition;
    internal bool HasAuthoritativePosition;
    internal int PingMs = -1;
}

[HarmonyPatch(typeof(PlayerScript), "Start")]
internal static class LocalCharacterCreationPatch
{
    private static void Prefix()
    {
        NetworkAvatarReplication.RestoreCharacterSelection();
    }
}

[HarmonyPatch(typeof(WeaponBackShow), "WepChanged")]
internal static class WeaponBackShowSlotGuardPatch
{
    private static void Prefix(BodyScript ___body)
    {
        if (___body != null && ___body.isPlayer)
            NetworkAvatarReplication.EnsureRespawnWeaponSlots(___body);
    }
}

[HarmonyPatch(typeof(PlayerScript), "BodyAmmoChanged")]
internal static class PlayerAmmoDisplaySlotGuardPatch
{
    private static void Prefix(PlayerScript __instance)
    {
        NetworkAvatarReplication.EnsurePlayerAmmoDisplaySlots(__instance);
    }
}

[HarmonyPatch(typeof(WeaponScript), "ReloadWeapon")]
internal static class WeaponReloadStateGuardPatch
{
    private static bool Prefix(WeaponScript __instance)
    {
        return NetworkAvatarReplication.PrepareWeaponReload(__instance);
    }
}
