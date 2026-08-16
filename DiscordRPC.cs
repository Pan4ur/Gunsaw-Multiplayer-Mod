using UnityEngine;
using DiscordRPC;

internal sealed class RPCManager : MonoBehaviour
{   // Stole that code from CU, and dll you also need to steal from CU and you need all 3 dlls
    // Would need TODO cleanup TODO option to disable TODO fallback if dll is missing TODO actual rpc TODO fix the logo
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
        /*if ((bool)PreRunScript.instance)
        {
            details = "Preparing for an expedition";
        }
        else if ((bool)WorldGeneration.world)
        {
            details = ((!PlayerCamera.main.body.alive) ? "Deceased" : ((WorldGeneration.world.biomeOverride != WorldGeneration.OverrideSceneType.None) ? "In the labs" : ("Descending layer " + (WorldGeneration.world.biomeDepth + 1))));
        }
        if ((bool)PlayerCamera.main)
        {
            Body body = PlayerCamera.main.body;
            if (body.alive)
            {
                float totalHappiness = body.totalHappiness;
                string text = (body.brainDying ? "In cardiac arrest" : (body.isCriticallyDying ? "On death's door" : (body.isDying ? "Dying" : ((totalHappiness > 90f) ? "Ecstatic" : ((totalHappiness > 70f) ? "Overjoyed" : ((totalHappiness > 50f) ? "Happy" : ((totalHappiness > 30f) ? "Excited" : ((totalHappiness > 10f) ? "Satisfied" : ((totalHappiness > -10f) ? "Steady" : ((totalHappiness > -30f) ? "Uneasy" : ((totalHappiness > -50f) ? "Scared" : ((totalHappiness > -70f) ? "Depressed" : ((!(totalHappiness > -90f)) ? "Suicidal" : "Miserable")))))))))))));
                state = ((!body.brainDying) ? (text + $" ({totalHappiness:0.0} mood)") : (text + " (" + WorldGeneration.world.TotalDepthString() + ")"));
            }
            else
            {
                state = WorldGeneration.world.TotalDepthString();
            }
        }*/
        state = "Gunsaw";
        details = "Testing testing testing";
        return new RichPresence
        {
            Details = details,
            State = state,
            Assets = new Assets
            {
                LargeImageKey = "",
                LargeImageText = ""
            }
        };
    }

    public void Initialize()
    {
        client = new DiscordRpcClient("424087019149328395");
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
