using UnityEngine;

[Serializable]
internal sealed class PlayerDetectorData
{
    public int outAdd = -1;
    public int outRemove = -1;
    public string team = "";
    public int mode;
}

internal sealed class PlayerDetectorDefinition : CustomPropDefinition<PlayerDetectorData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/PlayerDetector";
    public override string DisplayName => "Player Detector";
    public override string Description => "Mode 0: pulse for each player entering/leaving. Mode 1: pulse for first entry / last exit. Mode 2: Out Enter HIGH while occupied, Out Leave HIGH while empty. Empty team = everyone; otherwise team ID, color or name.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => NoPvpZoneDefinition.Template.sprite;
    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("Out Enter", "Activation ID; -1 = disabled", value => value.outAdd, (value, number) => value.outAdd = number, -1),
        Integer("Out Leave", "Activation ID; -1 = disabled", value => value.outRemove, (value, number) => value.outRemove = number, -1),
        Text("Team", "Empty = everyone; ID, color or name", value => value.team, (value, text) => value.team = text.Trim()),
        Integer("Mode", "0 = each; 1 = first/last; 2 = HIGH", value => value.mode, (value, number) => value.mode = Mathf.Clamp(number, 0, 2), 0)
    };

    public override void CreateRuntime(GameObject gameObject, PlayerDetectorData data) =>
        gameObject.AddComponent<PlayerDetectorRuntime>().Configure(data, gameObject.GetComponent<CustomPropMarker>().Instance.Size);
}

internal sealed class PlayerDetectorRuntime : LogicRuntimeBase
{
    private PlayerDetectorData data;
    private Vector2 size;
    private HashSet<ushort> inside = new();
    private HashSet<ushort> current = new();

    internal void Configure(PlayerDetectorData value, Vector2 dimensions)
    {
        data = value;
        size = NoPvpZoneDefinition.Size(dimensions);
    }

    public override void LogicEvaluate()
    {
        if (data == null || !LogicTickService.CanSimulate)
            return;
        
        current.Clear();
        CheckPlayer(MultiplayerSession.LocalPeerId, PlayerScript.player == null ? null : PlayerScript.player.bodyScript);
        if (MultiplayerSession.IsActive)
            foreach (var pair in NetworkAvatarManager.replicas)
                if (pair.Value != null && MultiplayerSession.HasPeer(pair.Key))
                    CheckPlayer(pair.Key, pair.Value.remoteBody);

        switch (data.mode)
        {
            case 2:
                LogicTickService.OutputHigh(current.Count > 0 ? data.outAdd : data.outRemove);
                break;
            case 0:
            {
                foreach (var unused in inside.Where(peer => !current.Contains(peer))) Emit(false);
                foreach (var unused in current.Where(peer => !inside.Contains(peer))) Emit(true);
                break;
            }
            default:
            {
                if (inside.Count == 0 && current.Count > 0) Emit(true);
                if (inside.Count > 0 && current.Count == 0) Emit(false);
                break;
            }
        }
        
        (inside, current) = (current, inside);
    }

    private void CheckPlayer(ushort peer, BodyScript body)
    {
        if (body == null || !body.isAlive || !body.gameObject.activeInHierarchy)
            return;
        
        if (!string.IsNullOrWhiteSpace(data.team) && !(TeamSystem.Enabled ? TeamSystem.MatchesSpawnTeam(peer, data.team) : string.Equals(body.team, data.team.Trim(), StringComparison.Ordinal)))
            return;
        
        var point = transform.InverseTransformPoint(body.transform.position);
        if (Mathf.Abs(point.x) <= size.x * 0.5f && Mathf.Abs(point.y) <= size.y * 0.5f)
            current.Add(peer);
    }

    private void Emit(bool added)
    {
        var id = added ? data.outAdd : data.outRemove;
        if (id < 0)
            return;
        
        foreach (var target in GameObject.FindGameObjectsWithTag("Activateable"))
            target.Let( it => it.SendMessage("Activate", id, SendMessageOptions.DontRequireReceiver));
    }
}