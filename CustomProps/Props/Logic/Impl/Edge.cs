using UnityEngine;

[Serializable]
internal sealed class EdgeDetectorData
{
    public int input = -1;
    public int output = -1;
    public int mode;
}

internal sealed class EdgeDetectorDefinition : CustomPropDefinition<EdgeDetectorData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/Logic/EDGE";
    public override string DisplayName => "EDGE";

    public override string Description => "Converts a logical level into one Activation pulse. Mode: 0 rising, 1 falling, 2 both.";

    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => LogicPropIcon.Get("edge");

    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("Input", "Activation ID", value => value.input, (value, number) => value.input = number, -1),
        Integer("Output", "Activation ID", value => value.output, (value, number) => value.output = number, -1),
        Integer("Mode", "0 rise / 1 fall / 2 both", value => value.mode, (value, number) => value.mode = Mathf.Clamp(number, 0, 2), 0)
    };

    public override void CreateRuntime(GameObject gameObject, EdgeDetectorData data) => gameObject.AddComponent<EdgeDetectorRuntime>().Configure(data);
}

internal sealed class EdgeDetectorRuntime : LogicRuntimeBase
{
    private EdgeDetectorData data;
    private bool inputHigh;
    private bool previous;
    internal void Configure(EdgeDetectorData value) => data = value;

    private void Activate(int id)
    {
        ReceiveActivation(id);
    }

    internal override void AddActivationRoutes(LogicTickService service, int targetIndex)
    {
        if (data == null) return;
        service.AddActivationReceiver(data.input, this, targetIndex);
    }

    internal override void ReceiveActivation(int id)
    {
        if (data != null && id == data.input) inputHigh = true;
    }

    public override void LogicEvaluate()
    {
        if (data == null) return;
        var current = inputHigh;
        inputHigh = false;
        var rising = !previous && current;
        var falling = previous && !current;
        previous = current;
        bool shouldToggle = (data.mode == 0 && rising) || (data.mode == 1 && falling) || (data.mode == 2 && (rising || falling));
        
        if (shouldToggle) LogicTickService.OutputHigh(data.output);
    }
}
