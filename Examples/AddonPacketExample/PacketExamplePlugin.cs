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
    
    private bool pendingPingNewPeers = true;
    private bool pingNewPeers = true;
    private string exampleText = "Lobby ping";
    private int exampleCount = 3;
    private float exampleInterval = 0.5f;
    private string exampleMode = "normal";

    private void Awake()
    {
        channel = MultiplayerApi.RegisterPacketChannel(PluginGuid, OnPeerReady);
        
        channel.RegisterPacket(PingPacket.Read, OnPing);
        channel.RegisterPacket(PongPacket.Read, OnPong);

        MultiplayerApi.RegisterChatCommand("/ping", _ => SendPing());
        MultiplayerApi.RegisterLobbySection(PluginGuid, "Addon Packet Example", section =>
        {
            section.AddToggle("Ping new peers when I host", () => pendingPingNewPeers, value => pendingPingNewPeers = value);
            section.AddText("Example text", () => exampleText, value => exampleText = value, maxLength: 32);
            section.AddInt("Example count", () => exampleCount, value => exampleCount = value, minimum: 1, maximum: 10);
            section.AddFloat("Example float", () => exampleInterval, value => exampleInterval = value, minimum: 0.1f, maximum: 5f);
            section.AddChoice("Example mode", new[] { ("normal", "Normal"), ("brief", "Brief"), ("verbose", "Verbose") }, () => exampleMode, value => exampleMode = value);
            section.AddButton("Send ping", SendPing, allowGuests: true);
        }, onHostSettingsApplied: () =>
        {
            pingNewPeers = pendingPingNewPeers;
            Logger.LogInfo($"Addon settings applied: {exampleText}, count {exampleCount}, float {exampleInterval}, mode {exampleMode}.");
        });
    }

    private void SendPing()
    {
        if (MultiplayerApi.Offline)
        {
            Logger.LogInfo("Join or host a lobby to send a ping.");
            return;
        }

        var packet = new PingPacket(sequence = unchecked((ushort) (sequence + 1)));
        var count = channel.Broadcast(packet); // for all players
        MultiplayerApi.AddLocalChatMessage($"Ping #{packet.Number} queued for {count} peers");
    }

    private void OnPeerReady(ushort peerId)
    {
        if (MultiplayerApi.IsHost && !pingNewPeers) return;

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