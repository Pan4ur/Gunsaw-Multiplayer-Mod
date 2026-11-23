using System.Net.Sockets;
using System.Text;
using GunsawMultiplayer;

internal static partial class MultiplayerSession
{
    private const int MaxAddonChannels = 32;
    private const int MaxAddonGuidBytes = 96;
    private const int MaxPendingAddonMessages = 128;
    private const int MaxAddonMessagesPerFrame = 32;
    private const long AddonManifestWaitTicks = TimeSpan.TicksPerSecond * 5;
    internal const int AddonManifestLimit = MaxAddonChannels * (MaxAddonGuidBytes + 1 + sizeof(ushort));
    internal const int AddonMessageLimit = MaxAddonGuidBytes + 1 + sizeof(ushort) + AddonChannel.MaxPayloadBytes;

    private static readonly Dictionary<string, AddonChannel> addonChannels = new(StringComparer.Ordinal);
    private static readonly Dictionary<ushort, AddonPeer> addonPeers = new();
    private static readonly Queue<(ushort Sender, PeerState Peer, AddonChannel Channel, byte[] Payload, long ReceivedAt)> addonInbox = new();

    private sealed class AddonPeer
    {
        internal bool ManifestSent;
        internal Dictionary<string, ushort>? Versions;
        internal bool Notified;
    }

    private static bool ValidAddonGuid(string guid) =>
        !string.IsNullOrWhiteSpace(guid) && guid == guid.Trim() && Encoding.UTF8.GetByteCount(guid) <= MaxAddonGuidBytes;

    internal static AddonChannel RegisterAddonChannel(string pluginGuid, ushort version, Action<ushort, byte[]>? onMessage, Action<ushort>? onPeerReady)
    {
        if (!ValidAddonGuid(pluginGuid)) 
            throw new ArgumentException("Invalid plugin GUID.", nameof(pluginGuid));
        
        if (version == 0) 
            throw new ArgumentOutOfRangeException(nameof(version));
        
        lock (statusLock)
        {
            if (socket != null) 
                throw new InvalidOperationException("Register addon channels before joining a lobby.");
            
            if (addonChannels.Count >= MaxAddonChannels) 
                throw new InvalidOperationException("Too many addon channels.");
            
            var channel = new AddonChannel(pluginGuid, version, addonChannels.Count << 16, onMessage, onPeerReady);
            addonChannels.Add(pluginGuid, channel);
            return channel;
        }
    }

    private static bool Compatible(AddonPeer peer, AddonChannel channel) =>
        peer.Versions != null && peer.Versions.TryGetValue(channel.Guid, out var version) && version == channel.Version;

    internal static ushort[] AddonChannelPeers(AddonChannel channel)
    {
        lock (statusLock)
            return !relayConnected ? [] :
            [
                .. addonPeers.Where(pair => peers.Contains(pair.Key) && pair.Value.ManifestSent && Compatible(pair.Value, channel)).Select(pair => pair.Key)
            ];
    }

    internal static void ValidateAddonPayload(byte[] payload)
    {
        if (payload == null) 
            throw new ArgumentNullException(nameof(payload));
        
        if (payload.Length > AddonChannel.MaxPayloadBytes) 
            throw new ArgumentOutOfRangeException(nameof(payload));
    }

    internal static bool TrySendAddonMessage(AddonChannel channel, ushort target, byte[] payload, DeliverySettings settings, ushort packetId)
    {
        ValidateAddonPayload(payload);
        lock (statusLock)
        {
            if (socket == null || !relayConnected || !peers.Contains(target) || !addonPeers.TryGetValue(target, out var peer) || !peer.ManifestSent || !Compatible(peer, channel))
                return false;
            
            var writer = new PacketWriter(payload.Length + MaxAddonGuidBytes + 8);
            PacketHeader.Write(ref writer, PacketType.AddonMessage);
            writer.WriteBinaryString(channel.Guid);
            writer.WriteUInt16(channel.Version);
            writer.WriteBytes(payload);
            var data = PacketCodec.FinishEncoding(writer.ToArray(), PacketType.AddonMessage, settings.Compress);
            return TrySendSerialized(PacketType.AddonMessage, settings, data, target, slot: channel.SendSlotBase | packetId);
        }
    }

    private static void ReceiveAddonPacket(UdpClient receiveSocket, ushort sender, PayloadPacket packet)
    {
        lock (statusLock)
        {
            if (socket != receiveSocket || !relayConnected || sender == 0 || sender == localPeerId || addonChannels.Count == 0 || blockedPeers.Contains(sender))
                return;
            
            var reader = new PacketReader(packet.Payload);
            if (packet.Type == PacketType.AddonManifest)
            {
                if (addonPeers.TryGetValue(sender, out var peer) && peer.Versions != null)
                    return;
                
                if (peer == null && !peers.Contains(sender) && addonPeers.Count(pair => !peers.Contains(pair.Key)) >= 64)
                    return;
                
                var versions = new Dictionary<string, ushort>(StringComparer.Ordinal);
                var count = 0;
                while (reader.Remaining > 0)
                {
                    if (++count > MaxAddonChannels)
                        return;
                    
                    var guid = reader.ReadBinaryString();
                    var version = reader.ReadUInt16();
                    if (!ValidAddonGuid(guid) || version == 0 || versions.ContainsKey(guid))
                        return;
                    
                    versions.Add(guid, version);
                }
                
                if (peer == null)
                    addonPeers.Add(sender, peer = new AddonPeer());
                
                peer.Versions = versions;
                return;
            }

            if (addonInbox.Count >= MaxPendingAddonMessages || !peers.TryGet(sender, out var senderPeer))
                return;
            
            var channelGuid = reader.ReadBinaryString();
            var channelVersion = reader.ReadUInt16();
            if (!addonChannels.TryGetValue(channelGuid, out var channel) || channel.Version != channelVersion || reader.Remaining > AddonChannel.MaxPayloadBytes)
                return;
            
            if (addonPeers.TryGetValue(sender, out var senderAddonPeer) && senderAddonPeer.Versions != null && !Compatible(senderAddonPeer, channel))
                return;
            
            addonInbox.Enqueue((sender, senderPeer, channel, reader.ReadRemainingBytes(), DateTime.UtcNow.Ticks));
        }
    }

    internal static void UpdateAddonChannels()
    {
        List<(AddonPeer Peer, ushort Id, AddonChannel Channel)>? ready = null;
        lock (statusLock)
        {
            if (addonChannels.Count == 0 || socket == null || !relayConnected)
                return;
            byte[]? manifest = null;
            foreach (var peerId in peers.Ids())
            {
                if (!addonPeers.TryGetValue(peerId, out var peer)) addonPeers.Add(peerId, peer = new AddonPeer());
                if (!peer.ManifestSent)
                {
                    manifest ??= EncodeAddonManifest();
                    peer.ManifestSent = TrySendSerialized(PacketType.AddonManifest, DeliverySettings.Lossless,
                        manifest, peerId, true);
                }

                if (!peer.ManifestSent || peer.Notified || peer.Versions == null) continue;
                peer.Notified = true;
                foreach (var channel in addonChannels.Values)
                    if (channel.OnPeerReady != null && Compatible(peer, channel))
                        (ready ??= new()).Add((peer, peerId, channel));
            }
        }

        if (ready != null)
            foreach (var (peer, id, channel) in ready)
            {
                lock (statusLock)
                    if (!addonPeers.TryGetValue(id, out var current) || current != peer) continue;
                MultiplayerApi.Invoke(channel.Guid, () => channel.OnPeerReady!(id));
            }

        int pending;
        lock (statusLock) pending = addonInbox.Count;
        var now = DateTime.UtcNow.Ticks;
        var dispatched = 0;
        for (var index = 0; index < pending && dispatched < MaxAddonMessagesPerFrame; index++)
        {
            (ushort Sender, PeerState Peer, AddonChannel Channel, byte[] Payload, long ReceivedAt) message;
            lock (statusLock)
            {
                if (addonInbox.Count == 0) break;
                message = addonInbox.Dequeue();
                if (!peers.TryGet(message.Sender, out var peer) || peer != message.Peer) continue;
                if (!addonPeers.TryGetValue(message.Sender, out var addonPeer) || !addonPeer.Notified)
                {
                    if (now - message.ReceivedAt < AddonManifestWaitTicks) addonInbox.Enqueue(message);
                    continue;
                }
                if (!Compatible(addonPeer, message.Channel)) continue;
            }
            dispatched++;
            MultiplayerApi.Invoke(message.Channel.Guid, () => message.Channel.Receive(message.Sender, message.Payload));
        }
    }

    private static byte[] EncodeAddonManifest()
    {
        var writer = new PacketWriter(128);
        PacketHeader.Write(ref writer, PacketType.AddonManifest);
        foreach (var channel in addonChannels.Values)
        {
            writer.WriteBinaryString(channel.Guid);
            writer.WriteUInt16(channel.Version);
        }
        return writer.ToArray();
    }
}