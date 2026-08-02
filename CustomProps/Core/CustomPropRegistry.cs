using System;
using System.Collections.Generic;

internal static class CustomPropRegistry
{
    private static readonly Dictionary<string, ICustomPropDefinition> definitions =
        new Dictionary<string, ICustomPropDefinition>(StringComparer.OrdinalIgnoreCase);

    internal static IEnumerable<ICustomPropDefinition> All
    {
        get { return definitions.Values; }
    }

    internal static void Register(ICustomPropDefinition definition)
    {
        if (definition == null) throw new ArgumentNullException("definition");
        if (string.IsNullOrEmpty(definition.TypeId)) throw new ArgumentException("Custom prop TypeId is empty.");
        if (definitions.ContainsKey(definition.TypeId)) return;
        definitions.Add(definition.TypeId, definition);
    }

    internal static bool TryGet(string typeId, out ICustomPropDefinition definition)
    {
        return definitions.TryGetValue(typeId ?? string.Empty, out definition);
    }
}