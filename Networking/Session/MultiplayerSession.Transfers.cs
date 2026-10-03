using System.Text;

internal static partial class MultiplayerSession
{
    private static readonly CustomLevelSender customLevelSender = new();
    private static string cachedLevelCode;
    private static byte[] cachedLevelData;

    private static int QueueCustomLevelTransfer(string levelCode, ushort targetId = 0)
    {
        byte[] data;
        lock (statusLock)
        {
            if (!ReferenceEquals(cachedLevelCode, levelCode) || cachedLevelData == null)
            {
                cachedLevelCode = levelCode;
                cachedLevelData = Encoding.UTF8.GetBytes(levelCode);
            }
            data = cachedLevelData;
        }
        var transferId = Interlocked.Increment(ref customLevelTransferId);
        var targets = targetId == 0 ? PeerIds() : new[] { targetId };
        if (!customLevelSender.TryEnqueue(data, transferId, targets))
            throw new IOException("Custom level send backlog is full.");
        sendSignal.Set();
        return transferId;
    }

    private static void PumpSessionTransfers()
    {
        for (var i = 0; i < 16 && sendQueue.Available > 0; i++)
        {
            lock (statusLock)
            {
                if (!customLevelSender.TrySendNext(CanSendLevelChunk, TrySendLevelChunk)) return;
            }
        }
    }

    private static bool CanSendLevelChunk(ushort targetId)
        => peers.Contains(targetId) && reliableChannel.GetPendingCount(targetId) < 128;

    private static bool TrySendLevelChunk(CustomLevelPacket packet, ushort targetId)
        => TrySendPacket(packet, targetId, false);

    private static void ClearSessionTransfers()
    {
        customLevelSender.Clear();
        lock (statusLock)
        {
            cachedLevelCode = null;
            cachedLevelData = null;
        }
    }
}