using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(BodyScript), "OnCollisionEnter2D")]
internal static class PropHuntHunterBodyCollisionPatch
{
    private static bool Prefix(BodyScript __instance, Collision2D collision) => !PropHuntHunterLimbCollisionPatch.Friendly(__instance, collision.collider);
}

[HarmonyPatch(typeof(LimbScript), "OnCollisionEnter2D")]
internal static class PropHuntHunterLimbCollisionPatch
{
    private static bool Prefix(LimbScript __instance, Collision2D col) => !Friendly(__instance.body, col.collider);

    internal static bool Friendly(BodyScript body, Collider2D other)
    {
        if (!PropHuntSystem.Enabled || body == null || other == null)
            return false;
        
        var target = other.GetComponent<LimbScript>()?.body ?? other.GetComponentInParent<BodyScript>();
        return PropHuntSystem.FriendlyHunters(NetworkAvatarManager.PeerIdOfBody(body), NetworkAvatarManager.PeerIdOfBody(target));
    }
}
