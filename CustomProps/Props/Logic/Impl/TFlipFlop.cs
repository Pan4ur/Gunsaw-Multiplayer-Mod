using UnityEngine;

[Serializable]
internal sealed class TFlipFlopData
{
    public int t = -1;
    public int clock = -1;
    public int q = -1;
    public int notQ = -1;
    public int initialQ;
}

internal sealed class TFlipFlopDefinition : CustomPropDefinition<TFlipFlopData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/Logic/TFF";
    public override string DisplayName => "T FLIP-FLOP";
    public override string Description => "Rising-edge T flip-flop. Toggles Q on a CLK edge while T is HIGH.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => LogicPropIcon.Get("tff");

    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("T", "Activation ID", value => value.t, (value, number) => value.t = number, -1),
        Integer("CLK", "Activation ID", value => value.clock, (value, number) => value.clock = number, -1),
        Integer("Q", "Activation ID", value => value.q, (value, number) => value.q = number, -1),
        Integer("/Q", "Activation ID", value => value.notQ, (value, number) => value.notQ = number, -1),
        Integer("Initial Q", "0 or 1", value => value.initialQ, (value, number) => value.initialQ = number > 0 ? 1 : 0, 0)
    };

    public override void CreateRuntime(GameObject gameObject, TFlipFlopData data) => gameObject.AddComponent<TFlipFlopRuntime>().Configure(data);
}

internal sealed class TFlipFlopRuntime : LogicRuntimeBase
{
    private TFlipFlopData data;
    private bool tHigh;
    private bool clockHigh;
    private bool previousClock;
    private bool q;

    internal void Configure(TFlipFlopData value)
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
        service.AddActivationReceiver(data.t, this, targetIndex);
        service.AddActivationReceiver(data.clock, this, targetIndex);
    }

    internal override void ReceiveActivation(int id)
    {
        if (data == null) return;
        if (id == data.t) tHigh = true;
        if (id == data.clock) clockHigh = true;
    }

    public override void LogicEvaluate()
    {
        if (data == null) return;
        var t = tHigh;
        var clock = clockHigh;
        tHigh = false;
        clockHigh = false;
        if (!previousClock && clock && t) q = !q;
        previousClock = clock;
        LogicTickService.OutputHigh(q ? data.q : data.notQ);
    }
}