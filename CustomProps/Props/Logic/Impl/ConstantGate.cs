using UnityEngine;

[Serializable]
internal sealed class ConstantGateData
{
    public int output = -1;
    public int value = 1;
}

internal sealed class ConstantGateDefinition : CustomPropDefinition<ConstantGateData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/Logic/CONST";
    public override string DisplayName => "CONST";
    public override string Description => "Constant logical level. Value 1 emits Output every logic tick; 0 stays LOW.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => LogicPropIcon.Get("const");

    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("Output", "Activation ID", value => value.output, (value, number) => value.output = number, -1),
        Integer("Value", "0 or 1", value => value.value, (value, number) => value.value = number > 0 ? 1 : 0, 0)
    };

    public override void CreateRuntime(GameObject gameObject, ConstantGateData data) => gameObject.AddComponent<ConstantGateRuntime>().Configure(data);
}

internal sealed class ConstantGateRuntime : LogicRuntimeBase
{
    private ConstantGateData data;
    internal void Configure(ConstantGateData value) => data = value;

    public override void LogicEvaluate()
    {
        if (data != null && data.value != 0) LogicTickService.OutputHigh(data.output);
    }
}