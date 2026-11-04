using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class EditorPermissionsUi
{
    private readonly GameObject permissionsRoot;
    private readonly RectTransform permissionsContent;
    private readonly TMP_Text template;
    private readonly Action<ushort[]> setAllowedEditors;
    private ushort[] allowedEditors = [];
    private readonly Dictionary<ushort, (GameObject Row, TMP_Text Name, Button Access)> permissionRows = new();

    internal bool IsOpen => permissionsRoot != null && permissionsRoot.activeSelf;

    internal EditorPermissionsUi(TMP_Text template, Action<ushort[]> setAllowedEditors)
    {
        this.template = template;
        this.setAllowedEditors = setAllowedEditors;
        permissionsRoot = new GameObject("Editor Permissions", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = permissionsRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;
        var scaler = permissionsRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var backdrop = PermissionPanel(permissionsRoot.transform, "Backdrop", Vector2.zero, Vector2.zero);
        backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);
        var backdropRect = backdrop.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;

        var panel = PermissionPanel(backdrop.transform, "Edit Access", Vector2.zero, new Vector2(620f, 610f));
        var outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(0.25f, 0.235f, 0.28f);
        outline.effectDistance = new Vector2(1f, -1f);
        PermissionText(panel.transform, "EDIT ACCESS", new Vector2(-25f, 267f), new Vector2(510f, 36f), 24f).color = new Color(1f, 0.72f, 0.46f);
        var close = PermissionButton(panel.transform, "", new Vector2(269f, 267f), new Vector2(36f, 36f));
        MultiplayerLobbyUi.CreateIcon(close.transform, MultiplayerLobbyUi.LobbyIcon.Close, new Color(0.88f, 0.89f, 0.91f));
        close.onClick.AddListener(() => permissionsRoot.SetActive(false));
        var view = PermissionPanel(panel.transform, "Players", new Vector2(0f, -5f), new Vector2(560f, 390f));
        view.GetComponent<Image>().color = new Color(0.058f, 0.054f, 0.068f);
        view.AddComponent<RectMask2D>();
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(view.transform, false);
        permissionsContent = content.GetComponent<RectTransform>();
        permissionsContent.anchorMin = new Vector2(0.5f, 1f);
        permissionsContent.anchorMax = new Vector2(0.5f, 1f);
        permissionsContent.pivot = new Vector2(0.5f, 1f);
        permissionsContent.anchoredPosition = Vector2.zero;
        var scroll = view.AddComponent<ScrollRect>();
        scroll.viewport = view.GetComponent<RectTransform>();
        scroll.content = permissionsContent;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        PermissionButton(panel.transform, "Deny All", new Vector2(-144f, -253f), new Vector2(272f, 42f))
            .onClick.AddListener(() => setAllowedEditors([]));
        PermissionButton(panel.transform, "Allow All", new Vector2(144f, -253f), new Vector2(272f, 42f))
            .onClick.AddListener(() => setAllowedEditors(MultiplayerSession.PeerIds()));
        permissionsRoot.SetActive(false);
    }

    internal void Open(ushort[] peers)
    {
        permissionsRoot.SetActive(true);
        Refresh(peers, true);
    }

    internal void Refresh(ushort[] peersWithAccess, bool canManage)
    {
        if (!IsOpen)
            return;
        if (!canManage || Input.GetKeyDown(KeyCode.Escape))
        {
            permissionsRoot.SetActive(false);
            return;
        }

        allowedEditors = peersWithAccess;
        var peers = MultiplayerSession.PeerIds().OrderBy(peer => peer).ToArray();
        foreach (var peer in permissionRows.Keys.Where(peer => !peers.Contains(peer)).ToArray())
        {
            UnityEngine.Object.Destroy(permissionRows[peer].Row);
            permissionRows.Remove(peer);
        }
        for (var index = 0; index < peers.Length; index++)
        {
            var peer = peers[index];
            if (!permissionRows.TryGetValue(peer, out var row))
            {
                var root = PermissionPanel(permissionsContent, "Player", Vector2.zero, new Vector2(540f, 48f));
                var rowRect = root.GetComponent<RectTransform>();
                rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 1f);
                var name = PermissionText(root.transform, "", new Vector2(-83f, 0f), new Vector2(350f, 38f), 18f);
                name.richText = false;
                name.overflowMode = TextOverflowModes.Ellipsis;
                var access = PermissionButton(root.transform, "", new Vector2(185f, 0f), new Vector2(146f, 36f));
                access.onClick.AddListener(() => setAllowedEditors(allowedEditors.Contains(peer)
                    ? allowedEditors.Where(id => id != peer).ToArray()
                    : allowedEditors.Concat(new[] { peer }).ToArray()));
                row = (root, name, access);
                permissionRows.Add(peer, row);
            }
            row.Row.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -32f - index * 56f);
            row.Name.text = MultiplayerSession.PlayerName(peer);
            row.Access.GetComponentInChildren<TMP_Text>().text = allowedEditors.Contains(peer) ? "Allowed" : "Denied";
            MultiplayerLobbyUi.SetModeButton(row.Access, allowedEditors.Contains(peer));
        }
        permissionsContent.sizeDelta = new Vector2(560f, Mathf.Max(390f, peers.Length * 56f + 8f));
    }

    internal void Destroy() => UnityEngine.Object.Destroy(permissionsRoot);

    private static GameObject PermissionPanel(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(parent, false);
        PermissionRect(panel.GetComponent<RectTransform>(), position, size);
        panel.GetComponent<Image>().color = new Color(0.075f, 0.071f, 0.086f);
        return panel;
    }

    private TMP_Text PermissionText(Transform parent, string value, Vector2 position, Vector2 size, float fontSize)
    {
        var root = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        root.transform.SetParent(parent, false);
        PermissionRect(root.GetComponent<RectTransform>(), position, size);
        var text = root.GetComponent<TextMeshProUGUI>();
        text.font = template.font;
        text.fontSharedMaterial = template.fontSharedMaterial;
        text.fontSize = fontSize;
        text.color = new Color(0.88f, 0.89f, 0.91f);
        text.alignment = TextAlignmentOptions.Left;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    private Button PermissionButton(Transform parent, string label, Vector2 position, Vector2 size)
    {
        var root = PermissionPanel(parent, label, position, size);
        root.GetComponent<Image>().color = Color.white;
        var button = root.AddComponent<Button>();
        button.targetGraphic = root.GetComponent<Image>();
        button.colors = MultiplayerLobbyUi.MenuButtonColors(button.colors);
        root.GetComponent<Image>().CrossFadeColor(button.colors.normalColor, 0f, true, true);
        PermissionText(root.transform, label, Vector2.zero, size, 16f).alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static void PermissionRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
