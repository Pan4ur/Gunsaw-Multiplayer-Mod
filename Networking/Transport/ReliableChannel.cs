using System.Diagnostics;

internal sealed class ReliableChannel
{
    private const int InitRetryMs = 250;
    private const int MinRetryMs = 25;
    private const int MaxRetryMs = 1000;
    
    private const int MaxAttempts = 30;
    private const int MaxReceivedIds = 512;
    private const int MaxPendingPackets = 1024;

    private readonly object sync = new();
    private readonly Dictionary<int, PendingPacket> pending = new();
    private readonly Dictionary<ushort, RttEstimator> RTTByTarget = new();
    private readonly HashSet<long> received = new();
    private readonly Queue<long> receivedOrder = new();
    private int sequence;

    internal int NextSequenceId() => Interlocked.Increment(ref sequence);

    internal void Track(int sequenceId, ushort targetId, byte[] routedPacket, long nowTicks)
    {
        lock (sync)
        {
            if (pending.Count >= MaxPendingPackets)
            {
                foreach (var oldId in pending.Keys)
                {
                    pending.Remove(oldId);
                    break;
                }
            }
            pending[sequenceId] = new PendingPacket(sequenceId, targetId, routedPacket, nowTicks);
        }
    }

    internal bool TryUnwrap(byte[] packet, ushort senderId, long nowTimestamp, out byte[] innerPacket, out byte[] acknowledgement)
    {
        innerPacket = packet;
        acknowledgement = null;
        PayloadPacket envelope;
        if (!PacketCodec.TryDecode(packet, out envelope)) return false;

        if (envelope.Type == PacketType.ReliableAck && envelope.Payload.Length == sizeof(int))
        {
            var reader = new PacketReader(envelope.Payload);
            var acknowledgedId = ReliableAckPacket.Read(ref reader).SequenceId;
            lock (sync)
            {
                PendingPacket pendingPacket;
                if (pending.TryGetValue(acknowledgedId, out pendingPacket) &&
                    (pendingPacket.TargetId == 0 || pendingPacket.TargetId == senderId))
                {
                    if (pendingPacket.Attempts == 1)
                        GetRTTEstimator(pendingPacket.TargetId).AddSample(ElapsedMilliseconds(pendingPacket.LastSentTimestamp, nowTimestamp));
                    pending.Remove(acknowledgedId);
                }
            }
            return false;
        }

        if (envelope.Type != PacketType.Reliable || envelope.Payload.Length <= sizeof(int)) return true;

        var reliableReader = new PacketReader(envelope.Payload);
        var reliable = ReliablePacket.Read(ref reliableReader);
        acknowledgement = PacketCodec.Encode(new ReliableAckPacket(reliable.SequenceId));
        var key = ((long)senderId << 32) | (uint)reliable.SequenceId;
        lock (sync)
        {
            if (!received.Add(key)) return false;
            receivedOrder.Enqueue(key);
            while (receivedOrder.Count > MaxReceivedIds) received.Remove(receivedOrder.Dequeue());
        }
        innerPacket = reliable.InnerPacket;
        return true;
    }

    internal List<byte[]> TakeDue(long nowTicks)
    {
        var due = new List<byte[]>();
        lock (sync)
        {
            var remove = new List<int>();
            foreach (var pair in pending)
            {
                var packet = pair.Value;
                if (ElapsedMilliseconds(packet.LastSentTimestamp, nowTicks) < GetRTTEstimator(packet.TargetId).RetryMs) continue;
                if (packet.Attempts >= MaxAttempts)
                {
                    remove.Add(pair.Key);
                    continue;
                }
                packet.Attempts++;
                packet.LastSentTimestamp = nowTicks;
                due.Add(packet.RoutedPacket);
            }
            foreach (var id in remove) pending.Remove(id);
        }
        return due;
    }

    internal void Reset()
    {
        lock (sync)
        {
            pending.Clear();
            RTTByTarget.Clear();
            received.Clear();
            receivedOrder.Clear();
            sequence = 0;
        }
    }

    private sealed class PendingPacket
    {
        internal readonly int SequenceId;
        internal readonly ushort TargetId;
        internal readonly byte[] RoutedPacket;
        internal long LastSentTimestamp;
        internal int Attempts;

        internal PendingPacket(int sequenceId, ushort targetId, byte[] routedPacket, long nowTicks)
        {
            SequenceId = sequenceId;
            TargetId = targetId;
            RoutedPacket = routedPacket;
            LastSentTimestamp = nowTicks;
            Attempts = 1;
        }
    }

    private RttEstimator GetRTTEstimator(ushort targetId)
    {
        RttEstimator estimator;
        if (!RTTByTarget.TryGetValue(targetId, out estimator))
        {
            estimator = new RttEstimator();
            RTTByTarget[targetId] = estimator;
        }
        return estimator;
    }

    private static int ElapsedMilliseconds(long start, long end)
    {
        var elapsed = end - start;
        if (elapsed <= 0) return 0;
        var milliseconds = elapsed * 1000L / Stopwatch.Frequency;
        return milliseconds > int.MaxValue ? int.MaxValue : (int) milliseconds;
    }

    private sealed class RttEstimator
    {
        private int smoothedRTT;
        private int RTTVariance;

        internal int RetryMs { get; private set; } = InitRetryMs;

        internal void AddSample(int sampleMs)
        {
            sampleMs = Math.Max(1, sampleMs);
            if (smoothedRTT == 0)
            {
                smoothedRTT = sampleMs;
                RTTVariance = sampleMs / 2;
            }
            else
            {
                RTTVariance = (3 * RTTVariance + Math.Abs(smoothedRTT - sampleMs)) / 4;
                smoothedRTT = (7 * smoothedRTT + sampleMs) / 8;
            }

            RetryMs = Math.Max(MinRetryMs, Math.Min(MaxRetryMs, smoothedRTT + 4 * RTTVariance));
        }
    }
}
