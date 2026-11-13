using UnityEngine.UI;
using UnityEngine;
using HarmonyLib;
using TMPro;

[HarmonyPatch(typeof(ControlBinder), "Start")]
internal static class RPCSettings
{
    private static void Postfix(ControlBinder __instance)
    {
        if (HeadlessLobbyService.IsHeadlessMode) return;

        var crossToggle = GameObject.Find("Canvas/Settings/CrosshairSettings/CrossToggle");
        var rpcToggle = UnityEngine.Object.Instantiate(crossToggle, crossToggle.transform.parent, true);
        rpcToggle.transform.localScale = crossToggle.transform.localScale;
        rpcToggle.transform.localPosition = new Vector3(crossToggle.transform.localPosition.x, crossToggle.transform.localPosition.y - 125f, crossToggle.transform.localPosition.z);
        var rpcToggleToggle = rpcToggle.GetComponent<Toggle>();
        rpcToggleToggle.SetIsOnWithoutNotify(0 == PlayerPrefs.GetInt("rpcdisable"));
        rpcToggleToggle.onValueChanged = new Toggle.ToggleEvent();
        rpcToggleToggle.onValueChanged.AddListener(ToggleRPC);

        var mainName = GameObject.Find("Canvas/Settings/CrosshairSettings/MainName (13)");
        var rpcName = UnityEngine.Object.Instantiate(mainName, mainName.transform.parent, true);
        rpcName.transform.localScale = mainName.transform.localScale;
        rpcName.transform.localPosition = new Vector3(mainName.transform.localPosition.x, mainName.transform.localPosition.y - 125f, mainName.transform.localPosition.z);
        rpcName.GetComponent<TextMeshProUGUI>().text = "Discord RPC";
    }

    private static void ToggleRPC(bool isOn)
    {
        RPCManager.instance.enable = isOn;
        PlayerPrefs.SetInt("rpcdisable", isOn ? 0 : 1);
    }
}