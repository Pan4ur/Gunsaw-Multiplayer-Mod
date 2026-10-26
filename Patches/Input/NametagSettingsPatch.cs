using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[HarmonyPatch(typeof(ControlBinder), "Start")]
internal static class NametagSettingsPatch
{
    private static void Postfix()
    {
        if (HeadlessLobbyService.IsHeadlessMode) return;
        var panel = GameObject.Find("Canvas/Settings/CrosshairSettings")?.transform;
        if (panel == null || panel.Find("Nametags") != null) return;
        var label = panel.Find("MainName (13)");
        var toggle = panel.Find("CrossToggle");
        var slider = panel.Find("SizeSlider");
        var heading = panel.Find("Category (3)");
        if (label == null || toggle == null || slider == null || heading == null) return;

        var title = Clone(heading, panel, "Nametags", -5f);
        title.GetComponent<TMP_Text>().text = "Nametags";
        var preview = new NametagPreview(panel, label.GetComponent<TMP_Text>());
        AddToggle(panel, label, toggle, "Ping", -50f, NametagSettings.Ping, NametagSettings.SetPing, preview);
        AddToggle(panel, label, toggle, "Health", -93f, NametagSettings.Health, NametagSettings.SetHealth, preview);
        AddToggle(panel, label, toggle, "Background", -136f, NametagSettings.Background, NametagSettings.SetBackground, preview);
        AddToggle(panel, label, toggle, "Ammo", -179f, NametagSettings.Ammo, NametagSettings.SetAmmo, preview);
        AddToggle(panel, label, toggle, "K/D", -222f, NametagSettings.KillDeath, NametagSettings.SetKillDeath, preview);
        Clone(label, panel, "NametagScaleLabel", -265f).GetComponent<TMP_Text>().text = "Scale";
        var scale = Clone(slider, panel, "NametagScale", -265f).GetComponent<Slider>();
        scale.onValueChanged = new Slider.SliderEvent();
        scale.wholeNumbers = false;
        scale.minValue = 0.5f;
        scale.maxValue = 1.5f;
        scale.SetValueWithoutNotify(NametagSettings.Scale);
        scale.onValueChanged.AddListener(value =>
        {
            NametagSettings.SetScale(value);
            preview.Refresh();
        });
        preview.Refresh();
    }

    private static void AddToggle(Transform panel, Transform label, Transform source, string name, float y, bool value, UnityAction<bool> changed, NametagPreview preview)
    {
        var id = name.Replace("/", "");
        Clone(label, panel, "Nametag" + id + "Label", y).GetComponent<TMP_Text>().text = name;
        var toggle = Clone(source, panel, "Nametag" + id, y).GetComponent<Toggle>();
        toggle.onValueChanged = new Toggle.ToggleEvent();
        toggle.SetIsOnWithoutNotify(value);
        toggle.onValueChanged.AddListener(enabled =>
        {
            changed(enabled);
            preview.Refresh();
        });
    }

    private static Transform Clone(Transform source, Transform panel, string name, float y)
    {
        var copy = UnityEngine.Object.Instantiate(source, panel, false);
        copy.name = name;
        var rect = (RectTransform)copy;
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
        return copy;
    }
}