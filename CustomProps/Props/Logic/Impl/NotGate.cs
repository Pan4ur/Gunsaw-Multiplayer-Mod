using UnityEngine;

[Serializable]
internal sealed class NotGateData
{
    public int input = -1;
    public int output = -1;
}

internal sealed class NotGateDefinition : CustomPropDefinition<NotGateData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/Logic/NOT";
    public override string DisplayName => "NOT";
    public override string Description => "Logical inverter. Emits Output every logic tick while Input is LOW.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => LogicPropIcon.Get("not");

    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("Input", "Activation ID", value => value.input, (value, number) => value.input = number, -1),
        Integer("Output", "Activation ID", value => value.output, (value, number) => value.output = number, -1)
    };

    public override void CreateRuntime(GameObject gameObject, NotGateData data) => gameObject.AddComponent<NotGateRuntime>().Configure(data);
}

internal sealed class NotGateRuntime : LogicRuntimeBase
{
    private NotGateData data;
    private bool inputHigh;
    internal void Configure(NotGateData value) => data = value;

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
        var high = inputHigh;
        inputHigh = false;
        if (!high) LogicTickService.OutputHigh(data.output);
    }
}