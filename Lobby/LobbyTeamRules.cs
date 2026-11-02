using UnityEngine;

internal sealed class LobbyTeamEntry
{
    internal string Name;
    internal string ColorValue;

    internal LobbyTeamEntry(string name, string color)
    {
        Name = name;
        ColorValue = color;
    }
}

internal static class LobbyTeamRules
{
    internal static List<LobbyTeamEntry> Parse(string rule)
    {
        var teams = new List<LobbyTeamEntry>();
        if (string.IsNullOrWhiteSpace(rule)) return teams;
        foreach (var item in rule.Split(';'))
        {
            var separator = item.IndexOf(':');
            teams.Add(separator < 0 ? new LobbyTeamEntry(item, "") :
                new LobbyTeamEntry(item.Substring(0, separator), item.Substring(separator + 1)));
        }
        return teams;
    }

    internal static string Serialize(IEnumerable<LobbyTeamEntry> teams)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>();
        foreach (var team in teams)
        {
            var name = team.Name.Trim();
            if (name.Length == 0) throw new ArgumentException("Each team needs a name.");
            if (name.Contains(":") || name.Contains(";")) throw new ArgumentException("Team names cannot contain : or ;.");
            if (!names.Add(name)) throw new ArgumentException("Team names must be unique: " + name);
            if (!ColorUtility.TryParseHtmlString(team.ColorValue.Trim(), out _))
                throw new ArgumentException("Choose a valid color for " + name + ".");
            values.Add(team.Name + ":" + team.ColorValue);
        }
        return string.Join(";", values);
    }
}