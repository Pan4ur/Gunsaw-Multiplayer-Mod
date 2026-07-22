using System;
using System.Collections.Generic;
using System.Text;

internal static class NetworkWireId
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;
    private static readonly Dictionary<string, ulong> Cache = new Dictionary<string, ulong>();

    internal static ulong FromString(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0UL;
        ulong id;
        if (Cache.TryGetValue(value, out id)) return id;
        id = OffsetBasis;
        var bytes = Encoding.UTF8.GetBytes(value);
        for (var index = 0; index < bytes.Length; index++)
        {
            id ^= bytes[index];
            id *= Prime;
        }
        Cache[value] = id;
        return id;
    }
}
