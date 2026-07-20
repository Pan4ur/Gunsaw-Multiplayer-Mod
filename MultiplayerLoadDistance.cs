using UnityEngine;

internal static class MultiplayerLoadDistance
{
    private static bool wasActive;

    internal static void Apply()
    {
        var active = MultiplayerSession.IsHosting || MultiplayerSession.IsConnected;
        if (active)
        {
            ResourceManager.maxDistance = float.PositiveInfinity;
            ObjectUnloader.dist = float.PositiveInfinity;
            ObjectUnloader.distTwo = float.PositiveInfinity;
            wasActive = true;
            return;
        }

        if (!wasActive) return;
        var configuredDistance = PlayerPrefs.GetFloat("loadDistance", 1800f);
        ResourceManager.maxDistance = configuredDistance;
        ObjectUnloader.dist = configuredDistance;
        ObjectUnloader.distTwo = configuredDistance - 300f;
        wasActive = false;
    }
}
