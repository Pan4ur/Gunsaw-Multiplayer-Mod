using UnityEngine;

[Serializable]
internal sealed class ActivateOnStartData
{
    public int output = -1;
}

internal sealed class ActivateOnStartDefinition : CustomPropDefinition<ActivateOnStartData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/ActivateOnStart";
    public override string DisplayName => "Activate On Start";
    public override string Description => "Sends one activation pulse to Output ID when the level starts.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.activate-on-start.png", 28f, new Vector2(0.5f, 0.15f));
    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("Output ID", "Activation ID; -1 = disabled", value => value.output, (value, number) => value.output = number, -1)
    };

    public override void CreateRuntime(GameObject gameObject, ActivateOnStartData data) =>
        gameObject.AddComponent<ActivateOnStartRuntime>().Configure(data);
}

internal sealed class ActivateOnStartRuntime : LogicRuntimeBase
{
    private ActivateOnStartData data;
    private bool fired;

    internal void Configure(ActivateOnStartData value) => data = value;

    public override void LogicEvaluate()
    {
        if (data == null || fired || !LogicTickService.CanSimulate) return;
        fired = true;
        LogicTickService.OutputHigh(data.output);
    }
}