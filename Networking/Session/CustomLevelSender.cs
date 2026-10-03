internal sealed class CustomLevelSender
{
    internal const int MaxLevelBytes = 8 * 1024 * 1024;
    internal const int ChunkPayload = UdpDatagram.FragmentPayload - PacketHeader.Size - OutboundPacket.ReliableHeaderSize - 12;
    private const int MaxQueuedTransfers = 128;
    private const long MaxBufferedBytes = 64 * 1024 * 1024;
    private readonly object sync = new();
    private readonly LinkedList<Transfer> transfers = new();
    private readonly Dictionary<byte[], int> references = new();
    private long bufferedBytes;

    internal bool TryEnqueue(byte[] data, int transferId, ushort[] targets)
    {
        if (data.Length > MaxLevelBytes) 
            throw new InvalidDataException("Custom level is too large.");
        
        lock (sync)
        {
            if (targets.Length == 0) return true;
            var shared = references.TryGetValue(data, out var count);
            if (transfers.Count >= MaxQueuedTransfers || (!shared && bufferedBytes + data.Length > MaxBufferedBytes))
                return false;
            if (!shared) bufferedBytes += data.Length;
            references[data] = count + 1;
            transfers.AddLast(new Transfer(data, transferId, targets));
            return true;
        }
    }

    internal bool TrySendNext(Func<ushort, bool> canSend, Func<CustomLevelPacket, ushort, bool> trySend)
    {
        lock (sync)
        {
            for (var node = transfers.First; node != null; node = node.Next)
            {
                var transfer = node.Value;
                for (var offsetIndex = 0; offsetIndex < transfer.Targets.Length; offsetIndex++)
                {
                    var peer = (transfer.Cursor + offsetIndex) % transfer.Targets.Length;
                    var target = transfer.Targets[peer];
                    if (transfer.NextChunk[peer] == transfer.ChunkCount || !canSend(target)) continue;
                    var index = transfer.NextChunk[peer];
                    var offset = index * ChunkPayload;
                    var length = Math.Min(ChunkPayload, transfer.Data.Length - offset);
                    var chunk = new byte[length];
                    Buffer.BlockCopy(transfer.Data, offset, chunk, 0, length);
                    var packet = new CustomLevelPacket(transfer.Id, (ushort)index, (ushort)transfer.ChunkCount, transfer.Data.Length, chunk);
                    if (!trySend(packet, target)) continue;
                    transfer.NextChunk[peer]++;
                    transfer.Cursor = (peer + 1) % transfer.Targets.Length;
                    if (transfer.NextChunk[peer] == transfer.ChunkCount) transfer.RemainingTargets--;
                    if (transfer.RemainingTargets == 0)
                    {
                        transfers.Remove(node);
                        Release(transfer.Data);
                    }
                    return true;
                }
            }
            return false;
        }
    }

    internal void RemovePeer(ushort peerId)
    {
        lock (sync)
        {
            var node = transfers.First;
            while (node != null)
            {
                var next = node.Next;
                var transfer = node.Value;
                
                for (var i = 0; i < transfer.Targets.Length; i++)
                {
                    if (transfer.Targets[i] != peerId || transfer.NextChunk[i] == transfer.ChunkCount) continue;
                    transfer.NextChunk[i] = transfer.ChunkCount;
                    transfer.RemainingTargets--;
                }
                
                if (transfer.RemainingTargets == 0)
                {
                    transfers.Remove(node);
                    Release(transfer.Data);
                }
                
                node = next;
            }
        }
    }

    internal void Clear()
    {
        lock (sync)
        {
            transfers.Clear();
            references.Clear();
            bufferedBytes = 0;
        }
    }

    private void Release(byte[] data)
    {
        var remaining = references[data] - 1;
        if (remaining > 0) references[data] = remaining;
        else
        {
            references.Remove(data);
            bufferedBytes -= data.Length;
        }
    }

    private sealed class Transfer
    {
        internal readonly byte[] Data;
        internal readonly int Id;
        internal readonly ushort[] Targets;
        internal readonly int[] NextChunk;
        internal readonly int ChunkCount;
        internal int Cursor;
        internal int RemainingTargets;

        internal Transfer(byte[] data, int id, ushort[] targets)
        {
            Data = data;
            Id = id;
            Targets = targets;
            NextChunk = new int[targets.Length];
            ChunkCount = Math.Max(1, (data.Length + ChunkPayload - 1) / ChunkPayload);
            RemainingTargets = targets.Length;
        }
    }
}