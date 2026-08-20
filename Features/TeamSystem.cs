using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal static class TeamSystem
{
    private static readonly List<Team> teams = new();
    private static readonly Dictionary<ushort, string> playerTeams = new();
    private static readonly Dictionary<string, TMP_Text> countLabels = new();
    private static GameObject panel;
    private static bool cursorVisible;
    private static CursorLockMode cursorLock;
    private static bool cursorWrite;
    internal static bool Enabled { get; private set; }

    internal static void Configure(bool enabled, string cfg)
    {
        Enabled = enabled;
        teams.Clear();
        playerTeams.Clear();
        ClosePanel();
        panel = null;
        if (!enabled) return;
        foreach (var item in (cfg ?? "").Split(';'))
        {
            var pair = item.Split(':');
            if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]) ||
                !ColorUtility.TryParseHtmlString(pair[1].Trim(), out var color)) continue;
            teams.Add(new Team { Name = pair[0].Trim(), Color = color });
        }

        if (teams.Count < 2) Enabled = false;
    }

    internal static void Tick()
    {
        if (!Enabled || !MultiplayerSession.IsConnected || SceneManager.GetActiveScene().name == "LevelSelect") return;
        if (playerTeams.ContainsKey(MultiplayerSession.LocalPeerId)) return;
        if (panel == null) CreatePanel();
        else
        {
            SetCursor(true, CursorLockMode.None);
            UpdatePanel();
        }
    }

    internal static void Choose(string name)
    {
        if (!Enabled || !HasTeam(name)) return;
        if (MultiplayerSession.IsHost) Set(MultiplayerSession.LocalPeerId, Best(name));
        else MultiplayerSession.Send(new TeamPacket(MultiplayerSession.LocalPeerId, name));
    }

    internal static void Receive(ushort sender, TeamPacket packet)
    {
        if (!Enabled) return;
        if (MultiplayerSession.IsHost)
        {
            if (sender != packet.PlayerId || !HasTeam(packet.Team)) return;
            Set(sender, Best(packet.Team));
            return;
        }

        if (sender == MultiplayerSession.HostPeerId) Set(packet.PlayerId, packet.Team);
    }

    internal static void SendAll(ushort peerId)
    {
        if (!MultiplayerSession.IsHost || !Enabled) return;
        foreach (var item in playerTeams) MultiplayerSession.Send(new TeamPacket(item.Key, item.Value), peerId);
    }

    internal static bool Same(ushort first, ushort second) => Enabled && first != 0 &&
                                                              playerTeams.TryGetValue(first, out var a) &&
                                                              playerTeams.TryGetValue(second, out var b) && a == b;

    internal static string Name(ushort id) => playerTeams.TryGetValue(id, out var value) ? value : "";

    internal static Color Color(ushort id)
    {
        foreach (var item in teams)
            if (item.Name == Name(id))
                return item.Color;
        return UnityEngine.Color.white;
    }

    internal static IEnumerable<string> Names()
    {
        foreach (var item in teams) yield return item.Name;
    }

    internal static string Hex(string name)
    {
        foreach (var item in teams)
            if (item.Name == name)
                return UnityEngine.ColorUtility.ToHtmlStringRGB(item.Color);
        return "FFFFFF";
    }

    private static void Set(ushort id, string name)
    {
        playerTeams[id] = name;
        if (id == MultiplayerSession.LocalPeerId) ClosePanel();
        if (MultiplayerSession.IsHost) MultiplayerSession.Send(new TeamPacket(id, name));
    }

    private static string Best(string selected)
    {
        var min = int.MaxValue;
        foreach (var team in teams) min = Math.Min(min, Count(team.Name));
        return Count(selected) <= min ? selected : teams.Find(item => Count(item.Name) == min).Name;
    }

    private static int Count(string name)
    {
        var count = 0;
        foreach (var value in playerTeams.Values)
            if (value == name)
                count++;
        return count;
    }

    private static bool HasTeam(string name) => teams.Exists(item => item.Name == name);

    private static void CreatePanel()
    {
        var root = new GameObject("TeamSelect", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        var image = root.AddComponent<Image>();
        image.color = new UnityEngine.Color(0f, 0f, 0f, 0.78f);
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        var title = Text(root.transform, "CHOOSE TEAM", new Vector2(0f, 200f), 34);
        title.fontStyle = FontStyles.Bold;
        for (var i = 0; i < teams.Count; i++)
        {
            var team = teams[i];
            var button = new GameObject("Team", typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(root.transform, false);
            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchoredPosition = new Vector2(0f, 100f - i * 70f);
            buttonRect.sizeDelta = new Vector2(460f, 54f);
            button.GetComponent<Image>().color = team.Color;
            var label = Text(button.transform, team.Name, new Vector2(0f, 10f), 24);
            label.color = UnityEngine.Color.white;
            var count = Text(button.transform, "", new Vector2(0f, -14f), 14);
            count.color = new UnityEngine.Color(1f, 1f, 1f, 0.88f);
            countLabels[team.Name] = count;
            var name = team.Name;
            button.GetComponent<Button>().onClick.AddListener(() => Choose(name));
        }

        panel = root;
        cursorVisible = Cursor.visible;
        cursorLock = Cursor.lockState;
        SetCursor(true, CursorLockMode.None);
        UpdatePanel();
    }

    private static TMP_Text Text(Transform parent, string value, Vector2 pos, float size)
    {
        var obj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        var rect = obj.GetComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(600f, 54f);
        var text = obj.GetComponent<TextMeshProUGUI>();
        TMP_Text source = PlayerScript.player == null ? null : PlayerScript.player.ammoText;
        if (source == null)
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TMP_Text>())
                if (candidate != null && candidate.font != null)
                {
                    source = candidate;
                    break;
                }

        if (source != null)
        {
            text.font = source.font;
            text.fontSharedMaterial = source.fontSharedMaterial;
            text.fontStyle = source.fontStyle;
        }

        text.text = value;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        return text;
    }

    private static void UpdatePanel()
    {
        foreach (var team in teams)
            if (countLabels.TryGetValue(team.Name, out var label) && label != null)
                label.text = Count(team.Name) + " PLAYERS";
    }

    private static void ClosePanel()
    {
        if (panel == null) return;
        UnityEngine.Object.Destroy(panel);
        panel = null;
        countLabels.Clear();
        SetCursor(cursorVisible, cursorLock);
    }

    private static void SetCursor(bool visible, CursorLockMode mode)
    {
        cursorWrite = true;
        Cursor.visible = visible;
        Cursor.lockState = mode;
        cursorWrite = false;
    }

    [HarmonyPatch(typeof(Cursor), "set_lockState")]
    private static class CursorLockPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => panel == null || cursorWrite;
    }

    [HarmonyPatch(typeof(Cursor), "set_visible")]
    private static class CursorVisiblePatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => panel == null || cursorWrite;
    }

    private sealed class Team
    {
        internal string Name;
        internal UnityEngine.Color Color;
    }
}