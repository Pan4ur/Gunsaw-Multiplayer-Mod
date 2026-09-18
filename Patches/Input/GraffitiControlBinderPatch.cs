using HarmonyLib;
using TMPro;
using UnityEngine;

[HarmonyPatch(typeof(ControlBinder), "Start")]
internal static class GraffitiControlBinderPatch
{
    // TODO proper bind system
    private static void Postfix(ControlBinder __instance)
    {
        var key = Controls.newControls[Controls.GRAFFITI];
        if (__instance.texts.ContainsKey(key) || !__instance.texts.TryGetValue("Unarmed", out var unarmedField)) return;
        var field = UnityEngine.Object.Instantiate(unarmedField.gameObject).transform;
        field.SetParent(unarmedField.transform);
        field.localScale = Vector3.one;
        field.localPosition = Vector3.down * 43f;
        field.name = key;
        __instance.texts.Add(key, field.GetComponent<TextMeshProUGUI>());
        var headlampKey = Controls.newControls[Controls.TOGGLE_HEADLAMP];
        var headlampField = UnityEngine.Object.Instantiate(field.gameObject).transform;
        headlampField.SetParent(field.parent);
        headlampField.localScale = Vector3.one;
        headlampField.localPosition = field.localPosition + Vector3.down * 43f;
        headlampField.name = headlampKey;
        __instance.texts.Add(headlampKey, headlampField.GetComponent<TextMeshProUGUI>());
        foreach (var label in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (label == null || label.text != "Unarmed" || label.gameObject.name == "Unarmed") continue;
            var copy = UnityEngine.Object.Instantiate(label.gameObject).transform;
            copy.SetParent(label.transform.parent);
            copy.localScale = Vector3.one;
            copy.localPosition = label.transform.localPosition + Vector3.down * 43f;
            copy.GetComponent<TextMeshProUGUI>().text = key;
            var headlampLabel = UnityEngine.Object.Instantiate(copy.gameObject).transform;
            headlampLabel.SetParent(copy.parent);
            headlampLabel.localScale = Vector3.one;
            headlampLabel.localPosition = copy.localPosition + Vector3.down * 43f;
            headlampLabel.GetComponent<TextMeshProUGUI>().text = headlampKey;
            break;
        }
        foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            if (gameObject != null && gameObject.name == "ResetBind" && gameObject.GetComponent<ResetButtonOffset>() == null)
                gameObject.AddComponent<ResetButtonOffset>();
        __instance.UpdateBindings();
    }
}

internal sealed class ResetButtonOffset : MonoBehaviour
{
    private RectTransform rect;
    private Vector2 basePosition;
    private bool positioned;

    private void OnEnable()
    {
        rect = transform as RectTransform;
        positioned = false;
        Canvas.willRenderCanvases += Apply;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= Apply;
        positioned = false;
    }

    private void LateUpdate() => Apply();

    private void Apply()
    {
        if (rect == null) return;
        if (!positioned)
        {
            basePosition = rect.anchoredPosition;
            positioned = true;
        }
        rect.anchoredPosition = basePosition + Vector2.down * 100f;
    }
}
