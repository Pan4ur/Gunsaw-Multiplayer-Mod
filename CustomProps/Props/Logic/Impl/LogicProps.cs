using UnityEngine;

internal static class LogicPropIcon
{
    internal static Sprite Get(string name)
    {
        return EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.logic-" + name + ".png", 28f, new Vector2(0.5f, 0.15f));
    }
}

internal enum BinaryLogicOperation
{
    And,
    Or,
    Xor,
    Nand,
    Nor
}

[Serializable]
internal sealed class BinaryLogicGateData
{
    public int inputA = -1;
    public int inputB = -1;
    public int output = -1;
}

internal abstract class BinaryLogicGateDefinition : CustomPropDefinition<BinaryLogicGateData>
{
    private CustomPropField[] fields;
    private readonly string typeId;
    private readonly string displayName;
    private readonly BinaryLogicOperation operation;

    private readonly string iconName;

    protected BinaryLogicGateDefinition(string typeId, string displayName, BinaryLogicOperation operation, string iconName)
    {
        this.typeId = typeId;
        this.displayName = displayName;
        this.operation = operation;
        this.iconName = iconName;
    }

    public override string TypeId => typeId;
    public override string DisplayName => displayName;

    public override string Description => displayName + " logic gate. HIGH means its Activation ID is received every logic tick.";

    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => LogicPropIcon.Get(iconName);

    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("Input A", "Activation ID", value => value.inputA, (value, number) => value.inputA = number, -1),
        Integer("Input B", "Activation ID", value => value.inputB, (value, number) => value.inputB = number, -1),
        Integer("Output", "Activation ID", value => value.output, (value, number) => value.output = number, -1)
    };

    public override void CreateRuntime(GameObject gameObject, BinaryLogicGateData data)
    {
        gameObject.AddComponent<BinaryLogicGateRuntime>().Configure(data, operation);
    }
}

internal sealed class AndGateDefinition : BinaryLogicGateDefinition
{
    internal AndGateDefinition() : base("MP/Logic/AND", "AND", BinaryLogicOperation.And, "and") { }
}

internal sealed class OrGateDefinition : BinaryLogicGateDefinition
{
    internal OrGateDefinition() : base("MP/Logic/OR", "OR", BinaryLogicOperation.Or, "or") { }
}

internal sealed class XorGateDefinition : BinaryLogicGateDefinition
{
    internal XorGateDefinition() : base("MP/Logic/XOR", "XOR", BinaryLogicOperation.Xor, "xor") { }
}

internal sealed class NandGateDefinition : BinaryLogicGateDefinition
{
    internal NandGateDefinition() : base("MP/Logic/NAND", "NAND", BinaryLogicOperation.Nand, "nand") { }
}

internal sealed class NorGateDefinition : BinaryLogicGateDefinition
{
    internal NorGateDefinition() : base("MP/Logic/NOR", "NOR", BinaryLogicOperation.Nor, "nor") { }
}

internal sealed class BinaryLogicGateRuntime : LogicRuntimeBase
{
    private BinaryLogicGateData data;
    private BinaryLogicOperation operation;
    private bool inputAHigh;
    private bool inputBHigh;

    internal void Configure(BinaryLogicGateData value, BinaryLogicOperation valueOperation)
    {
        data = value;
        operation = valueOperation;
    }

    private void Activate(int id)
    {
        if (data == null) return;
        if (id == data.inputA) inputAHigh = true;
        if (id == data.inputB) inputBHigh = true;
    }

    public override void LogicEvaluate()
    {
        if (data == null) return;
        var a = inputAHigh;
        var b = inputBHigh;
        inputAHigh = false;
        inputBHigh = false;

        bool high;
        switch (operation)
        {
            case BinaryLogicOperation.And: high = a && b; break;
            case BinaryLogicOperation.Or: high = a || b; break;
            case BinaryLogicOperation.Xor: high = a ^ b; break;
            case BinaryLogicOperation.Nand: high = !(a && b); break;
            case BinaryLogicOperation.Nor: high = !(a || b); break;
            default: high = false; break;
        }

        if (high) LogicTickService.OutputHigh(data.output);
    }
}