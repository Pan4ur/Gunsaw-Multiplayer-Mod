using UnityEngine;
using DiscordRPC;

internal sealed class RPCManager : MonoBehaviour
{   // Stole that code from CU, and dll
    // Would need TODO cleanup TODO option to disable TODO actual rpc
    private static RPCManager instance;
    private DiscordRpcClient client;
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
        instance = this;
        UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
    }

    private void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer < 0f)
        {
            timer = 5f;
            client?.SetPresence(GetCurrentRichPresence());
            bool value = true;//Settings.Get<SettingBool>("rpc").value;
            if (value && client == null)
            {
                Initialize();
            }
            if (!value && client != null)
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
