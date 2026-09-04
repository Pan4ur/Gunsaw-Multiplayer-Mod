using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using TMPro;

// Stores constants to use across all patches and precaches them
public static class Controls
{
    public static readonly string[] newControls = new string[] { "Open chat", "Close chat", "See players", "Markers", "Whine" };
    public static readonly int[] defaultValues = new int[] { (int)KeyCode.Return, (int)KeyCode.Escape, (int)KeyCode.Tab, (int)KeyCode.M, (int)KeyCode.B };

    public static KeyCode[] keys = new KeyCode[] { KeyCode.Return, KeyCode.Escape, KeyCode.Tab, KeyCode.M, KeyCode.B };

    public const int OPEN_CHAT = 0;
    public const int CLOSE_CHAT = 1;
    public const int SEE_PLAYER = 2;
    public const int TOGGLE_PLAYER_MARKERS = 3;
    public const int PAIN_SOUND = 4;
}

// Adding GameObject of controls and their value in ControlBinder.texts, also sets default values of PlayerPrefs if it missing
[HarmonyPatch(typeof(ControlBinder), "Start")]
internal static class ControlsGoCreator
{
    private static void Postfix(ControlBinder __instance)
    {
        GameObject fieldOrigianl = __instance.texts["Slowmo"].gameObject;
        GameObject labelOrigianl = GameObject.Find("Canvas/Settings/ControlSettings/MainName (13)");
        if (null == fieldOrigianl || null == labelOrigianl)
        {
            UnityEngine.Debug.LogError("[GunsawMP] Unable to create controls");
            return;
        }
        // Creating clone of original fields, because new fields will get set as a children of original
        //thus if we clone original, we clone childrens too
        GameObject fieldBase = UnityEngine.Object.Instantiate(fieldOrigianl);
        GameObject labelBase = UnityEngine.Object.Instantiate(labelOrigianl);

        for (int i = 0; i < Controls.newControls.Length; i++)
        {
            Transform newField = UnityEngine.Object.Instantiate(fieldBase).transform;
            Vector3 newOffset = new Vector3(0, -50 + -50 * i);
            newField.SetParent(fieldOrigianl.transform);
            newField.localScale = Vector3.one;
            newField.localPosition = newOffset;
            newField.name = Controls.newControls[i];
            __instance.texts.Add(Controls.newControls[i], newField.GetComponent<TextMeshProUGUI>());

            Transform newLabel = UnityEngine.Object.Instantiate(labelBase).transform;
            newLabel.SetParent(labelOrigianl.transform);
            newLabel.localScale = Vector3.one;
            newLabel.localPosition = newOffset;
            newLabel.GetComponent<TextMeshProUGUI>().text = Controls.newControls[i];
        }

        UnityEngine.Object.Destroy(fieldBase);
        UnityEngine.Object.Destroy(labelBase);

        for (int i = 0; i < Controls.newControls.Length; i++)
            if (!PlayerPrefs.HasKey(Controls.newControls[i]))
                PlayerPrefs.SetInt(Controls.newControls[i], Controls.defaultValues[i]);

        __instance.UpdateBindings();
    }
}

// Set default values
[HarmonyPatch(typeof(ControlBinder), "ResetBinds")]
internal static class ControlsResetPatch
{
    [HarmonyPrefix]
    public static void ResetCustomKeyCodes(ControlBinder __instance)
    {
        for (int i = 0; i < Controls.newControls.Length; i++)
            PlayerPrefs.SetInt(Controls.newControls[i], Controls.defaultValues[i]);
    }
}

// Add instances of keys into PlayerScript.keys and Controls.keys
[HarmonyPatch(typeof(PlayerScript), "Awake")]
internal static class ControlsAdder
{
    private static void Postfix(PlayerScript __instance)
    {
        for (int i = 0; i < Controls.newControls.Length; i++)
        {
            KeyCode code = (KeyCode)PlayerPrefs.GetInt(Controls.newControls[i]);
            __instance.keys.Add(Controls.newControls[i], code);
            Controls.keys[i] = code;
        }
    }
}

[HarmonyPatch(typeof(ControlBinder), "UpdateBindings")]
internal static class ControlsBindingSyncPatch
{
    private static void Postfix()
    {
        var settings = Controls.newControls;
        for (int i = 0; i < settings.Length; i++)
        {
            var ki = settings[i];
            if (PlayerPrefs.HasKey(ki)) 
                Controls.keys[i] = (KeyCode) PlayerPrefs.GetInt(ki);
        }
    }
}
