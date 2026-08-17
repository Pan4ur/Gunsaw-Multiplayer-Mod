using UnityEngine.UI;
using UnityEngine;
using HarmonyLib;
using TMPro;
using DiscordRPC;

// Creates RPC toggle button in setting
[HarmonyPatch(typeof(ControlBinder), "Start")]
internal static class RPCSettings
{
    private static void Postfix(ControlBinder __instance)
    {
        RPCManager.CheckInstance();
        GameObject crossToggle = GameObject.Find("Canvas/Settings/CrosshairSettings/CrossToggle");
        GameObject rpcToggle = UnityEngine.Object.Instantiate(crossToggle);
        rpcToggle.transform.SetParent(crossToggle.transform.parent);
        rpcToggle.transform.localScale = crossToggle.transform.localScale;
        rpcToggle.transform.localPosition = new Vector3(crossToggle.transform.localPosition.x, crossToggle.transform.localPosition.y - 125f, crossToggle.transform.localPosition.z);
        Toggle rpcToggleToggle = rpcToggle.GetComponent<Toggle>();
        rpcToggleToggle.isOn = 0 == PlayerPrefs.GetInt("rpcdisable");
        rpcToggleToggle.onValueChanged = new Toggle.ToggleEvent();
        rpcToggleToggle.onValueChanged.AddListener(ToggleRPC);

        GameObject mainName = GameObject.Find("Canvas/Settings/CrosshairSettings/MainName (13)");
        GameObject rpcName = UnityEngine.Object.Instantiate(mainName);
        rpcName.transform.SetParent(mainName.transform.parent);
        rpcName.transform.localScale = mainName.transform.localScale;
        rpcName.transform.localPosition = new Vector3(mainName.transform.localPosition.x, mainName.transform.localPosition.y - 125f, mainName.transform.localPosition.z);
        rpcName.GetComponent<TextMeshProUGUI>().text = "Discord RPC";
    }

    private static void ToggleRPC(bool isOn)
    {
        UnityEngine.Debug.Log(isOn);
        RPCManager.instance.enable = isOn;
        if (isOn)
            PlayerPrefs.SetInt("rpcdisable", 0);
   else     PlayerPrefs.SetInt("rpcdisable", 1);
    }
}

internal sealed class RPCManager : MonoBehaviour
{   // Stole that code from CU, and dll
    // Would need TODO cleanup TODO actual rpc
    public bool enable;
    public static RPCManager instance;
    private DiscordRpcClient client;
    private float timer = 5f;

    public static void CheckInstance()
    {
        if (!instance)
        { // Inverted, so its on by default
            new GameObject("RPCManager", typeof(RPCManager));
        }
    }

    private void Awake()
    {
        instance = this;
        enable = 0 == PlayerPrefs.GetInt("rpcdisable");
        UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
    }

    private void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer < 0f)
        {
            timer = 5f;
            client?.SetPresence(GetCurrentRichPresence());
            if (enable && client == null)
            {
                Initialize();
            }
            if (!enable && client != null)
            {
                DestroyClient();
            }
        }
    }

    public RichPresence GetCurrentRichPresence()
    {
        string state = "";
        string details = "";
        state = "Gunsaw";
        details = "Testing testing testing";
        return new RichPresence
        {
            Details = details,
            State = state,
            Assets = new Assets
            {
                /*
                 https://img.itch.zone/aW1nLzEyMTkxNTgyLnBuZw==/315x250%23c/DAf0%2F%2F.png
                 11:30 at 17 aug TODO come back later and see if image is still there
                */
                LargeImageKey = "",
                LargeImageText = ""
            }
        };
    }

    public void Initialize()
    {
        // Refer to rushellxyz regarding app
        client = new DiscordRpcClient("1538837414515052575");
        client.Initialize();
        client.SetPresence(GetCurrentRichPresence());
    }

    public void DestroyClient()
    {
        client?.Dispose();
        client = null;
    }

    private void OnDestroy()
    {
        DestroyClient();
    }

}
