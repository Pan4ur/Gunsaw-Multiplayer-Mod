using HarmonyLib;
using UnityEngine;

internal sealed class NetworkReplica : MonoBehaviour;

// ts wouldn't exist if it weren't for G4A and Albino
[HarmonyPatch(typeof(MainMenuManager), "Start")]
internal static class CharacterSelectionMenuRestorePatch
{
    private static void Prefix(MainMenuManager __instance, out string __state)
    {
        var s = LocalPlayerReplication.selectedCharacterPrefab;
        var i = __instance.characters.FindIndex(c => c != null && c.prefabPath == s);
        if (i >= 0) 
            PlayerPrefs.SetInt("charIndex", i);
        
        __state = i < 0 ? s : "";
    }

    private static void Postfix(string __state)
    {
        if (string.IsNullOrEmpty(__state)) return;
        LocalPlayerReplication.selectedCharacterPrefab = __state;
        NetworkAvatarManager.RestoreCharacterSelection();
    }
}

[HarmonyPatch(typeof(MainMenuManager), "UpdateCharacter")]
internal static class CharacterSelectionReplicationPatch
{
    private static void Postfix(MainMenuManager __instance)
    {
        NetworkAvatarManager.CaptureCharacterMenu(__instance);
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
        NetworkAvatarManager.RestoreCharacterSelection();
    }

    private static void Postfix(PlayerScript __instance)
    {
        if (!MultiplayerSession.IsHosting && !MultiplayerSession.IsConnected) return;
        if (__instance == null || __instance.bodyScript == null) return;
        LobbyRegenRule.Apply(__instance.bodyScript, MultiplayerSession.RegenFactor);
        LobbyHealthRule.RestoreFull(__instance.bodyScript);
        BlackoutRule.RegisterBody(__instance.bodyScript);
        __instance.curHealthShow = __instance.bodyScript.maxHealth;
    }
}

[HarmonyPatch(typeof(WeaponBackShow), "WepChanged")]
internal static class WeaponBackShowSlotGuardPatch
{
    private static void Prefix(BodyScript ___body)
    {
        if (MultiplayerSession.IsInLobby && ___body != null && ___body.isPlayer)
            LocalPlayerReplication.EnsureRespawnWeaponSlots(___body);
    }
}

[HarmonyPatch(typeof(PlayerScript), "BodyAmmoChanged")]
internal static class PlayerAmmoDisplaySlotGuardPatch
{
    private static void Prefix(PlayerScript __instance)
    {
        if (MultiplayerSession.IsInLobby) LocalPlayerReplication.EnsurePlayerAmmoDisplaySlots(__instance);
    }
}

[HarmonyPatch(typeof(WeaponScript), "ReloadWeapon")]
internal static class WeaponReloadStateGuardPatch
{
    private static bool Prefix(WeaponScript __instance)
    {
        return !MultiplayerSession.IsInLobby || LocalPlayerReplication.PrepareWeaponReload(__instance);
    }
}

[HarmonyPatch(typeof(BodyScript), nameof(BodyScript.GetClosestLimb))]
internal static class MultiplayerBodyClosestLimbPatch
{
    private static void Postfix(BodyScript __instance, LimbScript __result)
    {
        NetworkAvatarManager.RecordBodyColliderHit(__instance, __result);
    }
}
