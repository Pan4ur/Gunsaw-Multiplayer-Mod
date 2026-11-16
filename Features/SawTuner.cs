using System.Reflection;
using UnityEngine;

internal static class SawTuner
{
    internal static Dictionary<string, float> Values { get; private set; } = new();
    internal static readonly FieldInfo[] Fields = [.. SawTunerValues.Fields.Select(name => typeof(BodyScript).GetField(name)!)];
    private static readonly object snapshotLock = new();
    private static int revision;
    private static string overrides = "";
    internal static bool ApplyToPlayers { get; private set; } = true;
    internal static bool ApplyToNpcs { get; private set; }
    internal static (int Revision, string Overrides, bool Players, bool Npcs) Snapshot
    {
        get { lock (snapshotLock) return (revision, overrides, ApplyToPlayers, ApplyToNpcs); }
    }
    private static int receivedRevision;
    internal static int Generation { get; private set; }

    internal static void Reset()
    {
        Values = new();
        lock (snapshotLock) { revision = 0; overrides = ""; ApplyToPlayers = true; ApplyToNpcs = false; }
        receivedRevision = 0;
        Generation++;
    }

    internal static string Normalize(string text, bool clampToRange = false)
    {
        var values = SawTunerValues.Parse(text, clampToRange);
        foreach (var key in values.Keys.ToArray())
        {
            var split = key.LastIndexOf('|');
            var path = key.Substring(0, split);
            var stat = int.Parse(key.Substring(split + 1));
            if (!NetworkAvatarManager.knownCharacterPrefabs.Contains(path)) throw new ArgumentException("Unknown SawTuner character.");
            var body = Prefab(path);
            if (body == null) throw new ArgumentException("Missing SawTuner character.");
            if (Mathf.Approximately(values[key], DefaultValue(body, stat))) values.Remove(key);
        }
        return SawTunerValues.Serialize(values);
    }

    internal static void Configure(string text, bool players = true, bool npcs = false)
    {
        var normalized = Normalize(text);
        lock (snapshotLock)
        {
            if (normalized == overrides && players == ApplyToPlayers && npcs == ApplyToNpcs) return;
            overrides = normalized;
            ApplyToPlayers = players;
            ApplyToNpcs = npcs;
            revision++;
        }
        Receive(revision, normalized, players, npcs);
    }

    internal static void Receive(int revision, string text, bool players = true, bool npcs = false)
    {
        if (revision < receivedRevision) return;
        var values = SawTunerValues.Parse(Normalize(text));
        Values = values;
        lock (snapshotLock) { ApplyToPlayers = players; ApplyToNpcs = npcs; }
        receivedRevision = revision;
        Generation++;
    }

    internal static BodyScript? Prefab(string path)
    {
        var prefab = Resources.Load<GameObject>(path);
        var body = prefab == null ? null : prefab.GetComponentInChildren<BodyScript>(true);
        return body;
    }

    internal static float DefaultValue(BodyScript body, int stat) => stat == 0 ? body.health * 2f : stat == 1 ? body.regenOnSwap : (float)Fields[stat].GetValue(body);

    internal static string Path(BodyScript body)
    {
        if (body == null) return "";
        var state = body.GetComponent<SawTunerBodyState>();
        return state != null ? state.CharacterPath : LocalPlayerReplication.ResolveCharacterPrefab(body);
    }

    internal static float Value(BodyScript body, int stat, float fallback) => MultiplayerSession.IsActive && (body.isPlayer ? ApplyToPlayers : ApplyToNpcs) && Values.Count > 0 &&
        Values.TryGetValue(SawTunerValues.Key(Path(body), stat), out var value) ? value : fallback;

    internal static void Apply(BodyScript body, string? path = null)
    {
        if (body == null || !MultiplayerSession.IsActive) return;
        var state = body.GetComponent<SawTunerBodyState>();
        if (state == null)
        {
            path ??= LocalPlayerReplication.ResolveCharacterPrefab(body);
            if (!body.isPlayer)
            {
                var prefab = Prefab(path);
                if (prefab == null || prefab.speciesName != body.speciesName) return;
            }
            state = body.gameObject.AddComponent<SawTunerBodyState>();
            state.CharacterPath = path;
        }
        state.Apply(body);
    }
}

internal sealed class SawTunerBodyState : MonoBehaviour
{
    internal string CharacterPath = "";
    private float[]? original;
    private int generation = -1;
    private bool wasPlayer;
    private float baseMaxHealth, baseHealthRegen, baseThreshold;

    internal void Apply(BodyScript body)
    {
        var first = original == null;
        if (first)
        {
            original = SawTuner.Fields.Select(field => (float)field.GetValue(body)).ToArray();
            baseMaxHealth = body.maxHealth > 0f ? body.maxHealth : body.health;
            baseHealthRegen = body.healthRegen;
            baseThreshold = body.dyingStateTreshold;
        }
        if (generation == SawTuner.Generation && wasPlayer == body.isPlayer) return;
        generation = SawTuner.Generation;
        wasPlayer = body.isPlayer;
        for (var i = 2; i < original!.Length; i++) SawTuner.Fields[i].SetValue(body, SawTuner.Value(body, i, original[i]));
        if (!body.isPlayer)
        {
            var fullHealth = first && body.health >= baseMaxHealth;
            ApplyNpcHealth(body, true);
            if (fullHealth) body.health = body.maxHealth;
        }
    }

    private void ApplyNpcHealth(BodyScript body, bool tuned)
    {
        body.maxHealth = tuned ? SawTuner.Value(body, 0, baseMaxHealth) : baseMaxHealth;
        body.healthRegen = tuned ? SawTuner.Value(body, 1, baseHealthRegen) : baseHealthRegen;
        body.dyingStateTreshold = baseMaxHealth > 0f ? baseThreshold * body.maxHealth / baseMaxHealth : baseThreshold;
        body.health = Mathf.Min(body.health, body.maxHealth);
        body.stamina = Mathf.Min(body.stamina, body.maxHealth);
    }

    internal void RestoreNpc()
    {
        var body = GetComponent<BodyScript>();
        if (body.isPlayer || original == null) return;
        for (var i = 2; i < original.Length; i++) SawTuner.Fields[i].SetValue(body, original[i]);
        ApplyNpcHealth(body, false);
        generation = -1;
    }

    private void Update()
    {
        Apply(GetComponent<BodyScript>());
        if (!MultiplayerSession.IsActive) Destroy(this);
    }
}
