using UnityEngine.UI;
using UnityEngine;
using HarmonyLib;
using TMPro;
using DiscordIPC;

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
{
    // Would need TODO cleanup TODO actual rpc
    public bool enable;
    public string lobbyId;
    public static RPCManager instance;
    private bool _enable;
    private float timer = 5f;

    public static void CheckInstance()
    {
        if (!instance)
        {
            new GameObject("RPCManager", typeof(RPCManager));
        }
    }

    private void Awake()
    {
        instance = this; // Inverted, so its on by default
        enable = 0 == PlayerPrefs.GetInt("rpcdisable");
        UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
    }

    private void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer < 0f)
        {
            timer = 1f;
            if (enable)
            {
                UpdateRichPresence();
                if (!_enable)
                {
                    Initialize();
                    _enable = true;
                }
            }
            if (!enable && _enable)
            {
                DestroyClient();
                _enable = false;
            }
        }
    }

    internal void UpdateRichPresence()
    {
        if (MultiplayerSession.IsActive)
        {
            RichPresence.Party = new PresenceParty(lobbyId, MultiplayerSession.PlayerCount, MultiplayerSession.MaxPlayers);
            if ("gunsawudp.e621.su" == GunsawMultiplayerPlugin.Instance.lobbyServerAddress && MultiplayerSession.PlayerCount < MultiplayerSession.MaxPlayers)
                RichPresence.JoinSecret = lobbyId + ":";// Idk, discord refuses to show RPC if party ID and join secret are the same
        }

        RichPresence.Details = "details";
        RichPresence.State = "state";
    }

    private void Initialize()
    {
        // Refer to rushellxyz regarding app
        RichPresence.AppId = 1538837414515052575L;
        /*client.RegisterUriScheme(null, "/home/rushell/Desktop/gunsaw-demo-win/Gunsaw.exe");
        client.Subscribe(DiscordRPC.EventType.JoinRequest);
        client.OnJoinRequested += async delegate(object sender, JoinRequestMessage args)
        {
            client.Respond(args, acceptRequest: true);
        };
        client.Subscribe(DiscordRPC.EventType.Join);
        client.OnJoin += delegate(object sender, JoinMessage e)
        {

        };
        client.OnReady += delegate(object sender, ReadyMessage e)
        {

        };
        client.OnError += delegate(object sender, ErrorMessage e)
        {
            Debug.LogError("Discord RPC Error: " + e.Message);
        };
        client.Initialize();
        client.SetPresence(GetCurrentRichPresence());*/
        RichPresence.StartRolling();
    }

   // private void OnJoinRequested()

    private void DestroyClient()
    {
        RichPresence.StopRolling();
    }

    private void OnDestroy()
    {
        DestroyClient();
    }

}
