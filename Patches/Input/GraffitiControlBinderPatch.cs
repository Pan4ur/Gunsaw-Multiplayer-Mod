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
        field.localPosition = Vector3.down * 50f;
        field.name = key;
        __instance.texts.Add(key, field.GetComponent<TextMeshProUGUI>());
        foreach (var label in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (label == null || label.text != "Unarmed" || label.gameObject.name == "Unarmed") continue;
            var copy = UnityEngine.Object.Instantiate(label.gameObject).transform;
            copy.SetParent(label.transform.parent);
            copy.localScale = Vector3.one;
            copy.localPosition = label.transform.localPosition + Vector3.down * 50f;
            copy.GetComponent<TextMeshProUGUI>().text = key;
            break;
        }
        foreach (var button in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>())
        {
            var text = button == null ? null : button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text == null || text.text != "RESET") continue;
            button.transform.localPosition += Vector3.down * 75f;
            break;
        }
        __instance.UpdateBindings();
    }
}
