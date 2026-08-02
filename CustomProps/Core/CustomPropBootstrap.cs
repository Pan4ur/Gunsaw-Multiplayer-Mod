internal static class CustomPropBootstrap
{
    private static bool inited;

    internal static void EnsureRegistered()
    {
        if (inited) return;
        inited = true;

        CustomPropRegistry.Register(new NpcSpawnerPropDefinition());
    }
}