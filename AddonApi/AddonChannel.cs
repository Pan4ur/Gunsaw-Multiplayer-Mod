namespace GunsawMultiplayer;

public delegate TPacket AddonPacketReader<TPacket>(ref PacketReader reader);

public sealed class AddonChannel
{
    public const int MaxPayloadBytes = 16 * 1024;

    private static readonly DeliverySettings RawDelivery = new(true, false, false);

    internal readonly string Guid;
    internal readonly ushort Version;
    internal readonly int SendSlotBase;
    internal readonly Action<ushort>? OnPeerReady;

    private readonly Action<ushort, byte[]>? rawHandler;
    private readonly Dictionary<Type, ushort> packetIds = new();
    private readonly Dictionary<ushort, Action<ushort, PacketReader>> packetHandlers = new();

    internal AddonChannel(string guid, ushort version, int sendSlotBase, Action<ushort, byte[]>? rawHandler, Action<ushort>? onPeerReady)
    {
        Guid = guid;
        Version = version;
        SendSlotBase = sendSlotBase;
        this.rawHandler = rawHandler;
        OnPeerReady = onPeerReady;
    }

    public void RegisterPacket<TPacket>(AddonPacketReader<TPacket> read, Action<ushort, TPacket> onMessage) where TPacket : INetworkPacket => 
        RegisterPacket(unchecked((ushort)NetworkWireId.FromString(typeof(TPacket).FullName!)), read, onMessage);

    public void RegisterPacket<TPacket>(ushort packetId, AddonPacketReader<TPacket> read, Action<ushort, TPacket> onMessage) where TPacket : INetworkPacket
    {
        if (rawHandler != null) 
            throw new InvalidOperationException("Register packets on a packet channel.");
        
        if (MultiplayerSession.IsActive) 
            throw new InvalidOperationException("Register packets before hosting or joining a lobby.");
        
        if (read == null) 
            throw new ArgumentNullException(nameof(read));
        
        if (onMessage == null) 
            throw new ArgumentNullException(nameof(onMessage));
        
        if (packetHandlers.ContainsKey(packetId))
            throw new ArgumentException("Packet ID " + packetId + " is already registered. Choose a different explicit ID.", nameof(packetId));

        packetIds.Add(typeof(TPacket), packetId);
        
        packetHandlers.Add(packetId, (senderId, reader) =>
        {
            TPacket packet;
            try
            {
                packet = read(ref reader);
            }
            catch (InvalidDataException)
            {
                return;
            }
            catch (ArgumentOutOfRangeException)
            {
                return;
            }

            if (reader.Remaining == 0)
                onMessage(senderId, packet);
        });
    }

    public ushort[] GetCompatiblePeerIds() => MultiplayerSession.AddonChannelPeers(this);

    public bool TrySend(ushort peerId, INetworkPacket packet) =>
        MultiplayerSession.TrySendAddonMessage(this, peerId, Encode(packet, out var packetId), packet.Settings, packetId);

    public int Broadcast(INetworkPacket packet) => Broadcast(Encode(packet, out var packetId), packet.Settings, packetId);

    public bool TrySend(ushort peerId, byte[] payload) => MultiplayerSession.TrySendAddonMessage(this, peerId, payload, RawDelivery, 0);

    public int Broadcast(byte[] payload) => Broadcast(payload, RawDelivery, 0);

    private int Broadcast(byte[] payload, DeliverySettings settings, ushort packetId)
    {
        var accepted = 0;
        MultiplayerSession.ValidateAddonPayload(payload);
        foreach (var peerId in GetCompatiblePeerIds())
            if (MultiplayerSession.TrySendAddonMessage(this, peerId, payload, settings, packetId)) 
                accepted++;
        return accepted;
    }

    internal void Receive(ushort senderId, byte[] payload)
    {
        if (rawHandler != null)
        {
            rawHandler(senderId, payload);
            return;
        }

        if (payload.Length < sizeof(ushort))
            return;
        
        var reader = new PacketReader(payload);
        if (packetHandlers.TryGetValue(reader.ReadUInt16(), out var handler))
            handler(senderId, reader);
    }

    private byte[] Encode(INetworkPacket packet, out ushort packetId)
    {
        if (packet == null) 
            throw new ArgumentNullException(nameof(packet));
        
        if (!packetIds.TryGetValue(packet.GetType(), out packetId))
            throw new ArgumentException("Packet type is not registered on this channel.", nameof(packet));

        var writer = new PacketWriter(128);
        writer.WriteUInt16(packetId);
        packet.Write(ref writer);
        return writer.ToArray();
    }
}