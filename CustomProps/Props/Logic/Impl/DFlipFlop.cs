using UnityEngine;

[Serializable]
internal sealed class DFlipFlopData
{
    public int d = -1;
    public int clock = -1;
    public int q = -1;
    public int notQ = -1;
    public int initialQ;
}

internal sealed class DFlipFlopDefinition : CustomPropDefinition<DFlipFlopData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/Logic/DFF";
    public override string DisplayName => "D FLIP-FLOP";
    public override string Description => "Rising-edge D flip-flop. Q samples D when CLK changes LOW to HIGH.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => LogicPropIcon.Get("dff");

    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("D", "Activation ID", value => value.d, (value, number) => value.d = number, -1),
        Integer("CLK", "Activation ID", value => value.clock, (value, number) => value.clock = number, -1),
        Integer("Q", "Activation ID", value => value.q, (value, number) => value.q = number, -1),
        Integer("/Q", "Activation ID", value => value.notQ, (value, number) => value.notQ = number, -1),
        Integer("Initial Q", "0 or 1", value => value.initialQ, (value, number) => value.initialQ = number > 0 ? 1 : 0, 0)
    };

    public override void CreateRuntime(GameObject gameObject, DFlipFlopData data) =>
        gameObject.AddComponent<DFlipFlopRuntime>().Configure(data);
}

internal sealed class DFlipFlopRuntime : LogicRuntimeBase
{
    private DFlipFlopData data;
    private bool dHigh;
    private bool clockHigh;
    private bool previousClock;
    private bool q;

    internal void Configure(DFlipFlopData value)
    {
        data = value;
        q = value != null && value.initialQ != 0;
    }

    private void Activate(int id)
    {
        ReceiveActivation(id);
    }

    internal override void AddActivationRoutes(LogicTickService service, int targetIndex)
    {
        if (data == null) return;
        service.AddActivationReceiver(data.d, this, targetIndex);
        service.AddActivationReceiver(data.clock, this, targetIndex);
    }

    internal override void ReceiveActivation(int id)
    {
        if (data == null) return;
        if (id == data.d) dHigh = true;
        if (id == data.clock) clockHigh = true;
    }

    public override void LogicEvaluate()
    {
        if (data == null) return;
        var d = dHigh;
        var clock = clockHigh;
        dHigh = false;
        clockHigh = false;
        if (!previousClock && clock) q = d;
        previousClock = clock;

        LogicTickService.OutputHigh(q ? data.q : data.notQ);
    }
}