using System.Reflection;
using UnityEngine;

internal static class EmbeddedTextureLoader
{
    private record struct CacheKey
    {
        public string ResourceName;
        public TextureFormat TextureFormat;
        public bool MipChain;
        public bool Linear;
    }

    private static readonly Dictionary<CacheKey, Texture2D> cache = new Dictionary<CacheKey, Texture2D>();

    internal static Texture2D Load(string resourceName, TextureFormat textureFormat, bool mipChain = false, bool linear = false)
    {
        var key = new CacheKey
        {
            ResourceName = resourceName,
            TextureFormat = textureFormat,
            MipChain = mipChain,
            Linear = linear
        };

        Texture2D existing;
        if (cache.TryGetValue(key, out existing)) return existing;

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Debug.LogError("[GunsawMP] Embedded texture not found: " + resourceName);
            return null;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        var texture = new Texture2D(2, 2, textureFormat, mipChain, linear);
        texture.LoadImage(ms.ToArray());
        texture.filterMode = FilterMode.Point;

        cache[key] = texture;
        return texture;
    }
}
