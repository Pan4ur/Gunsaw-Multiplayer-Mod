using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx;

// Available in the editor and synced between players
// Can be enabled or disabled by ID
// Settings
// Species <Random/Milky>
// Weapon <Random/None/Sniper Rifle>
// Interval <1> - spawn interval in seconds
// Limit - the maximum number of npcs it can spawn

[Serializable]
internal sealed class NpcSpawnerData
{
    public string species = "Random";
    public string weapon = "Random";
    public float interval = 5f;
    public int aliveLimit = 0;
    public int activationId;
    public int enabled = 1;
}


internal sealed class NpcSpawnerPropDefinition : CustomPropDefinition<NpcSpawnerData>
{
    private CustomPropField[] fields;

    public override string TypeId
    {
        get { return "MP/NpcSpawner"; }
    }

    public override string DisplayName
    {
        get { return "NPC Spawner"; }
    }

    public override string Description
    {
        get { return "Spawns configured NPCs during play."; }
    }

    public override Sprite Icon
    {
        get
        {
            return EmbeddedSpriteLoader.Load(
                "GunsawMultiplayer.CustomProps.Assets.npc-spawner.png",
                28f,
                new Vector2(0.5f, 0.15f));
        }
    }

    public override CustomPropField[] Fields
    {
        get
        {
            if (fields == null)
            {
                fields = new[]
                {
                    Integer("Activation ID", "Signal ID", value => value.activationId, (value, number) => value.activationId = number, 0),
                    Text("Species", "Random or species name", value => value.species, (value, text) => value.species = text),
                    Text("Weapon", "None, Random or weapon name", value => value.weapon, (value, text) => value.weapon = text),
                    FloatIntegerPair(
                        "Interval / Limit",
                        "Seconds", value => value.interval, (value, number) => value.interval = number, 0f,
                        "Limit count", value => value.aliveLimit, (value, number) => value.aliveLimit = number, 0
                    ),
                    Integer("Enabled", "State", value => value.enabled, (value, number) => value.enabled = number > 0 ? 1 : 0, 0)
                };
            }
            return fields;
        }
    }

    public override void CreateRuntime(GameObject gameObject, NpcSpawnerData data)
    {
        var runtime = gameObject.AddComponent<NpcSpawnerRuntime>();
        runtime.Configure(data);
    }

}

internal sealed class NpcSpawnerRuntime : MonoBehaviour
{
    private NpcSpawnerData data;
    private float nextSpawn;
    private int spawnedTotal;
    private bool active;
    
    private static readonly List<string> BlockedPrefabs =
    [
        "Abomination",
        "BigEnemy",
        "MadnessEnemy",
        "TestEnemy"
    ];

    internal void Configure(NpcSpawnerData value)
    {
        data = value;
        active = value != null && value.enabled > 0;
        nextSpawn = Time.time + (value == null ? 0f : Mathf.Max(0f, value.interval));
    }

    private void Activate(int value)
    {
        if (data == null || value != data.activationId) return;
        active = !active;
        data.enabled = active ? 1 : 0;
        if (active) nextSpawn = Time.time;
    }

    private void Update()
    {
        if (data == null || !active || (MultiplayerSession.IsActive && !MultiplayerSession.IsHost)) return;
        if (Time.time < nextSpawn || (data.aliveLimit > 0 && spawnedTotal >= data.aliveLimit)) return;
        nextSpawn = Time.time + Mathf.Max(0.1f, data.interval);
        var body = Spawn();
        if (body != null) spawnedTotal++;
    }

    private BodyScript Spawn()
    {
        var prefab = SelectNpcPrefab(data.species);
        if (prefab == null) return null;
        var root = Instantiate(prefab, transform.position, transform.rotation);
        var body = root.GetComponentInChildren<BodyScript>(true);
        if (body == null)
        {
            Destroy(root);
            return null;
        }
        StartCoroutine(ConfigureWeapon(body));
        return body;
    }

    private IEnumerator ConfigureWeapon(BodyScript body)
    {
        yield return null;
        if (body == null) yield break;
        var weapon = data.weapon == null ? string.Empty : data.weapon.Trim();
        if (string.IsNullOrEmpty(weapon) || string.Equals(weapon, "None", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(weapon, "Unarmed", StringComparison.OrdinalIgnoreCase))
        {
            EnsureWeapon(body);
            body.ChangeToUnarmed();
            yield break;
        }

        var presets = Resources.LoadAll<WeaponPreset>("Weapons");
        WeaponPreset selected = null;
        if (string.Equals(weapon, "Random", StringComparison.OrdinalIgnoreCase))
        {
            if (presets.Length > 0) selected = presets[UnityEngine.Random.Range(0, presets.Length)];
        }
        else
        {
            foreach (var preset in presets)
            {
                if (preset != null && string.Equals(preset.name, weapon, StringComparison.OrdinalIgnoreCase))
                {
                    selected = preset;
                    break;
                }
            }
        }

        if (selected == null)
        {
            EnsureWeapon(body);
            body.ChangeToUnarmed();
            yield break;
        }

        while (body.weapons.Count <= selected.slot)
        {
            body.weapons.Add(null);
            body.weaponAmmos.Add(0);
        }
        for (var index = 0; index < body.weapons.Count; index++)
        {
            body.weapons[index] = null;
            body.weaponAmmos[index] = 0;
        }
        body.weapons[selected.slot] = selected;
        body.weaponAmmos[selected.slot] = selected.magSize;
        body.ChangeWeapon(selected.slot);
    }

    private static void EnsureWeapon(BodyScript body)
    {
        if (body == null || body.weapon != null || body.gunTransform == null) return;
        var weapon = body.gunTransform.GetComponent<WeaponScript>();
        if (weapon == null) weapon = body.gunTransform.gameObject.AddComponent<WeaponScript>();
        weapon.body = body;
        body.weapon = weapon;
    }

    private static GameObject SelectNpcPrefab(string species)
    {
        var choices = new List<GameObject>();
        var random = string.IsNullOrEmpty(species) || string.Equals(species, "Random", StringComparison.OrdinalIgnoreCase);
        AddNpcResources(choices, Resources.LoadAll<GameObject>("Enemies"), species, random);

        foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate == null || candidate.scene.IsValid() || candidate.transform.parent != null ||
                candidate.GetComponentInChildren<AIScript>(true) == null) continue;
            var body = candidate.GetComponentInChildren<BodyScript>(true);
            if (!IsSpawnableBody(body)) continue;
            if (!random && !string.Equals(body.speciesName, species, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate.name, species, StringComparison.OrdinalIgnoreCase)) continue;
            if (!choices.Contains(candidate)) choices.Add(candidate);
        }

        return choices.Count == 0 ? null : choices[UnityEngine.Random.Range(0, choices.Count)];
    }

    private static void AddNpcResources(List<GameObject> choices, GameObject[] resources, string species, bool random)
    {
        if (resources == null) return;
        foreach (var candidate in resources)
        {
            if (candidate == null || candidate.transform.parent != null ||
                candidate.GetComponentInChildren<AIScript>(true) == null) continue;
            var body = candidate.GetComponentInChildren<BodyScript>(true);
            if (!IsSpawnableBody(body)) continue;
            if (!random && !string.Equals(body.speciesName, species, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate.name, species, StringComparison.OrdinalIgnoreCase)) continue;
            if (!choices.Contains(candidate)) choices.Add(candidate);
        }
    }
    
    private static bool IsSpawnableBody(BodyScript body)
    {
        if (body == null)
            return false;

        string prefabName = body.transform.root.name
            .Replace("(Clone)", "")
            .Trim();
        
        return !BlockedPrefabs.Contains(prefabName);
    }
}
