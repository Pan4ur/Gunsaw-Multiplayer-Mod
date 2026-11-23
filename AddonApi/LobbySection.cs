using System.Globalization;

namespace GunsawMultiplayer;

public static partial class MultiplayerApi
{
    internal static readonly Dictionary<string, LobbySection> LobbySections = new(StringComparer.Ordinal);

    public static void RegisterLobbySection(string pluginGuid, string title, Action<LobbySection> build, Action? onHostSettingsApplied = null)
    {
        if (string.IsNullOrWhiteSpace(pluginGuid))
            throw new ArgumentException("A plugin GUID is required.", nameof(pluginGuid));
        
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A section title is required.", nameof(title));
        
        if (build == null)
            throw new ArgumentNullException(nameof(build));
        
        if (LobbySections.ContainsKey(pluginGuid))
            throw new ArgumentException("Lobby section is already registered.", nameof(pluginGuid));
        
        var section = new LobbySection(title, onHostSettingsApplied);
        build(section);
        section.Seal();
        LobbySections.Add(pluginGuid, section);
    }

    internal static void NotifyHostSettingsApplied()
    {
        foreach (var section in LobbySections.Values.ToArray())
            if (section.OnApplied != null)
                Invoke(section.Title, section.OnApplied);
    }
}

public sealed class LobbySection
{
    internal readonly string Title;
    internal readonly Action? OnApplied;
    internal readonly List<(string Label, Func<bool> Read, Action<bool> Write)> Toggles = new ();
    internal readonly List<(string Label, Action Click, bool AllowGuests)> Buttons = new ();
    internal readonly List<(string Label, Func<string> Read, Func<string, bool> Write, int Length, InputKind Kind, string? Range)> Inputs = new();
    internal readonly List<(string Label, string[] Options, Func<int> Read, Action<int> Write)> Choices = new();
    private bool sealedSection;

    internal enum InputKind { Text, Integer, Decimal }

    internal LobbySection(string title, Action? onApplied)
    {
        Title = title;
        OnApplied = onApplied;
    }

    internal void Seal() => sealedSection = true;

    public void AddToggle(string label, Func<bool> read, Action<bool> write)
    {
        ValidateLabel(label);
        Toggles.Add((label, read ?? throw new ArgumentNullException(nameof(read)), write ?? throw new ArgumentNullException(nameof(write))));
    }

    public void AddButton(string label, Action click, bool allowGuests = false)
    {
        ValidateLabel(label);
        Buttons.Add((label, click ?? throw new ArgumentNullException(nameof(click)), allowGuests));
    }

    public void AddText(string label, Func<string> read, Action<string> write, int maxLength = 128)
    {
        ValidateBinding(label, read, write);
        if (maxLength < 1)
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        
        Inputs.Add((label, read, value => { write(value); return true; }, maxLength, InputKind.Text, null));
    }

    public void AddInt(string label, Func<int> read, Action<int> write, int? minimum = null, int? maximum = null)
    {
        ValidateBinding(label, read, write);
        if (minimum > maximum) throw new ArgumentOutOfRangeException(nameof(maximum));
        var culture = CultureInfo.InvariantCulture;
        Inputs.Add((label, () => read().ToString(culture), value =>
        {
            if (!int.TryParse(value, NumberStyles.Integer, culture, out var number))
                return false;
            
            write(Math.Min(maximum ?? int.MaxValue, Math.Max(minimum ?? int.MinValue, number)));
            return true;
        }, 11, InputKind.Integer, FormatRange(minimum?.ToString(culture), maximum?.ToString(culture))));
    }

    public void AddFloat(string label, Func<float> read, Action<float> write, float? minimum = null, float? maximum = null)
    {
        ValidateBinding(label, read, write);
        if (minimum.HasValue && !IsFinite(minimum.Value))
            throw new ArgumentOutOfRangeException(nameof(minimum));
        
        if (maximum.HasValue && !IsFinite(maximum.Value) || minimum > maximum)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        
        var culture = CultureInfo.InvariantCulture;
        Inputs.Add((label, () => read().ToString("R", culture), value =>
        {
            if (!float.TryParse(value.Replace(',', '.'), NumberStyles.Float, culture, out var number) || !IsFinite(number)) return false;
            write(Math.Min(maximum ?? float.MaxValue, Math.Max(minimum ?? float.MinValue, number)));
            return true;
        }, 32, InputKind.Decimal, FormatRange(minimum?.ToString("R", culture), maximum?.ToString("R", culture))));
    }

    public void AddChoice<T>(string label, IReadOnlyList<(T Value, string Label)> options, Func<T> read, Action<T> write)
    {
        ValidateBinding(label, read, write);
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        
        var items = options.ToArray();
        if (items.Length == 0 || items.Any(item => string.IsNullOrWhiteSpace(item.Label)) || items.Select(item => item.Value).Distinct().Count() != items.Length)
            throw new ArgumentException("Choices need labels and distinct values.", nameof(options));

        Choices.Add((label, items.Select(item => item.Label).ToArray(), () =>
        {
            var value = read();
            return Array.FindIndex(items, item => EqualityComparer<T>.Default.Equals(item.Value, value));
        }, index => write(items[index].Value)));
    }

    private void ValidateBinding<T>(string label, Func<T> read, Action<T> write)
    {
        ValidateLabel(label);
        if (read == null)
            throw new ArgumentNullException(nameof(read));
        
        if (write == null)
            throw new ArgumentNullException(nameof(write));
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static string? FormatRange(string? minimum, string? maximum)
    {
        if (minimum == null) return maximum == null ? null : "≤ " + maximum;
        return maximum == null ? "≥ " + minimum : "[" + minimum + ".." + maximum + "]";
    }

    private void ValidateLabel(string label)
    {
        if (sealedSection)
            throw new InvalidOperationException("Add controls when registering the section.");
        
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("A control label is required.", nameof(label));
    }
}