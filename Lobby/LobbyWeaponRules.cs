internal static class LobbyWeaponRules
{
    internal static List<string> Split(string rule)
    {
        var values = new List<string>();
        var start = 0;
        var depth = 0;
        var text = rule ?? string.Empty;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '[') depth++;
            else if (text[index] == ']' && depth > 0) depth--;
            else if (text[index] == ';' && depth == 0)
            {
                values.Add(text.Substring(start, index - start));
                start = index + 1;
            }
        }
        values.Add(text.Substring(start));
        return values;
    }

    internal static bool TryParseRandom(string value, out bool include, out List<string> names)
    {
        include = false;
        names = new List<string>();
        var text = (value ?? string.Empty).Trim();
        if (string.Equals(text, "Random", StringComparison.OrdinalIgnoreCase)) return true;
        if (!text.StartsWith("Random[", StringComparison.OrdinalIgnoreCase) || !text.EndsWith("]")) return false;
        var filter = text.Substring(7, text.Length - 8).Trim();
        if (filter.StartsWith("in=", StringComparison.OrdinalIgnoreCase)) include = true;
        else if (filter.StartsWith("ex=", StringComparison.OrdinalIgnoreCase)) include = false;
        else return false;
        foreach (var name in filter.Substring(3).Split(';'))
        {
            var trimmed = name.Trim();
            if (!string.IsNullOrEmpty(trimmed)) names.Add(trimmed);
        }
        return true;
    }

    internal static string RandomRule(bool include, IEnumerable<string> names)
    {
        var selected = names.Select(name => name.Trim()).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected.Any(name => name.Contains(";"))) throw new ArgumentException("Weapon names cannot contain semicolons in a Random filter.");
        if (selected.Length == 0)
        {
            if (include) throw new ArgumentException("Select at least one weapon to include.");
            return "Random";
        }
        return "Random[" + (include ? "in=" : "ex=") + string.Join(";", selected) + "]";
    }

    internal static List<string> Sequence(string rule) => Split(rule).Select(name => name.Trim()).Where(name => name.Length > 0).ToList();

    internal static void MoveSequenceEntry(List<string> sequence, int index, int destination)
    {
        if (index < 0 || index >= sequence.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (destination < 0 || destination >= sequence.Count) throw new ArgumentOutOfRangeException(nameof(destination));
        var weapon = sequence[index];
        sequence.RemoveAt(index);
        sequence.Insert(destination, weapon);
    }

    internal static string SlotValue(string rule, int slot)
    {
        if (slot < 0 || slot >= 3) throw new ArgumentOutOfRangeException(nameof(slot));
        if (string.Equals((rule ?? "").Trim(), "Default", StringComparison.OrdinalIgnoreCase)) return "Default";
        var values = Split(rule);
        return slot < values.Count && !string.IsNullOrWhiteSpace(values[slot]) ? values[slot].Trim() : "None";
    }

    internal static string WithSlot(string rule, int slot, string value)
    {
        if (slot < 0 || slot >= 3) throw new ArgumentOutOfRangeException(nameof(slot));
        value = string.IsNullOrWhiteSpace(value) ? "None" : value.Trim();
        if (Split(value).Count != 1) throw new ArgumentException("Choose one weapon or Random rule for this slot.");
        var values = string.Equals((rule ?? "").Trim(), "Default", StringComparison.OrdinalIgnoreCase)
            ? new List<string> { "Default", "Default", "Default" } : Split(rule);
        while (values.Count < 3) values.Add("None");
        values[slot] = value;
        if (values.Count == 3 && values.All(item => string.Equals(item.Trim(), "Default", StringComparison.OrdinalIgnoreCase))) return "Default";
        var result = string.Join(";", values);
        if (result.Length > 512) throw new ArgumentException("Weapon loadout cannot exceed 512 characters.");
        return result;
    }
}