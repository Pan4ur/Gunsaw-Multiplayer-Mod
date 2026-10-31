using UnityEngine;

[Serializable]
internal sealed class NoPvpZoneData
{
    public string team = "";
    public int activationId;
}

internal sealed class NoPvpZoneDefinition : CustomPropDefinition<NoPvpZoneData>
{
    private static SpriteRenderer template;
    internal static SpriteRenderer Template => template != null ? template :
        template = Resources.Load<GameObject>("Editor/Building/Triggers/DeathZone").GetComponent<SpriteRenderer>();
    private CustomPropField[] fields;
    public override string TypeId => "MP/NoPvpZone";
    public override string DisplayName => "No PvP Zone";
    public override string Description => "Blocks PvP damage in this area. Empty team protects everyone. Otherwise use a team ID, color or name. Can be disabled.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => Template.sprite;
    public override CustomPropField[] Fields => fields ??= new[]
    {
        Text("Team", "Empty = everyone; ID, color or name", value => value.team, (value, text) => value.team = text),
        Integer("Activation ID", "0 = no activation", value => value.activationId, (value, number) => value.activationId = number, 0)
    };

    public override void CreateRuntime(GameObject gameObject, NoPvpZoneData data) =>
        gameObject.AddComponent<NoPvpZoneRuntime>().Configure(data, gameObject.GetComponent<CustomPropMarker>().Instance.Size);

    internal static void UpdateMarker(CustomPropMarker marker)
    {
        if (!(marker.Instance.Data is NoPvpZoneData data)) return;
        var renderer = marker.GetComponent<SpriteRenderer>();
        renderer.sprite = Template.sprite;
        renderer.sharedMaterial = Template.sharedMaterial;
        renderer.drawMode = Template.drawMode;
        renderer.size = marker.Instance.Size;
        renderer.color = new Color(0.2f, 0.8f, 1f, Template.color.a);
        var circle = marker.GetComponent<CircleCollider2D>();
        if (circle != null) circle.enabled = false;
        var box = marker.GetComponent<BoxCollider2D>() ?? marker.gameObject.AddComponent<BoxCollider2D>();
        box.size = renderer.sprite.bounds.size;
        box.autoTiling = true;
    }

    internal static Vector2 Size(Vector2 value) => new Vector2(Extent(value.x), Extent(value.y));
    private static float Extent(float value) => float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? 2f : value;
}

internal sealed class NoPvpZoneRuntime : MonoBehaviour, IActivationIdReceiver
{
    private static readonly List<NoPvpZoneRuntime> zones = new();
    private NoPvpZoneData data;
    internal static bool HasZones => zones.Count != 0;

    private bool powered = true;
    private ulong id;
    public int ActivationId => data != null ? data.activationId : -1;

    internal static ulong[] CaptureDisabled() => zones.Where(zone => !zone.powered).Select(zone => zone.id).ToArray();

    internal static void ApplyDisabled(ulong[] disabled)
    {
        foreach (var zone in zones) zone.powered = Array.IndexOf(disabled, zone.id) < 0;
    }

    private void Activate(int activationId)
    {
        if (data == null || ActivationId <= 0 || activationId != ActivationId ||
            (MultiplayerSession.IsActive && !MultiplayerSession.IsHost)) return;
        powered = !powered;
        if (GunsawMultiplayerPlugin.World != null) GunsawMultiplayerPlugin.World.nextSnapshot = 0f;
    }

    private Vector2 size;
    internal void Configure(NoPvpZoneData value, Vector2 dimensions)
    {
        data = value;
        id = NetworkWireId.FromString(gameObject.name);
        powered = true;
        size = NoPvpZoneDefinition.Size(dimensions);
    }
    private void OnEnable() => zones.Add(this);
    private void OnDisable() => zones.Remove(this);

    internal static bool Protects(BodyScript body, ushort peerId)
    {
        if (body == null) return false;
        for (var index = 0; index < zones.Count; index++)
        {
            var zone = zones[index];
            if (zone.data == null || !zone.powered) continue;
            var point = zone.transform.InverseTransformPoint(body.transform.position);
            if (Mathf.Abs(point.x) <= zone.size.x * 0.5f &&
                Mathf.Abs(point.y) <= zone.size.y * 0.5f &&
                (string.IsNullOrWhiteSpace(zone.data.team) || TeamSystem.MatchesSpawnTeam(peerId, zone.data.team)))
                return true;
        }
        return false;
    }
}