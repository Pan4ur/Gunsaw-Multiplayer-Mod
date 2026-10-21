using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;

[Serializable]
internal sealed class MedicalStationData
{
    public int oneTimeUse;
    public int cooldown = 30;
}

internal sealed class MedicalStationPropDefinition : CustomPropDefinition<MedicalStationData>
{
    private CustomPropField[] fields;

    public override string TypeId => "MP/MedicalStation";
    public override string DisplayName => "Medical Station";
    public override string Description => "Fully heals and extinguishes a player. One time use 1 - on, 0 - off; cooldown in seconds";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Misc;
    public override Sprite Icon => EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.medical-station.png", 28f, new Vector2(0.5f, 0.5f));
  
    // it needs something like Supress
    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("One time use", "0 - off, 1 - on", value => value.oneTimeUse, (value, number) => value.oneTimeUse = Mathf.Clamp(number, 0, 1), 0),
        Integer("Cooldown", "Seconds", value => value.cooldown, (value, number) => value.cooldown = number, 0)
    };

    public override void CreateRuntime(GameObject gameObject, MedicalStationData data)
    {
        var renderer = gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = Icon;
        renderer.sortingLayerName = "Background";
        renderer.sortingOrder = 999;
        gameObject.AddComponent<MedicalStationRuntime>().Configure(data);
    }
}

internal sealed class MedicalStationRuntime : MonoBehaviour
{
    private static readonly Dictionary<ulong, MedicalStationRuntime> stations = new();
    private static readonly AnimationCurve screenPosition = new(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(0.8333333f, 0.5069f, 0.5350666f, 0.5350666f),
        new Keyframe(1.5f, 0.8026f, 0f, 0f),
        new Keyframe(3.0833333f, 0.7952f, -0.01145812f, -0.01145812f),
        new Keyframe(4.0833335f, 0f, 0f, 0f));
    private static readonly AnimationCurve screenScale = new(
        new Keyframe(0f, 1f, 0f, 0f),
        new Keyframe(0.8333333f, 0.40858227f, -0.6242286f, -0.6242286f),
        new Keyframe(1.5f, 0.06365712f, 0f, 0f),
        new Keyframe(3.0833333f, 0.072244465f, 0.013296533f, 0.013296533f),
        new Keyframe(4.0833335f, 1f, 0f, 0f));
   
    private ulong id;
    private MedicalStationData data;
    private int useCount;
    private readonly HashSet<ushort> usedPlayers = new();
    private float readyAt;
    private bool animating;
    private float requestedAt = -10f;
    private float usedAt;
    private Transform screen;
    private SpriteRenderer useIcon;
    private BodyScript recipient;
    private LineRenderer bloodline, airLine;
    private readonly List<Light2D> lights = new();
    private Material tubeMaterial;

    internal static IEnumerable<MedicalStationRuntime> All => stations.Values;
    internal static bool HasCooldowns => stations.Values.Any(station => station != null && station.CooldownRemaining > 0f);
    internal float CooldownRemaining => Mathf.Max(0f, readyAt - Time.unscaledTime);
    private bool AvailableFor(ushort peerId) => !(data.oneTimeUse == 1 && usedPlayers.Contains(peerId)) && CooldownRemaining <= 0f;
    internal string CooldownText
    {
        get
        {
            var seconds = (long)Math.Ceiling(CooldownRemaining);
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }

    internal void Configure(MedicalStationData value)
    {
        data = value ?? new MedicalStationData();
        data.oneTimeUse = Mathf.Clamp(data.oneTimeUse, 0, 1);
        data.cooldown = Mathf.Max(0, data.cooldown);
        id = NetworkWireId.FromString(gameObject.name);
        stations[id] = this;
        var screenObject = new GameObject("Screen");
        screenObject.transform.SetParent(transform, false);
        screen = screenObject.transform;
        screen.localPosition = new Vector3(0f, 0.285715f, 0f);
        var renderer = screenObject.AddComponent<SpriteRenderer>();
        renderer.sprite = EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.medical-station-screen.png", 28f, new Vector2(0.5f, 0.5f));
        renderer.sortingLayerName = "Background";
        renderer.sortingOrder = 1000;
        AddLight(new Vector2(0f, 0.588f), Color.green);
        AddLight(new Vector2(-0.6422f, -0.8577f), Color.red);
        AddLight(new Vector2(0.6435f, -0.8577f), Color.red);
    }

    private void AddLight(Vector2 position, Color color)
    {
        var child = new GameObject("Station Light");
        child.transform.SetParent(transform, false);
        child.transform.localPosition = position * 0.5f;
        var light = child.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.pointLightOuterRadius = 1f;
        light.intensity = 0.5f;
        light.color = color;
        lights.Add(light);
    }

    private static MedicalStationRuntime Nearby()
    {
        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        if (body == null || !body.isAlive || !body.IsConsc() || Time.timeScale == 0f ||
            (GameManager.main != null && GameManager.main.paused) || MultiplayerHud.IsTyping ||
            Controls.IsTextInputSelected || ArsenalMenu.IsOpen) return null;
        MedicalStationRuntime nearest = null;
        var distance = 1.5625f;
        foreach (var station in stations.Values)
        {
            if (station == null || !station.AvailableFor(MultiplayerSession.LocalPeerId) || !station.isActiveAndEnabled) continue;
            var squared = ((Vector2)(body.transform.position - station.transform.position)).sqrMagnitude;
            if (squared >= distance) continue;
            nearest = station;
            distance = squared;
        }
        return nearest;
    }

    private void Update()
    {
        foreach (var light in lights) light.enabled = AvailableFor(MultiplayerSession.LocalPeerId);
        if (animating)
        {
            var elapsed = Time.time - usedAt;
            screen.localPosition = new Vector3(screenPosition.Evaluate(elapsed) * 0.5f, 0.285715f, 0f);
            screen.localScale = new Vector3(screenScale.Evaluate(elapsed), 1f, 1f);
            if (bloodline != null)
            {
                var active = recipient != null && elapsed < 3.8f;
                bloodline.enabled = airLine.enabled = active;
                if (active)
                {
                    bloodline.SetPosition(0, transform.TransformPoint(new Vector3(-0.0015f, 0.1765f, 0f)));
                    bloodline.SetPosition(1, recipient.transform.position);
                    airLine.SetPosition(0, transform.TransformPoint(new Vector3(-0.0015f, 0.3875f, 0f)));
                    airLine.SetPosition(1, recipient.headTransform.position);
                }
            }
            if (elapsed > 4.1f) animating = false;
        }
        if (Nearby() != this || !Input.GetKeyDown(PlayerScript.player.keys["Use"]) ||
            Time.unscaledTime - requestedAt < 0.5f) return;
        requestedAt = Time.unscaledTime;
        if (MultiplayerSession.IsConnected && !MultiplayerSession.IsHost)
            MultiplayerSession.Send(new WorldInteractionPacket(InteractionType.MedicalStationUse, id), 1);
        else TryUse(id, MultiplayerSession.LocalPeerId);
    }

    private void LateUpdate()
    {
        var nearby = Nearby() == this;
        if (useIcon == null)
        {
            var source = PlayerScript.player == null ? null : PlayerScript.player.buttonKeyIcon;
            if (!nearby || source == null) return;
            useIcon = Instantiate(source, transform);
            useIcon.gameObject.name = "Use Icon";
            useIcon.gameObject.SetActive(true);
        }
        useIcon.transform.position = transform.position + Vector3.up * 1.2f;
        useIcon.transform.rotation = Quaternion.identity;
        useIcon.enabled = nearby;
    }

    internal static void TryUse(ulong stationId, ushort peerId)
    {
        if (!stations.TryGetValue(stationId, out var station) || station == null || !station.AvailableFor(peerId)) return;
        var body = peerId == MultiplayerSession.LocalPeerId ? PlayerScript.player?.bodyScript : NetworkAvatarManager.RemoteBodyForPeer(peerId);
        if (body == null || !body.isAlive || !body.IsConsc() ||
            !(((Vector2)(body.transform.position - station.transform.position)).sqrMagnitude <= 1.5625f)) return;
        var packet = new WorldInteractionPacket(InteractionType.MedicalStationUsed, stationId, weaponSlot: peerId, previousAmmo: station.useCount + 1, damage: station.data.cooldown, manual: true);
        ApplyUsed(packet);
        if (MultiplayerSession.IsConnected) MultiplayerSession.Send(packet);
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
       
        foreach (var light in station.lights)
            light.enabled = station.AvailableFor(MultiplayerSession.LocalPeerId);
       
        if (!packet.Manual) 
            return;
        
        station.animating = true;
        station.usedAt = Time.time;
        var local = packet.WeaponSlot == MultiplayerSession.LocalPeerId;
        var body = local ? PlayerScript.player?.bodyScript : NetworkAvatarManager.RemoteBodyForPeer((ushort)packet.WeaponSlot);
       
        if (local && body != null && body.isAlive)
        {
            LobbyHealthRule.RestoreFull(body);
            body.burnIntensity = 0f;
            body.shockTime = 0f;
            body.fireImmunityTime = Mathf.Max(body.fireImmunityTime, 5f);
           
            foreach (var limb in body.limbs)
                if (limb != null)
                {
                    if (!limb.dismembered && limb.passer != null && limb.passer.relevantDismember != null)
                        limb.passer.relevantDismember.currentDamage = 0f;
                    
                    foreach (var fire in limb.GetComponentsInChildren<FireScript>())
                    {
                        fire.canIgnite = false;
                        fire.damageMult = 0f;
                        fire.fuel = -1f;
                    }
                }
        }
        
        station.recipient = body;
        
        if (body != null)
        {
            if (station.tubeMaterial == null)
            {
                station.tubeMaterial = new Material(Shader.Find("Sprites/Default"));
                station.bloodline = station.CreateTube("Bloodline", new Color(0.5f, 0f, 0f));
                station.airLine = station.CreateTube("Air Line", Color.white);
            }
        }
        
        if (EmbeddedAudioLoader.MedicalStationSound != null)
            Sound.Play(EmbeddedAudioLoader.MedicalStationSound, station.transform.position, false, false);
    }

    private LineRenderer CreateTube(string name, Color color)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        var line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = tubeMaterial;
        line.positionCount = 2;
        line.startWidth = line.endWidth = 0.11393f;
        line.startColor = line.endColor = color;
        line.numCapVertices = 3;
        line.sortingOrder = 500;
        return line;
    }

    internal static void SendHistory(ushort peerId)
    {
        foreach (var station in stations.Values)
            if (station != null && station.useCount > 0)
                MultiplayerSession.Send(new WorldInteractionPacket(InteractionType.MedicalStationUsed, station.id, weaponSlot: station.usedPlayers.Contains(peerId) ? peerId : 0, previousAmmo: station.useCount, damage: station.CooldownRemaining), peerId);
    }

    private void OnDestroy()
    {
        if (stations.TryGetValue(id, out var current) && current == this) 
            stations.Remove(id);
        
        if (tubeMaterial != null) 
            Destroy(tubeMaterial);
    }
}
