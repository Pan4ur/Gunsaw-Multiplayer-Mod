using UnityEngine;

internal abstract class StationRuntime : MonoBehaviour
{
    private const float UseDistanceSquared = 1.5625f;
    private static readonly Dictionary<ulong, StationRuntime> stations = new();

    private ulong id;
    private int cooldown;
    private bool oneTimeUse;
    private int useCount;
    private readonly HashSet<ushort> usedPlayers = new();
    private float readyAt;
    private float requestedAt = -10f;
    private SpriteRenderer? useIcon;

    internal static IEnumerable<StationRuntime> All => stations.Values;
    internal static bool HasCooldowns => stations.Values.Any(station => station != null && station.CooldownRemaining > 0f);
    internal float CooldownRemaining => Mathf.Max(0f, readyAt - Time.unscaledTime);
    protected virtual float PromptHeight => 1.2f;
    internal Vector3 PromptPosition => transform.position + Vector3.up * PromptHeight;

    protected bool AvailableFor(ushort peerId) =>
        !MultiplayerSession.PropHuntEnabled && !(oneTimeUse && usedPlayers.Contains(peerId)) && CooldownRemaining <= 0f;

    internal string CooldownText
    {
        get
        {
            var seconds = (long)Math.Ceiling(CooldownRemaining);
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }

    protected void Configure(bool once, int seconds)
    {
        oneTimeUse = once;
        cooldown = Mathf.Max(0, seconds);
        id = NetworkWireId.FromString(gameObject.name);
        stations[id] = this;
    }

    private static StationRuntime? Nearby()
    {
        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        if (body == null || !body.isAlive || !body.IsConsc() || Time.timeScale == 0f || (GameManager.main != null && GameManager.main.paused) || MultiplayerHud.IsTyping ||
            Controls.IsTextInputSelected || ArsenalMenu.IsOpen)
            return null;

        StationRuntime? nearest = null;
        var distance = UseDistanceSquared;
        foreach (var station in stations.Values)
        {
            if (station == null || !station.isActiveAndEnabled || !station.AvailableFor(MultiplayerSession.LocalPeerId)) continue;
            var squared = ((Vector2)(body.transform.position - station.transform.position)).sqrMagnitude;
            if (squared >= distance) 
                continue;
            
            nearest = station;
            distance = squared;
        }
        return nearest;
    }

    protected virtual void Update()
    {
        if (Nearby() != this || !Input.GetKeyDown(PlayerScript.player.keys["Use"]) || Time.unscaledTime - requestedAt < 0.5f) 
            return;
        
        requestedAt = Time.unscaledTime;
        if (MultiplayerSession.IsConnected && !MultiplayerSession.IsHost)
            MultiplayerSession.Send(new WorldInteractionPacket(InteractionType.StationUse, id), MultiplayerSession.HostPeerId);
        else TryUse(id, MultiplayerSession.LocalPeerId);
    }

    protected void LateUpdate()
    {
        var nearby = Nearby() == this;
        if (useIcon == null)
        {
            var source = PlayerScript.player == null ? null : PlayerScript.player.buttonKeyIcon;
            if (!nearby || source == null) 
                return;
            
            useIcon = Instantiate(source, transform);
            useIcon.gameObject.name = "Use Icon";
            useIcon.gameObject.SetActive(true);
        }
        useIcon.transform.position = PromptPosition;
        useIcon.transform.rotation = Quaternion.identity;
        useIcon.enabled = nearby;
    }

    internal static void TryUse(ulong stationId, ushort peerId)
    {
        if (!stations.TryGetValue(stationId, out var station) || station == null || !station.isActiveAndEnabled || !station.AvailableFor(peerId)) 
            return;
        
        var body = BodyFor(peerId);
        if (body == null || !body.isAlive || !body.IsConsc() || !(((Vector2)(body.transform.position - station.transform.position)).sqrMagnitude <= UseDistanceSquared)) 
            return;

        var packet = new WorldInteractionPacket(InteractionType.StationUsed, stationId, weaponSlot: peerId, previousAmmo: station.useCount + 1, damage: station.cooldown, manual: true);
        ApplyUsed(packet);
        if (MultiplayerSession.IsConnected) 
            MultiplayerSession.Send(packet);
    }

    internal static void ApplyUsed(WorldInteractionPacket packet)
    {
        if (!stations.TryGetValue(packet.TargetId, out var station) || station == null || packet.PreviousAmmo < station.useCount || packet.PreviousAmmo <= 0 ||
            (packet.Manual && packet.PreviousAmmo == station.useCount) || float.IsNaN(packet.Damage) || float.IsInfinity(packet.Damage) || packet.Damage < 0f)
            return;

        station.useCount = packet.PreviousAmmo;
       
        if (packet.Manual || packet.WeaponSlot != 0)
            station.usedPlayers.Add((ushort)packet.WeaponSlot);
       
        station.readyAt = Time.unscaledTime + packet.Damage;

        if (packet.Manual)
        {
            var peerId = (ushort) packet.WeaponSlot;
            station.OnUsed(BodyFor(peerId), peerId == MultiplayerSession.LocalPeerId);
        }
    }

    protected abstract void OnUsed(BodyScript? body, bool local);

    private static BodyScript? BodyFor(ushort peerId) =>
        peerId == MultiplayerSession.LocalPeerId ? PlayerScript.player?.bodyScript : NetworkAvatarManager.RemoteBodyForPeer(peerId);

    internal static void SendHistory(ushort peerId)
    {
        foreach (var station in stations.Values)
            if (station != null && station.useCount > 0)
                MultiplayerSession.Send(new WorldInteractionPacket(InteractionType.StationUsed, station.id, weaponSlot: station.usedPlayers.Contains(peerId) ? peerId : 0, previousAmmo: station.useCount, damage: station.CooldownRemaining), peerId);
    }

    protected virtual void OnDestroy()
    {
        if (stations.TryGetValue(id, out var current) && current == this)
            stations.Remove(id);
    }
}