internal static class CustomPropBootstrap
{
    private static bool inited;

    internal static void EnsureRegistered()
    {
        if (inited) return;
        inited = true;

        CustomPropRegistry.Register(new NpcSpawnerPropDefinition());
        CustomPropRegistry.Register(new RandomIdRouterPropDefinition());
        CustomPropRegistry.Register(new ArsenalPropDefinition());
        CustomPropRegistry.Register(new ConstantGateDefinition());
        CustomPropRegistry.Register(new ClockSignalDefinition());
        CustomPropRegistry.Register(new NotGateDefinition());
        CustomPropRegistry.Register(new AndGateDefinition());
        CustomPropRegistry.Register(new OrGateDefinition());
        CustomPropRegistry.Register(new XorGateDefinition());
        CustomPropRegistry.Register(new NandGateDefinition());
        CustomPropRegistry.Register(new NorGateDefinition());
        CustomPropRegistry.Register(new EdgeDetectorDefinition());
        CustomPropRegistry.Register(new DFlipFlopDefinition());
        CustomPropRegistry.Register(new TFlipFlopDefinition());
        CustomPropRegistry.Register(new SrLatchDefinition());
        CustomPropRegistry.Register(new JkFlipFlopDefinition());
    }
}