using UnityEngine;

internal static class CustomPropVisual
{
    private static Material? crateMaterial;

    internal static void AddSprite(GameObject gameObject, Sprite sprite, int sortingOrder = 999)
    {
        var renderer = gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerName = "Background";
        renderer.sortingOrder = sortingOrder;

        if (crateMaterial == null)
            crateMaterial = Resources.Load<GameObject>("Building/Crate")?.GetComponent<SpriteRenderer>()?.sharedMaterial;
      
        if (crateMaterial != null)
            renderer.sharedMaterial = crateMaterial;
    }
}