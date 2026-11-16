using System.Globalization;

internal static class SawTunerValues
{
    internal static string[] Fields = ["health", "healthRegen",  "runForce",     "maxSpeed",  "jumpForce",  "aimSpeedMult", "reloadSpeedMult", "kickForce",  "swimSpeed",  "oxygenUseMult"];
    internal static string[] Labels = ["HEALTH", "HEALTH REGEN", "ACCELERATION", "RUN SPEED", "JUMP FORCE", "AIM SPEED",    "RELOAD SPEED",    "KICK FORCE", "SWIM SPEED", "OXYGEN USE"   ];
    internal static float[] Mins =    [1f,       0f,             0f,             0f,          0f,           0.01f,          0.01f,             0f,           0f,           0f             ];
    internal static float[] Maxs =    [6000f,    100f,           1000f,          25f,        400f,          10f,            10f,               20f,          20f,          10f            ];

    internal static string Key(string path, int stat) => path + "|" + stat.ToString(CultureInfo.InvariantCulture);

    private static bool ValidKey(string key, out int stat)
    {
        stat = -1;
        var split = key.LastIndexOf('|');
        return key.Length <= 200 && split > 0 && key.IndexOfAny(['\r', '\n', '=']) < 0 &&
            int.TryParse(key.Substring(split + 1), out stat) && stat >= 0 && stat < Fields.Length &&
            key.Substring(split + 1) == stat.ToString(CultureInfo.InvariantCulture);
    }

    internal static bool Valid(string key, float value) => ValidKey(key, out var stat) &&
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= Mins[stat] && value <= Maxs[stat];

    internal static Dictionary<string, float> Parse(string text, bool clampToRange = false)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text)) 
            return result;
        
        if (text.Length > 150000) 
            throw new ArgumentException("SawTuner settings are too large.");
        
        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
                continue;
            
            var split = line.LastIndexOf('=');
            if (split <= 0 || !float.TryParse(line.Substring(split + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("Invalid SawTuner value.");
            var key = line.Substring(0, split);
            if (!ValidKey(key, out var stat)) throw new ArgumentException("Invalid SawTuner field.");
            if (clampToRange) value = Math.Max(Mins[stat], Math.Min(Maxs[stat], value));
            if (!Valid(key, value)) throw new ArgumentException("Invalid SawTuner value.");
            result[key] = value;

            if (result.Count > 1280) // 128 saws
                throw new ArgumentException("Too many SawTuner values.");
        }
        return result;
    }

    internal static string Serialize(Dictionary<string, float> values) => string.Join("\n", values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                                                                                  .Select(pair => pair.Key + "=" + pair.Value.ToString("R", CultureInfo.InvariantCulture)));

    internal static SawTunerOverride[] ApiOverrides(string text) =>
    [
        .. Parse(text).Select(entry =>
        {
            var split = entry.Key.LastIndexOf('|');
            return new SawTunerOverride
            {
                character = entry.Key.Substring(0, split),
                stat = Fields[int.Parse(entry.Key.Substring(split + 1), CultureInfo.InvariantCulture)],
                value = entry.Value
            };
        })
    ];
}

[Serializable]
internal sealed class SawTunerOverride
{
    public string character = "";
    public string stat = "";
    public float value;
}