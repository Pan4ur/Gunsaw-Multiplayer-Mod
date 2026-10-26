using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class NametagPreview
{
    private readonly MultiplayerHudUi.NametagVisual tag;

    internal NametagPreview(Transform panel, TMP_Text template)
    {
        var character = new GameObject("NametagCharacterPreview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        character.transform.SetParent(panel, false);
        var characterRect = (RectTransform)character.transform;
        characterRect.anchoredPosition = new Vector2(322f, -150f);
        characterRect.sizeDelta = new Vector2(200f, 150f);
        var characterImage = character.GetComponent<Image>();
        characterImage.sprite = EmbeddedSpriteLoader.Load("GunsawMultiplayer.Assets.orange-nametag-preview.png", 100f, new Vector2(0.5f, 0.5f));
        characterImage.preserveAspect = true;
        characterImage.raycastTarget = false;

        var background = new GameObject("NametagPreview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        background.transform.SetParent(panel, false);
        var rect = (RectTransform)background.transform;
        rect.anchoredPosition = new Vector2(298f, -50f);
        var textObject = new GameObject("Name", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(rect, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = template.font;
        text.fontSharedMaterial = template.fontSharedMaterial;
        text.fontSize = 15f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        tag = new MultiplayerHudUi.NametagVisual { Root = rect, Text = text, Background = background.GetComponent<Image>() };
        tag.Background.color = new Color(0f, 0f, 0f, 0.5f);
        tag.Background.raycastTarget = false;
    }

    internal void Refresh()
    {
        tag.UpdateText("Taint", NametagSettings.Ping ? 255 : int.MinValue,
            NametagSettings.Health ? 65 : -1, 2, NametagSettings.Ammo ? 14 : -1, 30, 0, false,
            NametagSettings.KillDeath ? 5 : -1, NametagSettings.KillDeath ? 2 : -1);
        tag.Background.enabled = NametagSettings.Background;
        tag.Root.localScale = Vector3.one * NametagSettings.Scale;
    }
}
