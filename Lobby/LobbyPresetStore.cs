using BepInEx;
using System.Text;
using UnityEngine;

[Serializable]
internal sealed class LobbyPreset
{
    public string name = "";
    public LobbySettings settings = new();
}

[Serializable]
internal sealed class LobbyPresetFile
{
    public List<LobbyPreset> presets = new();
}

internal static class LobbyPresetStore
{
    private static string JsonPath => Path.Combine(Paths.ConfigPath, "GunsawMultiplayer.LobbyPresets.json");
    private static string LegacyPath => Path.Combine(Paths.ConfigPath, "GunsawMultiplayer.LobbyPresets.txt");

    internal static List<LobbyPreset> Load()
    {
        if (File.Exists(JsonPath))
        {
            var json = File.ReadAllText(JsonPath);
            if (json.Contains("\"presets\""))
            {
                var data = JsonUtility.FromJson<LobbyPresetFile>(json);
                if (data?.presets != null)
                    return data.presets.FindAll(preset => preset != null && !string.IsNullOrWhiteSpace(preset.name) && preset.settings != null);
            }
            if (!File.Exists(LegacyPath)) throw new InvalidDataException("Invalid lobby preset JSON.");
        }
        if (!File.Exists(LegacyPath)) return new List<LobbyPreset>();
        var presets = new List<LobbyPreset>();
        foreach (var line in File.ReadAllLines(LegacyPath))
        {
            try
            {
                var fields = line.Split('|');
                if (fields.Length != 25) continue;
                var name = DecodeLegacyField(fields[0]);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var settings = new LobbySettings
                {
                    Pvp = DecodeLegacyBool(fields[1]),
                    CanGrab = DecodeLegacyBool(fields[2]),
                    GrabOnlyUnconscious = DecodeLegacyBool(fields[3]),
                    AllowRespawn = DecodeLegacyBool(fields[4]),
                    AutoRestart = DecodeLegacyBool(fields[5]),
                    RespawnAtStart = DecodeLegacyBool(fields[6]),
                    PlayerCollisions = DecodeLegacyBool(fields[7]),
                    Cheats = DecodeLegacyBool(fields[8]),
                    AllowSwap = DecodeLegacyBool(fields[9]),
                    AllowScaleChanging = DecodeLegacyBool(fields[10]),
                    AllowObserver = DecodeLegacyBool(fields[11]),
                    Teams = DecodeLegacyBool(fields[12]),
                    TeamsCfg = DecodeLegacyField(fields[13]),
                    InitialScale = DecodeLegacyField(fields[14]),
                    StartingWeapon = DecodeLegacyField(fields[15]),
                    RespawnWeapon = DecodeLegacyField(fields[16]),
                    StartingAmmo = DecodeLegacyField(fields[17]),
                    RespawnAmmo = DecodeLegacyField(fields[18]),
                    RespawnTime = DecodeLegacyField(fields[19]),
                    NumberOfLives = DecodeLegacyField(fields[20]),
                    HealthFactor = DecodeLegacyField(fields[21]),
                    RegenFactor = DecodeLegacyField(fields[22]),
                    MaxPlayers = DecodeLegacyField(fields[23])
                };
                if (Enum.TryParse(DecodeLegacyField(fields[24]), out ConnectionMode mode)) settings.ConnectionMode = mode;
                presets.Add(new LobbyPreset { name = name, settings = settings });
            }
            catch (Exception exception) { GunsawMultiplayerPlugin.LogInfo("Invalid legacy lobby preset: " + exception.Message); }
        }
        if (presets.Count > 0)
        {
            try { Save(presets); }
            catch (Exception exception) { GunsawMultiplayerPlugin.LogInfo("Could not migrate lobby presets: " + exception.Message); }
        }
        return presets;
    }

    internal static void Save(IReadOnlyList<LobbyPreset> presets)
    {
        Directory.CreateDirectory(Paths.ConfigPath);
        var data = new LobbyPresetFile { presets = new List<LobbyPreset>(presets) };
        var json = JsonUtility.ToJson(data, true);
        if (!json.Contains("\"presets\"") || (presets.Count > 0 && !json.Contains("\"settings\"")))
            throw new InvalidDataException("Could not serialize lobby presets.");
        var roundTrip = JsonUtility.FromJson<LobbyPresetFile>(json);
        if (roundTrip?.presets == null || roundTrip.presets.Count != presets.Count ||
            roundTrip.presets.Any(preset => preset == null || string.IsNullOrWhiteSpace(preset.name) || preset.settings == null))
            throw new InvalidDataException("Could not serialize lobby presets.");
        var temporaryPath = JsonPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, json);
        if (File.Exists(JsonPath)) File.Replace(temporaryPath, JsonPath, JsonPath + ".bak");
        else File.Move(temporaryPath, JsonPath);
    }

    private static string DecodeLegacyField(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static bool DecodeLegacyBool(string value) => bool.TryParse(DecodeLegacyField(value), out var result) && result;

}
