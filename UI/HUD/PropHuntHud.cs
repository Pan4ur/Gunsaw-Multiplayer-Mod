using TMPro;
using UnityEngine;

[DefaultExecutionOrder(1100)]
internal sealed class PropHuntHud : MonoBehaviour
{
    private readonly Dictionary<GameObject, bool> hidden = new();
    private PlayerScript player;
    private TMP_Text change, stun, locator;
    private PropHuntOutline outline;

    private void LateUpdate()
    {
        if (!PropHuntSystem.Enabled || (!PropHuntSystem.IsProp(PlayerScript.player?.bodyScript) && !PropHuntSystem.IsHunter(MultiplayerSession.LocalPeerId)))
        {
            Restore();
            Destroy(this);
            return;
        }
        player = PlayerScript.player;
        
        if (PropHuntSystem.IsHunter(MultiplayerSession.LocalPeerId))
        {
            if (player == null || player.mainCanvas == null || player.ammoText == null)
                return;
                
            if (locator == null)
                locator = CreateLine(-134f);
                
            locator.gameObject.SetActive(PropHuntSystem.LocatorVisible && !GameManager.main.paused);
            SetPerk(locator, "[J] Locator", PropHuntSystem.LocatorCooldown);
            return;
        }

        if (outline == null) 
            outline = gameObject.AddComponent<PropHuntOutline>();
            
        var target = PropHuntSystem.StunTarget();
        outline.SetTarget(target == 0 ? null : NetworkAvatarManager.RemoteBodyForPeer(target));
        
        if (player == null || player.mainCanvas == null || player.ammoText == null) 
            return;
            
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
        outline.Let(Destroy);
        
        foreach (var pair in hidden)
            pair.Key.Let(it => it.SetActive(pair.Value));

        hidden.Clear();
        
        locator.Let(it => Destroy(it.gameObject));
        change.Let(it => Destroy(it.gameObject));
        stun.Let(it => Destroy(it.gameObject));
    }

    private void OnDestroy() => Restore();
}
