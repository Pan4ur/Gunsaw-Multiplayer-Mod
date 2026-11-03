using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class ScreenMessageOverlay
{
    private GameObject root;
    private TMP_Text text;
    private Image background;

    internal void Show(string message, float opacity = 0.75f, Vector2? position = null, float fontSize = 48f)
    {
        if (root == null)
        {
            var template = PlayerScript.player?.ammoText;
            if (template == null) return;
            root = new GameObject("Screen message", typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 1;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var panel = new GameObject("Shade", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            background = panel.GetComponent<Image>();
            background.raycastTarget = false;
            var label = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(panel.transform, false);
            text = label.GetComponent<TextMeshProUGUI>();
            text.font = template.font;
            text.fontSharedMaterial = template.fontSharedMaterial;
            text.fontSize = 48f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(1600f, 600f);
        }
        root.SetActive(true);
        root.transform.SetAsLastSibling();
        text.rectTransform.anchoredPosition = position ?? Vector2.zero;
        text.fontSize = fontSize;
        background.color = new Color(0f, 0f, 0f, Mathf.Clamp01(opacity));
        text.text = message;
    }

    internal void Hide()
    {
        if (root != null) root.SetActive(false);
    }
}
