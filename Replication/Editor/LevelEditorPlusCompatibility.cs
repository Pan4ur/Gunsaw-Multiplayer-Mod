using System.Collections;
using System.Reflection;
using UnityEngine;

internal static class LevelEditorPlusCompatibility
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? selectedObjects = typeof(LevelEditor).GetField("selectedObjects", Fields);
    private static readonly FieldInfo? selectedCols = typeof(LevelEditor).GetField("selectedCols", Fields);
    private static readonly FieldInfo? primarySelectedObject = typeof(LevelEditor).GetField("primarySelectedObject", Fields);
    private static readonly FieldInfo? isTargeting = typeof(LevelEditor).GetField("isTargeting", Fields);
    private static readonly MethodInfo? updateSelectionVisuals = typeof(LevelEditor).GetMethod("UpdateSelectionVisuals", Fields);

    internal static GameObject[] Selected(LevelEditor editor) =>
        selectedObjects?.GetValue(editor) is List<GameObject> selected
            ? selected.Where(value => value != null).ToArray()
            : editor.currentlySelected == null ? [] : [editor.currentlySelected];

    internal static void ForgetSelection(LevelEditor editor, GameObject removed)
    {
        var selected = selectedObjects?.GetValue(editor) as List<GameObject>;
        if (editor.currentlySelected != removed && selected?.Contains(removed) != true)
            return;
        selected?.Clear();
        (selectedCols?.GetValue(editor) as IList)?.Clear();
        primarySelectedObject?.SetValue(editor, null);
        isTargeting?.SetValue(editor, false);
        editor.currentlySelected = null;
        editor.hasGrabPos = false;
        editor.objMenu.SetActive(false);
        RefreshSelection(editor);
    }

    internal static void SelectPasted(LevelEditor editor, GameObject[] created, bool onlyCustom)
    {
        if (selectedObjects?.GetValue(editor) is not List<GameObject> selected)
            return;
        if (onlyCustom) selected.Clear();
        selected.AddRange(created);
        primarySelectedObject?.SetValue(editor, null);
        editor.currentlySelected = null;
        editor.objMenu.SetActive(false);
        RefreshSelection(editor);
    }

    internal static void RefreshSelection(LevelEditor editor) => updateSelectionVisuals?.Invoke(editor, null);
}