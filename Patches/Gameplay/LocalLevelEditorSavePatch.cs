using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[HarmonyPatch(typeof(LevelEditor), "Start")]
internal static class LocalLevelEditorSavePatch
{
    private static void Postfix(LevelEditor __instance)
    {
        Ensure(__instance);
    }

    private static void Ensure(LevelEditor editor)
    {
        var plugin = GunsawMultiplayerPlugin.Instance;
        if (editor == null || plugin == null) return;
        if (editor.gameObject.scene.name == null) return;
        foreach (var existing in Resources.FindObjectsOfTypeAll<Button>())
            if (existing != null && existing.name == "Save Local Level Changes" &&
                existing.gameObject.scene == editor.gameObject.scene) return;

        TMP_Text clipboardCaption = null;
        Button templateButton = null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (candidate == null || candidate.gameObject.scene != editor.gameObject.scene ||
                candidate.text.IndexOf("clipboard", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            clipboardCaption = candidate;
            templateButton = candidate.GetComponentInParent<Button>();
            break;
        }
        if (clipboardCaption == null) return;
        if (templateButton == null)
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Button>())
                if (candidate != null && candidate.gameObject.scene == editor.gameObject.scene)
                {
                    templateButton = candidate;
                    break;
                }

        GameObject saveObject;
        if (templateButton != null)
            saveObject = UnityEngine.Object.Instantiate(templateButton.gameObject, clipboardCaption.transform.parent);
        else
        {
            saveObject = new GameObject("Save Local Level Changes", typeof(RectTransform), typeof(Image), typeof(Button));
            saveObject.transform.SetParent(clipboardCaption.transform.parent, false);
            saveObject.GetComponent<Image>().color = new Color(0.18f, 0.52f, 0.25f, 1f);
            var textObject = UnityEngine.Object.Instantiate(clipboardCaption.gameObject, saveObject.transform);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        }
        saveObject.name = "Save Local Level Changes";
        var sourceRect = clipboardCaption.GetComponent<RectTransform>();
        var saveRect = saveObject.GetComponent<RectTransform>();
        saveRect.anchoredPosition = sourceRect.anchoredPosition + Vector2.up * (sourceRect.rect.height + 8f);
        saveRect.sizeDelta = sourceRect.rect.size;
        var labelText = saveObject.GetComponentInChildren<TMP_Text>(true);
        if (labelText != null) labelText.text = "Save";
        var saveButton = saveObject.GetComponent<Button>();
        saveButton.onClick = new Button.ButtonClickedEvent();
        saveButton.onClick.AddListener(() =>
        {
            if (plugin.HasLocalLevelEditorTarget) plugin.SaveEditedLocalLevel(editor);
            else CustomLevelBrowserUi.OpenEditorSaveDialog(plugin, editor, saveButton, clipboardCaption);
        });
        return;
    }
}

[HarmonyPatch(typeof(LevelEditor), "BackToMenu")]
internal static class LocalLevelEditorClosePatch
{
    private static void Postfix()
    {
        GunsawMultiplayerPlugin.Instance?.EndLocalLevelEditing();
    }
}
