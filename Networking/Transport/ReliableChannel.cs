using System;
using System.Collections.Generic;
using System.Threading;

internal sealed class ReliableChannel
{
    private const long RetryTicks = TimeSpan.TicksPerMillisecond * 150;
    private const int MaxAttempts = 30;
    private const int MaxReceivedIds = 512;
    private const int MaxPendingPackets = 1024;

    private readonly object sync = new object();
    private readonly Dictionary<int, PendingPacket> pending = new Dictionary<int, PendingPacket>();
    private readonly HashSet<long> received = new HashSet<long>();
    private readonly Queue<long> receivedOrder = new Queue<long>();
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

    internal bool TryUnwrap(byte[] packet, ushort senderId, out byte[] innerPacket, out byte[] acknowledgement)
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
                    pending.Remove(acknowledgedId);
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
                if (nowTicks - packet.LastSentTicks < RetryTicks) continue;
                if (packet.Attempts >= MaxAttempts)
                {
                    remove.Add(pair.Key);
                    continue;
                }
                packet.Attempts++;
                packet.LastSentTicks = nowTicks;
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
        internal long LastSentTicks;
        internal int Attempts;

        internal PendingPacket(int sequenceId, ushort targetId, byte[] routedPacket, long nowTicks)
        {
            SequenceId = sequenceId;
            TargetId = targetId;
            RoutedPacket = routedPacket;
            LastSentTicks = nowTicks;
            Attempts = 1;
        }
    }
}
