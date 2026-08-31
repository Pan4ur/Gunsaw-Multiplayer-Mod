using UnityEngine;

[Serializable]
internal sealed class SrLatchData
{
    public int set = -1;
    public int reset = -1;
    public int q = -1;
    public int notQ = -1;
    public int initialQ;
}

internal sealed class SrLatchDefinition : CustomPropDefinition<SrLatchData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/Logic/SR";
    public override string DisplayName => "SR LATCH";

    public override string Description =>
        "SR memory latch. S sets Q, R resets Q. If both are HIGH, the previous state is kept.";

    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => LogicPropIcon.Get("sr");

    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("S", "Activation ID", value => value.set, (value, number) => value.set = number, -1),
        Integer("R", "Activation ID", value => value.reset, (value, number) => value.reset = number, -1),
        Integer("Q", "Activation ID", value => value.q, (value, number) => value.q = number, -1),
        Integer("/Q", "Activation ID", value => value.notQ, (value, number) => value.notQ = number, -1),
        Integer("Initial Q", "0 or 1", value => value.initialQ, (value, number) => value.initialQ = number > 0 ? 1 : 0, 0)
    };

    public override void CreateRuntime(GameObject gameObject, SrLatchData data) =>
        gameObject.AddComponent<SrLatchRuntime>().Configure(data);
}

internal sealed class SrLatchRuntime : LogicRuntimeBase
{
    private SrLatchData data;
    private bool setHigh;
    private bool resetHigh;
    private bool q;

    internal void Configure(SrLatchData value)
    {
        data = value;
        q = value != null && value.initialQ != 0;
    }

    private void Activate(int id)
    {
        if (data == null) return;
        if (id == data.set) setHigh = true;
        if (id == data.reset) resetHigh = true;
    }

    public override void LogicEvaluate()
    {
        if (data == null) return;
        var set = setHigh;
        var reset = resetHigh;
        setHigh = false;
        resetHigh = false;
        if (set && !reset) q = true;
        else if (reset && !set) q = false;
        LogicTickService.OutputHigh(q ? data.q : data.notQ);
    }
}