using UnityEngine;

[Serializable]
internal sealed class LampColorChangerData
{
    public int activationId;
    public int lampId;
    public string color = "#FFFFFF";
    public float delay;
}

internal sealed class LampColorChangerDefinition : CustomPropDefinition<LampColorChangerData>
{
    private CustomPropField[] fields;

    public override string TypeId => "MP/LampColorChanger";
    public override string DisplayName => "Lamp Color Changer";
    public override string Description => "Changes the color of all lamps with the specified ID when activated.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.lamp-color-changer.png", 28f, new Vector2(0.5f, 0.15f));

    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("Activation ID", "Signal ID", value => value.activationId, (value, number) => value.activationId = number, 0),
        Integer("Lamp ID", "Level lamp ID", value => value.lampId, (value, number) => value.lampId = number, 0),
        Text("Color", "#RRGGBB or #RRGGBBAA", value => value.color, (value, text) => value.color = text),
        Float("Delay", "Seconds", value => value.delay, (value, number) => value.delay = number, 0f)
    };

    public override void CreateRuntime(GameObject gameObject, LampColorChangerData data) =>
        gameObject.AddComponent<LampColorChangerRuntime>().Configure(data);
}

internal sealed class LampColorChangerRuntime : MonoBehaviour, IActivationIdReceiver
{
    private LampColorChangerData data;
    private readonly List<ToggleableLampRuntime> lamps = new();
    private readonly List<Color> starts = new();
    private Color target;
    private float elapsed;
    private bool changing;

    public int ActivationId => data != null ? data.activationId : -1;

    internal void Configure(LampColorChangerData value) => data = value;

    private void Activate(int id)
    {
        if (data == null || id != data.activationId || (MultiplayerSession.IsActive && !MultiplayerSession.IsHost))
            return;
        
        if (!ColorUtility.TryParseHtmlString(data.color, out var nextTarget))
            return;
        
        if (changing && SameColor(nextTarget, target))
            return;
        
        ToggleableLampSystem.RuntimesForId(data.lampId, lamps);
        starts.Clear();
        target = nextTarget;
        elapsed = 0f;
        changing = false;
        foreach (var lamp in lamps)
        {
            var start = lamp.Color;
            starts.Add(start);
            if (data.delay > 0f && !SameColor(start, target))
                changing = true;
            else
                lamp.SetColor(target);
        }
    }

    private void Update()
    {
        if (!changing)
            return;
        
        elapsed += Time.deltaTime;
        var progress = Mathf.Clamp01(elapsed / data.delay);
        for (var index = 0; index < lamps.Count; index++)
            if (lamps[index] != null)
                lamps[index].SetColor(Color.Lerp(starts[index], target, progress));
        
        if (elapsed >= data.delay)
            changing = false;
    }

    private static bool SameColor(Color left, Color right) => Mathf.Approximately(left.r, right.r) &&
                                                              Mathf.Approximately(left.g, right.g) &&
                                                              Mathf.Approximately(left.b, right.b) &&
                                                              Mathf.Approximately(left.a, right.a);
}