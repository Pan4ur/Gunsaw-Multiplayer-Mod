using HarmonyLib;

[HarmonyPatch(typeof(AIScript), "Start")]
internal static class NoNpcsPatch
{
    internal static bool Prefix(AIScript __instance)
    {
        if (!MultiplayerSession.IsInLobby || !MultiplayerSession.NoNpcs || __instance.gameObject.scene.name == "LevelEditor" || (__instance.body != null && (__instance.body.isPlayer || NetworkAvatarManager.IsRemoteAvatarBody(__instance.body))))
            return true;

        __instance.gameObject.SetActive(false);
        UnityEngine.Object.Destroy(__instance.gameObject);
        return false;
    }
}