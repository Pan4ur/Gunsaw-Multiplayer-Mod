using BepInEx.Configuration;
using System.Reflection;

[AttributeUsage(AttributeTargets.Field)]
internal sealed class LobbySettingAttribute : Attribute
{
    internal readonly string? ConfigKey;
    internal readonly string Description;
    internal readonly string? CommandLineFlag;

    internal LobbySettingAttribute(string? configKey, string description, string? commandLineFlag = null)
    {
        ConfigKey = configKey;
        Description = description;
        CommandLineFlag = commandLineFlag;
    }
}

internal static class LobbySettingsSchema
{
    private static readonly (FieldInfo Field, LobbySettingAttribute Option)[] fields = DiscoverFields();

    internal static List<Func<bool>> BindConfig(ConfigFile config, Func<LobbySettings> currentSettings)
    {
        var savers = new List<Func<bool>>();
        foreach (var (field, option) in fields)
        {
            if (option.ConfigKey == null) continue;
            if (field.FieldType == typeof(bool))
            {
                var entry = config.Bind("Lobby", option.ConfigKey, (bool)field.GetValue(currentSettings())!, option.Description);
                field.SetValue(currentSettings(), entry.Value);
                savers.Add(() => SetIfChanged(entry, (bool)field.GetValue(currentSettings())!));
            }
            else if (field.FieldType == typeof(string))
            {
                var entry = config.Bind("Lobby", option.ConfigKey, (string?)field.GetValue(currentSettings()) ?? "", option.Description);
                field.SetValue(currentSettings(), entry.Value);
                savers.Add(() => SetIfChanged(entry, (string?)field.GetValue(currentSettings()) ?? ""));
            }
            else throw new InvalidOperationException("Unsupported lobby config field: " + field.Name);
        }
        return savers;
    }

    internal static void ApplyCommandLine(LobbySettings settings, string[] args)
    {
        foreach (var (field, option) in fields)
        {
            var flag = option.CommandLineFlag;
            if (flag == null) continue;
            var value = CommandLineValue(args, flag);
            if (field.FieldType == typeof(bool))
            {
                if (bool.TryParse(value, out var parsed)) field.SetValue(settings, parsed);
                else if (value == "1") field.SetValue(settings, true);
                else if (value == "0") field.SetValue(settings, false);
                else if (HasCommandLineFlag(args, "--no-" + flag.Substring(2))) field.SetValue(settings, false);
                else if (HasCommandLineFlag(args, flag)) field.SetValue(settings, true);
            }
            else if (field.FieldType == typeof(string))
            {
                if (!string.IsNullOrWhiteSpace(value)) field.SetValue(settings, value);
            }
            else if (field.FieldType == typeof(ConnectionMode))
            {
                if (Enum.TryParse(value, true, out ConnectionMode mode)) field.SetValue(settings, mode);
            }
            else throw new InvalidOperationException("Unsupported lobby command-line field: " + field.Name);
        }
    }

    internal static bool HasCommandLineFlag(string[] args, string flag)
    {
        foreach (var arg in args)
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    internal static string CommandLineValue(string[] args, string flag)
    {
        for (var index = 0; index + 1 < args.Length; index++)
            if (string.Equals(args[index], flag, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        return "";
    }

    internal static bool SetIfChanged<T>(ConfigEntry<T> entry, T value)
    {
        if (EqualityComparer<T>.Default.Equals(entry.Value, value)) return false;
        entry.Value = value;
        return true;
    }

    private static (FieldInfo, LobbySettingAttribute)[] DiscoverFields()
    {
        var discovered = new List<(FieldInfo, LobbySettingAttribute)>();
        var configKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commandLineFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in typeof(LobbySettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            var option = field.GetCustomAttribute<LobbySettingAttribute>();
            if (option == null) 
                throw new InvalidOperationException("Lobby setting lacks metadata: " + field.Name);
            if (option.ConfigKey == null && option.CommandLineFlag == null)
                throw new InvalidOperationException("Lobby setting has no persistence or command-line binding: " + field.Name);
            if (option.ConfigKey != null && !configKeys.Add(option.ConfigKey))
                throw new InvalidOperationException("Duplicate lobby config key: " + option.ConfigKey);
            if (option.CommandLineFlag != null && !commandLineFlags.Add(option.CommandLineFlag))
                throw new InvalidOperationException("Duplicate lobby command-line flag: " + option.CommandLineFlag);
            discovered.Add((field, option));
        }
        discovered.Sort((left, right) => left.Item1.MetadataToken.CompareTo(right.Item1.MetadataToken));
        return discovered.ToArray();
    }
}
