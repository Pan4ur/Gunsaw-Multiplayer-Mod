namespace GunsawMultiplayer;

public static partial class MultiplayerApi
{
    public static bool Offline => !MultiplayerSession.IsInLobby;
    public static bool IsHost => MultiplayerSession.IsHosting;
    public static ushort LocalPeerId => Offline ? (ushort) 0 : MultiplayerSession.LocalPeerId;
    public static ushort HostPeerId => Offline ? (ushort) 0 : MultiplayerSession.HostPeerId;
    public static ushort[] GetConnectedPeerIds() => Offline ? [] : MultiplayerSession.PeerIds();

    public static BodyScript? GetPlayerBody(ushort peerId)
    {
        if (Offline || peerId == 0) return null;
        return peerId == LocalPeerId ? PlayerScript.player?.bodyScript : MultiplayerSession.HasPeer(peerId) ? NetworkAvatarManager.RemoteBodyForPeer(peerId) : null;
    }

    public static ulong GetNpcId(BodyScript body) => Offline ? 0 : NpcReplication.GetNpcId(body);

    public static BodyScript? GetNpcBody(ulong npcId) => Offline ? null : NpcReplication.GetNpcBody(npcId);

    public static AddonChannel RegisterChannel(string pluginGuid, ushort version, Action<ushort, byte[]> onMessage, Action<ushort>? onPeerReady = null) =>
        MultiplayerSession.RegisterAddonChannel(pluginGuid, version, onMessage ?? throw new ArgumentNullException(nameof(onMessage)), onPeerReady);

    public static AddonChannel RegisterPacketChannel(string pluginGuid, Action<ushort>? onPeerReady = null, ushort version = 1) =>
        MultiplayerSession.RegisterAddonChannel(pluginGuid, version, null, onPeerReady);

    public static void RegisterChatCommand(string command, Action<string> handler) =>
        ChatCommandSystem.Register(command, handler);

    public static void AddLocalChatMessage(string message) => MultiplayerHud.AddSystemMessage(message);

    internal static void Invoke(string owner, Action callback)
    {
        try { callback(); }
        catch (Exception exception) { GunsawMultiplayerPlugin.LogInfo(owner + ": " + exception); }
    }
}
