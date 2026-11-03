using UnityEngine;

internal sealed class WeaponPropRespawnSystem : MonoBehaviour
{
    private const float FormatMarker = -987654f;
    private static readonly List<Entry> entries = new List<Entry>();
    private static readonly List<DroppedWeapon> weapons = new List<DroppedWeapon>();
    private static WeaponPropRespawnSystem instance;
    private float nextCheck;

    internal static void Prepare(string levelJson)
    {
        entries.Clear();
        weapons.Clear();
        Level level;
        try { level = JsonUtility.FromJson<Level>(levelJson); }
        catch { return; }
        if (level == null || level.parts == null) return;
        foreach (var part in level.parts)
        {
            if (part == null || part.force.y != FormatMarker || part.force.x < 0f) continue;
            var prefab = Resources.Load<GameObject>(part.path);
            if (prefab == null || !prefab.CompareTag("WepSpawn")) continue;
            var preset = LevelLoader.GetWeaponByName(part.team);
            if (preset == null) continue;
            entries.Add(new Entry { Position = part.pos, Rotation = part.rot, Weapon = preset, Ammo = part.id, Delay = part.force.x });
        }
    }

    internal static void AttachRuntime()
    {
        if (entries.Count == 0 || instance != null) return;
        foreach (var dropped in FindObjectsOfType<DroppedWeapon>(true))
            TrackWeapon(dropped);
        instance = new GameObject("Weapon Prop Respawns").AddComponent<WeaponPropRespawnSystem>();
    }

    private void Update()
    {
        if (MultiplayerSession.IsConnected && !MultiplayerSession.IsHost) return;
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.25f;
        for (var i = weapons.Count - 1; i >= 0; i--)
            if (weapons[i] == null)
                weapons.RemoveAt(i);
        foreach (var entry in entries) UpdateEntry(entry);
    }

    private static void UpdateEntry(Entry entry)
    {
        if (HasWeaponNearby(entry))
        {
            entry.MissingSince = -1f;
            return;
        }
        if (entry.MissingSince < 0f) entry.MissingSince = Time.time;
        if (Time.time < entry.MissingSince + entry.Delay) return;
        var prefab = Resources.Load<GameObject>("Spawnables/PickupWeapon");
        if (prefab == null) return;
        var dropped = Instantiate(prefab, entry.Position, Quaternion.Euler(0f, 0f, entry.Rotation)).GetComponent<DroppedWeapon>();
        if (dropped == null) return;
        dropped.ChangeWeapon(entry.Weapon, entry.Ammo);
        TrackWeapon(dropped);
        entry.NearbyWeapon = dropped;
        entry.MissingSince = -1f;
    }

    internal static void TrackWeapon(DroppedWeapon dropped)
    {
        if (entries.Count > 0 && dropped != null && !weapons.Contains(dropped))
            weapons.Add(dropped);
    }

    private static bool HasWeaponNearby(Entry entry)
    {
        if (IsNearby(entry, entry.NearbyWeapon)) return true;
        entry.NearbyWeapon = null;
        foreach (var dropped in weapons)
        {
            if (!IsNearby(entry, dropped)) continue;
            entry.NearbyWeapon = dropped;
            return true;
        }
        return false;
    }

    private static bool IsNearby(Entry entry, DroppedWeapon dropped) =>
        dropped != null && dropped.stats == entry.Weapon && ((Vector2)dropped.transform.position - entry.Position).sqrMagnitude <= 1f;

    internal static bool IsWeaponSpawn(LevelPartGame levelPart) =>
         levelPart != null && levelPart.part != null &&
               (levelPart.CompareTag("WepSpawn") || string.Equals(levelPart.fullName, "Weapon", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(levelPart.part.path) && levelPart.part.path.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0));

    internal static float EditorTime(LevelPart part) => part != null && part.force.y == FormatMarker ? part.force.x : -1f;

    internal static void SetEditorTime(LevelPart part, float value)
    {
        if (part == null) return;
        part.force.x = Mathf.Max(-1f, value);
        part.force.y = FormatMarker;
    }

    private sealed class Entry
    {
        internal DroppedWeapon NearbyWeapon;
        internal Vector2 Position;
        internal float Rotation;
        internal WeaponPreset Weapon;
        internal int Ammo;
        internal float Delay;
        internal float MissingSince = -1f;
    }
}