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
    public override string Description => "Changes the color of the lamp with the specified ID when activated.";
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
    private ToggleableLampRuntime lamp;
    private Color start;
    private Color target;
    private float elapsed;
    private bool changing;

    public int ActivationId => data != null ? data.activationId : -1;

    internal void Configure(LampColorChangerData value) => data = value;

    private void Activate(int id)
    {
        if (data == null || id != data.activationId || (MultiplayerSession.IsActive && !MultiplayerSession.IsHost))
            return;
        
        if (!ColorUtility.TryParseHtmlString(data.color, out target))
            return;
        
        lamp = ToggleableLampSystem.RuntimeForId(data.lampId);
        if (lamp == null || (changing && SameColor(target, this.target)))
            return;
        
        start = lamp.Color;
        elapsed = 0f;
        changing = data.delay > 0f && !SameColor(start, target);
        
        if (!changing)
            lamp.SetColor(target);
    }

    private void Update()
    {
        if (!changing || lamp == null)
            return;
        
        elapsed += Time.deltaTime;
        lamp.SetColor(Color.Lerp(start, target, Mathf.Clamp01(elapsed / data.delay)));
        
        if (elapsed >= data.delay)
            changing = false;
    }

    private static bool SameColor(Color left, Color right) => Mathf.Approximately(left.r, right.r) &&
                                                              Mathf.Approximately(left.g, right.g) &&
                                                              Mathf.Approximately(left.b, right.b) &&
                                                              Mathf.Approximately(left.a, right.a);
}