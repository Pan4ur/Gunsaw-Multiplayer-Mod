using BepInEx;
using GunsawMultiplayer;

namespace AddonPacketExample;

[BepInPlugin(PluginGuid, "Addon Packet Example", "1.0.0")]
[BepInDependency(GunsawMultiplayerPlugin.PluginGuid)]
public sealed class PacketExamplePlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.examples.modpacket";
    private AddonChannel channel = null!;
    private ushort sequence;

    private void Awake()
    {
        channel = MultiplayerApi.RegisterPacketChannel(PluginGuid, OnPeerReady);
        
        channel.RegisterPacket(PingPacket.Read, OnPing);
        channel.RegisterPacket(PongPacket.Read, OnPong);

        MultiplayerApi.RegisterChatCommand("/ping", OnPingCommand);
    }

    private void OnPingCommand(string args)
    {
        var packet = new PingPacket(sequence = unchecked((ushort) (sequence + 1)));
        var count = channel.Broadcast(packet); // for all players
        MultiplayerApi.AddLocalChatMessage($"Ping #{packet.Number} queued for {count} peers");
    }

    private void OnPeerReady(ushort peerId)
    {
        var packet = new PingPacket(sequence = unchecked((ushort) (sequence + 1)));
        Logger.LogInfo(channel.TrySend(peerId, packet) // for player by peerId
            ? $"Ping #{packet.Number} queued for peer {peerId}"
            : $"Could not queue ping #{packet.Number} for peer {peerId}"
        );
    }

    private void OnPing(ushort senderId, PingPacket packet)
    {
        MultiplayerApi.AddLocalChatMessage($"Received ping #{packet.Number} from peer {senderId}");
        channel.TrySend(senderId, new PongPacket(packet.Number));
    }

    private void OnPong(ushort senderId, PongPacket packet) => Logger.LogInfo($"Peer {senderId} pinged u #{packet.Number}");
}

internal readonly struct PingPacket : INetworkPacket
{
    internal readonly ushort Number;

    internal PingPacket(ushort number) => Number = number;

    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer) => writer.WriteUInt16(Number);

    internal static PingPacket Read(ref PacketReader reader) => new(reader.ReadUInt16());
}

internal readonly struct PongPacket : INetworkPacket
{
    internal readonly ushort Number;

    internal PongPacket(ushort number) => Number = number;

    public DeliverySettings Settings => new(reliable: false, replaceable: true, priority: false);

    public void Write(ref PacketWriter writer) => writer.WriteUInt16(Number);

    internal static PongPacket Read(ref PacketReader reader) => new(reader.ReadUInt16());
}