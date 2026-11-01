using TMPro;
using UnityEngine;

[DefaultExecutionOrder(1100)]
internal sealed class PropHuntHud : MonoBehaviour
{
    private readonly Dictionary<GameObject, bool> hidden = new();
    private PlayerScript player;
    private TMP_Text change, stun;

    private void LateUpdate()
    {
        if (!PropHuntSystem.Enabled || !PropHuntSystem.IsProp(PlayerScript.player?.bodyScript))
        {
            Restore();
            Destroy(this);
            return;
        }
        player = PlayerScript.player;
        if (player == null || player.mainCanvas == null || player.ammoText == null) return;
        if (change == null)
        {
            change = CreateLine(-26f);
            stun = CreateLine(-62f);
        }
        Hide(player.weaponNameText?.gameObject);
        Hide(player.ammoText.gameObject);
        Hide(player.maxAmmoText?.gameObject);
        foreach (var text in player.ammoTexts) Hide(text?.gameObject);
        foreach (var image in player.weaponImgs ?? Array.Empty<UnityEngine.UI.Image>()) Hide(image?.gameObject);
        foreach (var image in player.weaponBackImgs ?? Array.Empty<UnityEngine.UI.Image>()) Hide(image?.gameObject);
        foreach (var image in player.wepAmmoTypeImg ?? Array.Empty<UnityEngine.UI.Image>()) Hide(image?.gameObject);
        var visible = PropHuntSystem.AbilitiesVisible && !GameManager.main.paused;
        change.gameObject.SetActive(visible);
        stun.gameObject.SetActive(visible);
        if (!visible) return;
        SetPerk(change, "[J] CHANGE PROP", PropHuntSystem.ChangeCooldown);
        SetPerk(stun, "[LMB] STUN HUNTER", PropHuntSystem.StunCooldown);
    }

    private TMP_Text CreateLine(float y)
    {
        var obj = new GameObject("Prop Hunt ability", typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(player.mainCanvas.transform, false);
        var text = obj.GetComponent<TextMeshProUGUI>();
        text.font = player.ammoText.font;
        text.fontSharedMaterial = player.ammoText.fontSharedMaterial;
        text.fontSize = 22f;
        text.alignment = TextAlignmentOptions.TopRight;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-26f, y);
        rect.sizeDelta = new Vector2(460f, 34f);
        return text;
    }

    private static void SetPerk(TMP_Text text, string label, float cooldown)
    {
        var seconds = Mathf.CeilToInt(cooldown);
        text.text = seconds > 0 ? label + "  " + seconds + "s" : label;
        text.color = seconds > 0 ? Color.gray : Color.white;
    }

    private void Hide(GameObject obj)
    {
        if (obj == null) return;
        if (!hidden.ContainsKey(obj)) hidden.Add(obj, obj.activeSelf);
        obj.SetActive(false);
    }

    private void Restore()
    {
        foreach (var pair in hidden)
            if (pair.Key != null) pair.Key.SetActive(pair.Value);
        hidden.Clear();
        if (change != null) Destroy(change.gameObject);
        if (stun != null) Destroy(stun.gameObject);
    }

    private void OnDestroy() => Restore();
}
