using System.Reflection;
using System.Text.RegularExpressions;

internal static class KillMessageService
{
    private const string Red = "#9F0928";
    private const string Blue = "#218C83";
    private static readonly Dictionary<string, List<string>> messages =
        new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Random random = new System.Random();
    private static bool loaded;

    internal static string Create(PlayerDeathCause cause, string player, string killer, string weapon)
    {
        Load();
        var category = Category(cause, !string.IsNullOrEmpty(killer));
        List<string> variants;
        if (!messages.TryGetValue(category, out variants) || variants.Count == 0)
            return Render(Fallback(cause, !string.IsNullOrEmpty(killer)), player, killer, weapon);
        return Render(variants[random.Next(variants.Count)], player, killer, weapon);
    }

    private static string Render(string template, string player, string killer, string weapon)
    {
        var result = template.Replace("%player%", Value(player));
        result = result.Replace("%killer%", Value(killer));
        result = result.Replace("%weapon%", Value(weapon));
        return "<color=" + Red + ">" + result + "</color>";
    }

    private static string Value(string value)
    {
        var escaped = (value ?? "Player").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        return "<color=" + Blue + ">" + escaped + "</color>";
    }

    private static string Category(PlayerDeathCause cause, bool hasKiller)
    {
        if (cause == PlayerDeathCause.Explosion) return hasKiller ? "player_explosion" : "explosion";
        if (cause == PlayerDeathCause.SelfKill) return "self_kill";
        if (cause == PlayerDeathCause.Fall) return "fall";
        if (cause == PlayerDeathCause.Fire) return "fire";
        if (cause == PlayerDeathCause.HotPlate) return "hot_plate";
        if (cause == PlayerDeathCause.Saw) return "saw";
        if (cause == PlayerDeathCause.Acid) return "acid";
        if (cause == PlayerDeathCause.Drowning || cause == PlayerDeathCause.Suffocation) return "drowning";
        return hasKiller ? "player_kill" : "";
    }

    private static string Fallback(PlayerDeathCause cause, bool hasKiller)
    {
        if (cause == PlayerDeathCause.Restart) return "%player% restarted the level.";
        if (cause == PlayerDeathCause.SelfKill) return "%player% used /kill.";
        if (cause == PlayerDeathCause.Explosion)
            return hasKiller ? "%killer% blew up %player% with %weapon%." : "%player% was killed in an explosion.";
        if (hasKiller) return "%killer% killed %player% with %weapon%.";
        if (cause == PlayerDeathCause.Saw) return "%player% was cut down by a saw.";
        if (cause == PlayerDeathCause.Acid) return "%player% dissolved in acid.";
        return "%player% died.";
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.EndsWith("killmessages.json", StringComparison.OrdinalIgnoreCase)) continue;
            using (var stream = assembly.GetManifestResourceStream(name))
            using (var reader = stream == null ? null : new StreamReader(stream))
            {
                if (reader == null) return;
                Parse(reader.ReadToEnd());
            }
            return;
        }
    }

    private static void Parse(string json)
    {
        var categoryPattern = new Regex("\\\"name\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"\\s*,\\s*\\\"messages\\\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
        var messagePattern = new Regex("\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"");
        foreach (Match categoryMatch in categoryPattern.Matches(json))
        {
            var name = categoryMatch.Groups[1].Value;
            var values = new List<string>();
            foreach (Match messageMatch in messagePattern.Matches(categoryMatch.Groups[2].Value))
                values.Add(Unescape(messageMatch.Groups[1].Value));
            if (values.Count > 0) messages[name] = values;
        }
    }

    private static string Unescape(string value)
    {
        return value.Replace("\\\\", "\\").Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "\r");
    }

    [Serializable]
    private sealed class KillMessageFile
    {
        public KillMessageCategory[] categories;
    }

    [Serializable]
    private sealed class KillMessageCategory
    {
        public string name;
        public string[] messages;
    }
}
