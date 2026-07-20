using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class GameResourceCache
{
    private static Dictionary<string, GameObject> prefabsByName;
    private static Dictionary<string, WeaponPreset> presetsByName;
    private static string prefabsScene = "";
    private static string presetsScene = "";

    internal static GameObject FindPrefab(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        EnsurePrefabs();
        GameObject prefab;
        return prefabsByName.TryGetValue(name, out prefab) ? prefab : null;
    }

    internal static WeaponPreset FindWeaponPreset(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        EnsurePresets();
        WeaponPreset preset;
        return presetsByName.TryGetValue(name, out preset) ? preset : null;
    }

    private static void EnsurePrefabs()
    {
        var scene = SceneManager.GetActiveScene().name;
        if (prefabsByName != null && prefabsScene == scene) return;
        prefabsScene = scene;
        prefabsByName = new Dictionary<string, GameObject>();
        foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate == null || candidate.scene.IsValid()) continue;
            var key = CleanName(candidate.name);
            if (!prefabsByName.ContainsKey(key)) prefabsByName.Add(key, candidate);
        }
    }

    private static void EnsurePresets()
    {
        var scene = SceneManager.GetActiveScene().name;
        if (presetsByName != null && presetsScene == scene) return;
        presetsScene = scene;
        presetsByName = new Dictionary<string, WeaponPreset>();
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
        {
            if (preset == null || presetsByName.ContainsKey(preset.name)) continue;
            presetsByName.Add(preset.name, preset);
        }
    }

    private static string CleanName(string name)
    {
        return string.IsNullOrEmpty(name) ? "" : name.Replace("(Clone)", "").Trim();
    }
}
