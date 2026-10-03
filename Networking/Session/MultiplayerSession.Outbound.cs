internal static partial class MultiplayerSession
{
    private static bool SendPacket(INetworkPacket packet, ushort targetId = 0, bool? priority = null)
    {
        if (packet == null)
            return false;
        var accepted = TrySendPacket(packet, targetId, priority);
        if (!accepted && packet.Settings.Reliable)
            SetStatus("Reliable send window and backlog are full; packet was not accepted.");
        
        return accepted;
    }

    private static bool TrySendPacket(INetworkPacket packet, ushort targetId = 0, bool? priority = null)
    {
        var generation = 0;
        var slot = 0;
        if (packet is NpcSnapshotPacket snapshot)
        {
            generation = snapshot.TransferId;
            slot = snapshot.ChunkIndex;
        }
        return TrySendSerialized(packet.Type, packet.Settings, PacketCodec.Encode(packet), targetId, priority, generation, slot);
    }

    private static bool SendSerialized(PacketType type, DeliverySettings policy, byte[] data, ushort targetId, bool? priority = null, int generation = 0, int slot = 0)
    {
        var accepted = TrySendSerialized(type, policy, data, targetId, priority, generation, slot);
        if (!accepted && policy.Reliable) 
            SetStatus("Reliable send window and backlog are full; packet was not accepted.");
        return accepted;
    }

    private static bool TrySendSerialized(PacketType type, DeliverySettings policy, byte[] data, ushort targetId, bool? priority = null, int generation = 0, int slot = 0)
    {
        if (socket == null || !relayConnected) throw new IOException("Relay connection is closed.");
        if (targetId == 0 && (connectionMode != ConnectionMode.Relay || (policy.Reliable && isHost)))
        {
            ushort[] targets;
            lock (statusLock)
            {
                var targetSet = new HashSet<ushort>(peers.Ids());
                if (connectionMode != ConnectionMode.Relay)
                    foreach (var peerId in p2pPeers.Keys)
                        if (peerId != 0 && peerId != localPeerId) targetSet.Add(peerId);
                targets = targetSet.ToArray();
            }
            if (targets.Length > 0)
            {
                var accepted = true;
                foreach (var peerId in targets)
                    accepted &= TrySendSerialized(type, policy, data, peerId, priority, generation, slot);
                return accepted;
            }
        }
        var id = policy.Reliable ? reliableChannel.NextSequenceId() : 0;
        var outbound = new OutboundPacket(type, policy, targetId, data, priority, policy.Reliable, id, generation, slot);
        if (outbound.Reliable)
        {
            if (!reliableChannel.Track(outbound, out var waiting)) return false;
            if (waiting)
            {
                sendSignal.Set();
                return true;
            }
        }
        
        if (!sendQueue.TryEnqueue(outbound))
        {
            if (outbound.Reliable)
            {
                reliableChannel.ScheduleInitialSend(outbound.SequenceId);
                sendSignal.Set();
                return true;
            }
            SetStatus("Network send queue is overloaded; dropping a packet.");
            return false;
        }
        sendSignal.Set();
        return true;
    }
}