using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[HarmonyPatch(typeof(ControlBinder), "Start")]
internal static class GraffitiSettingsPatch
{
    private static void Postfix()
    {
        if (GunsawMultiplayerPlugin.IsHeadlessMode) return;
        var source = GameObject.Find("Canvas/Settings/CrosshairSettings/CrossToggle");
        var labelSource = GameObject.Find("Canvas/Settings/CrosshairSettings/MainName (13)");
        if (source == null || labelSource == null) return;
        var toggleObject = UnityEngine.Object.Instantiate(source);
        toggleObject.transform.SetParent(source.transform.parent);
        toggleObject.transform.localScale = source.transform.localScale;
        toggleObject.transform.localPosition = source.transform.localPosition + Vector3.down * 175f;
        var toggle = toggleObject.GetComponent<Toggle>();
        toggle.onValueChanged = new Toggle.ToggleEvent();
        toggle.SetIsOnWithoutNotify(GraffitiSystem.ShowGraffiti);
        toggle.onValueChanged.AddListener(GraffitiSystem.SetShowGraffiti);
        var label = UnityEngine.Object.Instantiate(labelSource);
        label.transform.SetParent(labelSource.transform.parent);
        label.transform.localScale = labelSource.transform.localScale;
        label.transform.localPosition = labelSource.transform.localPosition + Vector3.down * 175f;
        label.GetComponent<TextMeshProUGUI>().text = "Show graffiti";
    }
}
