using HarmonyLib;
using UnityEngine;

internal sealed class NetworkReplica : MonoBehaviour { }

[HarmonyPatch(typeof(MainMenuManager), "UpdateCharacter")]
internal static class CharacterSelectionReplicationPatch
{
    private static void Postfix(MainMenuManager __instance)
    {
        if (!MultiplayerSession.IsHosting && !MultiplayerSession.IsConnected) return;
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
        if (!MultiplayerSession.IsHosting && !MultiplayerSession.IsConnected) return;
        NetworkAvatarReplication.RestoreCharacterSelection();
    }
}

[HarmonyPatch(typeof(WeaponBackShow), "WepChanged")]
internal static class WeaponBackShowSlotGuardPatch
{
    private static void Prefix(BodyScript ___body)
    {
        if (MultiplayerSession.IsConnected && ___body != null && ___body.isPlayer)
            NetworkAvatarReplication.EnsureRespawnWeaponSlots(___body);
    }
}

[HarmonyPatch(typeof(PlayerScript), "BodyAmmoChanged")]
internal static class PlayerAmmoDisplaySlotGuardPatch
{
    private static void Prefix(PlayerScript __instance)
    {
        if (MultiplayerSession.IsConnected) NetworkAvatarReplication.EnsurePlayerAmmoDisplaySlots(__instance);
    }
}

[HarmonyPatch(typeof(WeaponScript), "ReloadWeapon")]
internal static class WeaponReloadStateGuardPatch
{
    private static bool Prefix(WeaponScript __instance)
    {
        return !MultiplayerSession.IsConnected || NetworkAvatarReplication.PrepareWeaponReload(__instance);
    }
}
