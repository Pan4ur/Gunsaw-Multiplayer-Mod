using UnityEngine;

[Serializable]
internal sealed class JkFlipFlopData
{
    public int j = -1;
    public int k = -1;
    public int clock = -1;
    public int q = -1;
    public int notQ = -1;
    public int initialQ;
}

internal sealed class JkFlipFlopDefinition : CustomPropDefinition<JkFlipFlopData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/Logic/JK";
    public override string DisplayName => "JK FLIP-FLOP";
    public override string Description => "Rising-edge JK flip-flop: 00 hold, 01 reset, 10 set, 11 toggle.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => LogicPropIcon.Get("jk");

    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("J", "Activation ID", value => value.j, (value, number) => value.j = number, -1),
        Integer("K", "Activation ID", value => value.k, (value, number) => value.k = number, -1),
        Integer("CLK", "Activation ID", value => value.clock, (value, number) => value.clock = number, -1),
        Integer("Q", "Activation ID", value => value.q, (value, number) => value.q = number, -1),
        Integer("/Q", "Activation ID", value => value.notQ, (value, number) => value.notQ = number, -1),
        Integer("Initial Q", "0 or 1", value => value.initialQ, (value, number) => value.initialQ = number > 0 ? 1 : 0, 0)
    };

    public override void CreateRuntime(GameObject gameObject, JkFlipFlopData data) => gameObject.AddComponent<JkFlipFlopRuntime>().Configure(data);
}

internal sealed class JkFlipFlopRuntime : LogicRuntimeBase
{
    private JkFlipFlopData data;
    private bool jHigh;
    private bool kHigh;
    private bool clockHigh;
    private bool prevClock;
    private bool q;

    internal void Configure(JkFlipFlopData value)
    {
        data = value;
        q = value != null && value.initialQ != 0;
    }

    private void Activate(int id)
    {
        if (data == null) return;
        if (id == data.j) jHigh = true;
        if (id == data.k) kHigh = true;
        if (id == data.clock) clockHigh = true;
    }

    public override void LogicEvaluate()
    {
        if (data == null) return;
        var j = jHigh;
        var k = kHigh;
        var clock = clockHigh;
        jHigh = false;
        kHigh = false;
        clockHigh = false;
        
        if (!prevClock && clock)
        {
            if (!j && k) 
                q = false;
            else if (j && !k)
                q = true;
            else if (j && k) 
                q = !q;
        }

        prevClock = clock;
        LogicTickService.OutputHigh(q ? data.q : data.notQ);
    }
}