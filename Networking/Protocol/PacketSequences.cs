using System.Threading;

internal static class PacketSequences
{
    private static int playerSnapshot;
    private static int npcTransfer;

    internal static int NextPlayerSnapshot() => Interlocked.Increment(ref playerSnapshot);
    internal static int NextNpcTransfer() => Interlocked.Increment(ref npcTransfer);

    internal static void Reset()
    {
        Interlocked.Exchange(ref playerSnapshot, 0);
        Interlocked.Exchange(ref npcTransfer, 0);
    }
}
