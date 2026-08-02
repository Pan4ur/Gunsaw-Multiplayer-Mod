using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

internal static class EmbeddedSpriteLoader
{
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    internal static Sprite Load(string resourceName, float pixelsPerUnit, Vector2 pivot)
    {
        Sprite existing;
        if (cache.TryGetValue(resourceName, out existing)) return existing;

        var assembly = Assembly.GetExecutingAssembly();
        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null) return null;
            var bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(bytes);
            texture.filterMode = FilterMode.Point;
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                pivot,
                pixelsPerUnit
            );
            cache[resourceName] = sprite;
            return sprite;
        }
    }
}