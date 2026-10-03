using System.Diagnostics;

internal sealed class ReliableChannel
{
    private const int InitRetryMs = 250;
    private const int MinRetryMs = 25;
    private const int MaxRetryMs = 1000;
    
    private const int MaxAttempts = 30;
    private const int MaxReceivedIds = 512;
    private const int MaxPendingPacketsPerTarget = 1024;
    private const int MaxWaitingPacketsPerTarget = 1024;

    private readonly object sync = new();
    private readonly Dictionary<int, PendingPacket> pending = new();
    private readonly Dictionary<ushort, int> pendingByTarget = new();
    private readonly Dictionary<ushort, int> activeByTarget = new();
    private readonly Dictionary<ushort, Queue<int>> waitingByTarget = new();
    private readonly Dictionary<ushort, RttEstimator> RTTByTarget = new();
    private readonly HashSet<long> received = new();
    private readonly Queue<long> receivedOrder = new();
    private readonly List<int> expired = new();
    private int sequence;

    internal int PendingCount { get { lock (sync) return pending.Count; } }

    internal int GetPendingCount(ushort targetId)
    {
        lock (sync) return pendingByTarget.TryGetValue(targetId, out var count) ? count : 0;
    }

    internal int NextSequenceId() => Interlocked.Increment(ref sequence);

    internal bool Track(OutboundPacket packet) => Track(packet, out _);

    internal bool Track(OutboundPacket packet, out bool waiting)
    {
        waiting = false;
        lock (sync)
        {
            var count = pendingByTarget.TryGetValue(packet.TargetId, out var pendingCount) ? pendingCount : 0;
            if (count >= MaxPendingPacketsPerTarget + MaxWaitingPacketsPerTarget || pending.ContainsKey(packet.SequenceId)) return false;
            var active = activeByTarget.TryGetValue(packet.TargetId, out var activeCount) ? activeCount : 0;
            waiting = active >= MaxPendingPacketsPerTarget;
            pending[packet.SequenceId] = new PendingPacket(packet) { WaitingForWindow = waiting };
            pendingByTarget[packet.TargetId] = count + 1;
            if (waiting)
            {
                if (!waitingByTarget.TryGetValue(packet.TargetId, out var queue))
                {
                    queue = new Queue<int>();
                    waitingByTarget.Add(packet.TargetId, queue);
                }
                queue.Enqueue(packet.SequenceId);
            }
            else activeByTarget[packet.TargetId] = active + 1;
            return true;
        }
    }

    internal void ScheduleInitialSend(int sequenceId)
    {
        lock (sync)
            if (pending.TryGetValue(sequenceId, out var packet) && packet.Attempts == 0)
                packet.InitialReady = true;
    }

    internal bool TryBeginSend(int sequenceId)
    {
        lock (sync)
        {
            if (!pending.TryGetValue(sequenceId, out var packet) || packet.WaitingForWindow || packet.InFlight || packet.Attempts >= MaxAttempts)
                return false;
            packet.InFlight = true;
            packet.InitialReady = false;
            return true;
        }
    }

    internal void MarkSent(int sequenceId, long now)
    {
        lock (sync)
        {
            if (!pending.TryGetValue(sequenceId, out var packet) || !packet.InFlight) return;
            packet.InFlight = false;
            packet.Attempts++;
            packet.LastSentTimestamp = now;
            packet.RetryNotBefore = 0;
            packet.Failures = 0;
            if (packet.Acknowledged) Acknowledge(sequenceId, packet, packet.AckTimestamp);
        }
    }

    internal void SendFailed(int sequenceId, long now)
    {
        lock (sync)
        {
            if (!pending.TryGetValue(sequenceId, out var packet) || !packet.InFlight) return;
            packet.InFlight = false;
            if (packet.Acknowledged || ++packet.Failures >= MaxAttempts)
            {
                RemovePending(sequenceId);
                return;
            }
            packet.InitialReady = packet.Attempts == 0;
            packet.RetryNotBefore = now + Stopwatch.Frequency * GetRTTEstimator(packet.Packet.TargetId).RetryMs / 1000;
        }
    }

    internal void TakeInitialSends(long now, List<OutboundPacket> ready)
    {
        ready.Clear();
        lock (sync)
        {
            foreach (var packet in pending.Values)
                if (!packet.WaitingForWindow && packet.Attempts == 0 && packet.InitialReady && !packet.InFlight && now >= packet.RetryNotBefore)
                    ready.Add(packet.Packet);
        }
    }

    internal void RemovePeer(ushort peerId)
    {
        lock (sync)
        {
            expired.Clear();
            foreach (var pair in pending)
                if (pair.Value.Packet.TargetId == peerId) expired.Add(pair.Key);
            foreach (var id in expired) RemovePending(id);
            waitingByTarget.Remove(peerId);
            RTTByTarget.Remove(peerId);
        }
    }

    internal bool TryUnwrap(ArraySegment<byte> packet, ushort senderId, long nowTimestamp, out ArraySegment<byte> innerPacket, out byte[] acknowledgement)
    {
        innerPacket = packet;
        acknowledgement = null;
        PayloadPacket envelope;
        if (!PacketCodec.TryDecode(packet, out envelope)) return false;

        if (envelope.Type == PacketType.ReliableAck)
        {
            if (envelope.Payload.Count != sizeof(int)) return false;
            var reader = new PacketReader(envelope.Payload);
            var acknowledgedId = ReliableAckPacket.Read(ref reader).SequenceId;
            lock (sync)
            {
                PendingPacket pendingPacket;
                if (pending.TryGetValue(acknowledgedId, out pendingPacket) &&
                    (pendingPacket.Packet.TargetId == 0 || pendingPacket.Packet.TargetId == senderId))
                {
                    if (pendingPacket.InFlight)
                    {
                        pendingPacket.Acknowledged = true;
                        pendingPacket.AckTimestamp = nowTimestamp;
                    }
                    else if (pendingPacket.Attempts > 0) Acknowledge(acknowledgedId, pendingPacket, nowTimestamp);
                }
            }
            return false;
        }

        if (envelope.Type != PacketType.Reliable) return true;
        if (envelope.Payload.Count < sizeof(int) + PacketHeader.Size) return false;

        var reliableReader = new PacketReader(envelope.Payload);
        var sequenceId = reliableReader.ReadInt32();
        var inner = reliableReader.ReadRemainingSegment();
        if (!PacketHeader.TryRead(inner.Array, inner.Offset, out var innerHeader) ||
            innerHeader.Type == PacketType.Reliable || innerHeader.Type == PacketType.ReliableAck) return false;
        acknowledgement = PacketCodec.Encode(new ReliableAckPacket(sequenceId));
        var key = ((long)senderId << 32) | (uint)sequenceId;
        lock (sync)
        {
            if (!received.Add(key)) return false;
            receivedOrder.Enqueue(key);
            while (receivedOrder.Count > MaxReceivedIds) received.Remove(receivedOrder.Dequeue());
        }
        innerPacket = inner;
        return true;
    }

    internal void TakeDue(long nowTicks, List<OutboundPacket> due)
    {
        due.Clear();
        lock (sync)
        {
            expired.Clear();
            foreach (var pair in pending)
            {
                var packet = pair.Value;
                if (packet.Attempts == 0 || packet.InFlight || nowTicks < packet.RetryNotBefore) continue;
                if (ElapsedMilliseconds(packet.LastSentTimestamp, nowTicks) < GetRTTEstimator(packet.Packet.TargetId).RetryMs) continue;
                if (packet.Attempts >= MaxAttempts)
                {
                    expired.Add(pair.Key);
                    continue;
                }
                due.Add(packet.Packet);
            }
            foreach (var id in expired) RemovePending(id);
        }
    }

    internal void Reset()
    {
        lock (sync)
        {
            pending.Clear();
            pendingByTarget.Clear();
            activeByTarget.Clear();
            waitingByTarget.Clear();
            RTTByTarget.Clear();
            received.Clear();
            receivedOrder.Clear();
            sequence = 0;
        }
    }

    private sealed class PendingPacket
    {
        internal readonly OutboundPacket Packet;
        internal long LastSentTimestamp;
        internal int Attempts;
        internal int Failures;
        internal bool InitialReady;
        internal bool WaitingForWindow;
        internal bool InFlight;
        internal bool Acknowledged;
        internal long AckTimestamp;
        internal long RetryNotBefore;

        internal PendingPacket(OutboundPacket packet) => Packet = packet;
    }

    private void Acknowledge(int sequenceId, PendingPacket packet, long now)
    {
        if (packet.Attempts == 1)
            GetRTTEstimator(packet.Packet.TargetId).AddSample(ElapsedMilliseconds(packet.LastSentTimestamp, now));
        RemovePending(sequenceId);
    }

    private void RemovePending(int sequenceId)
    {
        if (!pending.TryGetValue(sequenceId, out var packet)) return;
        pending.Remove(sequenceId);
        var targetId = packet.Packet.TargetId;
        var count = pendingByTarget[targetId] - 1;
        if (count == 0) pendingByTarget.Remove(targetId);
        else pendingByTarget[targetId] = count;
        if (packet.WaitingForWindow) return;
        var active = activeByTarget[targetId] - 1;
        if (active == 0) activeByTarget.Remove(targetId);
        else activeByTarget[targetId] = active;
        if (!waitingByTarget.TryGetValue(targetId, out var queue)) return;
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!pending.TryGetValue(id, out var next)) continue;
            next.WaitingForWindow = false;
            next.InitialReady = true;
            activeByTarget[targetId] = active + 1;
            break;
        }
        if (queue.Count == 0) waitingByTarget.Remove(targetId);
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