using UnityEngine;

[DefaultExecutionOrder(1200)]
internal sealed class PropHuntOutline : MonoBehaviour
{
    private readonly Dictionary<Sprite, Sprite> silhouettes = new();
    private readonly List<(SpriteRenderer Source, SpriteRenderer Outline)> renderers = [];
    private BodyScript target;
    private Material material;

    internal void SetTarget(BodyScript body)
    {
        if (target == body) 
            return;
        
        ClearRenderers();
        target = body;
        
        if (body == null) 
            return;
      
        if (material == null)
            material = new Material(Shader.Find("Sprites/Default"));
        
        var sources = body.GetComponentsInChildren<SpriteRenderer>(true)
                                            .Concat((body.tails ?? []).Where(tail => tail != null)
                                            .SelectMany(tail => tail.GetComponentsInChildren<SpriteRenderer>(true)))
                                            .Distinct()
                                            .ToArray();
        
        foreach (var source in sources)
        {
            var obj = new GameObject("Stun outline");
            obj.transform.SetParent(transform, false);
            var outline = obj.AddComponent<SpriteRenderer>();
            outline.sharedMaterial = material;
            outline.sortingLayerID = source.sortingLayerID;
            outline.sortingOrder = sources.Where(other => other.sortingLayerID == source.sortingLayerID)
                                          .Min(other => other.sortingOrder) - 1;
            renderers.Add((source, outline));
        }
    }

    private void LateUpdate()
    {
        var color = new Color(1f, 0.9f, 0f, 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / 1.2f));
        
        foreach (var pair in renderers)
        {
            var source = pair.Source;
            var outline = pair.Outline;
            outline.enabled = target != null && source != null && source.enabled && source.gameObject.activeInHierarchy && source.sprite != null && source.color.a > 0f;
            if (!outline.enabled) continue;
            outline.color = color;
         
            if (!silhouettes.TryGetValue(source.sprite, out var silhouette))
            {
                silhouette = CreateSilhouette(source.sprite);
                silhouettes.Add(source.sprite, silhouette);
            }
          
            outline.sprite = silhouette;
            outline.flipX = source.flipX;
            outline.flipY = source.flipY;
            outline.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            outline.transform.localScale = Vector3.one;
            var parentScale = outline.transform.lossyScale;
            var scale = source.transform.lossyScale;
            outline.transform.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, 1f);
        }
    }

    private static Sprite CreateSilhouette(Sprite source)
    {
        var rect = source.textureRect;
        var width = Mathf.RoundToInt(rect.width);
        var height = Mathf.RoundToInt(rect.height);
        var radius = Mathf.Max(1, Mathf.CeilToInt(source.pixelsPerUnit * 0.035f));
        var buffer = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color32[] pixels;
        try
        {
            Graphics.Blit(source.texture, buffer,
                new Vector2(rect.width / source.texture.width, rect.height / source.texture.height),
                new Vector2(rect.x / source.texture.width, rect.y / source.texture.height));
            RenderTexture.active = buffer;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            pixels = texture.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(buffer);
            Destroy(texture);
        }
        
        var paddedWidth = width + radius * 2;
        var paddedHeight = height + radius * 2;
        texture = new Texture2D(paddedWidth, paddedHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(ExpandAlpha(pixels, width, height, radius));
        texture.Apply(false, true);
        var pivot = source.pivot - source.textureRectOffset + Vector2.one * radius;
        return Sprite.Create(texture, new Rect(0, 0, paddedWidth, paddedHeight), new Vector2(pivot.x / paddedWidth, pivot.y / paddedHeight), source.pixelsPerUnit);
    }

    private static Color32[] ExpandAlpha(Color32[] pixels, int width, int height, int radius)
    {
        var stride = width + radius * 2;
        var result = new Color32[stride * (height + radius * 2)];
        
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var alpha = pixels[y * width + x].a;
            if (alpha == 0) 
                continue;
            
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) 
                    continue;
                
                var index = (y + radius + dy) * stride + x + radius + dx;
                if (result[index].a < alpha) result[index] = new Color32(255, 255, 255, alpha);
            }
        }
        return result;
    }

    private void ClearRenderers()
    {
        foreach (var pair in renderers)
        {
            pair.Outline.enabled = false;
            Destroy(pair.Outline.gameObject);
        }
        renderers.Clear();
    }

    private void OnDestroy()
    {
        ClearRenderers();
        foreach (var silhouette in silhouettes.Values)
        {
            Destroy(silhouette.texture);
            Destroy(silhouette);
        }
        
        if (material != null) 
            Destroy(material);
    }
}