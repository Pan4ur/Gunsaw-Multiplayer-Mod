using UnityEngine;

internal static class LevelCleanupSystem
{
    private const float Lifetime = 120f;
    private static readonly HashSet<GameObject> bloodProps = [];

    internal static void Register(GameObject gameObject)
    {
        if (gameObject == null || (gameObject.name != "BloodOnWall" && gameObject.name != "BloodDrop" &&
            gameObject.name != "BulletHole" && gameObject.name != "MP BulletHole")) return;
        if (!bloodProps.Add(gameObject)) return;
        gameObject.AddComponent<LevelPropLifetime>();
        UnityEngine.Object.Destroy(gameObject, Lifetime);
    }

    internal static void Unregister(GameObject gameObject)
    {
        bloodProps.Remove(gameObject);
    }

    internal static bool Clear()
    {
        foreach (var gameObject in bloodProps)
        {
            if (gameObject == null) continue;
            UnityEngine.Object.Destroy(gameObject);
        }
        bloodProps.Clear();
        return true;
    }
}

internal sealed class LevelPropLifetime : MonoBehaviour
{
    private void OnDestroy()
    {
        LevelCleanupSystem.Unregister(gameObject);
    }
}
