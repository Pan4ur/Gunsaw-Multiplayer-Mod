using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal static class TeamSystem
{
    private static readonly List<Team> teams = new();
    private static readonly Dictionary<ushort, string> playerTeams = new();
    private static readonly Dictionary<string, TMP_Text> teamHeaders = new();
    private static readonly Dictionary<string, TMP_Text> teamPlayers = new();
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
        if (MultiplayerSession.IsHost) Set(MultiplayerSession.LocalPeerId, name);
        else MultiplayerSession.Send(new TeamPacket(MultiplayerSession.LocalPeerId, name));
    }

    internal static void Receive(ushort sender, TeamPacket packet)
    {
        if (!Enabled) return;
        if (MultiplayerSession.IsHost)
        {
            if (sender != packet.PlayerId || !HasTeam(packet.Team)) return;
            Set(sender, packet.Team);
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

    internal static bool MatchesSpawnTeam(ushort id, string value)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(value) || !playerTeams.TryGetValue(id, out var playerTeam))
            return false;
        foreach (var team in teams)
        {
            if (!string.Equals(team.Name, playerTeam, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(team.Name, value.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            Color color;
            return ColorUtility.TryParseHtmlString(value.Trim(), out color) && color == team.Color;
        }
        return false;
    }

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
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        var image = root.AddComponent<Image>();
        image.color = new UnityEngine.Color(0f, 0f, 0f, 0.78f);
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        var title = Text(root.transform, "CHOOSE TEAM", new Vector2(0f, 255f), 30);
        title.fontStyle = FontStyles.Bold;
        var columnWidth = Mathf.Clamp((1720f - 18f * (teams.Count - 1)) / teams.Count, 150f, 400f);
        for (var i = 0; i < teams.Count; i++)
        {
            var team = teams[i];
            var column = new GameObject("TeamColumn", typeof(RectTransform), typeof(Image));
            column.transform.SetParent(root.transform, false);
            var columnRect = column.GetComponent<RectTransform>();
            columnRect.anchoredPosition = new Vector2((i - (teams.Count - 1) * 0.5f) * (columnWidth + 18f), -24f);
            columnRect.sizeDelta = new Vector2(columnWidth, 410f);
            var columnColor = team.Color;
            columnColor.a = 0.18f;
            column.GetComponent<Image>().color = columnColor;

            var header = Text(column.transform, team.Name.ToUpperInvariant(), new Vector2(0f, 160f), 21);
            header.color = team.Color;
            header.fontStyle = FontStyles.Bold;
            header.rectTransform.sizeDelta = new Vector2(columnWidth - 24f, 38f);
            teamHeaders[team.Name] = header;

            var button = new GameObject("Join", typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(column.transform, false);
            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchoredPosition = new Vector2(0f, 108f);
            buttonRect.sizeDelta = new Vector2(columnWidth - 28f, 42f);
            button.GetComponent<Image>().color = team.Color;
            var label = Text(button.transform, "JOIN", Vector2.zero, 18);
            label.rectTransform.sizeDelta = buttonRect.sizeDelta;
            label.color = UnityEngine.Color.white;
            var name = team.Name;
            button.GetComponent<Button>().onClick.AddListener(() => Choose(name));

            var players = Text(column.transform, "", new Vector2(0f, -76f), 16);
            players.alignment = TextAlignmentOptions.TopLeft;
            players.enableWordWrapping = true;
            players.rectTransform.sizeDelta = new Vector2(columnWidth - 34f, 240f);
            teamPlayers[team.Name] = players;
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
        {
            var count = Count(team.Name);
            if (teamHeaders.TryGetValue(team.Name, out var header) && header != null)
                header.text = team.Name.ToUpperInvariant() + "\n<size=14>" + count + " PLAYER" + (count == 1 ? "" : "S") + "</size>";
            if (teamPlayers.TryGetValue(team.Name, out var players) && players != null)
                players.text = PlayersText(team.Name);
        }
    }

    private static string PlayersText(string team)
    {
        var values = new List<string>();
        if (Name(MultiplayerSession.LocalPeerId) == team)
            values.Add(MultiplayerSession.LocalPlayerName + " <size=13>(YOU)</size>");
        foreach (var remote in NetworkAvatarRegistry.RemotePlayers())
            if (Name(remote.PeerId) == team) values.Add(remote.Name);
        return values.Count == 0 ? "<color=#FFFFFF99>NO PLAYERS</color>" : string.Join("\n", values);
    }

    private static void ClosePanel()
    {
        if (panel == null) return;
        UnityEngine.Object.Destroy(panel);
        panel = null;
        teamHeaders.Clear();
        teamPlayers.Clear();
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
