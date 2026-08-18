using UnityEngine.SceneManagement;
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
        GameObject crossToggle = GameObject.Find("Canvas/Settings/CrosshairSettings/CrossToggle");
        GameObject rpcToggle = UnityEngine.Object.Instantiate(crossToggle);
        rpcToggle.transform.SetParent(crossToggle.transform.parent);
        rpcToggle.transform.localScale = crossToggle.transform.localScale;
        rpcToggle.transform.localPosition = new Vector3(crossToggle.transform.localPosition.x,
            crossToggle.transform.localPosition.y - 125f, crossToggle.transform.localPosition.z);
        Toggle rpcToggleToggle = rpcToggle.GetComponent<Toggle>();
        rpcToggleToggle.isOn = 0 == PlayerPrefs.GetInt("rpcdisable");
        rpcToggleToggle.onValueChanged = new Toggle.ToggleEvent();
        rpcToggleToggle.onValueChanged.AddListener(ToggleRPC);

        GameObject mainName = GameObject.Find("Canvas/Settings/CrosshairSettings/MainName (13)");
        GameObject rpcName = UnityEngine.Object.Instantiate(mainName);
        rpcName.transform.SetParent(mainName.transform.parent);
        rpcName.transform.localScale = mainName.transform.localScale;
        rpcName.transform.localPosition = new Vector3(mainName.transform.localPosition.x,
            mainName.transform.localPosition.y - 125f, mainName.transform.localPosition.z);
        rpcName.GetComponent<TextMeshProUGUI>().text = "Discord RPC";
    }

    private static void ToggleRPC(bool isOn)
    {
        RPCManager.instance.enable = isOn;
        if (isOn)
            PlayerPrefs.SetInt("rpcdisable", 0);
        else 
            PlayerPrefs.SetInt("rpcdisable", 1);
    }
}

internal sealed class RPCManager : MonoBehaviour
{
    public bool enable;
    public string lobbyId;
    public static RPCManager instance;
    private bool _enable;
    private float timer = 5f;

    private static readonly Dictionary<string, string> levels = new Dictionary<string, string>
    {
        {"tutorial1", "Basic Training"},
        {"actualLevel1", "Lock Break"},
        {"actualLevel2", "Boc Check"},
        {"beautyLevel", "Belt Dropdown"},
        {"campaign3", "Box Check"},
        // Green skies is the only level with second word starting from small letter
        // You will never unsee this
        {"campaign4", "Green skies"},
        {"campaign5", "Zigzag"},
        {"campaign6", "Downdrops"},
        {"campaign7", "Crush Forces"},
        {"campaign8", "Mount Basins"},
        {"campaign9", "Blue Sewers"},
        {"campaign10", "Weird Technology"},
        {"campaign11", "Foggy Whites"},
        {"campaign12", "Rooftops"},
        {"campaign13", "Vanished Forts"},
        {"campaign14", "Acid Plants"},
        {"SampleScene", "Trash Containment"},
        {"LevelSelect", "Chooses level"},
        {"LevelEditor", "Level editor"},
        // I would love to somehow display which custom level
        {"LevelLoader", "Custom level"},
        // Two secret levels, yep theres secret levels
        // try them with unity explorer
        {"level1", "Secret level"},
        {"level2", "Secret level"},
    };

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
        bool shouldResetJoinSecret = true;
        if (MultiplayerSession.IsConnected || MultiplayerSession.IsHosting)
        {
            bool defaultServer = GunsawMultiplayerPlugin.Instance.lobbyServerAddress.Contains("e621.su");
            string lobbyId = GunsawMultiplayerPlugin.Instance.GetCurrentLobbyId();
            RichPresence.Party = new PresenceParty(lobbyId,
                MultiplayerSession.PlayerCount, MultiplayerSession.MaxPlayers, defaultServer);
            if (defaultServer)
            {
                RichPresence.JoinSecret = lobbyId + ":" + GunsawMultiplayerPlugin.PluginVersion;
                shouldResetJoinSecret = false;
            }
            // It is supposed to be encrypted, wooah
            // But uhh, we dont have privacy settings to begins with, lol
            // Also, ipc 5005 we must provide different key
            if (MultiplayerSession.PvpEnabled)
                RichPresence.Details = "PVP";
       else     RichPresence.Details = "CO-OP";
        }
   else {
            RichPresence.Party = null;
            if (null == PlayerScript.player || null == PlayerScript.player.bodyScript || string.IsNullOrEmpty(PlayerScript.player.bodyScript.speciesName))
                RichPresence.Details = "In main menu";
       else {
                string playerSpecie = PlayerScript.player.bodyScript.speciesName;
                // Detect albino, rabomination, ename g4a to G-4A and else capialize first letter
                if ("experiment" == playerSpecie)
                {   // why albino and abomination are named expie
                    // im not sure how руссификатор works, but it might break, not only this, everything in here actually
                    if ("Cannon fodder...?" == PlayerScript.player.bodyScript.afterSwapTip)
                        RichPresence.Details = "Albino";
               else if ("An abomination\nLow stats" == PlayerScript.player.bodyScript.afterSwapTip)
                        RichPresence.Details = "Abomination";
               else     RichPresence.Details = "Experiment";
                }
            else if ("g4a" == playerSpecie)
                    RichPresence.Details = "G4-A";
            else    RichPresence.Details = char.ToUpper(playerSpecie[0]) + playerSpecie.Substring(1);
            }
        }
        if (null != GameManager.main && GameManager.main.hardMode)
            RichPresence.Details += " |BRUTAL";
        if (shouldResetJoinSecret)
            RichPresence.JoinSecret = null;


        string scene = SceneManager.GetActiveScene().name;
        if (levels.TryGetValue(scene, out string level))
            RichPresence.State = level;
   else     RichPresence.State = scene;
    }

    private void Initialize()
    {
        // Refer to rushellxyz regarding app
        RichPresence.AppId = 1538837414515052575L;
        RichPresence.AutoRegister = true;
        RichPresence.OnJoin += OnJoin;
        RichPresence.OnJoinRequest += OnJoinRequest;
    }

    private void OnJoin(string secret)
    {
        Console.WriteLine($"OnJoin - {secret}");
        if (MultiplayerSession.IsConnected || MultiplayerSession.IsHosting)
        {
            Console.WriteLine("You cant join discord invite while already in game");
            GunsawMultiplayerPlugin.Instance.status = "You cant join discord invite while already in game";
            return;
        }

        string[] decode = secret.Split(':');
        if (1 > decode.Length)
        {
            Console.WriteLine("The invite is invalid.");
            GunsawMultiplayerPlugin.Instance.status = "The invite is invalid.";
            return;
        }

        if (GunsawMultiplayerPlugin.PluginVersion != decode[1])
        {
            Console.WriteLine("Mismatching game version.");
            GunsawMultiplayerPlugin.Instance.status = "Mismatching game version.";
            return;
        }

        GunsawMultiplayerPlugin.Instance.JoinLobby(decode[0]);
    }

    private void OnJoinRequest(IPCUser user)
    {
        // What it does?
        Console.WriteLine($"OnJoinRequest - {user.Id}");
        if (MultiplayerSession.IsActive && MultiplayerSession.PlayerCount < MultiplayerSession.MaxPlayers)
            RichPresence.AcceptJoinRequest(user);
        else 
            RichPresence.RejectJoinRequest(user);
    }

    private void DestroyClient()
    {
        RichPresence.OnJoin -= OnJoin;
        RichPresence.OnJoinRequest -= OnJoinRequest;
    }

    private void OnDestroy()
    {
        DestroyClient();
    }
}
