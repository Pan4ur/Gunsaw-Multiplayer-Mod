using HarmonyLib;
using TMPro;
using UnityEngine;

[HarmonyPatch(typeof(ControlBinder), "Start")]
internal static class GraffitiControlBinderPatch
{
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
        foreach (var label in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (label == null || label.text != "Unarmed" || label.gameObject.name == "Unarmed") continue;
            var copy = UnityEngine.Object.Instantiate(label.gameObject).transform;
            copy.SetParent(label.transform.parent);
            copy.localScale = Vector3.one;
            copy.localPosition = label.transform.localPosition + Vector3.down * 43f;
            copy.GetComponent<TextMeshProUGUI>().text = key;
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
