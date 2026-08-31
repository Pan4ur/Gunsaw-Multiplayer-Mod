using UnityEngine;

[Serializable]
internal sealed class ClockSignalData
{
    public int output = -1;
    public float period = 1f;
    public int initialHigh;
}

internal sealed class ClockSignalDefinition : CustomPropDefinition<ClockSignalData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/Logic/CLOCK";
    public override string DisplayName => "CLOCK";
    public override string Description => "Square-wave clock. Period is a full LOW+HIGH cycle in seconds.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => LogicPropIcon.Get("clock");

    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("Output", "Activation ID", value => value.output, (value, number) => value.output = number, -1),
        Float("Period", "Seconds", value => value.period, (value, number) => value.period = number, 0.04f),
        Integer("Initial HIGH", "0 or 1", value => value.initialHigh, (value, number) => value.initialHigh = number > 0 ? 1 : 0, 0)
    };

    public override void CreateRuntime(GameObject gameObject, ClockSignalData data) => gameObject.AddComponent<ClockSignalRuntime>().Configure(data);
}

internal sealed class ClockSignalRuntime : LogicRuntimeBase
{
    private ClockSignalData data;
    private float elapsed;
    private bool high;

    internal void Configure(ClockSignalData value)
    {
        data = value;
        high = value != null && value.initialHigh != 0;
    }

    public override void LogicEvaluate()
    {
        if (data == null) return;
        var halfPeriod = Mathf.Max(Time.fixedDeltaTime, data.period * 0.5f);
        elapsed += Time.fixedDeltaTime;
        while (elapsed >= halfPeriod)
        {
            elapsed -= halfPeriod;
            high = !high;
        }

        if (high) LogicTickService.OutputHigh(data.output);
    }
}