using BepInEx;
using DiscordIPC.Internal;
using System.Globalization;
using System.Reflection;
using System.Text;

[Serializable]
internal sealed class LobbyPreset
{
    public string name = "";
    public LobbySettings settings = new();
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
            try { return Deserialize(json); }
            catch (Exception exception) when (File.Exists(LegacyPath))
            {
                GunsawMultiplayerPlugin.LogInfo("Invalid lobby preset JSON, loading legacy presets: " + exception.Message);
            }
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
        var entries = new List<object>(presets.Count);
        foreach (var preset in presets)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.name) || preset.settings == null)
                throw new InvalidDataException("Invalid lobby preset.");
            var settings = new Dictionary<string, object>();
            foreach (var field in SettingFields)
            {
                var value = field.GetValue(preset.settings);
                settings[field.Name] = field.FieldType.IsEnum ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : value!;
            }
            entries.Add(new Dictionary<string, object> { ["name"] = preset.name, ["settings"] = settings });
        }
        var json = MiniJson.Serialize(new Dictionary<string, object> { ["presets"] = entries });
        var roundTrip = Deserialize(json);
        if (roundTrip.Count != presets.Count || roundTrip.Where((preset, index) =>
                preset.name != presets[index].name || SettingFields.Any(field =>
                    !Equals(field.GetValue(preset.settings), field.GetValue(presets[index].settings)))).Any())
            throw new InvalidDataException("Could not serialize lobby presets.");
        var temporaryPath = JsonPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, json);
        if (File.Exists(JsonPath)) File.Replace(temporaryPath, JsonPath, JsonPath + ".bak");
        else File.Move(temporaryPath, JsonPath);
    }

    private static readonly FieldInfo[] SettingFields = typeof(LobbySettings).GetFields(BindingFlags.Instance | BindingFlags.Public);

    private static List<LobbyPreset> Deserialize(string json)
    {
        if (!(MiniJson.Deserialize(json) is Dictionary<string, object> data) ||
            !data.TryGetValue("presets", out var value) || !(value is List<object> entries))
            throw new InvalidDataException("Invalid lobby preset JSON.");
        var presets = new List<LobbyPreset>();
        foreach (var entry in entries)
        {
            if (!(entry is Dictionary<string, object> preset) ||
                !preset.TryGetValue("name", out var name) || !(name is string text) || string.IsNullOrWhiteSpace(text) ||
                !preset.TryGetValue("settings", out var settingsValue) || !(settingsValue is Dictionary<string, object> values))
                continue;
            var settings = new LobbySettings();
            foreach (var field in SettingFields)
            {
                if (!values.TryGetValue(field.Name, out var fieldValue)) continue;
                if (field.FieldType.IsEnum && fieldValue is long number)
                    field.SetValue(settings, Enum.ToObject(field.FieldType, number));
                else if ((fieldValue == null && field.FieldType == typeof(string)) || field.FieldType.IsInstanceOfType(fieldValue))
                    field.SetValue(settings, fieldValue);
                else throw new InvalidDataException("Invalid lobby preset setting: " + field.Name);
            }
            presets.Add(new LobbyPreset { name = text, settings = settings });
        }
        return presets;
    }

    private static string DecodeLegacyField(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static bool DecodeLegacyBool(string value) => bool.TryParse(DecodeLegacyField(value), out var result) && result;

}
