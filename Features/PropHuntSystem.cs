using UnityEngine;
using UnityEngine.SceneManagement;

internal static class PropHuntSystem
{
    private static readonly PropHuntRound round = new();
    private static readonly ScreenMessageOverlay overlay = new();
    private static readonly Dictionary<string, GameObject> templates = new(StringComparer.Ordinal);
    private static readonly Dictionary<ushort, string> props = new();
    private static readonly HashSet<ushort> alive = [];
    private static readonly Dictionary<ushort, PropHuntDisguise> disguises = new();
    private static PropHuntPhase phase;
    private static readonly HashSet<ushort> hunters = new(), previousHunters = new();
    private static int scene = int.MinValue;
    private static int epoch = -1;
    private static uint sequence, lastSequence;
    private static float deadline, broadcastAt, changeAt, stunAt, pushAt, locatorAt, locatorUntil;
    private static float gravityPenaltyTime;
    private static readonly Dictionary<ushort, float> shots = new();
    private static BodyScript equippedBody;
    private static WeaponPreset magnum;
    private static bool catalogBuilt;
    private static bool restarting;
    internal static bool FriendlyHunters(ushort first, ushort second) => Enabled && first != second && IsHunter(first) && IsHunter(second);
    internal static bool IsHunter(ushort peer) => hunters.Contains(peer);
    internal static bool IsProtectedHunter(BodyScript body) => Enabled && phase == PropHuntPhase.Hiding && body != null &&
        body.isAlive && IsHunter(NetworkAvatarManager.PeerIdOfBody(body)) && alive.Contains(NetworkAvatarManager.PeerIdOfBody(body));
    internal static IEnumerable<BodyScript> HunterBodies => hunters.Select(Body).Where(body => body != null);
    internal static int TotalProps => props.Count;
    internal static int AliveProps => props.Keys.Count(alive.Contains);
    internal static float LocatorCooldown => Mathf.Max(0f, locatorAt - Time.unscaledTime);
    internal static bool LocatorVisible => AbilitiesVisible && phase == PropHuntPhase.Hunting && IsHunter(MultiplayerSession.LocalPeerId) && MultiplayerSession.PropHuntLocator;
    internal static float ChangeCooldown => Mathf.Max(0f, changeAt - Time.unscaledTime);
    internal static float StunCooldown => Mathf.Max(0f, stunAt - Time.unscaledTime);
    internal static bool AbilitiesVisible => Enabled && alive.Contains(MultiplayerSession.LocalPeerId) && (phase == PropHuntPhase.Hiding || phase == PropHuntPhase.Hunting);
    internal static bool CanUseGravity(BodyScript body) => Enabled && phase == PropHuntPhase.Hunting && body != null && IsHunter(NetworkAvatarManager.PeerIdOfBody(body)) && alive.Contains(NetworkAvatarManager.PeerIdOfBody(body));
    internal static bool CanBeGrabbed(BodyScript body) => IsProp(body) && body.isAlive && alive.Contains(NetworkAvatarManager.PeerIdOfBody(body));
    internal static float SecondsRemaining => phase == PropHuntPhase.Waiting || phase >= PropHuntPhase.PropsWin ? 0f : Mathf.Max(0f, deadline - Time.unscaledTime);

    internal static bool Enabled => MultiplayerSession.IsActive && MultiplayerSession.PropHuntEnabled &&
        MultiplayerSession.LobbySettingsReceived && GameManager.main != null &&
        SceneManager.GetActiveScene().name != "LevelSelect" && SceneManager.GetActiveScene().name != "LevelEditor";
    internal static bool BlocksInput => Enabled && (phase == PropHuntPhase.Waiting || phase >= PropHuntPhase.PropsWin ||
        !alive.Contains(MultiplayerSession.LocalPeerId) || (IsHunter(MultiplayerSession.LocalPeerId) && phase == PropHuntPhase.Hiding));
    internal static bool IsProp(BodyScript body) => Enabled && body != null && props.ContainsKey(NetworkAvatarManager.PeerIdOfBody(body));
    internal static bool CanShoot(WeaponScript weapon) => !Enabled || (phase == PropHuntPhase.Hunting &&
        IsHunter(NetworkAvatarManager.PeerIdOfBody(weapon.body)) && alive.Contains(NetworkAvatarManager.PeerIdOfBody(weapon.body)) && weapon.stats == Magnum());

    internal static void Tick()
    {
        if (!Enabled)
        {
            if (scene != int.MinValue) 
                Reset();
            
            if (!MultiplayerSession.IsActive || !MultiplayerSession.PropHuntEnabled || SceneManager.GetActiveScene().name == "LevelSelect")
                previousHunters.Clear();
            
            return;
        }
        EnsureScene();
        
        if (!catalogBuilt) 
            BuildCatalog();
        
        if (MultiplayerSession.IsHost) 
            TickHost();
        
        var body = PlayerScript.player?.bodyScript;
        if (body != null && !HeadlessLobbyService.IsHeadlessServer)
        {
            if (phase != PropHuntPhase.Waiting && body.isAlive && !alive.Contains(MultiplayerSession.LocalPeerId))
            {
                body.Death();
            }
          
            EnforceLoadout(body);
           
            if ((IsProp(body) || IsHunter(MultiplayerSession.LocalPeerId)) && !PlayerScript.player.TryGetComponent<PropHuntHud>(out _))
                PlayerScript.player.gameObject.AddComponent<PropHuntHud>();
           
            if (!body.TryGetComponent<PropHuntVitals>(out _)) 
                body.gameObject.AddComponent<PropHuntVitals>();
        }
        
        foreach (var pair in props)
        {
            var target = Body(pair.Key);
            if (target == null || !templates.TryGetValue(pair.Value, out var template)) continue;
            if (!disguises.TryGetValue(pair.Key, out var disguise) || disguise == null || disguise.gameObject != target.gameObject)
            {
                if (disguise != null) UnityEngine.Object.Destroy(disguise);
                disguise = target.gameObject.AddComponent<PropHuntDisguise>();
                disguises[pair.Key] = disguise;
            }
            disguise.Apply(target, pair.Key, pair.Value, template);
            if (!HeadlessLobbyService.IsHeadlessServer && phase == PropHuntPhase.Hunting && alive.Contains(pair.Key) && Time.unscaledTime < locatorUntil)
                disguise.RevealLocator(locatorUntil);
        }
        
        if (LocatorVisible && body != null && body.isAlive && !MultiplayerHud.IsTyping && !Controls.IsTextInputSelected &&
            !GameManager.main.paused && Input.GetKeyDown(KeyCode.J) && LocatorCooldown <= 0f)
        {
            locatorAt = Time.unscaledTime + MultiplayerSession.PropHuntLocatorCooldown;
            Request(new PropHuntPacket(PropHuntAction.Locator));
        }

        if (props.ContainsKey(MultiplayerSession.LocalPeerId) && alive.Contains(MultiplayerSession.LocalPeerId) && !MultiplayerHud.IsTyping && !Controls.IsTextInputSelected && !GameManager.main.paused)
        {
            if (Input.GetKeyDown(KeyCode.J) && Time.unscaledTime >= changeAt)
            {
                changeAt = Time.unscaledTime + MultiplayerSession.PropHuntChangeCooldown;
                Request(new PropHuntPacket(PropHuntAction.Change));
            }
            
            if (Input.GetMouseButtonDown(0))
            {
                var target = StunTarget();
                if (target != 0)
                {
                    stunAt = Time.unscaledTime + MultiplayerSession.PropHuntStunCooldown;
                    Request(new PropHuntPacket(PropHuntAction.Stun, target));
                }
            }
        }
        DrawOverlay();
    }

    private static void TickHost()
    {
        var peers = MultiplayerSession.PeerIds().Where(id => id != MultiplayerSession.LocalPeerId).ToArray();
        if (round.Phase == PropHuntPhase.Waiting)
        {
            var players = peers.Where(id => Body(id) != null && Body(id).isAlive).ToList();
            if (!HeadlessLobbyService.IsHeadlessServer && PlayerScript.player?.bodyScript?.isAlive == true)
                players.Add(MultiplayerSession.LocalPeerId);
            
            if (templates.Count < 2 || players.Count < 2 || peers.Any(id => Body(id) == null || NetworkAvatarManager.ReplicaForBody(Body(id))?.receivedPlayerState != true || NetworkAvatarManager.ReplicaForBody(Body(id))?.receivedFirstSnapshot != true) || (!HeadlessLobbyService.IsHeadlessServer && PlayerScript.player?.bodyScript == null))
                return;
            
            var candidates = players.OrderBy(id => UnityEngine.Random.value).OrderBy(id => previousHunters.Contains(id)).ToArray();
            var selected = candidates.Take(Math.Min(MultiplayerSession.PropHuntHunters, players.Count - 1)).ToArray();
            round.Start([.. players], selected, Time.unscaledTime, MultiplayerSession.PropHuntTime);
            foreach (var player in players.Where(player => !round.Hunters.Contains(player)))
                round.Change(player, templates.Keys.ToArray(), Time.unscaledTime - MultiplayerSession.PropHuntChangeCooldown, MultiplayerSession.PropHuntChangeCooldown);
            shots.Clear();
        }
        foreach (var player in round.Alive.ToArray())
        {
            var body = Body(player);
            
            var disconnected = player != MultiplayerSession.LocalPeerId && !peers.Contains(player);
            var confirmedDead = body != null && !body.isAlive && (player == MultiplayerSession.LocalPeerId ||
                NetworkAvatarManager.ReplicaForBody(body)?.receivedPlayerState == true);
            if (confirmedDead || disconnected)
            {
                round.Alive.Remove(player);
            }
        }
        
        var previousPhase = round.Phase;
        round.Advance(Time.unscaledTime);
        
        if (round.Phase != previousPhase) 
            broadcastAt = 0f;
        
        if (!restarting && round.RestartDue(Time.unscaledTime))
        {
            restarting = true;
            previousHunters.Clear();
            previousHunters.UnionWith(round.Hunters);
            ScoreboardSystem.PreserveForNextScene();
            MultiplayerSession.MarkNextSceneReloadAutoRestart();
            GameManager.main.Restart();
            return;
        }
        
        if (Time.unscaledTime < broadcastAt) 
            return;
        
        broadcastAt = Time.unscaledTime + 0.5f;
        
        var packet = new PropHuntPacket(PropHuntAction.State, round.Hunter, Mathf.Max(0f, round.Deadline - Time.unscaledTime), round.Phase, [.. round.Props], [.. round.Alive], sequence: ++sequence, hunters: round.Hunters.ToArray());
        AcceptState(packet);
        MultiplayerSession.Send(packet);
    }

    internal static void Receive(ushort sender, PropHuntPacket packet)
    {
        if (!Enabled || packet.Epoch != MultiplayerSession.CurrentSceneEpoch || !Enum.IsDefined(typeof(PropHuntAction), packet.Action))
            return;
        
        EnsureScene();
        
        if (packet.Action == PropHuntAction.State)
        {
            if (!MultiplayerSession.IsHost && sender == MultiplayerSession.HostPeerId &&
                Enum.IsDefined(typeof(PropHuntPhase), packet.Phase) && !float.IsNaN(packet.Value) && !float.IsInfinity(packet.Value) &&
                packet.Value >= 0f && packet.Value <= 3630f && packet.Props.All(pair => pair.Value.Length <= 128)) AcceptState(packet);
            return;
        }
        if (packet.Action >= PropHuntAction.Damage &&
            ((packet.Action != PropHuntAction.Push && packet.Action != PropHuntAction.KickMiss && packet.Action != PropHuntAction.Locator) || !MultiplayerSession.IsHost))
        {
            if (sender == MultiplayerSession.HostPeerId && (packet.Player == MultiplayerSession.LocalPeerId || packet.Action == PropHuntAction.Shock || packet.Action == PropHuntAction.Located)) ApplyEffect(packet);
            return;
        }
        
        if (!MultiplayerSession.IsHost || !round.Alive.Contains(sender))
            return;
        
        var changed = false;
        switch (packet.Action)
        {
            case PropHuntAction.Locator:
                if (round.Locate(sender, Time.unscaledTime, MultiplayerSession.PropHuntLocator, MultiplayerSession.PropHuntLocatorCooldown))
                    Effect(new PropHuntPacket(PropHuntAction.Located, sender));
                break;
            case PropHuntAction.KickMiss:
                if (round.KickMiss(sender, Time.unscaledTime)) Effect(new PropHuntPacket(PropHuntAction.Penalty, sender));
                break;
            case PropHuntAction.Push:
                var pusher = Body(sender);
                if (pusher != null && disguises.TryGetValue(packet.Player, out var pushed) && pushed != null &&
                    round.Push(sender, packet.Player, packet.Value, pushed.DistanceFrom(pusher.transform.position), Time.unscaledTime))
                    Effect(new PropHuntPacket(PropHuntAction.Push, packet.Player, packet.Value));
                break;
            case PropHuntAction.Change:
                changed = round.Change(sender, templates.Keys.ToArray(), Time.unscaledTime, MultiplayerSession.PropHuntChangeCooldown);
                break;
            case PropHuntAction.Stun:
                var source = Body(sender);
                var target = Body(packet.Player);
                if (source != null && target != null && round.Stun(sender, packet.Player,
                    Vector2.Distance(source.transform.position, target.transform.position), Time.unscaledTime, MultiplayerSession.PropHuntStunCooldown))
                    Effect(new PropHuntPacket(PropHuntAction.Shock, packet.Player));
                break;
            case PropHuntAction.Hit:
            case PropHuntAction.Miss:
                if (!round.Hunters.Contains(sender) || round.Phase != PropHuntPhase.Hunting || (shots.TryGetValue(sender, out var nextShotAt) && Time.unscaledTime < nextShotAt)) break;
                var weapon = Magnum();
                if (weapon == null) break;
                if (packet.Action == PropHuntAction.Hit)
                {
                    if (packet.Player == sender || !round.Alive.Contains(packet.Player) || !round.Props.ContainsKey(packet.Player) ||
                        float.IsNaN(packet.Value) || float.IsInfinity(packet.Value) || packet.Value < 0f || packet.Value > weapon.range) break;
                    var victim = Body(packet.Player);
                    var shooter = Body(sender);
                    if (victim == null || shooter == null || Vector2.Distance(victim.mainTorso.position, shooter.mainTorso.position) > weapon.range + 5f) break;
                    var damage = Mathf.Clamp(weapon.damage + UnityEngine.Random.Range(-weapon.damageRange, weapon.damageRange) - packet.Value * weapon.falloff, 1f, 999999f);
                    Effect(new PropHuntPacket(PropHuntAction.Damage, packet.Player, damage, hunters: new[] { sender }));
                    Effect(new PropHuntPacket(PropHuntAction.Reward, sender));
                }
                else Effect(new PropHuntPacket(PropHuntAction.Penalty, sender));
                shots[sender] = Time.unscaledTime + Mathf.Max(0.05f, weapon.fireDelay * 0.8f);
                break;
        }
        if (changed) broadcastAt = 0f;
    }

    private static void AcceptState(PropHuntPacket packet)
    {
        if (packet.Sequence <= lastSequence) return;
        lastSequence = packet.Sequence;
        hunters.Clear();
        hunters.UnionWith(packet.Hunters);
        phase = packet.Phase;
        deadline = Time.unscaledTime + packet.Value;
        props.Clear();
        foreach (var prop in packet.Props) props[prop.Key] = prop.Value;
        alive.Clear();
        foreach (var player in packet.Alive) alive.Add(player);
    }

    private static void Effect(PropHuntPacket packet)
    {
        if (packet.Player == MultiplayerSession.LocalPeerId || packet.Action == PropHuntAction.Shock || packet.Action == PropHuntAction.Located) ApplyEffect(packet);
        if (packet.Action == PropHuntAction.Shock || packet.Action == PropHuntAction.Located) MultiplayerSession.Send(packet);
        else if (packet.Player != MultiplayerSession.LocalPeerId) MultiplayerSession.Send(packet, packet.Player);
    }

    private static void ApplyEffect(PropHuntPacket packet)
    {
        if (packet.Action == PropHuntAction.Located)
        {
            locatorUntil = Time.unscaledTime + 3f;
            if (packet.Player == MultiplayerSession.LocalPeerId)
                locatorAt = Time.unscaledTime + MultiplayerSession.PropHuntLocatorCooldown;
            if (EmbeddedAudioLoader.LocatorSound != null && !HeadlessLobbyService.IsHeadlessServer)
                Sound.Play(EmbeddedAudioLoader.LocatorSound, Vector2.zero, twoDimensional: true, pitchShift: false);
            return;
        }
        var body = Body(packet.Player);
        if (body == null || !body.isAlive) return;
        if (packet.Action == PropHuntAction.Shock && EmbeddedAudioLoader.StunSound != null)
            Sound.Play(EmbeddedAudioLoader.StunSound, body.transform.position, false, false);
        if (packet.Player != MultiplayerSession.LocalPeerId) return;
        switch (packet.Action)
        {
            case PropHuntAction.Push:
                if ((packet.Value == -1f || packet.Value == 1f) && IsProp(body) && alive.Contains(MultiplayerSession.LocalPeerId))
                    body.GetComponent<PropHuntDisguise>()?.ApplyPush(packet.Value);
                break;
            case PropHuntAction.Damage:
                if (float.IsNaN(packet.Value) || float.IsInfinity(packet.Value) || packet.Value <= 0f || packet.Value > 999999f) return;
                NetworkAvatarManager.RecordDamageSource(body, Body(packet.Hunters.FirstOrDefault()));
                var damage = LobbyHealthRule.ClampDamage(body, packet.Value);
                body.health -= damage;
                body.stamina -= damage * 1.38f;
                body.Damaged();
                body.DoGrunt();
                ScoreboardSystem.RecordLocalDamageReceived(damage);
                break;
            case PropHuntAction.Penalty:
                body.health -= body.maxHealth * (MultiplayerSession.PropHuntShotPenalty / 100f);
                body.Damaged();
                break;
            case PropHuntAction.Reward:
                body.health = Mathf.Min(body.maxHealth, body.health + body.maxHealth * (MultiplayerSession.PropHuntShotHealing / 100f));
                body.GetComponent<PropHuntVitals>()?.AllowHealth(body.health);
                break;
            case PropHuntAction.Shock:
                body.EnterHalfControl();
                body.shockTime = 5f;
                break;
        }
    }

    internal static void ShotHit(ushort player, float distance) => Request(new PropHuntPacket(
        player == 0 ? PropHuntAction.Miss : PropHuntAction.Hit, player, distance));

    internal static void KickHit(BodyScript attacker, Rigidbody2D target)
    {
        if (attacker != PlayerScript.player?.bodyScript || !CanUseGravity(attacker) || target == null ||
            target.GetComponent<PropHuntHitbox>() != null) return;
        if (target.GetComponent<CrateScript>() != null || WorldBodyReplication.IsSafetyRailingBody(target) || WorldBodyReplication.IsChainlinkFenceBody(target))
            Request(new PropHuntPacket(PropHuntAction.KickMiss));
    }

    internal static void TickGravityPenalty(LevitatorScript levitator)
    {
        var body = levitator.refBody;
        if (body != PlayerScript.player?.bodyScript)
            return;
            
        var target = levitator.currentlyLevitating;
        
        if (!CanUseGravity(body) || !body.isAlive || target == null || target.GetComponent<PropHuntHitbox>() != null || target.GetComponentInParent<BodyScript>() != null)
        {
            gravityPenaltyTime = 0f;
            return;
        }
        
        gravityPenaltyTime += Time.fixedDeltaTime;
        if (gravityPenaltyTime < MultiplayerSession.PropHuntTelekinesisInterval) return;
        var penalties = Mathf.FloorToInt(gravityPenaltyTime / MultiplayerSession.PropHuntTelekinesisInterval);
        gravityPenaltyTime -= penalties * MultiplayerSession.PropHuntTelekinesisInterval;
        body.health -= body.maxHealth * (MultiplayerSession.PropHuntTelekinesisPenalty / 100f) * penalties;
        body.Damaged();
    }

    internal static void Push(ushort player, float direction)
    {
        if (!CanUseGravity(PlayerScript.player?.bodyScript) || Time.unscaledTime < pushAt) return;
        pushAt = Time.unscaledTime + 0.1f;
        Request(new PropHuntPacket(PropHuntAction.Push, player, direction));
    }

    private static void Request(PropHuntPacket packet)
    {
        if (MultiplayerSession.IsHost) Receive(MultiplayerSession.LocalPeerId, packet);
        else MultiplayerSession.Send(packet, MultiplayerSession.HostPeerId);
    }

    private static BodyScript Body(ushort peer) => peer == MultiplayerSession.LocalPeerId
        ? PlayerScript.player?.bodyScript : NetworkAvatarManager.RemoteBodyForPeer(peer);

    internal static WeaponPreset Magnum()
    {
        if (magnum != null) return magnum;
        magnum = Resources.Load<WeaponPreset>("Weapons/HeavyRevolver");
        return magnum;
    }

    private static void EnforceLoadout(BodyScript body)
    {
        if (!body.isAlive) return;
        LocalPlayerReplication.EnsureRespawnWeaponSlots(body);
        var armed = phase == PropHuntPhase.Hunting && IsHunter(MultiplayerSession.LocalPeerId) && alive.Contains(MultiplayerSession.LocalPeerId);
        var weapon = armed ? Magnum() : null;
        var invalid = body != equippedBody || body.weapons[0] != weapon || body.weapons.Skip(1).Any(p => p != null) ||
            (weapon == null ? !body.unarmed : body.unarmed || body.weapon == null || body.weapon.stats != weapon);
        if (!invalid) return;
        for (var slot = 0; slot < body.weapons.Count; slot++)
        {
            body.weapons[slot] = null;
            body.weaponAmmos[slot] = 0;
        }
        body.ChangeToUnarmed();
        LobbyAmmoRules.Apply(body, "0;0;0;0");
        if (weapon != null)
        {
            body.weapons[0] = weapon;
            body.weaponAmmos[0] = Math.Min(weapon.magSize, 100);
            body.currentWeapon = 0;
            body.ChangeWeapon(0);
            while (body.ammoAmount.Count <= weapon.ammoType) body.ammoAmount.Add(0);
            body.ammoAmount[weapon.ammoType] = 100 - body.weaponAmmos[0];
            PlayerScript.player?.BodyAmmoChanged();
        }
        equippedBody = body;
    }

    private static void BuildCatalog()
    {
        catalogBuilt = true;
        var prefabs = Resources.LoadAll<GameObject>("Building").Concat(new[]
        {
            Resources.Load<GameObject>("Building/Pallet"),
            Resources.Load<GameObject>("Building/WheelPallet"),
            Resources.Load<GameObject>("Spawnables/HalfPallet"),
            Resources.Load<GameObject>("Spawnables/PalletSplit")
        });
        foreach (var body in prefabs.Where(prefab => prefab != null).SelectMany(prefab => prefab.GetComponentsInChildren<Rigidbody2D>(true)))
        {
            if (body == null || body.gameObject.scene.IsValid()) continue;
            if (body.name != "Pallet" && body.name != "WheelPallet" && body.GetComponent<CrateScript>() == null && !WorldBodyReplication.IsSafetyRailingBody(body) && !WorldBodyReplication.IsChainlinkFenceBody(body)) continue;
            var name = WorldReplication.CleanCloneName(body.name);
            if (name.Length > 128 || templates.ContainsKey(name) || body.GetComponentsInChildren<SpriteRenderer>(true).All(r => r.sprite == null)) continue;
            templates.Add(name, PropHuntDisguise.CreateTemplate(body));
        }
        GunsawMultiplayerPlugin.LogInfo($"Prop Hunt: loaded {templates.Count} disguises.");
    }

    private static void DrawOverlay()
    {
        if (phase == PropHuntPhase.Waiting && GameManager.main.paused)
        {
            overlay.Hide();
            return;
        }
        if (phase == PropHuntPhase.PropsWin) overlay.Show("PROPS WIN!");
        else if (phase == PropHuntPhase.HunterWins) overlay.Show("HUNTERS WIN!");
        else if (phase == PropHuntPhase.Waiting) overlay.Show("WAITING FOR PLAYERS", 1f);
        else if (phase == PropHuntPhase.Hiding && IsHunter(MultiplayerSession.LocalPeerId))
            overlay.Show("You're hunter!\n" + Mathf.CeilToInt(Mathf.Max(0f, deadline - Time.unscaledTime)), 1f);
        else overlay.Hide();
    }
    
    internal static ushort StunTarget()
    {
        var body = PlayerScript.player?.bodyScript;
        var camera = Camera.main;
        if (!AbilitiesVisible || !IsProp(body) || !body.isAlive || StunCooldown > 0f || camera == null || MultiplayerHud.IsTyping || Controls.IsTextInputSelected || GameManager.main.paused) 
            return 0;
        
        var point = camera.ScreenToWorldPoint(Input.mousePosition);
        return hunters.FirstOrDefault(id => alive.Contains(id) && Body(id) != null && Body(id).isAlive &&
                                            Vector2.Distance(body.transform.position, Body(id).transform.position) <= PlayerCarrySystem.Range &&
                                            PlayerCarrySystem.PointerOverBody(Body(id), point));
    }
    
    private static void EnsureScene()
    {
        if (scene == SceneManager.GetActiveScene().handle && epoch == MultiplayerSession.CurrentSceneEpoch) return;
        Reset();
        scene = SceneManager.GetActiveScene().handle;
        epoch = MultiplayerSession.CurrentSceneEpoch;
    }

    private static void Reset()
    {
        foreach (var disguise in disguises.Values)
            if (disguise != null) UnityEngine.Object.Destroy(disguise);
        foreach (var template in templates.Values)
            if (template != null) UnityEngine.Object.Destroy(template);
        disguises.Clear();
        templates.Clear();
        props.Clear();
        alive.Clear();
        round.Reset();
        phase = PropHuntPhase.Waiting;
        hunters.Clear();
        scene = int.MinValue;
        epoch = -1;
        sequence = lastSequence = 0;
        catalogBuilt = false;
        restarting = false;
        equippedBody = null;
        broadcastAt = changeAt = stunAt = pushAt = locatorAt = locatorUntil = 0f;
        gravityPenaltyTime = 0f;
        shots.Clear();
        overlay.Hide();
    }
}

internal sealed class PropHuntVitals : MonoBehaviour
{
    private BodyScript body;
    private float ceiling;
    private float maxHealth;

    private void Awake()
    {
        body = GetComponent<BodyScript>();
        ceiling = body.health;
        maxHealth = body.maxHealth;
    }

    internal void AllowHealth(float value) => ceiling = value;

    internal static bool BlockHunterDamage(BodyScript body)
    {
        if (!PropHuntSystem.IsProtectedHunter(body))
            return false;
            
        var vitals = body.GetComponent<PropHuntVitals>();
        body.health = vitals != null ? vitals.ceiling : NetworkAvatarManager.ReplicaForBody(body)?.lastRemoteHealth ?? body.maxHealth;
        return true;
    }

    private void LateUpdate()
    {
        if (!PropHuntSystem.Enabled)
        {
            Destroy(this);
            return;
        }

        if (body.maxHealth != maxHealth)
        {
            maxHealth = body.maxHealth;
            ceiling = body.health;
        }
        if (!BlockHunterDamage(body))
            body.health = Mathf.Min(body.health, ceiling);
        ceiling = body.health;
    }
}
