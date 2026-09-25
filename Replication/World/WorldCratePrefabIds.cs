using UnityEngine;

// Generally, only crates spawn dynamically, but I want to add the ability to specify exactly what to spawn in the crate spawner.
// This utility also has a reserve of 244 additional IDs that can be used by mods that add new movable props (if any)
internal static class WorldCratePrefabIds
{
    private static readonly string[] vanillaNames =
    {
        "Crate",
        "ExplodingBarrel",
        "HalfPallet",
        "LabCrate Variant",
        "MetalCrate",
        "MilitaryCrate",
        "Pallet",
        "PalletSplit",
        "SmallCrate",
        "SteelBarrel",
        "WheelPallet"
    };

    private static readonly Dictionary<string, byte> vanillaIds = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, byte> unknownIds = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, byte> cachedIds = new(StringComparer.Ordinal);
    private static readonly string?[] unknownNames = new string?[byte.MaxValue - vanillaNames.Length];
    private static bool unknownsBuilt;

    static WorldCratePrefabIds()
    {
        for (var index = 0; index < vanillaNames.Length; index++)
            vanillaIds.Add(vanillaNames[index], (byte)(index + 1));
    }

    internal static byte GetId(string rawName)
    {
        var name = WorldReplication.CleanCloneName(rawName);
        if (string.IsNullOrEmpty(name)) return 0;
        byte id;
        if (cachedIds.TryGetValue(name, out id)) return id;
        var baseName = StripInstanceOrdinal(name);
        if (vanillaIds.TryGetValue(name, out id) || vanillaIds.TryGetValue(baseName, out id))
        {
            cachedIds[name] = id;
            return id;
        }
        BuildUnknowns();
        if (!unknownIds.TryGetValue(name, out id) && !unknownIds.TryGetValue(baseName, out id)) id = 0;
        cachedIds[name] = id;
        return id;
    }

    internal static string? GetName(byte id)
    {
        if (id == 0) return null;
        if (id <= vanillaNames.Length) return vanillaNames[id - 1];
        BuildUnknowns();
        return unknownNames[id - vanillaNames.Length - 1];
    }

    internal static void ResetUnknowns()
    {
        unknownIds.Clear();
        cachedIds.Clear();
        Array.Clear(unknownNames, 0, unknownNames.Length);
        unknownsBuilt = false;
    }

    private static void BuildUnknowns()
    {
        if (unknownsBuilt) return;
        unknownsBuilt = true;
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate == null || candidate.scene.IsValid() || candidate.transform.parent != null ||
                candidate.GetComponentInChildren<CrateScript>(true) == null) continue;
            var name = WorldReplication.CleanCloneName(candidate.name);
            if (!string.IsNullOrEmpty(name) && !vanillaIds.ContainsKey(name)) names.Add(name);
        }
        var index = 0;
        foreach (var name in names)
        {
            if (index >= unknownNames.Length) break;
            var id = (byte)(vanillaNames.Length + index + 1);
            unknownIds.Add(name, id);
            unknownNames[index] = name;
            index++;
        }
    }

    private static string StripInstanceOrdinal(string name)
    {
        if (!name.EndsWith(")", StringComparison.Ordinal)) return name;
        var start = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (start < 0 || start + 3 >= name.Length) return name;
        for (var index = start + 2; index < name.Length - 1; index++)
            if (name[index] < '0' || name[index] > '9') return name;
        return name.Substring(0, start);
    }
}
