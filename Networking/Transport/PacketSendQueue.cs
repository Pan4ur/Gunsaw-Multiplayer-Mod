internal sealed class PacketSendQueue
{
    private const int Capacity = 2048;
    private readonly object sync = new();
    private readonly LinkedList<OutboundPacket> priority = new();
    private readonly LinkedList<OutboundPacket> normal = new();
    private readonly Dictionary<(ushort, PacketType), StateGroup> states = new();
    private readonly Queue<LinkedListNode<OutboundPacket>> recycled = new();
    private readonly Stack<StateGroup> recycledGroups = new();

    internal int StateGroupCount { get { lock (sync) return states.Count; } }

    internal int Available { get { lock (sync) return Capacity - priority.Count - normal.Count; } }

    internal bool TryEnqueue(OutboundPacket packet)
    {
        lock (sync)
        {
            StateGroup group = null;
            if (packet.Policy.Replaceable && !packet.Reliable)
            {
                var key = (packet.TargetId, packet.Type);
                if (!states.TryGetValue(key, out group))
                {
                    group = recycledGroups.Count > 0 ? recycledGroups.Pop() : new StateGroup();
                    group.Generation = packet.StateGeneration;
                    states.Add(key, group);
                }
                if (group.Generation != packet.StateGeneration)
                {
                    foreach (var node in group.Slots.Values)
                    {
                        node.List.Remove(node);
                        Recycle(node);
                    }
                    group.Slots.Clear();
                    group.Generation = packet.StateGeneration;
                }
                if (group.Slots.TryGetValue(packet.StateSlot, out var previous))
                {
                    var destination = packet.Priority ? priority : normal;
                    if (previous.List != destination)
                    {
                        previous.List.Remove(previous);
                        destination.AddLast(previous);
                    }
                    previous.Value = packet;
                    return true;
                }
            }
            if (priority.Count + normal.Count >= Capacity)
            {
                if (group != null && group.Slots.Count == 0) ReleaseGroup((packet.TargetId, packet.Type), group);
                return false;
            }
            var queue = packet.Priority ? priority : normal;
            var added = recycled.Count > 0 ? recycled.Dequeue() : new LinkedListNode<OutboundPacket>(packet);
            added.Value = packet;
            queue.AddLast(added);
            if (group != null) group.Slots[packet.StateSlot] = added;
            return true;
        }
    }

    internal bool TryDequeue(out OutboundPacket packet)
    {
        lock (sync) return Dequeue(priority.Count > 0 ? priority : normal, out packet);
    }

    internal bool TryDequeuePriority(out OutboundPacket packet)
    {
        lock (sync) return Dequeue(priority, out packet);
    }

    private bool Dequeue(LinkedList<OutboundPacket> queue, out OutboundPacket packet)
    {
        if (queue.First == null) { packet = default; return false; }
        var first = queue.First;
        packet = first.Value;
        queue.RemoveFirst();
        Recycle(first);
        if (packet.Policy.Replaceable && !packet.Reliable)
        {
            var key = (packet.TargetId, packet.Type);
            var group = states[key];
            group.Slots.Remove(packet.StateSlot);
            if (group.Slots.Count == 0) ReleaseGroup(key, group);
        }
        return true;
    }

    internal void RemovePeer(ushort peerId)
    {
        lock (sync)
        {
            RemovePeer(priority, peerId);
            RemovePeer(normal, peerId);
            var keys = new List<(ushort, PacketType)>();
            foreach (var key in states.Keys)
                if (key.Item1 == peerId) keys.Add(key);
            foreach (var key in keys) ReleaseGroup(key, states[key]);
        }
    }

    private void RemovePeer(LinkedList<OutboundPacket> queue, ushort peerId)
    {
        var node = queue.First;
        while (node != null)
        {
            var next = node.Next;
            if (node.Value.TargetId == peerId)
            {
                queue.Remove(node);
                Recycle(node);
            }
            node = next;
        }
    }

    internal void Clear()
    {
        lock (sync)
        {
            priority.Clear();
            normal.Clear();
            states.Clear();
            recycled.Clear();
            recycledGroups.Clear();
        }
    }

    private void ReleaseGroup((ushort, PacketType) key, StateGroup group)
    {
        states.Remove(key);
        group.Slots.Clear();
        recycledGroups.Push(group);
    }

    private void Recycle(LinkedListNode<OutboundPacket> node)
    {
        node.Value = default;
        recycled.Enqueue(node);
    }

    private sealed class StateGroup
    {
        internal int Generation;
        internal readonly Dictionary<int, LinkedListNode<OutboundPacket>> Slots = new();
    }
}