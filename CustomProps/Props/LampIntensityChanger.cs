using UnityEngine;

[Serializable]
internal sealed class LampIntensityChangerData
{
    public int activationId;
    public int lampId;
    public float intensity = 1f;
    public float delay;
}

internal sealed class LampIntensityChangerDefinition : CustomPropDefinition<LampIntensityChangerData>
{
    private CustomPropField[] fields;

    public override string TypeId => "MP/LampIntensityChanger";
    public override string DisplayName => "Lamp Intensity Changer";

    public override string Description => "Changes the intensity of all lamps with the specified ID when activated.";

    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.lamp-intensity-changer.png", 28f, new Vector2(0.5f, 0.15f));

    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("Activation ID", "Signal ID", value => value.activationId, (value, number) => value.activationId = number, 0),
        Integer("Lamp ID", "Level lamp ID", value => value.lampId, (value, number) => value.lampId = number, 0),
        Float("Intensity", "Brightness", value => value.intensity, (value, number) => value.intensity = number, 0f),
        Float("Delay", "Seconds", value => value.delay, (value, number) => value.delay = number, 0f)
    };

    public override void CreateRuntime(GameObject gameObject, LampIntensityChangerData data) =>
        gameObject.AddComponent<LampIntensityChangerRuntime>().Configure(data);
}

internal sealed class LampIntensityChangerRuntime : MonoBehaviour, IActivationIdReceiver
{
    private LampIntensityChangerData data;
    private readonly List<ToggleableLampRuntime> lamps = new();
    private readonly List<float> starts = new();
    private float target;
    private float elapsed;
    private bool changing;

    public int ActivationId => data != null ? data.activationId : -1;

    internal void Configure(LampIntensityChangerData value) => data = value;

    private void Activate(int id)
    {
        if (data == null || id != data.activationId || (MultiplayerSession.IsActive && !MultiplayerSession.IsHost))
            return;
        
        if (changing && Mathf.Approximately(target, data.intensity))
            return;
        
        ToggleableLampSystem.RuntimesForId(data.lampId, lamps);
        starts.Clear();
        target = data.intensity;
        elapsed = 0f;
        changing = false;
        foreach (var lamp in lamps)
        {
            var start = lamp.Intensity;
            starts.Add(start);
            if (data.delay > 0f && !Mathf.Approximately(start, target))
                changing = true;
            else
                lamp.SetIntensity(target);
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
                lamps[index].SetIntensity(Mathf.Lerp(starts[index], target, progress));
        
        if (elapsed >= data.delay)
            changing = false;
    }
}