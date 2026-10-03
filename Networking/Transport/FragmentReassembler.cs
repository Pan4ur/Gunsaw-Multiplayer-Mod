using System.Diagnostics;

internal sealed class FragmentReassembler
{
    private const int MaxTransfers = 128;
    private const long MaxBufferedBytes = 16 * 1024 * 1024;
    private static readonly long TransferLifetime = Stopwatch.Frequency * 5;
    private readonly object sync = new();
    private readonly Dictionary<long, Transfer> transfers = new();
    private long bufferedBytes;

    internal bool TryAccept(byte[] datagram, int metadataOffset, ushort senderId, long now, out ArraySegment<byte> packet)
    {
        packet = default;
        if (senderId == 0 || datagram == null || metadataOffset < 0 || metadataOffset > datagram.Length - UdpDatagram.FragmentHeaderSize)
            return false;
        
        var messageId = BitConverter.ToInt32(datagram, metadataOffset);
        var index = BitConverter.ToUInt16(datagram, metadataOffset + 4);
        var count = BitConverter.ToUInt16(datagram, metadataOffset + 6);
        var totalLength = BitConverter.ToInt32(datagram, metadataOffset + 8);
        var payloadOffset = metadataOffset + UdpDatagram.FragmentHeaderSize;
        var length = datagram.Length - payloadOffset;
        if (totalLength < 0 || totalLength > UdpDatagram.MaxPacketLength || count == 0 || index >= count || count != UdpDatagram.FragmentCount(totalLength) || length != Math.Min(UdpDatagram.FragmentPayload, totalLength - index * UdpDatagram.FragmentPayload))
            return false;

        lock (sync)
        {
            RemoveExpired(now);
            if (count == 1)
            {
                packet = new ArraySegment<byte>(datagram, payloadOffset, length);
                return true;
            }

            var key = ((long)senderId << 32) | (uint)messageId;
            if (transfers.TryGetValue(key, out var transfer))
            {
                if (transfer.Data.Length != totalLength || transfer.Received.Length != count) return false;
            }
            else
            {
                if (transfers.Count >= MaxTransfers || bufferedBytes + totalLength > MaxBufferedBytes)
                    return false;
                transfer = new Transfer(totalLength, count, now);
                transfers.Add(key, transfer);
                bufferedBytes += totalLength;
            }
            
            if (!transfer.Received[index])
            {
                Buffer.BlockCopy(datagram, payloadOffset, transfer.Data, index * UdpDatagram.FragmentPayload, length);
                transfer.Received[index] = true;
                transfer.Count++;
            }
            
            if (transfer.Count != count) return
                false;
            
            packet = new ArraySegment<byte>(transfer.Data);
            Remove(key);
            return true;
        }
    }

    internal void Reset()
    {
        lock (sync)
        {
            transfers.Clear();
            bufferedBytes = 0;
        }
    }

    private void RemoveExpired(long now)
    {
        List<long> expired = null;
        foreach (var pair in transfers)
            if (now - pair.Value.Created >= TransferLifetime)
            {
                if (expired == null) expired = new List<long>();
                expired.Add(pair.Key);
            }
        
        if (expired != null)
            foreach (var key in expired) Remove(key);
    }

    private void Remove(long key)
    {
        bufferedBytes -= transfers[key].Data.Length;
        transfers.Remove(key);
    }

    private sealed class Transfer
    {
        internal readonly byte[] Data;
        internal readonly bool[] Received;
        internal readonly long Created;
        internal int Count;

        internal Transfer(int length, int count, long now)
        {
            Data = new byte[length];
            Received = new bool[count];
            Created = now;
        }
    }
}