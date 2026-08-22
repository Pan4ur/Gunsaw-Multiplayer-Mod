using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class NetworkAvatarReplication : MonoBehaviour
{
    private const bool BufferedRemoteInterpolation = true;
    private const float SnapshotInterval = 1f / 50f;
    private const string PvpRemoteTeam = "gunsaw_mp_remote_player";
    private const string ProtogenPrefabPath = "Enemies/RobotEnemy";
    private static readonly List<string> knownCharacterPrefabs = [];
    private static readonly Dictionary<string, string> characterDisplayNames = new();
    private static readonly Dictionary<string, Sprite> spriteCache = new();
    private static readonly Dictionary<Sprite, string> spriteIdCache = new();
    private static readonly Dictionary<Texture2D, string> textureSignatureCache = new();
    private static readonly Dictionary<string, WeaponPreset> weaponPresetCache = new();
    private static string selectedCharacterPrefab = "";
    private static string pendingRespawnCharacterPrefab = "";
    private static int remoteAvatarCreationDepth;
    internal BodyScript remoteBody;
    internal Vector2 lastAuthoritativePosition;
    internal bool hasAuthoritativePosition;
    private GameObject remoteAvatar;
    private Transform remoteAvatarParent;
    private string remotePrefabPath = "";
    internal string remoteName = "Player";
    private string identitySent = "";
    private float nextIdentity;
    private BodyScript identityBody;
    private string identityCharacterName = "";
    private string identitySpeciesName = "";
    private string identityRootName = "";
    private string identityFallback = "";
    private string resolvedIdentityPrefab = "";
    private string? cachedCharacterPrefabPreference;
    private float nextSnapshot;
    private PlayerVisualState lastSerializedVisualState;
    private float visualResendUntil;
    private float nextFullVisualSnapshot;
    private float nextAvatarTrafficSample;
    private int avatarCoreBytesWindow;
    private int avatarLimbBytesWindow;
    private int avatarRigBytesWindow;
    private int avatarWeaponBytesWindow;
    private int avatarEffectsBytesWindow;
    private int avatarVisualBytesWindow;
    private int avatarCoreBytesPerSecond;
    private int avatarLimbBytesPerSecond;
    private int avatarRigBytesPerSecond;
    private int avatarWeaponBytesPerSecond;
    private int avatarEffectsBytesPerSecond;
    private int avatarVisualBytesPerSecond;
    internal string localName;
    private readonly Queue<RemoteProjectileVisual> remoteProjectiles = new();
    private readonly Dictionary<Rigidbody2D, TargetState> targets = new();
    private readonly Dictionary<Transform, WorldTargetState> worldTargets = new();
    private readonly Dictionary<Transform, WorldTargetState> localTargets = new();
    private bool receivedFirstSnapshot;
    private int appliedWeapon = -1;
    private ulong appliedWeaponSprite;
    private string appliedInventory = "";
    private string lastSerializedInventory = "";
    private float nextFullInventory;
    private VisualLayout localVisualLayout;
    private VisualLayout remoteVisualLayout;
    private LineRenderer remoteLevitLine;
    private LineRenderer remoteCrystalTongueLine;
    private GameObject remoteScarf;
    private GameObject remoteScarfHold;
    private readonly Dictionary<int, GameObject> remoteFires = new();
    private readonly Dictionary<Collider2D, bool> remoteColliderTriggers = new();
    private BodyScript collisionRuleLocalBody;
    private bool collisionRuleApplied;
    private bool collisionRulePlayerCollisions;
    private readonly Dictionary<SpriteRenderer, Sprite> originalDismemberSprites = new();
    private readonly List<Transform> staleWorldTargets = [];
    private readonly List<KeyValuePair<Transform, WorldTargetState>> orderedWorldTargets = new();
    private Rigidbody2D[] remoteRigidbodies = new Rigidbody2D[0];
    private readonly List<Rigidbody2D> remoteTailBases = [];
    private Transform[] remoteTails = new Transform[0];
    private readonly List<SpriteRenderer[]> remoteTailSprites = [];
    private readonly List<SpriteRenderer[]> remoteTailRootSprites = [];
    private bool remotePhysicsModeKnown;
    private float remoteVehicleHeadRotation;
    private float vehicleHeadFromRotation;
    private float vehicleHeadStartedAt;
    private bool hasRemoteVehicleHeadRotation;
    private Vector2 vehicleArmsTarget;
    private float vehicleArmsTargetRotation;
    private Vector2 vehicleArmsLocalPosition;
    private float vehicleArmsLocalRotation;
    private Vector2 vehicleArmsFromLocalPosition;
    private float vehicleArmsFromLocalRotation;
    private float vehicleArmsStartedAt;
    private bool hasVehicleArmsTarget;
    private Vector2 vehicleTailTarget;
    private float vehicleTailTargetRotation;
    private readonly List<VehicleTailTarget> vehicleTailTargets = [];
    private readonly List<VehicleTailTransformTarget> vehicleTailTransformTargets = [];
    private bool hasVehicleRigTarget;
    private static NetworkAvatarReplication instance;
    internal static NetworkAvatarReplication Instance => instance;
    private static readonly Dictionary<int, BodyScript> lastDamageSources = new();
    private static readonly Dictionary<int, string> lastDamageSourceNames = new();
    private static readonly Dictionary<int, ushort> lastDamageSourcePeerIds = new();
    private static readonly Dictionary<int, string> lastDamageWeapons = new();
    private static readonly Dictionary<int, float> lastDamageSourceTimes = new();
    private static readonly Dictionary<int, PlayerDeathCause> environmentalDeathCauses = new();
    private static readonly Dictionary<int, float> environmentalDeathCauseTimes = new();
    private static readonly Dictionary<int, PlayerDeathCause> deathCauses = new();
    private static readonly Dictionary<int, float> localKillBloodTimes = new();
    private static readonly HashSet<int> announcedDeaths = [];
    private static BodyScript suppressNpcKillEffectFor;
    private bool coordinator;
    internal ushort remotePeerId;
    private float lastRemoteHealth;
    private bool lastRemoteAlive = true;
    private static BodyScript currentShooter;
    private static ShotState activeShotState;
    private static RocketProjectile activeRocketProjectile;
    private static int nextShotSpreadSeed;
    private static bool applyingNetworkPlayerDamage;
    private static Material fallbackTracerMaterial;
    private static readonly HashSet<WebScript> localVelvetWebs = [];
    private static int suppressedTargetScreenEffects;
    private static float suppressedCameraUntil = -1f;
    private static PlayerScript localPlayerInstance;
    private static Transform localGlobalBody;
    private static BodyScript initialScaleAppliedBody;
    private static float appliedInitialScale = float.NaN;
    private BodyScript startingLoadoutAppliedBody;
    private BodyScript startingAmmoAppliedBody;
    private BodyScript pendingRespawnLoadoutBody;
    private BodyScript pendingRespawnLoadoutSource;
    private bool remoteDeathDropSpawned;
    private int appliedDismembermentHash = int.MinValue;
    private float pendingRemoteDamage;
    private ushort outgoingGrabPeerId;
    private bool remoteCanBeGrabbed;
    private GrabCommand incomingGrab;
    private float incomingGrabUntil;
    private int localSpawnScene = int.MinValue;
    private Vector3 localSpawnPosition;
    private Vector3 localDeathPosition;
    private bool localWasAlive = true;
    private float respawnAt = -1f;
    private bool localRespawnPending;
    private int localRespawnGeneration;
    private static float localRespawnProtectionUntil = -1f;
    private const float RespawnProtectionSeconds = 3f;
    private ushort spectatorPeerId;
    private bool spectating;
    private bool remoteVehicleReflected;
    private bool hasRemoteVehicleReflection;
    private Vector3 remoteScaleBeforeVehicle;
    private bool hasRemoteScaleBeforeVehicle;
    private BodyScript localVehicleBody;
    private VehicleBase localVehicle;
    private bool localVehicleLocked;
    private bool localVehicleWasSimulated;
    private readonly AvatarColorDebug colorEffects = new();

    internal static int AvatarCoreBytesPerSecond { get { return instance == null ? 0 : instance.avatarCoreBytesPerSecond; } }
    internal static int AvatarLimbBytesPerSecond { get { return instance == null ? 0 : instance.avatarLimbBytesPerSecond; } }
    internal static int AvatarRigBytesPerSecond { get { return instance == null ? 0 : instance.avatarRigBytesPerSecond; } }
    internal static int AvatarWeaponBytesPerSecond { get { return instance == null ? 0 : instance.avatarWeaponBytesPerSecond; } }
    internal static int AvatarEffectsBytesPerSecond { get { return instance == null ? 0 : instance.avatarEffectsBytesPerSecond; } }
    internal static int AvatarVisualBytesPerSecond { get { return instance == null ? 0 : instance.avatarVisualBytesPerSecond; } }

    internal bool TryGetLocalSpawnPosition(out Vector3 position)
    {
        var body = PlayerScript.player?.bodyScript;
        if (body == null)
        {
            position = default(Vector3);
            return false;
        }
        var scene = SceneManager.GetActiveScene();
        if (localSpawnScene != scene.handle)
        {
            localSpawnScene = scene.handle;
            localSpawnPosition = body.transform.position;
        }
        position = localSpawnPosition;
        return true;
    }
    
    internal static bool IsSpectating { get { return instance != null && instance.spectating && !MultiplayerSession.AllowRespawn; } }
    private bool HasActiveColorEffect => colorEffects.IsActive;

    internal static string SpectatorTargetName()
    {
        if (instance == null || instance.spectatorPeerId == 0) return "NO ALIVE PLAYERS";
        NetworkAvatarReplication replica;
        return NetworkAvatarRegistry.replicas.TryGetValue(instance.spectatorPeerId, out replica) && replica != null
            ? "SPECTATING " + replica.remoteName : "NO ALIVE PLAYERS";
    }

    internal static string RespawnCountdownText()
    {
        var player = PlayerScript.player;
        if (instance == null || !MultiplayerSession.AllowRespawn || instance.respawnAt < 0f ||
            player == null || player.bodyScript == null || player.bodyScript.isAlive) return "";
        return "RESPAWN IN " + Mathf.Max(0, Mathf.CeilToInt(instance.respawnAt - Time.unscaledTime));
    }

    internal static bool TrySetPendingRespawnCharacter(string character, out string characterName)
    {
        if (!TryResolveCharacterPrefab(character, out var prefabPath, out characterName)) return false;
        pendingRespawnCharacterPrefab = prefabPath;
        return true;
    }

    internal static IEnumerable<string> SwapCharacterNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in knownCharacterPrefabs)
        {
            string displayName;
            if (characterDisplayNames.TryGetValue(path, out displayName) && !string.IsNullOrWhiteSpace(displayName))
                names.Add(displayName);
            else names.Add(path.Substring(path.LastIndexOf('/') + 1));
        }
        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryResolveCharacterPrefab(string character, out string prefabPath, out string characterName)
    {
        prefabPath = "";
        characterName = "";
        var requested = (character ?? "").Trim();
        if (string.IsNullOrEmpty(requested)) return false;
        foreach (var path in knownCharacterPrefabs)
        {
            var prefab = Resources.Load<GameObject>(path);
            var body = prefab == null ? null : prefab.GetComponentInChildren<BodyScript>(true);
            if (body == null) continue;
            var prefabName = CleanCloneName(prefab.name);
            string displayName;
            characterDisplayNames.TryGetValue(path, out displayName);
            if (!string.Equals(requested, body.characterName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(requested, prefabName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(requested, displayName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(requested, path.Substring(path.LastIndexOf('/') + 1), StringComparison.OrdinalIgnoreCase))
                continue;
            prefabPath = path;
            characterName = string.IsNullOrWhiteSpace(displayName)
                ? (string.IsNullOrWhiteSpace(body.characterName) ? prefabName : body.characterName) : displayName;
            return true;
        }
        return false;
    }

    internal static bool TryBroadcastSwapRequest(ushort senderId, string message)
    {
        if (!MultiplayerSession.IsHost || string.IsNullOrWhiteSpace(message)) return false;
        const string prefix = "/swap";
        if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            (message.Length > prefix.Length && !char.IsWhiteSpace(message[prefix.Length]))) return false;
        var requested = message.Length == prefix.Length ? "" : message.Substring(prefix.Length).Trim();
        if (!MultiplayerSession.AllowSwap) return true;
        if (!TryResolveCharacterPrefab(requested, out _, out var characterName)) return true;
        var playerName = senderId == MultiplayerSession.LocalPeerId
            ? MultiplayerSession.LocalPlayerName : MultiplayerSession.PlayerName(senderId);
        BroadcastSwapAnnouncement(playerName, characterName);
        return true;
    }

    internal static void BroadcastSwapAnnouncement(string playerName, string characterName)
    {
        var message = playerName + " will respawn as " + characterName + ".";
        MultiplayerHud.AddSystemMessage(message);
        ChatPacket packet;
        if (ChatService.TryCreate(message, true, out packet)) MultiplayerSession.Send(packet);
    }

    internal static void EjectRemoteVehicleOccupants(VehicleBase vehicle)
    {
        if (!MultiplayerSession.IsHost || vehicle == null) return;
        foreach (var replica in NetworkAvatarRegistry.replicas.Values)
        {
            if (replica == null || replica.remotePeerId == 0 || replica.remoteBody == null ||
                !replica.remoteBody.inVehicle || replica.remoteBody.curVehicle != vehicle) continue;
            var body = replica.remoteBody;
            if (vehicle.occupant != body)
                body.ExitVehicle();
            MultiplayerSession.Send(new VehicleEjectPacket(), replica.remotePeerId);
        }
    }

    internal static void ForceRefreshRemotePhysics()
    {
        foreach (var replica in NetworkAvatarRegistry.replicas.Values)
        {
            if (replica == null) continue;
            replica.remotePhysicsModeKnown = false;
            replica.UpdateRemotePhysicsMode();
        }
    }

    internal static void EnsurePlayerSingletonForUpdate()
    {
        EnsureLocalPlayerSingleton();
    }

    internal static bool PrepareLocalPlayerUpdate(PlayerScript player)
    {
        if (player == null || player != PlayerScript.player || player.bodyScript == null)
            return false;

        if (instance != null && instance.localRespawnPending) return false;
        var body = player.bodyScript;
        if (!body.gameObject.activeInHierarchy || body.limbs == null || body.limbs.Count < 15 ||
            body.limbs[0] == null || body.limbs[11] == null || body.limbs[14] == null ||
            GameManager.main == null || ResourceManager.main == null || ScreenFXManager.main == null)
            return false;
        if (MultiplayerSession.IsConnected) body.dropWeapon = false;

        var buttons = player.buttons;
        if (buttons != null)
            foreach (var button in buttons)
                if (button == null)
                {
                    player.buttons = null;
                    break;
                }
        return true;
    }

    internal static bool ReplaceLocalPlayerBody(BodyScript oldBody, BodyScript newBody)
    {
        var player = PlayerScript.player;
        if (instance == null || player == null || newBody == null ||
            newBody.limbs == null || newBody.limbs.Count == 0)
            return false;

        instance.localRespawnGeneration++;
        instance.respawnAt = -1f;
        instance.localRespawnPending = false;
        instance.localWasAlive = true;
        instance.localDeathPosition = newBody.transform.position;
        localRespawnProtectionUntil = Time.unscaledTime + RespawnProtectionSeconds;

        if (oldBody != null)
        {
            oldBody.OnWeaponChanged.RemoveListener(player.BodyWeaponChanged);
            oldBody.OnDeath.RemoveListener(player.OnDied);
            oldBody.OnAmmoChanged.RemoveListener(player.BodyAmmoChanged);
        }

        EnsureRespawnWeaponSlots(newBody);
        newBody.isPlayer = true;
        newBody.team = "goodguys";
        newBody.crateDamage = true;
        newBody.healthRegen = newBody.regenOnSwap;
        newBody.isWalking = false;
        newBody.isAlive = true;
        newBody.health = Mathf.Max(1f, newBody.maxHealth);
        newBody.dropWeapon = false;
        newBody.CurrentState = 0;
        newBody.controlState = 0;
        newBody.noLegs = false;
        newBody.deHeaded = false;
        newBody.onScreen = true;
        foreach (LimbScript limb in newBody.limbs)
        { // Restore limbs health and regenration to merchant's level
            if (null == limb || null == limb.passer || null == limb.passer.relevantDismember)
                continue;
            limb.passer.relevantDismember.damageFall = 4f;
            limb.passer.relevantDismember.currentDamage = 54f;
        }
        newBody.EnterFullControl();
        newBody.WakeUp();

        var levitator = newBody.GetComponent<LevitatorScript>();
        if (levitator == null) levitator = newBody.gameObject.AddComponent<LevitatorScript>();
        levitator.levitMask = LayerMask.GetMask("Ground");
        levitator.grabMask = LayerMask.GetMask("Default", "Ground", "Entity", "EntityStand", "DropWeapon");
        levitator.rb = newBody.rb;
        levitator.refBody = newBody;
        var weaponBack = newBody.GetComponent<WeaponBackShow>();
        if (weaponBack != null) weaponBack.active = true;

        player.bodyScript = newBody;
        if (player.bloodBars != null) player.bloodBars.body = newBody;
        player.levit = levitator;
        player.enabled = true;
        localPlayerInstance = player;
        localGlobalBody = newBody.transform;
        RestoreLocalPlayerSingleton();
        EnsurePlayerAmmoDisplaySlots(player);
        newBody.OnWeaponChanged.AddListener(player.BodyWeaponChanged);
        newBody.OnDeath.AddListener(player.OnDied);
        newBody.OnAmmoChanged.AddListener(player.BodyAmmoChanged);
        player.BodyWeaponChanged();
        player.BodyAmmoChanged();
        player.UnDie();
        if (CameraFollow.cam != null) CameraFollow.cam.target = newBody.transform;
        return true;
    }

    internal static void RecordDamageSource(BodyScript victim)
    {
        if (victim == null) return;
        if (victim.isAlive) announcedDeaths.Remove(victim.GetInstanceID());
        var explosion = activeShotState != null && activeShotState.IsExplosion;
        if (explosion) RecordEnvironmentalDeathCause(victim, PlayerDeathCause.Explosion);
        if (applyingNetworkPlayerDamage || currentShooter == null || currentShooter == victim) return;
        SetDamageSource(victim, currentShooter,
            explosion ? WeaponName(currentShooter.weapon) : ActiveWeaponName(currentShooter));
    }

    internal static void TryCreateLocalKillBloodSplat(BodyScript victim)
    {
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost || victim == null || victim.isRobot ||
            player == null || player.bodyScript == null || currentShooter != player.bodyScript ||
            victim == player.bodyScript || victim.health > 0f || CameraFollow.cam == null ||
            CameraFollow.cam.DistanceFromCam(victim.transform.position) >= 4f) return;
        var id = victim.GetInstanceID();
        float previous;
        if (localKillBloodTimes.TryGetValue(id, out previous) && Time.unscaledTime - previous < 0.5f) return;
        localKillBloodTimes[id] = Time.unscaledTime;
        CameraFollow.cam.CreateBloodSplat(victim.transform.position, victim.bloodColor);
    }

    internal static void RecordDamageSource(BodyScript victim, BodyScript source)
    {
        if (victim == null) return;
        if (victim.isAlive) announcedDeaths.Remove(victim.GetInstanceID());
        if (source != null && source != victim) SetDamageSource(victim, source, WeaponName(source.weapon));
    }

    internal static BodyScript DamageSourceFor(BodyScript victim)
    {
        if (victim == null) return null;
        BodyScript source;
        return lastDamageSources.TryGetValue(victim.GetInstanceID(), out source) ? source : null;
    }

    internal static string DamageWeaponFor(BodyScript victim)
    {
        if (victim == null) return "";
        string weapon;
        return lastDamageWeapons.TryGetValue(victim.GetInstanceID(), out weapon) ? weapon : "";
    }

    internal static string DamageSourceNameFor(BodyScript victim)
    {
        if (victim == null) return "";
        string name;
        return lastDamageSourceNames.TryGetValue(victim.GetInstanceID(), out name) ? name : "";
    }

    internal static ushort DamageSourcePeerIdFor(BodyScript victim)
    {
        if (victim == null) return 0;
        ushort peerId;
        return lastDamageSourcePeerIds.TryGetValue(victim.GetInstanceID(), out peerId) ? peerId : (ushort)0;
    }

    private static void SetDamageSource(BodyScript victim, BodyScript source, string weaponName)
    {
        var id = victim.GetInstanceID();
        lastDamageSources[id] = source;
        lastDamageSourceNames.Remove(id);
        var player = PlayerScript.player;
        var peerId = source == player?.bodyScript ? MultiplayerSession.LocalPeerId :
            NetworkAvatarRegistry.ReplicaForBody(source)?.remotePeerId ?? 0;
        if (peerId == 0) lastDamageSourcePeerIds.Remove(id);
        else lastDamageSourcePeerIds[id] = peerId;
        lastDamageSourceTimes[id] = Time.unscaledTime;
        if (string.IsNullOrEmpty(weaponName)) lastDamageWeapons.Remove(id);
        else lastDamageWeapons[id] = weaponName;
    }

    private static void SetDamageSourceName(BodyScript victim, string sourceName, string weaponName)
    {
        if (victim == null) return;
        var id = victim.GetInstanceID();
        lastDamageSources.Remove(id);
        lastDamageSourcePeerIds.Remove(id);
        if (string.IsNullOrWhiteSpace(sourceName)) lastDamageSourceNames.Remove(id);
        else lastDamageSourceNames[id] = sourceName.Trim();
        lastDamageSourceTimes[id] = Time.unscaledTime;
        if (string.IsNullOrEmpty(weaponName)) lastDamageWeapons.Remove(id);
        else lastDamageWeapons[id] = weaponName;
    }

    private static void ClearDamageSource(BodyScript victim)
    {
        if (victim == null) return;
        var id = victim.GetInstanceID();
        lastDamageSources.Remove(id);
        lastDamageSourceNames.Remove(id);
        lastDamageSourcePeerIds.Remove(id);
        lastDamageWeapons.Remove(id);
        lastDamageSourceTimes.Remove(id);
    }

    private static string ActiveWeaponName(BodyScript source)
    {
        return activeShotState != null && activeShotState.Weapon != null &&
            activeShotState.Weapon.body == source ? WeaponName(activeShotState.Weapon) : "";
    }

    private static string WeaponName(WeaponScript weapon)
    {
        return weapon == null || weapon.stats == null || string.IsNullOrWhiteSpace(weapon.stats.name)
            ? "" : weapon.stats.name.Replace("(Clone)", "").Trim();
    }

    internal static void CaptureDeathCause(BodyScript body)
    {
        if (body == null) return;
        var id = body.GetInstanceID();
        float damageTime;
        if (!lastDamageSourceTimes.TryGetValue(id, out damageTime) ||
            Time.unscaledTime - damageTime > 0.25f)
        {
            lastDamageSources.Remove(id);
            lastDamageSourceNames.Remove(id);
            lastDamageSourcePeerIds.Remove(id);
            lastDamageWeapons.Remove(id);
            lastDamageSourceTimes.Remove(id);
        }
        PlayerDeathCause cause;
        float environmentalTime;
        if (environmentalDeathCauses.TryGetValue(id, out cause) &&
            environmentalDeathCauseTimes.TryGetValue(id, out environmentalTime) &&
            Time.unscaledTime - environmentalTime <= 0.5f) { }
        else
        {
            environmentalDeathCauses.Remove(id);
            environmentalDeathCauseTimes.Remove(id);
            cause = PlayerDeathCause.Unknown;
            if (body.burnIntensity > 0.01f) cause = PlayerDeathCause.Fire;
            else if (body.oxygen <= 0.01f && body.headInWater) cause = PlayerDeathCause.Drowning;
            else if (body.oxygen <= 0.01f && body.forcedOxyLoss > 0) cause = PlayerDeathCause.Suffocation;
            else if (body.fallDamageCooldown > 0f) cause = PlayerDeathCause.Fall;
        }
        deathCauses[id] = cause;
    }

    internal static void RecordSawDamage(SawScript saw, Collision2D collision)
    {
        if (saw == null || collision == null) return;
        var limb = collision.gameObject.GetComponent<LimbScript>();
        var body = limb == null ? collision.gameObject.GetComponent<BodyScript>() : limb.body;
        RecordEnvironmentalDeathCause(body, IsHotPlate(saw) ? PlayerDeathCause.HotPlate : PlayerDeathCause.Saw);
    }

    private static bool IsHotPlate(SawScript saw)
    {
        for (var current = saw == null ? null : saw.transform; current != null; current = current.parent)
        {
            var name = current.name;
            if (name.IndexOf("hotplate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (name.IndexOf("hot", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 name.IndexOf("plate", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
        }
        return false;
    }

    internal static void RecordAcidDamage(WaterScript water, Collider2D collision)
    {
        if (water == null || water.damagePerSecond <= 0f || collision == null) return;
        var limb = collision.GetComponent<LimbScript>();
        RecordEnvironmentalDeathCause(limb == null ? null : limb.body, PlayerDeathCause.Acid);
    }

    internal static void RecordIncineratorDamage(Incinerator incinerator, Collider2D collision)
    {
        if (incinerator == null || collision == null) return;
        var body = collision.GetComponent<BodyScript>();
        if (body == null)
        {
            var limb = collision.GetComponent<LimbScript>();
            body = limb == null ? null : limb.body;
        }
        RecordEnvironmentalDeathCause(body, PlayerDeathCause.Incinerator);
    }

    internal static bool HandleClientRestart()
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        KillLocalPlayer(PlayerDeathCause.SelfKill);
        return false;
    }

    internal static bool KillLocalPlayer(PlayerDeathCause cause)
    {
        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        if (body == null || !body.isAlive) return false;
        RecordEnvironmentalDeathCause(body, cause);
        body.Death();
        return true;
    }

    public static void RecordEnvironmentalDeathCause(BodyScript body, PlayerDeathCause cause)
    {
        if (body == null) return;
        var id = body.GetInstanceID();
        environmentalDeathCauses[id] = cause;
        environmentalDeathCauseTimes[id] = Time.unscaledTime;
        lastDamageSources.Remove(id);
        lastDamageSourceNames.Remove(id);
        lastDamageWeapons.Remove(id);
        lastDamageSourceTimes.Remove(id);
    }

    internal static PlayerDeathCause DeathCauseFor(BodyScript body)
    {
        if (body == null) return PlayerDeathCause.Unknown;
        PlayerDeathCause cause;
        return deathCauses.TryGetValue(body.GetInstanceID(), out cause) ? cause : PlayerDeathCause.Unknown;
    }

    internal static void RouteNpcKillScreenEffect(BodyScript victim)
    {
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || victim == null ||
            victim.isPlayer || !victim.isAlive) return;
        var killer = DamageSourceFor(victim);
        var replica = NetworkAvatarRegistry.ReplicaForBody(killer);
        if (replica == null || replica.remotePeerId == 0) return;

        MultiplayerSession.Send(new KillScreenEffectPacket(), replica.remotePeerId);
        suppressNpcKillEffectFor = victim;
    }

    internal static void RoutePlayerKillScreenEffect(ushort killerPeerId)
    {
        if (!MultiplayerSession.IsHosting || killerPeerId == 0) return;
        if (killerPeerId == MultiplayerSession.LocalPeerId)
        {
            PlayKillScreenEffect();
            return;
        }
        MultiplayerSession.Send(new KillScreenEffectPacket(), killerPeerId);
    }

    internal static void PlayKillScreenEffect()
    {
        if (ScreenFXManager.main != null) ScreenFXManager.main.OnKill(true);
    }

    internal static bool AllowNpcKillScreenEffect()
    {
        if (suppressNpcKillEffectFor == null) return true;
        suppressNpcKillEffectFor = null;
        return false;
    }

    internal static void EndNpcKillScreenEffect(BodyScript victim)
    {
        if (suppressNpcKillEffectFor == victim) suppressNpcKillEffectFor = null;
    }

    internal static bool BeginDeathAnnouncement(BodyScript victim)
    {
        if (victim == null) return false;
        var id = victim.GetInstanceID();
        if (victim.isAlive)
        {
            announcedDeaths.Remove(id);
            deathCauses.Remove(id);
            lastDamageSources.Remove(id);
            lastDamageSourceNames.Remove(id);
            lastDamageWeapons.Remove(id);
            lastDamageSourceTimes.Remove(id);
            environmentalDeathCauses.Remove(id);
            environmentalDeathCauseTimes.Remove(id);
            return false;
        }
        return announcedDeaths.Add(id);
    }

    internal static string RemoteNameTag(BodyScript body)
    {
        var replica = NetworkAvatarRegistry.ReplicaForBody(body);
        if (replica == null) return "Player";
        var ping = MultiplayerSession.PingMs;
        ping = MultiplayerSession.PeerPing(replica.remotePeerId);
        var label = replica.remoteName + " [" + (ping < 0 ? "-" : ping.ToString()) + "]";
        if (!body.isAlive) return "DEAD " + label;
        if (!body.IsConsc()) return "unconscious " + label;
        return label;
    }

    internal static bool SuppressLocalShotScreenCrack()
    {
        var player = PlayerScript.player;
        return suppressedTargetScreenEffects > 0 ||
            Time.unscaledTime < suppressedCameraUntil ||
            (MultiplayerSession.IsConnected && MultiplayerSession.PvpEnabled &&
            player != null && player.bodyScript != null && currentShooter == player.bodyScript);
    }

    internal static bool SuppressTargetedScreenEffect()
    {
        return suppressedTargetScreenEffects > 0 || Time.unscaledTime < suppressedCameraUntil;
    }

    internal static TargetScreenEffectState BeginTargetScreenEffect(BodyScript target)
    {
        var state = new TargetScreenEffectState();
        var localPlayer = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || target == null || localPlayer == null ||
            localPlayer.bodyScript == null || target == localPlayer.bodyScript)
            return state;
        state.Suppress = true;
        suppressedTargetScreenEffects++;
        if (CameraFollow.cam != null) state.ScreenShake = CameraFollow.cam.screenShakeAmount;
        return state;
    }

    internal static void EndTargetScreenEffect(TargetScreenEffectState state)
    {
        if (state == null || !state.Suppress) return;
        if (CameraFollow.cam != null) CameraFollow.cam.screenShakeAmount = state.ScreenShake;
        suppressedCameraUntil = Time.unscaledTime + 0.35f;
        if (suppressedTargetScreenEffects > 0) suppressedTargetScreenEffects--;
    }

    internal static void ClearSuppressedCameraShake(CameraFollow camera)
    {
        if (camera == null || (suppressedTargetScreenEffects <= 0 &&
            Time.unscaledTime >= suppressedCameraUntil)) return;
        camera.screenShakeAmount = 0f;
    }

    private void Awake()
    {
        if (instance != null)
        {
            coordinator = false;
            return;
        }
        instance = this;
        coordinator = true;
    }

    internal void Configure(string name)
    {
        if (!coordinator) return;
        localName = string.IsNullOrEmpty(name) ? "Player" : name;
    }

    private static float CurrentSnapshotInterval()
    {
        return SnapshotInterval;
    }

    private void Update()
    {
        if (!coordinator)
        {
            TickRemote();
            return;
        }
        var performanceStarted = MultiplayerPerformance.Start();
        try
        {
        if (!MultiplayerSession.IsHosting && !MultiplayerSession.IsConnected)
        {
            NetworkAvatarRegistry.DestroyAllReplicas();
            return;
        }
        NetworkAvatarRegistry.CleanupDisconnectedReplicas();
        if (!MultiplayerSession.IsConnected) return;

        EnsureLocalPlayerSingleton();
        MultiplayerSession.UpdatePing();
        var player = PlayerScript.player;
        if (player == null || player.bodyScript == null) return;
        localPlayerInstance = player;
        localGlobalBody = PlayerScript.globalBody == null
            ? player.bodyScript.transform : PlayerScript.globalBody;
        ApplyInitialLobbyScale(player.bodyScript);
        ApplyPendingRespawnLobbyLoadout(player.bodyScript);
        ApplyStartingLobbyLoadout(player.bodyScript);
        ApplyStartingLobbyAmmo(player.bodyScript);
        UpdateLocalRespawn(player);
        player = PlayerScript.player;
        if (player == null || player.bodyScript == null) return;

        ushort senderId;
        PlayerTeleportPacket playerTeleport;
        while (MultiplayerSession.TryTakePlayerTeleport(out senderId, out playerTeleport))
            ApplyRemoteTeleport(player.bodyScript, playerTeleport);

        VehicleEjectPacket vehicleEject;
        while (MultiplayerSession.TryTakeVehicleEject(out senderId, out vehicleEject))
            ApplyVehicleEject(player.bodyScript);

        VehicleImpactPacket vehicleImpact;
        while (MultiplayerSession.TryTakeVehicleImpact(out senderId, out vehicleImpact))
            ApplyVehicleImpact(player.bodyScript, vehicleImpact);

        TeleportRequestPacket teleportRequest;
        while (MultiplayerSession.TryTakeTeleportRequest(out senderId, out teleportRequest))
            HandleTeleportRequest(senderId, teleportRequest);

        PlayerDamagePacket playerDamage;
        while (MultiplayerSession.TryTakePlayerDamage(out senderId, out playerDamage))
        {
            if (playerDamage.HasPlayerSource)
                RecordNetworkPlayerDamageSource(player.bodyScript, playerDamage);
            else
                SetDamageSourceName(player.bodyScript, playerDamage.SourceName, playerDamage.SourceWeapon);
            ApplyPlayerDamage(player.bodyScript, playerDamage);
        }
        PlayerDamagePacket pvpDamage;
        while (MultiplayerSession.TryTakePvpDamage(out senderId, out pvpDamage))
            ApplyPvpDamage(player.bodyScript, senderId, pvpDamage);
        ShotVisualPacket shotVisual;
        while (MultiplayerSession.TryTakeShotVisual(out senderId, out shotVisual))
        {
            var shooter = NetworkAvatarRegistry.GetOrCreateReplica(senderId);
            if (shooter != null) shooter.PlayRemoteShot(shotVisual);
        }
        ProjectileImpactPacket projectileImpact;
        while (MultiplayerSession.TryTakeProjectileImpact(out senderId, out projectileImpact))
        {
            var shooter = NetworkAvatarRegistry.GetOrCreateReplica(senderId);
            if (shooter != null) shooter.PlayRemoteProjectileImpact(projectileImpact);
        }
        VelvetWebPacket velvetWeb;
        while (MultiplayerSession.TryTakeVelvetWeb(out senderId, out velvetWeb))
        {
            var shooter = NetworkAvatarRegistry.GetOrCreateReplica(senderId);
            if (shooter != null) shooter.PlayRemoteVelvetWeb(velvetWeb);
        }
        PlayerGrabPacket playerGrab;
        while (MultiplayerSession.TryTakePlayerGrab(out senderId, out playerGrab))
            ReceivePlayerGrab(playerGrab);
        UpdateLocalRespawn(player);
        player = PlayerScript.player;
        if (player == null) return;
        if (player.bodyScript == null) return;
        UpdateSpectator(player);
        colorEffects.Update(player.bodyScript);

        var serverOnlyHost = GunsawMultiplayerPlugin.IsHeadlessServer;
        var prefab = ResolveLocalCharacterPrefab(player.bodyScript);
        var currentIdentity = localName + "\n" + prefab;
        if (!serverOnlyHost && (identitySent != currentIdentity || Time.unscaledTime >= nextIdentity))
        {
            identitySent = currentIdentity;
            nextIdentity = Time.unscaledTime + 2f;
            MultiplayerSession.Send(new IdentityPacket(localName, prefab));
        }

        string identity;
        while (MultiplayerSession.TryTakeIdentity(out senderId, out identity))
        {
            var replica = NetworkAvatarRegistry.GetOrCreateReplica(senderId);
            if (replica != null) replica.CreateRemote(identity, player.bodyScript);
        }

        if (!serverOnlyHost && Time.unscaledTime >= nextSnapshot)
        {
            nextSnapshot = Time.unscaledTime + CurrentSnapshotInterval();
            MultiplayerSession.Send(Serialize(PacketSequences.NextPlayerSnapshot(), player.bodyScript));
        }

        PlayerSnapshotPacket snapshot;
        while (MultiplayerSession.TryTakeSnapshot(out senderId, out snapshot))
        {
            NetworkAvatarReplication replica;
            if (NetworkAvatarRegistry.replicas.TryGetValue(senderId, out replica) && replica != null) replica.Apply(snapshot);
        }
        }
        finally
        {
            MultiplayerPerformance.AddAvatar(performanceStarted);
        }
    }

    private void TickRemote()
    {
        if (!MultiplayerSession.HasPeer(remotePeerId))
        {
            if (remoteAvatar != null) DestroyRemote();
            return;
        }
        if (remoteAvatar == null) return;
        if (PlayerCarrySystem.MustLockRemoteCarryPose(remoteBody))
        {
            targets.Clear();
            worldTargets.Clear();
            localTargets.Clear();
            return;
        }
        UpdateRemotePhysicsMode();
        VoyagerBody.UpdatePvpVoyagerVisuals(remoteBody, Time.deltaTime);
        foreach (var pair in targets)
        {
            var body = pair.Key;
            var target = pair.Value;
            if (body == null) continue;
            var alpha = Mathf.Clamp01((Time.unscaledTime - target.startedAt) /
                Mathf.Max(0.001f, target.duration));
            body.transform.position = Vector3.Lerp(target.fromPosition, target.position, alpha);
            body.transform.rotation = Quaternion.Lerp(target.fromRotation, target.rotation, alpha);
        }

        staleWorldTargets.Clear();
        orderedWorldTargets.Clear();
        foreach (var pair in worldTargets) orderedWorldTargets.Add(pair);
        orderedWorldTargets.Sort((left, right) => TransformDepth(left.Key).CompareTo(TransformDepth(right.Key)));
        foreach (var pair in orderedWorldTargets)
        {
            var transform = pair.Key;
            if (transform == null) { staleWorldTargets.Add(transform); continue; }
            var progress = Mathf.Clamp01((Time.unscaledTime - pair.Value.startedAt) / pair.Value.duration);
            transform.position = Vector3.Lerp(pair.Value.fromPosition, pair.Value.position, progress);
            transform.rotation = Quaternion.Slerp(pair.Value.fromRotation, pair.Value.rotation, progress);
        }
        foreach (var transform in staleWorldTargets) worldTargets.Remove(transform);

        staleWorldTargets.Clear();
        foreach (var pair in localTargets)
        {
            var transform = pair.Key;
            if (transform == null) { staleWorldTargets.Add(transform); continue; }
            var progress = Mathf.Clamp01((Time.unscaledTime - pair.Value.startedAt) / pair.Value.duration);
            transform.localPosition = Vector3.Lerp(pair.Value.fromPosition, pair.Value.position, progress);
            transform.localRotation = Quaternion.Slerp(pair.Value.fromRotation, pair.Value.rotation, progress);
        }
        foreach (var transform in staleWorldTargets) localTargets.Remove(transform);
        MaintainRemoteVehicleAttachment();
    }

    private void LateUpdate()
    {
        if (coordinator)
            UpdateLocalVehicleLock();
        
        if (!MultiplayerSession.IsConnected || remoteAvatar == null)
            return;
        
        if (remoteBody != null && remoteBody.inVehicle)
        {
            MaintainRemoteVehiclePose();
            ApplyVehicleArmsTarget();
            SnapRemoteVehicleArmLimbs();
            
            if (hasRemoteVehicleReflection)
                ApplyVehicleReflection();

            if (hasRemoteVehicleHeadRotation)
                ApplyVehicleHeadRotation();
        
            ApplyVehicleTailTargets();  
        }
    }

    private void FixedUpdate()
    {
        if (coordinator) ApplyIncomingGrab();
    }

    private void OnDestroy()
    {
        colorEffects.Restore();
        if (coordinator)
        {
            RestoreLocalVehiclePhysics();
            return;
        }
        
        NetworkAvatarReplication current;
        if (NetworkAvatarRegistry.replicas.TryGetValue(remotePeerId, out current) && current == this)
            NetworkAvatarRegistry.replicas.Remove(remotePeerId);
    }

    private void CreateRemote(string identity, BodyScript localBody)
    {
        var split = identity.IndexOf('\n');
        if (split < 1) return;
        var name = identity.Substring(0, split);
        var prefabPath = identity.Substring(split + 1);
        var sanitizedName = SanitizePlayerName(name);
        if (remoteBody != null && remotePrefabPath == prefabPath)
        {
            remoteName = sanitizedName;
            return;
        }
        var prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError("[Gunsaw MP] Remote character prefab not found: " + prefabPath);
            return;
        }

        DestroyRemote();
        remoteName = sanitizedName;
        GameObject avatar = null;
        remoteAvatarCreationDepth++;
        try
        {
            avatar = Instantiate(prefab, localBody.transform.position + new Vector3(2f, 0f, 0f), Quaternion.identity);
            foreach (var remotePlayer in avatar.GetComponentsInChildren<PlayerScript>(true))
                DestroyImmediate(remotePlayer);
            RestoreLocalPlayerSingleton();
            avatar.AddComponent<NetworkReplica>();
        }
        finally
        {
            remoteAvatarCreationDepth--;
        }
        remoteAvatar = avatar;
        remoteAvatarParent = avatar.transform.parent;
        remotePrefabPath = prefabPath;
        remoteBody = avatar.GetComponentInChildren<BodyScript>();
        if (remoteBody == null) { Destroy(avatar); return; }
        RemoveReplicaScarfArtifacts(avatar);
        remoteBody.WakeUp();
        remoteBody.isPlayer = true;
        remoteBody.dropWeapon = false;
        remoteBody.team = RemoteTeam(localBody);
        foreach (var chatter in avatar.GetComponentsInChildren<Chatter>(true)) DestroyImmediate(chatter);
        foreach (var ai in avatar.GetComponentsInChildren<AIScript>(true)) DestroyImmediate(ai);
        foreach (var collider in avatar.GetComponentsInChildren<Collider2D>(true))
            remoteColliderTriggers[collider] = collider.isTrigger;
        remoteRigidbodies = avatar.GetComponentsInChildren<Rigidbody2D>(true);
        remotePhysicsModeKnown = false;
      
        remoteTailBases.Clear();
        remoteTailBases.AddRange(GetNetworkTailBodies(remoteBody));

        remoteTails = GetTransforms(remoteBody, "tails");
        remoteTailSprites.Clear();
        foreach (var tailBase in remoteTailBases)
            remoteTailSprites.Add(tailBase == null ? new SpriteRenderer[0] :
                tailBase.GetComponentsInChildren<SpriteRenderer>(true));
        remoteTailRootSprites.Clear();
        foreach (var tail in remoteTails)
            remoteTailRootSprites.Add(tail == null ? new SpriteRenderer[0] :
                tail.GetComponentsInChildren<SpriteRenderer>(true));
        foreach (var behaviour in avatar.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
        foreach (var animator in avatar.GetComponentsInChildren<Animator>()) animator.enabled = false;
        var remoteCrystalTongue = avatar.GetComponentInChildren<CrystalTongue>(true);
        remoteCrystalTongueLine = remoteCrystalTongue == null ? null : remoteCrystalTongue.line;
        if (remoteCrystalTongueLine != null) remoteCrystalTongueLine.enabled = false;
        UpdateRemotePhysicsMode();
        CacheDismembermentVisuals();
        CreateRemoteLevitLine(avatar.transform);
        Debug.Log("[Gunsaw MP] Spawned remote avatar for " + name + ".");
    }

    internal void DestroyRemote()
    {
        if (remoteAvatar != null)
        {
            foreach (var remotePlayer in remoteAvatar.GetComponentsInChildren<PlayerScript>(true))
                DestroyImmediate(remotePlayer);
            Destroy(remoteAvatar);
            RestoreLocalPlayerSingleton();
        }
        remoteAvatar = null;
        remoteAvatarParent = null;
        remoteBody = null;
        remotePrefabPath = "";
        remoteName = "Player";
        remoteLevitLine = null;
        remoteCrystalTongueLine = null;
        remoteScarf = null;
        remoteScarfHold = null;
        remoteFires.Clear();
        remoteRigidbodies = new Rigidbody2D[0];
        remotePhysicsModeKnown = false;
        remoteTailBases.Clear();
        remoteTails = new Transform[0];
        remoteTailSprites.Clear();
        remoteTailRootSprites.Clear();
        remoteColliderTriggers.Clear();
        collisionRuleLocalBody = null;
        collisionRuleApplied = false;
        originalDismemberSprites.Clear();
        targets.Clear();
        worldTargets.Clear();
        localTargets.Clear();
        receivedFirstSnapshot = false;
        appliedWeapon = -1;
        appliedWeaponSprite = 0UL;
        appliedInventory = "";
        lastSerializedInventory = "";
        nextFullInventory = 0f;
        remoteDeathDropSpawned = false;
        appliedDismembermentHash = int.MinValue;
        pendingRemoteDamage = 0f;
        remoteCanBeGrabbed = false;
        incomingGrabUntil = 0f;
        hasRemoteScaleBeforeVehicle = false;
    }

    private static void RestoreLocalPlayerSingleton()
    {
        PlayerScript.player = localPlayerInstance;
        PlayerScript.globalBody = localGlobalBody;
    }

    internal static void CaptureCharacterMenu(MainMenuManager menu)
    {
        if (menu == null) return;
        var characters = menu.characters;
        if (characters == null) return;

        for (var index = 0; index < characters.Count; index++)
        {
            var path = characters[index] == null ? null : characters[index].prefabPath;
            if (!string.IsNullOrEmpty(path) && !knownCharacterPrefabs.Contains(path))
                knownCharacterPrefabs.Add(path);
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrWhiteSpace(characters[index].name))
                characterDisplayNames[path] = characters[index].name.Trim();
        }

        var selectedIndex = menu.charIndex;
        if (selectedIndex >= 0 && selectedIndex < characters.Count)
        {
            var path = characters[selectedIndex] == null ? null : characters[selectedIndex].prefabPath;
            if (!string.IsNullOrEmpty(path)) selectedCharacterPrefab = path;
        }
    }

    internal static void RestoreCharacterSelection()
    {
        if (!string.IsNullOrEmpty(selectedCharacterPrefab))
            PlayerPrefs.SetString("charPrefab", selectedCharacterPrefab);
    }

    private static string ResolveCharacterPrefab(BodyScript body)
    {
        var fallback = string.IsNullOrEmpty(selectedCharacterPrefab)
            ? PlayerPrefs.GetString("charPrefab")
            : selectedCharacterPrefab;
        var bestPath = "";
        var bestScore = -1;
        var currentRootName = CleanCloneName(body.transform.root.name);

        var paths = new List<string>(knownCharacterPrefabs);
        if (!string.IsNullOrEmpty(fallback) && !paths.Contains(fallback)) paths.Add(fallback);
        foreach (var path in paths)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) continue;
            var prefabBody = prefab.GetComponentInChildren<BodyScript>(true);
            if (prefabBody == null) continue;
            var score = path == fallback ? 1 : 0;
            if (prefabBody.characterName == body.characterName) score += 100;
            if (prefabBody.speciesName == body.speciesName) score += 10;
            if (CleanCloneName(prefab.name) == currentRootName) score += 200;
            if (score <= bestScore) continue;
            bestScore = score;
            bestPath = path;
        }
        return string.IsNullOrEmpty(bestPath) ? fallback : bestPath;
    }

    private string ResolveLocalCharacterPrefab(BodyScript body)
    {
        if (body == null) return "";
        if (IsProtogenBody(body)) return ProtogenPrefabPath;
        var fallback = string.IsNullOrEmpty(selectedCharacterPrefab)
            ? (cachedCharacterPrefabPreference ??= PlayerPrefs.GetString("charPrefab"))
            : selectedCharacterPrefab;
        var characterName = body.characterName ?? "";
        var speciesName = body.speciesName ?? "";
        var rootName = CleanCloneName(body.transform.root.name);
        if (identityBody == body && identityCharacterName == characterName &&
            identitySpeciesName == speciesName && identityRootName == rootName &&
            identityFallback == fallback)
            return resolvedIdentityPrefab;
        identityBody = body;
        identityCharacterName = characterName;
        identitySpeciesName = speciesName;
        identityRootName = rootName;
        identityFallback = fallback;
        resolvedIdentityPrefab = ResolveCharacterPrefab(body);
        return resolvedIdentityPrefab;
    }

    private static bool IsProtogenBody(BodyScript body)
    {
        var root = body == null || body.transform.root == null ? "" :
            CleanCloneName(body.transform.root.name);
        return string.Equals(root, "RobotEnemy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(body.characterName, "G4", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanCloneName(string name)
    {
        const string suffix = "(Clone)";
        if (name != null && name.EndsWith(suffix, StringComparison.Ordinal))
            return name.Substring(0, name.Length - suffix.Length).Trim();
        return name == null ? "" : name.Trim();
    }

    private static string SanitizePlayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Player";
        name = name.Replace("<", "").Replace(">", "").Replace("\r", " ").Replace("\n", " ").Trim();
        return name.Length > 32 ? name.Substring(0, 32) : name;
    }


    private void CreateRemoteLevitLine(Transform avatar)
    {
        var player = PlayerScript.player;
        var source = player == null ? null : player.levitLine;
        if (source == null) return;
        var beam = new GameObject("MP Levit Beam");
        beam.transform.SetParent(avatar.root, true);
        remoteLevitLine = beam.AddComponent<LineRenderer>();
        remoteLevitLine.sharedMaterial = source.sharedMaterial;
        remoteLevitLine.widthMultiplier = source.widthMultiplier;
        remoteLevitLine.startWidth = source.startWidth;
        remoteLevitLine.endWidth = source.endWidth;
        remoteLevitLine.startColor = source.startColor;
        remoteLevitLine.endColor = source.endColor;
        remoteLevitLine.useWorldSpace = source.useWorldSpace;
        remoteLevitLine.textureMode = source.textureMode;
        remoteLevitLine.alignment = source.alignment;
        remoteLevitLine.numCapVertices = source.numCapVertices;
        remoteLevitLine.numCornerVertices = source.numCornerVertices;
        remoteLevitLine.sortingLayerID = source.sortingLayerID;
        remoteLevitLine.sortingOrder = source.sortingOrder;
        beam.SetActive(false);
    }

    private PlayerSnapshotPacket Serialize(int sequence, BodyScript body)
    {
        var performanceStarted = MultiplayerPerformance.Start();
        localVisualLayout = GetVisualLayout(localVisualLayout, body.transform);
        var visualState = SerializeVisualState(localVisualLayout);
        var visualChanged = !PlayerVisualState.Equals(lastSerializedVisualState, visualState);
        if (visualChanged)
        {
            lastSerializedVisualState = visualState;
            if (!HasActiveColorEffect) visualResendUntil = Time.unscaledTime + 1f;
        }
        var includeVisualState = visualChanged || (!HasActiveColorEffect && Time.unscaledTime < visualResendUntil) ||
            Time.unscaledTime >= nextFullVisualSnapshot;
        if (includeVisualState && Time.unscaledTime >= nextFullVisualSnapshot)
            nextFullVisualSnapshot = Time.unscaledTime + 1f;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            var breakdown = new AvatarWireBreakdown();
            var sectionStarted = writer.BaseStream.Position;
            var vehicleId = body.inVehicle && body.curVehicle != null && GunsawMultiplayerPlugin.World != null
                ? GunsawMultiplayerPlugin.World.VehicleWireId(body.curVehicle) : 0UL;
            var inVehicle = vehicleId != 0UL;
            var isVehicleDriver = inVehicle && body.curVehicle.occupant == body;
            var isReflected = body.transform.localScale.x < 0f;
            var isActive = body.transform.root.gameObject.activeInHierarchy;
            writer.Write(inVehicle);
            writer.Write(vehicleId);
            writer.Write(isVehicleDriver);
            writer.Write((byte)body.CurrentState);
            writer.Write(body.isRight);
            writer.Write(isReflected);
            writer.Write(isActive);
            var headReferenceRotation = vehicleId != 0UL && body.curVehicle.mainPart != null &&
                body.curVehicle.mainPart.rb != null ? body.curVehicle.mainPart.rb.rotation : body.rb.rotation;
            var headRotation = body.headTransform == null ? 0f :
                Mathf.DeltaAngle(headReferenceRotation, body.headTransform.eulerAngles.z);
            writer.Write(headRotation);
            WriteBody(writer, body.rb);
            breakdown.Core += (int)(writer.BaseStream.Position - sectionStarted);

            sectionStarted = writer.BaseStream.Position;
            var limbs = GetList(body, "limbs");
            var limbStates = new PlayerSnapshotLimbState[limbs.Count];
            writer.Write((ushort)limbs.Count);
            var limbIndex = 0;
            foreach (LimbScript limb in limbs)
            {
                var limbBody = limb.rb == null ? new PlayerSnapshotBodyState(0f, 0f, 0f) :
                    new PlayerSnapshotBodyState(limb.rb.position.x, limb.rb.position.y, limb.rb.rotation);
                var dismembered = limb.dismembered;
                var burning = IsBurning(limb);
                writer.Write(limbBody.X);
                writer.Write(limbBody.Y);
                writer.Write(limbBody.Rotation);
                writer.Write(dismembered);
                writer.Write(burning);
                limbStates[limbIndex++] = new PlayerSnapshotLimbState(limbBody, dismembered, burning);
            }
            breakdown.Limbs += (int)(writer.BaseStream.Position - sectionStarted);

            sectionStarted = writer.BaseStream.Position;
            var tailBases = GetNetworkTailBodies(body);
            var tailBaseStates = new PlayerSnapshotTailState[tailBases.Count];
            writer.Write((ushort)tailBases.Count);
            var tailBaseIndex = 0;
            foreach (Rigidbody2D tailBase in tailBases)
            {
                var tailBaseTransform = tailBase == null ? null : tailBase.transform;
                var tailBaseRotation = tailBase != null ? tailBase.rotation : 0f;
                WriteTailTransform(writer, body.rb, tailBaseTransform, tailBaseRotation);
                tailBaseStates[tailBaseIndex++] = CreateTailBaseState(body.rb, tailBaseTransform, tailBaseRotation);
            }
            var tails = GetTransforms(body, "tails");
            var tailStates = new PlayerSnapshotTailState[tails.Length];
            writer.Write((ushort)tails.Length);
            for (var tailIndex = 0; tailIndex < tails.Length; tailIndex++)
            {
                var tail = tails[tailIndex];
                var tailRotation = tail == null ? 0f : tail.eulerAngles.z;
                WriteTailTransform(writer, body.rb, tail, tailRotation);
                tailStates[tailIndex] = CreateTailBaseState(body.rb, tail, tailRotation);
            }

            var arms = body.Arms;
            var gunTransform = GetTransform(body, "gunTransform");
            var gunAnimationTransform = GetTransform(body, "gunAnimTransform");
            var weaponTransform = body.weapon == null ? null : body.weapon.transform;
            WriteWorldTransform(writer, arms);
            WriteLocalTransform(writer, gunTransform);
            WriteLocalTransform(writer, gunAnimationTransform);
            WriteWorldTransform(writer, weaponTransform);
            var armsTransform = arms == null ? new PlayerSnapshotTransform(0f, 0f, 0f) :
                new PlayerSnapshotTransform(arms.position.x, arms.position.y, arms.eulerAngles.z);
            var gunTransformState = gunTransform == null ? new PlayerSnapshotTransform(0f, 0f, 0f) :
                new PlayerSnapshotTransform(gunTransform.localPosition.x, gunTransform.localPosition.y,
                    gunTransform.localEulerAngles.z);
            var gunAnimationTransformState = gunAnimationTransform == null ? new PlayerSnapshotTransform(0f, 0f, 0f) :
                new PlayerSnapshotTransform(gunAnimationTransform.localPosition.x, gunAnimationTransform.localPosition.y,
                    gunAnimationTransform.localEulerAngles.z);
            var weaponTransformState = weaponTransform == null ? new PlayerSnapshotTransform(0f, 0f, 0f) :
                new PlayerSnapshotTransform(weaponTransform.position.x, weaponTransform.position.y,
                    weaponTransform.eulerAngles.z);
            breakdown.Rig += (int)(writer.BaseStream.Position - sectionStarted);

            sectionStarted = writer.BaseStream.Position;
            var health = body.health;
            var isAlive = body.isAlive;
            var deathCause = DeathCauseFor(body);
            var stamina = body.stamina;
            var controlState = (byte)body.controlState;
            var canBeGrabbed = CanGrabOnlyState(body);
            var burnIntensity = body.burnIntensity;
            var hasNoLegs = body.noLegs;
            var isDecapitated = body.deHeaded;
            writer.Write(health);
            writer.Write(isAlive);
            writer.Write(stamina);
            writer.Write(controlState);
            writer.Write(canBeGrabbed);
            writer.Write(burnIntensity);
            writer.Write(hasNoLegs);
            writer.Write(isDecapitated);
            var weaponSlot = body.unarmed ? -1 : body.currentWeapon;
            var weaponAmmo = body.weapon == null ? 0 : body.weapon.ammo;
            var weapons = GetList(body, "weapons");
            var activeRenderer = localVisualLayout.WeaponRenderer;
            var weaponSpriteId = NetworkWireId.FromString(SpriteId(activeRenderer == null ? null : activeRenderer.sprite));
            writer.Write(weaponSlot);
            writer.Write(weaponAmmo);
            writer.Write(weaponSpriteId);
            writer.Write((ushort)weapons.Count);
            var inventoryIds = new ulong[weapons.Count];
            for (var index = 0; index < weapons.Count; index++)
            {
                var preset = weapons[index] as WeaponPreset;
                inventoryIds[index] = NetworkWireId.FromString(preset == null ? "" : SpriteId(preset.sprite));
            }
            var inventoryKey = string.Join("|", inventoryIds);
            var inventoryChanged = inventoryKey != lastSerializedInventory || Time.unscaledTime >= nextFullInventory;
            writer.Write(inventoryChanged);
            if (inventoryChanged)
            {
                foreach (var inventoryId in inventoryIds) writer.Write(inventoryId);
                lastSerializedInventory = inventoryKey;
                nextFullInventory = Time.unscaledTime + 1f;
            }
            breakdown.Weapons += (int)(writer.BaseStream.Position - sectionStarted);

            sectionStarted = writer.BaseStream.Position;
            var weaponLaserState = CreateWeaponLaserState(body.wepLaserLine);
            WriteLineState(writer, weaponLaserState);
            var player = PlayerScript.player;
            var levitatorLaser = player == null ? null : player.levitLine;
            var levitatorLaserState = CreateWeaponLaserState(levitatorLaser);
            WriteLineState(writer, levitatorLaserState);
            var scarfState = CreateScarfState(body);
            WriteScarfState(writer, scarfState);
            var crystalTongue = body.GetComponent<CrystalTongue>();
            var crystalTongueState = CreateWeaponLaserState(crystalTongue == null ? null : crystalTongue.line);
            WriteLineState(writer, crystalTongueState);
            breakdown.Effects += (int)(writer.BaseStream.Position - sectionStarted);

            sectionStarted = writer.BaseStream.Position;
            writer.Write(includeVisualState);
            if (includeVisualState) WriteVisualState(writer, visualState);
            writer.Write((byte)deathCause);
            writer.Write(body.susnessMult);
            breakdown.Visual += (int)(writer.BaseStream.Position - sectionStarted);
            AddAvatarWireBreakdown(breakdown);
            MultiplayerPerformance.AddAvatarSerialize(performanceStarted);
            var coreBody = body.rb == null ? new PlayerSnapshotBodyState(0f, 0f, 0f) :
                new PlayerSnapshotBodyState(body.rb.position.x, body.rb.position.y, body.rb.rotation);
            var packetVisualState = includeVisualState ? CreatePacketVisualState(visualState) :
                (PlayerSnapshotVisualState?)null;
            return new PlayerSnapshotPacket(sequence, inVehicle, vehicleId, isVehicleDriver,
                (byte)body.CurrentState, body.isRight, isReflected, isActive, headRotation, coreBody, health,
                isAlive, deathCause, body.susnessMult, body.characterScale, stamina, controlState, canBeGrabbed, burnIntensity, hasNoLegs, isDecapitated,
                armsTransform, gunTransformState, gunAnimationTransformState, weaponTransformState, limbStates,
                tailBaseStates, tailStates, weaponSlot, weaponAmmo, weaponSpriteId, inventoryIds, inventoryChanged,
                scarfState, weaponLaserState, levitatorLaserState, crystalTongueState, includeVisualState,
                packetVisualState);
        }
    }

    private void AddAvatarWireBreakdown(AvatarWireBreakdown breakdown)
    {
        avatarCoreBytesWindow += breakdown.Core;
        avatarLimbBytesWindow += breakdown.Limbs;
        avatarRigBytesWindow += breakdown.Rig;
        avatarWeaponBytesWindow += breakdown.Weapons;
        avatarEffectsBytesWindow += breakdown.Effects;
        avatarVisualBytesWindow += breakdown.Visual;
        if (Time.unscaledTime < nextAvatarTrafficSample) return;
        nextAvatarTrafficSample = Time.unscaledTime + 1f;
        avatarCoreBytesPerSecond = avatarCoreBytesWindow;
        avatarLimbBytesPerSecond = avatarLimbBytesWindow;
        avatarRigBytesPerSecond = avatarRigBytesWindow;
        avatarWeaponBytesPerSecond = avatarWeaponBytesWindow;
        avatarEffectsBytesPerSecond = avatarEffectsBytesWindow;
        avatarVisualBytesPerSecond = avatarVisualBytesWindow;
        avatarCoreBytesWindow = avatarLimbBytesWindow = avatarRigBytesWindow = avatarWeaponBytesWindow =
            avatarEffectsBytesWindow = avatarVisualBytesWindow = 0;
    }

    private static PlayerVisualState SerializeVisualState(VisualLayout layout)
    {
        var renderers = layout == null || layout.Renderers == null
            ? new SpriteRenderer[0]
            : layout.Renderers;
        var rendererStates = new RendererVisualState[renderers.Length];
        for (var index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            var path = layout.RendererPaths != null && index < layout.RendererPaths.Length
                ? layout.RendererPaths[index] ?? ""
                : "";
            rendererStates[index] = renderer == null
                ? new RendererVisualState(path, false, Color.white, false, false)
                : new RendererVisualState(path, renderer.enabled && renderer.gameObject.activeInHierarchy,
                    renderer.color, renderer.flipX, renderer.flipY);
        }

        var lights = layout == null || layout.Lights == null ? new Component[0] : layout.Lights;
        var lightStates = new LightVisualState[lights.Length];
        for (var index = 0; index < lights.Length; index++)
        {
            var light = lights[index];
            var path = layout.LightPaths != null && index < layout.LightPaths.Length
                ? layout.LightPaths[index] ?? ""
                : "";
            if (light == null)
            {
                lightStates[index] = new LightVisualState(path, false, 0f, Color.white);
                continue;
            }
            var behaviour = light as Behaviour;
            var light2D = light as UnityEngine.Experimental.Rendering.Universal.Light2D;
            lightStates[index] = new LightVisualState(path,
                behaviour == null || behaviour.enabled && light.gameObject.activeInHierarchy,
                light2D == null ? 0f : light2D.intensity, light2D == null ? Color.white : light2D.color);
        }
        var expressions = layout == null || layout.Root == null
            ? Array.Empty<FacialExpression>() : layout.Root.GetComponentsInChildren<FacialExpression>(true);
        var expressionStates = new byte[expressions.Length];
        for (var index = 0; index < expressions.Length; index++)
            expressionStates[index] = FacialExpressionState(expressions[index]);
        return new PlayerVisualState(rendererStates, lightStates, expressionStates);
    }

    private static PlayerSnapshotVisualState CreatePacketVisualState(PlayerVisualState state)
    {
        var sourceRenderers = state == null || state.Renderers == null
            ? new RendererVisualState[0]
            : state.Renderers;
        var renderers = new PlayerSnapshotRendererState[sourceRenderers.Length];
        for (var index = 0; index < sourceRenderers.Length; index++)
        {
            var renderer = sourceRenderers[index];
            renderers[index] = new PlayerSnapshotRendererState(renderer.Path, renderer.Visible,
                new PlayerSnapshotColor(renderer.Color.r, renderer.Color.g, renderer.Color.b, renderer.Color.a),
                renderer.FlipX, renderer.FlipY);
        }
        var sourceLights = state == null || state.Lights == null ? new LightVisualState[0] : state.Lights;
        var lights = new PlayerSnapshotLightState[sourceLights.Length];
        for (var index = 0; index < sourceLights.Length; index++)
        {
            var light = sourceLights[index];
            lights[index] = new PlayerSnapshotLightState(light.Path, light.Visible, light.Intensity,
                new PlayerSnapshotColor(light.Color.r, light.Color.g, light.Color.b, light.Color.a));
        }
        return new PlayerSnapshotVisualState(renderers, lights, state == null ? Array.Empty<byte>() : state.FacialExpressions);
    }

    private void Apply(PlayerSnapshotPacket snapshot)
    {
        if (remoteBody == null || remoteBody.rb == null) return;
        var performanceStarted = MultiplayerPerformance.Start();
        try
        {
            var packetWriter = new PacketWriter(512);
            snapshot.Write(ref packetWriter);
            using (var reader = new BinaryReader(new MemoryStream(packetWriter.ToArray(), false)))
            {
                reader.ReadInt32();
                var remoteInVehicle = reader.ReadBoolean();
                var remoteVehicleId = reader.ReadUInt64();
                var remoteVehicleDriver = reader.ReadBoolean();
                var remoteState = (BodyScript.EntityState)reader.ReadByte();
                if (remoteState < BodyScript.EntityState.Idle || remoteState > BodyScript.EntityState.MoveLeft)
                    remoteState = BodyScript.EntityState.Idle;
                var remoteVehicleAttached = SynchronizeRemoteVehicle(remoteInVehicle, remoteVehicleId, remoteVehicleDriver, remoteState);
                var remoteVehicleStreamed = remoteInVehicle && remoteBody.inVehicle &&
                    remoteBody.curVehicle != null && remoteBody.curVehicle.mainPart != null &&
                    remoteBody.curVehicle.mainPart.rb != null;
             
                var isRight = reader.ReadBoolean();
                var reflected = reader.ReadBoolean();

                remoteAvatar.SetActive(reader.ReadBoolean());
                var remoteHeadRotation = reader.ReadSingle();
                var hadVehicleHeadRotation = hasRemoteVehicleHeadRotation;

                hasRemoteVehicleHeadRotation = remoteVehicleStreamed;
                hasVehicleArmsTarget = remoteVehicleStreamed;
                hasRemoteVehicleReflection = remoteVehicleStreamed;
                remoteVehicleReflected = reflected;

                if (remoteVehicleStreamed)
                {
                    if (hadVehicleHeadRotation)
                    {
                        var progress = Mathf.Clamp01(
                            (Time.unscaledTime -
                             vehicleHeadStartedAt) /
                            0.10f);

                        vehicleHeadFromRotation =
                            Mathf.LerpAngle(
                                vehicleHeadFromRotation,
                                remoteVehicleHeadRotation,
                                progress);
                    }
                    else
                    {
                        vehicleHeadFromRotation = remoteHeadRotation;
                    }

                    vehicleHeadStartedAt = Time.unscaledTime;
                }

                remoteVehicleHeadRotation = remoteHeadRotation;

                if (!remoteVehicleStreamed &&
                    remoteBody.isRight != isRight)
                {
                    remoteBody.SwitchDir(true);
                }
                
                var limbs = GetList(remoteBody, "limbs");
                var sourceVehicleRoot = Vector2.zero;
                var sourceVehicleRotation = 0f;
                if (remoteInVehicle)
                {
                    ReadVehicleRoot(reader, out sourceVehicleRoot, out sourceVehicleRotation);
                    lastAuthoritativePosition = remoteBody.rb.position;
                }
                else
                {
                    lastAuthoritativePosition = SetTarget(reader, remoteBody.rb);
                }
                hasAuthoritativePosition = true;
                var limbCount = reader.ReadUInt16();
                var dismembermentHash = 17;
                for (var index = 0; index < limbCount; index++)
                {
                    if (index >= limbs.Count)
                    {
                        SkipBody(reader);
                        reader.ReadBoolean();
                        reader.ReadBoolean();
                        continue;
                    }
                    var limb = (LimbScript)limbs[index];
                    if (remoteInVehicle) SkipBody(reader);
                    else SetTarget(reader, limb.rb);
                    limb.dismembered = reader.ReadBoolean();
                    dismembermentHash = unchecked(dismembermentHash * 31 + (limb.dismembered ? 1 : 0));
                    SetRemoteFire(index, limb, reader.ReadBoolean());
                }
                if (dismembermentHash != appliedDismembermentHash)
                {
                    appliedDismembermentHash = dismembermentHash;
                    ApplyDismembermentVisuals();
                }
                var tailCount = reader.ReadUInt16();
                if (!remoteVehicleStreamed)
                {
                    vehicleTailTargets.Clear();
                    vehicleTailTransformTargets.Clear();
                }
                    
                for (var index = 0; index < tailCount; index++)
                    ReadTailTarget(reader,
                        index < remoteTailBases.Count ? remoteTailBases[index] : null,
                        index < remoteTailSprites.Count ? remoteTailSprites[index] : null,
                        remoteVehicleStreamed, sourceVehicleRoot, sourceVehicleRotation);
                var tailRootCount = reader.ReadUInt16();
                for (var index = 0; index < tailRootCount; index++)
                    ReadTailTarget(reader,
                        index < remoteTails.Length ? remoteTails[index] : null,
                        index < remoteTailRootSprites.Count ? remoteTailRootSprites[index] : null,
                        remoteVehicleStreamed, sourceVehicleRoot, sourceVehicleRotation);
                
                if (remoteVehicleStreamed)
                {
                    ReadVehicleArmsTarget( reader, sourceVehicleRoot, sourceVehicleRotation);
                    ReadLocalRotationImmediately( reader, GetTransform(remoteBody, "gunTransform"));
                    ReadLocalRotationImmediately( reader, GetTransform(remoteBody, "gunAnimTransform"));
                }
                else
                {
                    ReadWorldTransform(reader, remoteBody.Arms);
                    ReadLocalTransform(reader, GetTransform(remoteBody, "gunTransform"));
                    ReadLocalTransform(reader, GetTransform(remoteBody, "gunAnimTransform"));
                }
                
                if (remoteVehicleStreamed) ApplyVehicleHeadRotation();
                
                ReadWorldTarget(reader, null);
                var remoteHealth = reader.ReadSingle();
                if (MultiplayerSession.IsHost)
                {
                    if (remoteHealth < lastRemoteHealth)
                        pendingRemoteDamage = Mathf.Max(0f, pendingRemoteDamage - (lastRemoteHealth - remoteHealth));
                    else if (remoteHealth > lastRemoteHealth)
                        pendingRemoteDamage = 0f;
                }
                var wasRemoteAlive = lastRemoteAlive;
                remoteBody.health = remoteHealth;
                remoteBody.isAlive = reader.ReadBoolean();
                remoteBody.susnessMult = Mathf.Clamp(snapshot.SusnessMultiplier, 0.25f, 1f);
                ApplyRemoteCharacterScale(snapshot.CharacterScale);
                remoteBody.stamina = reader.ReadSingle();
                remoteBody.controlState = (BodyScript.RagdollState)reader.ReadByte();
                remoteCanBeGrabbed = reader.ReadBoolean();
                lastRemoteHealth = remoteBody.health;
                lastRemoteAlive = remoteBody.isAlive;
                if (MultiplayerSession.IsHost && wasRemoteAlive && !lastRemoteAlive)
                    remoteBody.DropAllWeapons();
                if (lastRemoteAlive && !wasRemoteAlive)
                {
                    if (MultiplayerSession.IsHost) ScoreboardSystem.NoteHostPlayerRespawn(remotePeerId);
                    ClearReplicaBloodEffects(remoteBody);
                    remoteDeathDropSpawned = false;
                    pendingRemoteDamage = 0f;
                }
                remoteBody.burnIntensity = reader.ReadSingle();
                remoteBody.noLegs = reader.ReadBoolean();
                remoteBody.deHeaded = reader.ReadBoolean();
                if (remoteBody.limbMat != null)
                    remoteBody.limbMat.SetFloat("BurnIntensity", remoteBody.burnIntensity);
                var weaponSlot = reader.ReadInt32();
                var weaponAmmo = reader.ReadInt32();
                var weaponSprite = reader.ReadUInt64();
                var inventoryCount = reader.ReadUInt16();
                var inventoryChanged = reader.ReadBoolean();
                var inventorySprites = new ulong[inventoryCount];
                if (inventoryChanged)
                    for (var index = 0; index < inventoryCount; index++) inventorySprites[index] = reader.ReadUInt64();
                else
                    for (var index = 0; index < inventoryCount && index < remoteBody.weapons.Count; index++)
                        inventorySprites[index] = NetworkWireId.FromString(remoteBody.weapons[index] == null ? "" : SpriteId(remoteBody.weapons[index].sprite));
                var inventoryKey = weaponSlot + "|" + string.Join("|", inventorySprites);
                if (inventoryKey != appliedInventory)
                {
                    while (remoteBody.weapons.Count < inventorySprites.Length) remoteBody.weapons.Add(null);
                    while (remoteBody.weaponAmmos.Count < inventorySprites.Length) remoteBody.weaponAmmos.Add(0);
                    for (var index = 0; index < inventorySprites.Length; index++)
                        remoteBody.weapons[index] = FindWeaponPreset(inventorySprites[index]);
                }
                if (weaponSlot >= 0 && weaponSlot < remoteBody.weaponAmmos.Count)
                    remoteBody.weaponAmmos[weaponSlot] = weaponAmmo;
                if (weaponSlot < 0)
                {
                    if (!remoteBody.unarmed) remoteBody.ChangeToUnarmed();
                    appliedWeapon = -1;
                    appliedWeaponSprite = 0UL;
                    appliedInventory = inventoryKey;
                }
                else if (weaponSlot != appliedWeapon || inventoryKey != appliedInventory)
                {
                    remoteBody.ChangeWeapon(weaponSlot);
                    appliedWeapon = weaponSlot;
                }
                if (weaponSlot >= 0 && remoteBody.weapon != null)
                {
                    remoteBody.weapon.ammo = weaponAmmo;
                    if (weaponSprite != appliedWeaponSprite || inventoryKey != appliedInventory)
                    {
                        ApplyWeaponVisual(remoteBody, weaponSprite, weaponSlot, inventorySprites);
                        appliedWeaponSprite = weaponSprite;
                        appliedInventory = inventoryKey;
                    }
                }
                var remoteLaser = remoteBody.wepLaserLine;
                ReadLineState(reader, remoteLaser, remoteBody.wepLaser);
                ReadLineState(reader, remoteLevitLine, remoteLevitLine == null ? null : remoteLevitLine.gameObject);
                ReadScarfState(reader);
                ReadLineState(reader, remoteCrystalTongueLine, null);
                if (reader.ReadBoolean()) ApplyVisualState(ReadVisualState(reader), remoteBody.transform);
                receivedFirstSnapshot = true;
            }
        }
        catch (EndOfStreamException) { }
        finally { MultiplayerPerformance.AddAvatarApply(performanceStarted); }
    }

    private static void ApplyInitialLobbyScale(BodyScript body)
    {
        if (!MultiplayerSession.IsActive)
        {
            initialScaleAppliedBody = null;
            appliedInitialScale = float.NaN;
            return;
        }
        var target = MultiplayerSession.InitialScale;
        if (body == initialScaleAppliedBody && Mathf.Abs(appliedInitialScale - target) < 0.001f) return;
        if (!AvatarScaleHandler.TrySet(body, target)) return;
        initialScaleAppliedBody = body;
        appliedInitialScale = target;
    }

    private void ApplyRemoteCharacterScale(float characterScale)
    {
        if (remoteBody == null || float.IsNaN(characterScale) || float.IsInfinity(characterScale)) return;
        AvatarScaleHandler.TrySet(remoteBody, characterScale);
    }

    private bool SynchronizeRemoteVehicle(bool inVehicle, ulong vehicleId, bool driver, BodyScript.EntityState state)
    {
        if (remoteBody == null) return false;
        remoteBody.CurrentState = state;
        if (!inVehicle || vehicleId == 0UL)
        {
            if (remoteBody.inVehicle && remoteBody.curVehicle != null)
            {
                var previousVehicle = remoteBody.curVehicle;
                remoteBody.ExitVehicle();
                SetRemoteVehicleCollisions(previousVehicle, false);
                DetachRemoteAvatar();
            }
            return false;
        }
        var world = GunsawMultiplayerPlugin.World;
        var vehicle = world == null ? null : world.FindVehicle(vehicleId);
        if (vehicle == null || (driver && vehicle.occupant != null && vehicle.occupant != remoteBody)) return false;
        if (!remoteBody.inVehicle || remoteBody.curVehicle != vehicle)
        {
            if (remoteBody.inVehicle && remoteBody.curVehicle != null)
            {
                var previousVehicle = remoteBody.curVehicle;
                remoteBody.ExitVehicle();
                SetRemoteVehicleCollisions(previousVehicle, false);
                DetachRemoteAvatar();
            }
            AttachRemoteToVehicle(vehicle, driver);
        }
        if (!remoteBody.inVehicle || remoteBody.curVehicle != vehicle) return false;
        return true;
    }

    private void AttachRemoteToVehicle(VehicleBase vehicle, bool driver)
    {
        if (vehicle == null || remoteBody == null || vehicle.mainPart == null ||
            vehicle.mainPart.rb == null || vehicle.seatPos == null) return;
        
        remoteScaleBeforeVehicle = remoteBody.transform.localScale;
        hasRemoteScaleBeforeVehicle = true;
        
        if (driver)
        {
            vehicle.occupant = remoteBody;
            vehicle.occupJoint = null;
            KartPassengers.RegisterDriver(vehicle, remoteBody);
        }
        else KartPassengers.Attach(vehicle, remoteBody, false);
        
        remoteBody.inVehicle = true;
        remoteBody.curVehicle = vehicle;
        remoteBody.rb.freezeRotation = true;
        SetRemoteVehicleRigPhysics(false);
        remoteBody.enabled = false;
        
        foreach (var limb in remoteAvatar.GetComponentsInChildren<LimbScript>(true)) limb.enabled = false;
        
        if (remoteBody.BodyAnimator != null)
        {
            remoteBody.BodyAnimator.enabled = true;
            remoteBody.BodyAnimator.SetBool("inVehicle", true);
            remoteBody.BodyAnimator.Play("PlayerSit");
        }
        
        if (remoteAvatar != null)
        {
            var offset = (Vector3)KartPassengers.SeatPosition(vehicle, remoteBody) - remoteBody.transform.position;
            remoteAvatar.transform.SetParent(vehicle.mainPart.transform, true);
            remoteAvatar.transform.position += offset;
        }
        
        remoteBody.BodyAnimator.Update(0f);
        SnapRemoteVehicleLimbs();
        targets.Clear();
        worldTargets.Clear();
        localTargets.Clear();
        CaptureRemoteVehicleRigPose(vehicle);
        SetRemoteVehicleCollisions(vehicle, true);
    }

    private void MaintainRemoteVehiclePose()
    {
        if (remoteBody == null || !remoteBody.inVehicle || remoteBody.BodyAnimator == null) return;
        var animator = remoteBody.BodyAnimator;
        animator.enabled = true;
        animator.SetBool("inVehicle", true);
        animator.Play("PlayerSit", 0, 0f);
        animator.Update(0f);
        remoteBody.standAnimForce = 1f;
    }

    private void SnapRemoteVehicleLimbs()
    {
        if (remoteBody == null) return;
        foreach (var limb in remoteBody.limbs)
        {
            if (limb == null || limb.dismembered || limb.rb == null || limb.transformToFollow == null) continue;
            var position = limb.transformToFollow.localPosition;
            if (limb.reverseXPosWhenFlipped && !remoteBody.isRight) position = -position;
            limb.transform.localPosition = position;
            limb.transform.localRotation = Quaternion.Euler(0f, 0f, limb.transformToFollow.localEulerAngles.z);
            limb.rb.position = limb.transform.position;
            limb.rb.rotation = limb.transform.eulerAngles.z;
            limb.rb.velocity = Vector2.zero;
            limb.rb.angularVelocity = 0f;
        }
    }

    private void SetRemoteVehicleRigPhysics(bool simulated)
    {
        foreach (var body in remoteRigidbodies)
        {
            if (body == null) continue;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = simulated;
        }
    }

    private void CaptureRemoteVehicleRigPose(VehicleBase vehicle)
    {
        hasVehicleArmsTarget = remoteBody != null && remoteBody.Arms != null && vehicle != null &&
            vehicle.mainPart != null && vehicle.mainPart.rb != null;
        
        vehicleTailTargets.Clear();
        
        if (!hasVehicleArmsTarget) return;
        vehicleArmsLocalPosition = vehicle.mainPart.transform.InverseTransformPoint(remoteBody.Arms.position);
        vehicleArmsLocalRotation = Mathf.DeltaAngle(vehicle.mainPart.rb.rotation, remoteBody.Arms.eulerAngles.z);
        vehicleArmsFromLocalPosition = vehicleArmsLocalPosition;
        vehicleArmsFromLocalRotation = vehicleArmsLocalRotation;
        vehicleArmsStartedAt = Time.unscaledTime;
        foreach (var tailBody in remoteTailBases)
        {
            if (tailBody == null)
                continue;

            var localRotation = Mathf.DeltaAngle(
                vehicle.mainPart.rb.rotation,
                tailBody.rotation);

            vehicleTailTargets.Add(new VehicleTailTarget
            {
                Body = tailBody,

                LocalRotation = localRotation,
                FromLocalRotation = localRotation,
                StartedAt = Time.unscaledTime
            });
        }
        
        for (var index = 0;
             index < vehicleTailTransformTargets.Count;
             index++)
        {
            var state =
                vehicleTailTransformTargets[index];

            if (state.Transform == null)
                continue;

            var localRotation = Mathf.DeltaAngle(
                vehicle.mainPart.rb.rotation,
                state.Transform.eulerAngles.z);

            state.LocalRotation = localRotation;
            state.FromLocalRotation = localRotation;
            state.StartedAt = Time.unscaledTime;

            vehicleTailTransformTargets[index] = state;
        }
        
        hasVehicleRigTarget = true;
    }

    private void DetachRemoteAvatar()
    {
        if (remoteBody != null)
        {
            remoteBody.inVehicle = false;
            remoteBody.curVehicle = null;
            remoteBody.standAnimForce = 1f;
            if (remoteBody.BodyAnimator != null)
            {
                remoteBody.BodyAnimator.enabled = true;
                remoteBody.BodyAnimator.SetBool("inVehicle", false);
                remoteBody.BodyAnimator.Rebind();
                remoteBody.BodyAnimator.Update(0f);
            }
            
            if (hasRemoteScaleBeforeVehicle)
            {
                remoteBody.transform.localScale = remoteScaleBeforeVehicle;
                hasRemoteScaleBeforeVehicle = false;
            }

            
            SnapRemoteVehicleLimbs();
            remoteBody.enabled = false;
        }
        if (remoteAvatar != null)
            foreach (var limb in remoteAvatar.GetComponentsInChildren<LimbScript>(true)) limb.enabled = false;
        if (remoteBody != null && remoteBody.BodyAnimator != null)
            remoteBody.BodyAnimator.enabled = false;
        if (remoteAvatar != null) remoteAvatar.transform.SetParent(remoteAvatarParent, true);
        remotePhysicsModeKnown = false;
        UpdateRemotePhysicsMode();
        targets.Clear();
        worldTargets.Clear();
        localTargets.Clear();
        vehicleTailTargets.Clear();
        vehicleTailTransformTargets.Clear();
        hasRemoteVehicleHeadRotation = false;
        hasVehicleArmsTarget = false;
    }

    private void MaintainRemoteVehicleAttachment()
    {
        if (remoteBody == null || !remoteBody.inVehicle || remoteBody.curVehicle == null ||
            remoteBody.curVehicle.seatPos == null || remoteBody.curVehicle.mainPart == null ||
            remoteBody.curVehicle.mainPart.rb == null || remoteBody.rb == null) return;
        if (remoteAvatar == null || remoteAvatar.transform.parent == remoteBody.curVehicle.mainPart.transform)
            return;
        var offset = (Vector3)KartPassengers.SeatPosition(remoteBody.curVehicle, remoteBody) - remoteBody.transform.position;
        remoteAvatar.transform.SetParent(remoteBody.curVehicle.mainPart.transform, true);
        remoteAvatar.transform.position += offset;
    }

    private static void ReadVehicleRoot(BinaryReader reader, out Vector2 position, out float rotation)
    {
        position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        rotation = reader.ReadSingle();
    }

    private void ApplyVehicleHeadRotation()
    {
        if (remoteBody == null || remoteBody.headTransform == null || remoteBody.curVehicle == null ||
            remoteBody.curVehicle.mainPart == null || remoteBody.curVehicle.mainPart.rb == null) return;
        var progress = Mathf.Clamp01((Time.unscaledTime - vehicleHeadStartedAt) / 0.10f);
        var relativeRotation = Mathf.LerpAngle(vehicleHeadFromRotation, remoteVehicleHeadRotation, progress);
        remoteBody.headTransform.rotation = Quaternion.Euler(0f, 0f,
            remoteBody.curVehicle.mainPart.rb.rotation + relativeRotation);
    }

    private void ReadVehicleArmsTarget(BinaryReader reader, Vector2 sourceRoot, float sourceRootRotation)
    {
        var position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        var rotation = reader.ReadSingle();
        if (remoteBody == null || remoteBody.curVehicle == null || remoteBody.curVehicle.mainPart == null ||
            remoteBody.curVehicle.mainPart.rb == null) return;
        var vehicle = remoteBody.curVehicle;
        var angle = vehicle.mainPart.rb.rotation - sourceRootRotation;
        var target = KartPassengers.SeatPosition(vehicle, remoteBody) +
            (Vector2)(Quaternion.Euler(0f, 0f, angle) * (position - sourceRoot));
        var localPosition = vehicle.mainPart.transform.InverseTransformPoint(target);
        var localRotation = Mathf.DeltaAngle(vehicle.mainPart.rb.rotation,
            vehicle.mainPart.rb.rotation + Mathf.DeltaAngle(sourceRootRotation, rotation));
        var progress = Mathf.Clamp01((Time.unscaledTime - vehicleArmsStartedAt) / 0.02f);
        vehicleArmsFromLocalPosition = Vector2.Lerp(vehicleArmsFromLocalPosition, vehicleArmsLocalPosition, progress);
        vehicleArmsFromLocalRotation = Mathf.LerpAngle(vehicleArmsFromLocalRotation, vehicleArmsLocalRotation, progress);
        vehicleArmsLocalPosition = localPosition;
        vehicleArmsLocalRotation = localRotation;
        vehicleArmsStartedAt = Time.unscaledTime;
        hasVehicleArmsTarget = true;
        hasVehicleRigTarget = true;
    }

    private void ApplyVehicleArmsTarget()
    {
        if (!hasVehicleArmsTarget ||
            remoteBody == null ||
            !remoteBody.inVehicle ||
            remoteBody.Arms == null ||
            remoteBody.curVehicle == null ||
            remoteBody.curVehicle.mainPart == null ||
            remoteBody.curVehicle.mainPart.rb == null)
            return;

        var progress = Mathf.Clamp01(
            (Time.unscaledTime - vehicleArmsStartedAt) / 0.02f);

        var localRotation = Mathf.LerpAngle(
            vehicleArmsFromLocalRotation,
            vehicleArmsLocalRotation,
            progress);

        var vehicle = remoteBody.curVehicle;

        vehicleArmsTargetRotation =
            vehicle.mainPart.rb.rotation + localRotation;

        remoteBody.Arms.rotation = Quaternion.Euler(
            0f,
            0f,
            vehicleArmsTargetRotation);
    }

    private static void ReadLocalRotationImmediately(
        BinaryReader reader,
        Transform transform)
    {
        reader.ReadSingle();
        reader.ReadSingle();

        var rotation = reader.ReadSingle();

        if (transform == null)
            return;

        transform.localRotation =
            Quaternion.Euler(0f, 0f, rotation);
    }

    private void ApplyVehicleTailTargets()
    {
        if (remoteBody == null ||
            !remoteBody.inVehicle ||
            remoteBody.curVehicle == null ||
            remoteBody.curVehicle.mainPart == null ||
            remoteBody.curVehicle.mainPart.rb == null)
            return;

        var vehicle =
            remoteBody.curVehicle;

        var vehicleRotation =
            vehicle.mainPart.rb.rotation;

        var seatPosition = KartPassengers.SeatPosition(vehicle, remoteBody)
                           -
                           (Vector2)vehicle.mainPart.transform.right * 0.15f; // оффсет так называемой попы (мне кажется я делаю что-то не так) 
        
        foreach (var target in vehicleTailTransformTargets)
        {
            if (target.Transform == null)
                continue;

            var position =
                target.Transform.position;

            position.x = seatPosition.x;
            position.y = seatPosition.y;

            target.Transform.position = position;

            var progress = Mathf.Clamp01(
                (Time.unscaledTime - target.StartedAt) /
                CurrentSnapshotInterval());

            var localRotation = Mathf.LerpAngle(
                target.FromLocalRotation,
                target.LocalRotation,
                progress);

            target.Transform.rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    vehicleRotation + localRotation);
        }
        
        foreach (var target in vehicleTailTargets)
        {
            if (target.Body == null)
                continue;

            var progress = Mathf.Clamp01(
                (Time.unscaledTime - target.StartedAt) /
                CurrentSnapshotInterval());

            var localRotation = Mathf.LerpAngle(
                target.FromLocalRotation,
                target.LocalRotation,
                progress);

            var worldRotation =
                vehicleRotation + localRotation;

            target.Body.transform.rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    worldRotation);

            target.Body.rotation = worldRotation;
            target.Body.velocity = Vector2.zero;
            target.Body.angularVelocity = 0f;
        }
    }

    private void SetRemoteVehicleCollisions(VehicleBase vehicle, bool ignored)
    {
        if (vehicle == null) return;
        var vehicleColliders = vehicle.GetComponentsInChildren<Collider2D>(true);
        foreach (var remoteCollider in remoteColliderTriggers.Keys)
        {
            if (remoteCollider == null) continue;
            foreach (var vehicleCollider in vehicleColliders)
                if (vehicleCollider != null)
                    Physics2D.IgnoreCollision(remoteCollider, vehicleCollider, ignored);
        }
    }

    internal static bool IsCreatingRemoteAvatar()
    {
        return remoteAvatarCreationDepth > 0;
    }

    internal static bool HandleHostRemoteDamaged(BodyScript body, bool critical)
    {
        var replica = NetworkAvatarRegistry.ReplicaForBody(body);
        if (!MultiplayerSession.IsConnected || replica == null || !replica.receivedFirstSnapshot) return false;
        var amount = Mathf.Clamp(replica.lastRemoteHealth - body.health, 0f, 1000f);
        if (amount > 0.001f &&
            activeShotState != null &&
            activeShotState.Weapon != null &&
            activeShotState.Weapon.stats != null)
        {
            float baseDamage = amount;

            if (critical)
            {
                float critMultiplier = activeShotState.Weapon.stats.critDamage;

                float divisor = 1f + critMultiplier;

                if (Mathf.Abs(divisor) > 0.0001f)
                    baseDamage = amount / divisor;
            }

            baseDamage = Mathf.Clamp(baseDamage, 0f, 1000f);
            QueueBaseDamage(activeShotState, replica.remotePeerId, baseDamage);
        }
        
        body.health = replica.lastRemoteHealth;
        body.isAlive = replica.lastRemoteAlive;
        if (amount > 0.001f) RouteRemotePlayerDamage(replica, amount, critical);
        return true;
    }

    internal static bool HandleHostRemoteDeath(BodyScript body)
    {
        var replica = NetworkAvatarRegistry.ReplicaForBody(body);
        if (!MultiplayerSession.IsConnected || replica == null || !replica.receivedFirstSnapshot) return false;
        body.health = replica.lastRemoteHealth;
        body.isAlive = replica.lastRemoteAlive;
        RouteRemotePlayerDamage(replica, Mathf.Max(1f, replica.lastRemoteHealth + 1f), true);
        return true;
    }
    
    private static void QueueBaseDamage(
        ShotState state,
        ushort targetPeerId,
        float baseDamage)
    {
        if (state == null || targetPeerId == 0)
            return;

        if (!state.PendingBaseDamage.TryGetValue(targetPeerId, out var queue))
        {
            queue = new Queue<float>();
            state.PendingBaseDamage[targetPeerId] = queue;
        }

        queue.Enqueue(baseDamage);
    }

    private static float TakeBaseDamage(
        ShotState state,
        ushort targetPeerId)
    {
        if (state == null || targetPeerId == 0)
            return 0f;

        if (!state.PendingBaseDamage.TryGetValue(targetPeerId, out var queue))
            return 0f;

        if (queue.Count == 0)
            return 0f;

        var damage = queue.Dequeue();

        if (queue.Count == 0)
            state.PendingBaseDamage.Remove(targetPeerId);

        return damage;
    }

    private static void RouteRemotePlayerDamage(NetworkAvatarReplication replica, float amount, bool critical)
    {
        if (replica != null && TeamSystem.Same(MultiplayerSession.LocalPeerId, replica.remotePeerId)) return;
        if (replica != null && KartPassengers.IsProtectedPassenger(replica.remoteBody))
        {
            return;
        }
        if (MultiplayerSession.PvpEnabled && currentShooter == PlayerScript.player?.bodyScript)
            ScoreboardSystem.RecordLocalPvpHit(amount, critical);
        if (MultiplayerSession.IsHost)
        {
            if (currentShooter == null || !currentShooter.isPlayer || MultiplayerSession.PvpEnabled)
            {
                replica.SpawnRemoteDeathDropIfNeeded(amount);
                SendRemotePlayerDamage(replica.remotePeerId, amount, critical, currentShooter);
            }
            return;
        }
        if (currentShooter == null) return;
        var localPlayer = PlayerScript.player;

        if (localPlayer == null || currentShooter != localPlayer.bodyScript) return;
        MultiplayerSession.Send(new PvpDamagePacket(amount, critical), replica.remotePeerId);
    }

    private static void SendRemotePlayerDamage(ushort targetPeerId, float amount, bool critical, BodyScript source)
    {
        if (MultiplayerSession.IsHost)
            MultiplayerSession.Send(PlayerDamagePacket.Damage(amount, critical, source != null && source.isPlayer,
                DamageSourcePeerId(source), DamageSourceName(source), DamageWeapon(source)), targetPeerId);
    }

    private static ushort DamageSourcePeerId(BodyScript source)
    {
        if (source == null || !source.isPlayer) return 0;
        var player = PlayerScript.player;
        return source == player?.bodyScript ? MultiplayerSession.LocalPeerId :
            NetworkAvatarRegistry.ReplicaForBody(source)?.remotePeerId ?? 0;
    }

    private static void RecordNetworkPlayerDamageSource(BodyScript victim, PlayerDamagePacket packet)
    {
        if (victim == null) return;
        var source = packet.SourcePeerId == MultiplayerSession.LocalPeerId ? PlayerScript.player?.bodyScript :
            NetworkAvatarRegistry.RemoteBodyForPeer(packet.SourcePeerId);
        if (source != null) SetDamageSource(victim, source, packet.SourceWeapon);
        else SetDamageSourceName(victim, packet.SourceName, packet.SourceWeapon);
        if (packet.SourcePeerId != 0) lastDamageSourcePeerIds[victim.GetInstanceID()] = packet.SourcePeerId;
    }

    private static string DamageSourceName(BodyScript source)
    {
        if (source == null) return "";
        if (source.isPlayer)
        {
            var player = PlayerScript.player;
            if (player != null && player.bodyScript == source) return MultiplayerSession.LocalPlayerName;
            return NetworkAvatarRegistry.RemoteNameForBody(source);
        }
        if (!string.IsNullOrWhiteSpace(source.characterName)) return source.characterName.Trim();
        return source.gameObject == null ? "Bot" : source.gameObject.name.Replace("(Clone)", "").Trim();
    }

    private static string DamageWeapon(BodyScript source)
    {
        var weapon = ActiveWeaponName(source);
        return string.IsNullOrEmpty(weapon) ? WeaponName(source == null ? null : source.weapon) : weapon;
    }

    private static void ApplyPlayerDamage(BodyScript body, PlayerDamagePacket playerDamage)
    {
        if (body == null) return;
        if (KartPassengers.IsProtectedPassenger(body))
        {
            return;
        }
        
        var amount = Mathf.Clamp(playerDamage.Amount, 0f, 1000f);
        var critical = playerDamage.Critical;
        var effectType = playerDamage.Effect;
        
        if (CameraFollow.cam != null)
        {
            CameraFollow.cam.AddOffset(new Vector2(
                UnityEngine.Random.Range(-amount, amount) * 0.3f,
                UnityEngine.Random.Range(-amount, amount) * 0.3f
            ));

            CameraFollow.cam.AddRot(UnityEngine.Random.Range(-amount, amount) * 0.2f);
        }
        
        if (effectType == PlayerDamageEffect.Explosion)
        {
            ApplyExplosionImpulse(body, playerDamage);
            return;
        }
        
        if (Time.unscaledTime < localRespawnProtectionUntil) return;
        
        if (amount > 0f && body.isAlive)
        {
            var appliedAmount = Mathf.Min(amount, Mathf.Max(0f, body.health));
            body.health -= amount;
            if (body == PlayerScript.player?.bodyScript)
                ScoreboardSystem.RecordLocalDamageReceived(appliedAmount);
            applyingNetworkPlayerDamage = true;
            try // TODO Debug exception handling
            {
                body.Damaged(critical);
                body.DoGrunt();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            finally { applyingNetworkPlayerDamage = false; }
        }
        
        if (effectType == PlayerDamageEffect.Wound) ApplyNetworkWound(body, playerDamage);
    }

    private static void ApplyExplosionImpulse(BodyScript body, PlayerDamagePacket packet)
    {
        var position = new Vector2(packet.ExplosionX, packet.ExplosionY);
        var range = packet.ExplosionRange;
        var force = packet.ExplosionForce;
        
        if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(range) || !IsFinite(force) ||
            range <= 0f || force <= 0f) return;
        
        range = Mathf.Min(range, 100f);
        force = Mathf.Min(force, 1000f);

        if (body.rb != null) body.lastMoveDir = body.rb.velocity;
        body.EnterHalfControl();

        var affected = new HashSet<Rigidbody2D>();
        if (body.rb != null && affected.Add(body.rb))
            ApplyExplosionForce(body.rb, body.rb.position, position, range, force);
        
        foreach (var collider in body.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider == null) continue;
            var rigidbody = collider.attachedRigidbody;
            if (rigidbody == null || !affected.Add(rigidbody)) continue;
            ApplyExplosionForce(rigidbody, collider.transform.position, position, range, force);
        }
    }

    private static bool ApplyExplosionForce(Rigidbody2D rigidbody, Vector2 targetPosition,
        Vector2 origin, float range, float force)
    {
        var offset = targetPosition - origin;
        if (offset.sqrMagnitude > range * range) return false;
        var direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector2.up;
        rigidbody.AddForce(direction * (force * rigidbody.mass), ForceMode2D.Impulse);
        rigidbody.AddTorque(UnityEngine.Random.Range(-force, force), ForceMode2D.Impulse);
        return true;
    }

    internal static bool BlockLocalRespawnDeath(BodyScript body)
    {
        if (body == null || Time.unscaledTime >= localRespawnProtectionUntil) return false;
        var player = PlayerScript.player;
        if (player == null || player.bodyScript != body) return false;

        ReviveRespawnBody(body);
        return true;
    }

    private static void ApplyNetworkWound(BodyScript body, PlayerDamagePacket packet)
    {
        var localPlayer = PlayerScript.player;

        if (localPlayer == null || localPlayer.bodyScript == null || body != localPlayer.bodyScript)
            return;
        
        var limbIndex = packet.LimbIndex;
        var localPoint = new Vector2(packet.LocalPointX, packet.LocalPointY);
        var direction = new Vector2(packet.DirectionX, packet.DirectionY);
        var weaponSprite = packet.WeaponSprite;
        var woundSprite = packet.WoundSprite;
        var hasSplash = packet.HasSplash;
        var createScreenCrack = packet.CreateScreenCrack;
        var limbs = GetList(body, "limbs");
        float baseDamage = Mathf.Clamp(packet.BaseDamage, 0f, 1000f);
        
        if (limbIndex >= 0 && limbIndex < limbs.Count)
        {
            var limb = limbs[limbIndex] as LimbScript;
            var preset = FindWeaponPreset(weaponSprite);
            
            if (limb != null && preset != null)
            {
                float staminaDamage = baseDamage * 1.38f;
                if (limb.isCritical)
                    staminaDamage += baseDamage * preset.critDamage;

                body.stamina -= staminaDamage;
                
                body.DoGrunt();

                if (limb.passer != null && limb.passer.relevantDismember != null)
                    limb.passer.relevantDismember.currentDamage += baseDamage;
                
                if (limb.limbType == 1) // arm (not ARM) (shake aim)
                {
                    if (packet.BodyColliderHit)
                    {
                        if (Mathf.Abs(body.currentRecoil) < 250f)
                        {
                            float recoilMult = 1f;
                            if (body.crouchAmount > 0.5f) recoilMult = 0.5f;
                            body.currentRecoil += localPlayer.aimPunchAmount * recoilMult;
                            localPlayer.aimPunchAmount *= -1f;
                        }
                    }
                    else
                    {
                        body.currentRecoil += localPlayer.aimPunchAmount;
                        localPlayer.aimPunchAmount *= -1f;
                    }
                }
                
                else if (limb.limbType == 2) // leg (reduce jump height)
                    body.temporarySlowdown += baseDamage * 0.065f;
                
                if (packet.BodyColliderHit && body.crouchAmount < 0.4f)
                {
                    body.crouchAmount += 0.15f;
                }
                
                GameObject sourceObject = new GameObject("MP Wound Source");
                sourceObject.SetActive(false);
                var sourceWeapon = sourceObject.AddComponent<WeaponScript>();
                sourceWeapon.stats = preset;
                sourceWeapon.body = body;
                var hitPoint = (Vector2)limb.transform.TransformPoint(localPoint);
                
                if (direction.sqrMagnitude > 0.001f) // hit velocity
                {
                    Vector2 hitDir = direction.normalized;
                    Rigidbody2D hitRb = packet.BodyColliderHit ? body.rb : limb.rb;

                    if (hitRb != null)
                        hitRb.AddForceAtPosition(hitDir * preset.knockback * 1.6f, hitPoint, ForceMode2D.Impulse);
                    
                    if (packet.BodyColliderHit && body.controlState != BodyScript.RagdollState.FullControl && limb.rb != null)
                        limb.rb.AddForceAtPosition(hitDir * preset.knockback * 1.65f, hitPoint, ForceMode2D.Impulse);
                }
                
                sourceWeapon.DoWound(limb, hitPoint, direction, hasSplash ? preset.bloodSplash : null);
                if (!string.IsNullOrEmpty(woundSprite))
                {
                    var wound = FindLatestWound(limb, hitPoint);
                    var sprite = FindSprite(woundSprite);
                    if (wound != null && sprite != null) wound.sprite = sprite;
                }
                
                if (sourceObject != null) Destroy(sourceObject);
                
                if (createScreenCrack && CameraFollow.cam != null)
                    CameraFollow.cam.CreateScreenCrack();
            }
        }
    }

    private static SpriteRenderer FindLatestWound(LimbScript limb, Vector2 hitPoint)
    {
        SpriteRenderer best = null;
        var bestDistance = float.MaxValue;
        foreach (var renderer in limb.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer == null || renderer.gameObject.name != "gunshotwound") continue;
            var distance = ((Vector2)renderer.transform.position - hitPoint).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = renderer;
        }
        return best;
    }

    private void SpawnRemoteDeathDropIfNeeded(float amount)
    {
        pendingRemoteDamage += amount;
        remoteDeathDropSpawned = true;
    }

    internal static bool BlockNetworkPlayerDrop(BodyScript body, bool allWeapons)
    {
        var player = PlayerScript.player;
        if ((!MultiplayerSession.IsConnected && !MultiplayerSession.IsHosting) || body == null) return false;
        var isLocalPlayer = player != null && body == player.bodyScript;
        var isStartingPlayerBody = body.GetComponentInParent<PlayerScript>() != null;
        if (!isLocalPlayer && !isStartingPlayerBody && !body.isPlayer && !NetworkAvatarRegistry.IsRemoteAvatarBody(body)) return false;
        if (allWeapons && MultiplayerSession.IsHost && (isLocalPlayer || NetworkAvatarRegistry.IsRemoteAvatarBody(body))) return false;
        if (isLocalPlayer && !MultiplayerSession.IsHost && body.isAlive && !allWeapons)
        {
            ClearDroppedWeapon(body, false);
            return true;
        }
        if (body.isAlive && !allWeapons) return false;
        ClearDroppedWeapon(body, allWeapons);
        return true;
    }

    internal static void ConsumeLocalDeathWeapon(BodyScript body, bool allWeapons)
    {
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || player == null || body == null || body.isAlive ||
            body != player.bodyScript) return;
        ClearDroppedWeapon(body, allWeapons);
    }

    private static void ClearDroppedWeapon(BodyScript body, bool allWeapons)
    {
        if (body.weapons == null || body.weaponAmmos == null) return;
        if (allWeapons)
        {
            for (var index = 0; index < body.weapons.Count; index++)
            {
                body.weapons[index] = null;
                if (index < body.weaponAmmos.Count) body.weaponAmmos[index] = 0;
            }
        }
        else
        {
            var slot = body.currentWeapon;
            if (slot >= 0 && slot < body.weapons.Count) body.weapons[slot] = null;
            if (slot >= 0 && slot < body.weaponAmmos.Count) body.weaponAmmos[slot] = 0;
        }
        if (!body.unarmed) body.ChangeToUnarmed();
    }

    private void UpdateLocalRespawn(PlayerScript player)
    {
        if (localRespawnPending) return;
        var body = player == null ? null : player.bodyScript;
        if (body == null) return;
        var scene = SceneManager.GetActiveScene();
        if (scene.handle != localSpawnScene)
        {
            localSpawnScene = scene.handle;
            localSpawnPosition = body.transform.position;
            localDeathPosition = localSpawnPosition;
            localWasAlive = body.isAlive;
            respawnAt = -1f;
        }

        if (body.isAlive)
        {
            localWasAlive = true;
            respawnAt = -1f;
            return;
        }


        if (localWasAlive)
        {
            localWasAlive = false;
            localDeathPosition = body.transform.position;
            respawnAt = MultiplayerSession.AllowRespawn
                ? Time.unscaledTime + MultiplayerSession.RespawnTimeSeconds
                : -1f;
        }
        if (MultiplayerSession.AllowRespawn && respawnAt >= 0f && Time.unscaledTime >= respawnAt)
            RespawnLocalPlayer(player, body);
    }

    private void UpdateSpectator(PlayerScript player)
    {
        var body = player == null ? null : player.bodyScript;
        if (body == null) return;
        if (MultiplayerSession.AllowRespawn || body.isAlive)
        {
            if (spectating && CameraFollow.cam != null) CameraFollow.cam.target = body.transform;
            RestoreSpectatorVisuals(player);
            spectating = false;
            spectatorPeerId = 0;
            return;
        }

        var candidates = new List<NetworkAvatarReplication>();
        foreach (var pair in NetworkAvatarRegistry.replicas)
        {
            var replica = pair.Value;
            if (replica != null && replica.remoteBody != null && replica.remoteBody.isAlive)
                candidates.Add(replica);
        }
        candidates.Sort((left, right) => left.remotePeerId.CompareTo(right.remotePeerId));

        if (candidates.Count == 0)
        {
            spectating = true;
            spectatorPeerId = 0;
            if (CameraFollow.cam != null) CameraFollow.cam.target = body.transform;
            SuppressSpectatorDeathEffects(player);
            return;
        }

        var requestedChange = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) ||
            Input.GetKeyDown(KeyCode.Q) || Input.mouseScrollDelta.y < 0f;
        var requestedNext = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) ||
            Input.GetKeyDown(KeyCode.E) || Input.mouseScrollDelta.y > 0f;
        var selectedIndex = -1;
        for (var index = 0; index < candidates.Count; index++)
            if (candidates[index].remotePeerId == spectatorPeerId)
            {
                selectedIndex = index;
                break;
            }
        if (selectedIndex < 0) selectedIndex = 0;
        if (requestedChange) selectedIndex = (selectedIndex + candidates.Count - 1) % candidates.Count;
        if (requestedNext) selectedIndex = (selectedIndex + 1) % candidates.Count;

        var target = candidates[selectedIndex];
        spectating = true;
        spectatorPeerId = target.remotePeerId;
        SuppressSpectatorDeathEffects(player);
        if (CameraFollow.cam != null && CameraFollow.cam.target != target.remoteBody.transform)
            CameraFollow.cam.target = target.remoteBody.transform;
    }

    internal static void SuppressSpectatorDeathEffects(PlayerScript player)
    {
        if (instance == null || !instance.spectating || MultiplayerSession.AllowRespawn ||
            player == null || player.bodyScript == null || player.bodyScript.isAlive) return;
        if (player.deathNoise != null) player.deathNoise.color = Color.clear;
        if (player.deathText != null) player.deathText.SetActive(false);
        if (player.crosshair != null) player.crosshair.gameObject.SetActive(false);
        if (player.crossDot != null) player.crossDot.gameObject.SetActive(false);
        if (player.crossLine != null) player.crossLine.gameObject.SetActive(false);
        var screen = ScreenFXManager.main;
        if (screen == null) return;
        screen.targetVign = 0f;
        if (screen.vign != null) screen.vign.intensity.value = 0f;
    }

    private static void RestoreSpectatorVisuals(PlayerScript player)
    {
        var screen = ScreenFXManager.main;
        if (screen != null) screen.targetVign = 0.45f;
        if (player == null) return;
        if (player.crosshair != null) player.crosshair.gameObject.SetActive(true);
        if (player.crossDot != null) player.crossDot.gameObject.SetActive(true);
        if (player.crossLine != null) player.crossLine.gameObject.SetActive(true);
    }

    private void RespawnLocalPlayer(PlayerScript player, BodyScript oldBody)
    {
        if (localRespawnPending) return;
        localRespawnPending = true;
        var generation = ++localRespawnGeneration;
        respawnAt = -1f;
        localRespawnProtectionUntil = Time.unscaledTime + RespawnProtectionSeconds;
        var prefabPath = string.IsNullOrEmpty(pendingRespawnCharacterPrefab)
            ? ResolveCharacterPrefab(oldBody) : pendingRespawnCharacterPrefab;
        var prefab = string.IsNullOrEmpty(prefabPath) ? null : Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            localRespawnPending = false;
            Debug.LogError("[Gunsaw MP] Could not respawn player: character prefab is missing.");
            return;
        }
        if (!string.IsNullOrEmpty(pendingRespawnCharacterPrefab))
        {
            selectedCharacterPrefab = pendingRespawnCharacterPrefab;
            PlayerPrefs.SetString("charPrefab", selectedCharacterPrefab);
            pendingRespawnCharacterPrefab = "";
        }
        EnsureRespawnWeaponSlots(oldBody);

        var position = ResolveRespawnPosition(oldBody);
        pendingRespawnLoadoutSource = prefab.GetComponentInChildren<BodyScript>(true);
        var avatar = Instantiate(prefab, position, Quaternion.identity);

        foreach (var prefabPlayer in avatar.GetComponentsInChildren<PlayerScript>(true))
            DestroyImmediate(prefabPlayer);
        var newBody = avatar.GetComponentInChildren<BodyScript>();
        if (newBody == null)
        {
            Destroy(avatar);
            localRespawnPending = false;
            Debug.LogError("[Gunsaw MP] Could not respawn player: character body is invalid.");
            return;
        }

        ReviveRespawnBody(newBody);
        newBody.isPlayer = true;
        newBody.team = "goodguys";
        newBody.crateDamage = true;
        newBody.healthRegen = newBody.regenOnSwap;
        newBody.isWalking = false;
        newBody.EnterFullControl();
        foreach (var chatter in avatar.GetComponentsInChildren<Chatter>(true)) DestroyImmediate(chatter);
        foreach (var ai in avatar.GetComponentsInChildren<AIScript>(true)) DestroyImmediate(ai);
        if (newBody.limbs == null || newBody.limbs.Count == 0)
        {
            Destroy(avatar);
            localRespawnPending = false;
            Debug.LogError("[Gunsaw MP] Could not respawn player: character has no initialized limbs.");
            return;
        }
        
        EnsureRespawnWeaponSlots(newBody);
        RestoreRespawnScarf(newBody);
        var levitator = newBody.gameObject.AddComponent<LevitatorScript>();
        levitator.levitMask = LayerMask.GetMask("Ground");
        levitator.grabMask = LayerMask.GetMask("Default", "Ground", "Entity", "EntityStand", "DropWeapon");
        levitator.rb = newBody.rb;
        levitator.refBody = newBody;
        var weaponBack = newBody.GetComponent<WeaponBackShow>();
        if (weaponBack != null) weaponBack.active = true;
        player.bodyScript = newBody;
        player.levit = levitator;
        player.enabled = true;
        localPlayerInstance = player;
        localGlobalBody = newBody.transform;
        RestoreLocalPlayerSingleton();
        localWasAlive = true;
        StartCoroutine(FinalizeLocalRespawn(newBody, oldBody, generation));
        
        if (ScreenFXManager.main != null) ScreenFXManager.main.Teleported();
        
        Sound.Play(EmbeddedAudioLoader.RespawnSound, position, true, false);
    }

    private IEnumerator FinalizeLocalRespawn(BodyScript newBody, BodyScript oldBody, int generation)
    {
        yield return null;
        try
        {
            if (generation != localRespawnGeneration)
            {
                var current = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
                if (newBody != null && newBody != current) Destroy(newBody.transform.root.gameObject);
                yield break;
            }
            if (newBody == null || newBody.limbs == null ||
                newBody.limbs.Count == 0) yield break;
            EnsureRespawnWeaponSlots(oldBody);
            EnsureRespawnWeaponSlots(newBody);

            if (localPlayerInstance == null) yield break;
            localPlayerInstance.bodyScript = newBody;
            localGlobalBody = newBody.transform;
            RestoreLocalPlayerSingleton();
            EnsurePlayerAmmoDisplaySlots(localPlayerInstance);

            ReviveRespawnBody(newBody);
            newBody.isPlayer = true;
            newBody.team = "goodguys";
           
            if (newBody.isInWater)
                newBody.EnterHalfControl();
            else
                newBody.EnterFullControl();
            
            if (oldBody != null)
            {
                oldBody.OnWeaponChanged.RemoveListener(localPlayerInstance.BodyWeaponChanged);
                oldBody.OnDeath.RemoveListener(localPlayerInstance.OnDied);
                oldBody.OnAmmoChanged.RemoveListener(localPlayerInstance.BodyAmmoChanged);
            }
            newBody.OnWeaponChanged.AddListener(localPlayerInstance.BodyWeaponChanged);
            newBody.OnDeath.AddListener(localPlayerInstance.OnDied);
            newBody.OnAmmoChanged.AddListener(localPlayerInstance.BodyAmmoChanged);
            localPlayerInstance.BodyWeaponChanged();
            localPlayerInstance.BodyAmmoChanged();
            RebindLimbDamageIndicators(localPlayerInstance, newBody, oldBody);
            localPlayerInstance.UnDie();
            pendingRespawnLoadoutBody = newBody;
            if (CameraFollow.cam != null) CameraFollow.cam.target = newBody.transform;
            localRespawnProtectionUntil = Time.unscaledTime + RespawnProtectionSeconds;
            if (localPlayerInstance.bloodBars != null)
                localPlayerInstance.bloodBars.body = newBody;
            
            Debug.Log("[Gunsaw MP] Local player respawned at " +
                (MultiplayerSession.RespawnAtStart ? "level start." : "death position."));
        }
        finally
        {
            if (oldBody != null && oldBody.transform != null && newBody != null &&
                oldBody.transform.root != newBody.transform.root)
                Destroy(oldBody.transform.root.gameObject);
            localRespawnPending = false;
        }
    }

    private static void RebindLimbDamageIndicators(PlayerScript player, BodyScript newBody, BodyScript oldBody)
    {
        if (player == null || newBody == null || player.dismemberMananagers == null) return;
        var managers = player.dismemberMananagers;
        var fallback = newBody.GetComponentsInChildren<DismemberManager>(true);
        var fallbackIndex = 0;
        for (var slot = 0; slot < managers.Length; slot++)
        {
            DismemberManager rebound = null;
            var previous = managers[slot];
            if (previous != null && oldBody != null && oldBody.limbs != null && newBody.limbs != null)
            {
                var limbIndex = oldBody.limbs.IndexOf(previous.GetComponent<LimbScript>());
                if (limbIndex >= 0 && limbIndex < newBody.limbs.Count && newBody.limbs[limbIndex] != null)
                    rebound = newBody.limbs[limbIndex].GetComponent<DismemberManager>();
            }
            if (rebound == null && fallbackIndex < fallback.Length) rebound = fallback[fallbackIndex++];
            managers[slot] = rebound;
        }
    }

    private static void RestoreRespawnScarf(BodyScript body)
    {
        if (body == null || body.limbs == null || body.limbs.Count < 2 || body.limbs[1] == null) return;
        RemoveReplicaScarfArtifacts(body.transform.root.gameObject);
        PlayerScript.AddScarfToCreature(body);
        var neck = body.limbs[1];
        var neckRenderer = neck.GetComponent<SpriteRenderer>();
        var sprite = Resources.Load<Sprite>("scarfImage");
        if (neckRenderer == null || sprite == null) return;
        var hold = new GameObject("ScarfHold", typeof(SpriteRenderer));
        var renderer = hold.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.material = body.limbMat;
        renderer.color = Color.HSVToRGB(PlayerPrefs.GetFloat("scStHue"),
            PlayerPrefs.GetFloat("scStSat"), PlayerPrefs.GetFloat("scStVal"));
        renderer.sortingOrder = neckRenderer.sortingOrder + 1;
        renderer.sortingLayerName = neckRenderer.sortingLayerName;
        hold.transform.SetParent(neck.transform);
        hold.transform.localPosition = Vector3.zero;
        hold.transform.localRotation = Quaternion.identity;
        hold.transform.localScale = Vector3.one;
    }

    private Vector3 ResolveRespawnPosition(BodyScript oldBody)
    {
        Vector3 spawnPoint;
        if (MultiplayerSession.RespawnAtStart &&
            CustomLevelSpawnSelection.TryGetRandomSpawnPosition(out spawnPoint))
        {
            if (!IsRespawnPositionBlocked(spawnPoint, oldBody)) return spawnPoint;
            for (var index = 0; index < 8; index++)
            {
                var angle = index * Mathf.PI * 0.25f;
                var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 2.5f;
                if (!IsRespawnPositionBlocked(spawnPoint + offset, oldBody)) return spawnPoint + offset;
            }
        }
        var candidate = MultiplayerSession.RespawnAtStart
            ? localSpawnPosition
            : (oldBody == null ? localDeathPosition : oldBody.transform.position);
        if (!IsRespawnPositionBlocked(candidate, oldBody)) return candidate;
        if (!MultiplayerSession.RespawnAtStart &&
            TryFindRespawnPositionNearPlayer(oldBody, out candidate)) return candidate;
        if (!IsRespawnPositionBlocked(localSpawnPosition, oldBody)) return localSpawnPosition;
        for (var index = 0; index < 8; index++)
        {
            var angle = index * Mathf.PI * 0.25f;
            var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 2.5f;
            if (!IsRespawnPositionBlocked(localSpawnPosition + offset, oldBody))
                return localSpawnPosition + offset;
        }
        return localSpawnPosition;
    }

    private static bool TryFindRespawnPositionNearPlayer(BodyScript oldBody, out Vector3 position)
    {
        foreach (var player in FindObjectsOfType<BodyScript>())
        {
            if (player == null || player == oldBody || !player.isPlayer || !player.isAlive ||
                !player.gameObject.activeInHierarchy) continue;
            for (var index = 0; index < 8; index++)
            {
                var angle = index * Mathf.PI * 0.25f;
                var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 2.5f;
                var candidate = player.transform.position + offset;
                if (!IsRespawnPositionBlocked(candidate, oldBody))
                {
                    position = candidate;
                    return true;
                }
            }
        }
        position = default(Vector3);
        return false;
    }

    private static bool IsRespawnPositionBlocked(Vector3 position, BodyScript oldBody)
    {
        foreach (var body in FindObjectsOfType<BodyScript>())
        {
            if (body == null || body == oldBody || body.isPlayer || !body.isAlive ||
                !body.gameObject.activeInHierarchy) continue;
            if (Vector2.Distance(position, body.transform.position) < 2.25f) return true;
        }
        return false;
    }

    private static void ReviveRespawnBody(BodyScript body)
    {
        if (body == null) return;
        if (body.maxHealth <= 0f) body.maxHealth = 100f;
        body.health = body.maxHealth;
        body.CurrentState = 0;
        body.controlState = BodyScript.RagdollState.FullControl;
        body.isAlive = true;
        body.WakeUp();
        body.health = body.maxHealth;
        body.isAlive = true;
    }

    internal static void EnsureRespawnWeaponSlots(BodyScript body)
    {
        if (body == null) return;

        if (body.weapons == null) body.weapons = new List<WeaponPreset>();
        while (body.weapons.Count < 3) body.weapons.Add(null);

        if (body.weaponAmmos == null) body.weaponAmmos = new List<int>();
        while (body.weaponAmmos.Count < body.weapons.Count) body.weaponAmmos.Add(0);

        if (body.currentWeapon < 0 || body.currentWeapon >= 3)
            body.currentWeapon = 0;
    }

    internal static void ApplyLobbyLoadout(BodyScript body, string rule)
    {
        if (body == null || string.Equals((rule ?? "").Trim(), "Default", StringComparison.OrdinalIgnoreCase)) return;
        EnsureRespawnWeaponSlots(body);
        var values = (rule ?? "").Split(';');
        var firstWeapon = -1;
        for (var slot = 0; slot < 3; slot++)
        {
            var value = slot < values.Length ? values[slot].Trim() : "None";
            if (string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
            {
                if (body.weapons[slot] != null && firstWeapon < 0) firstWeapon = slot;
                continue;
            }
            if (string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(value))
            {
                body.weapons[slot] = null;
                body.weaponAmmos[slot] = 0;
                continue;
            }
            var preset = FindWeaponPresetByName(value);
            body.weapons[slot] = preset;
            body.weaponAmmos[slot] = preset == null ? 0 : preset.magSize;
            if (preset != null && firstWeapon < 0) firstWeapon = slot;
        }
        if (firstWeapon >= 0)
        {
            body.currentWeapon = firstWeapon;
            body.ChangeWeapon(firstWeapon);
        }
        else body.ChangeToUnarmed();
    }

    private void ApplyStartingLobbyLoadout(BodyScript body)
    {
        if (body == null || body == startingLoadoutAppliedBody || localRespawnPending) return;
        var rule = MultiplayerSession.StartingWeapon;
        if (string.Equals((rule ?? "").Trim(), "Default", StringComparison.OrdinalIgnoreCase)) return;
        ApplyLobbyLoadout(body, rule);
        startingLoadoutAppliedBody = body;
    }

    private void ApplyPendingRespawnLobbyLoadout(BodyScript body)
    {
        if (body == null || body != pendingRespawnLoadoutBody || !body.isAlive) return;
        var rule = MultiplayerSession.RespawnWeapon;
        if (string.Equals((rule ?? "").Trim(), "Default", StringComparison.OrdinalIgnoreCase))
            ApplyDefaultLobbyLoadout(body, pendingRespawnLoadoutSource);
        else ApplyLobbyLoadout(body, rule);
        LobbyAmmoRules.Apply(body, MultiplayerSession.RespawnAmmo);
        startingLoadoutAppliedBody = body;
        startingAmmoAppliedBody = body;
        pendingRespawnLoadoutBody = null;
        pendingRespawnLoadoutSource = null;
    }

    private static void ApplyDefaultLobbyLoadout(BodyScript body, BodyScript source)
    {
        if (body == null) return;
        EnsureRespawnWeaponSlots(body);
        var preset = body.desiredStartWep ?? (source == null ? null : source.desiredStartWep);
        if (preset != null && preset.slot >= 0 && preset.slot < 3)
        {
            for (var slot = 0; slot < 3; slot++)
            {
                body.weapons[slot] = null;
                body.weaponAmmos[slot] = 0;
            }
            body.weapons[preset.slot] = preset;
            body.weaponAmmos[preset.slot] = preset.magSize;
            body.currentWeapon = preset.slot;
            body.ChangeWeapon(preset.slot);
            return;
        }
        ApplyLobbyLoadout(body, "None;None;None");
    }

    private void ApplyStartingLobbyAmmo(BodyScript body)
    {
        if (body == null || body == startingAmmoAppliedBody || !MultiplayerSession.LobbySettingsReceived) return;
        LobbyAmmoRules.Apply(body, MultiplayerSession.StartingAmmo);
        startingAmmoAppliedBody = body;
    }

    private static WeaponPreset FindWeaponPresetByName(string name)
    {
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (preset != null && preset.sprite != null && string.Equals(preset.name, name, StringComparison.OrdinalIgnoreCase))
                return preset;
        return null;
    }

    internal static void EnsurePlayerAmmoDisplaySlots(PlayerScript player)
    {
        if (player == null || player.bodyScript == null) return;
        var ammoTexts = player.ammoTexts;
        var required = ammoTexts == null ? 0 : ammoTexts.Count;
        if (required <= 0) return;
        if (player.bodyScript.ammoAmount == null)
            player.bodyScript.ammoAmount = new List<int>();
        while (player.bodyScript.ammoAmount.Count < required)
            player.bodyScript.ammoAmount.Add(0);
    }

    internal static bool PrepareWeaponReload(WeaponScript weapon)
    {
        if (weapon == null || weapon.stats == null || weapon.body == null) return false;

        var body = weapon.body;
        var ammoType = weapon.stats.ammoType;
        if (ammoType < 0) return false;
        if (body.ammoAmount == null) body.ammoAmount = new List<int>();
        while (body.ammoAmount.Count <= ammoType) body.ammoAmount.Add(0);

        var slot = body.currentWeapon;
        if (slot < 0) return false;
        if (body.weaponAmmos == null) body.weaponAmmos = new List<int>();
        while (body.weaponAmmos.Count <= slot) body.weaponAmmos.Add(0);

        if (!weapon.stats.hasSpecialReload) return true;
        if (weapon.stats.specialAnims == null) return false;
        var animationIndex = body.ammoAmount[ammoType] < weapon.stats.magSize &&
            body.ammoAmount[ammoType] < weapon.stats.magSize - weapon.ammo
            ? weapon.stats.magSize - body.ammoAmount[ammoType]
            : weapon.ammo;
        return animationIndex >= 0 && animationIndex < weapon.stats.specialAnims.Length;
    }

    private static void EnsureLocalPlayerSingleton()
    {
        if (localPlayerInstance != null && localPlayerInstance.bodyScript != null &&
            localPlayerInstance.gameObject.activeInHierarchy)
        {
            localGlobalBody = localPlayerInstance.bodyScript.transform;
            RestoreLocalPlayerSingleton();
            return;
        }

        var current = PlayerScript.player;
        if (current != null && current.bodyScript != null)
        {
            localPlayerInstance = current;
            if (PlayerScript.globalBody == null)
                localGlobalBody = current.bodyScript.transform;
            RestoreLocalPlayerSingleton();
            return;
        }

        foreach (var candidate in FindObjectsOfType<PlayerScript>())
        {
            if (candidate == null || candidate.bodyScript == null ||
                !candidate.bodyScript.isPlayer) continue;
            localPlayerInstance = candidate;
            localGlobalBody = candidate.bodyScript.transform;
            RestoreLocalPlayerSingleton();
            return;
        }
    }

    private static void ApplyPvpDamage(BodyScript body, ushort senderId, PlayerDamagePacket packet)
    {
        if (!MultiplayerSession.PvpEnabled || body == null || TeamSystem.Same(MultiplayerSession.LocalPeerId, senderId)) return;
        var source = senderId == MultiplayerSession.LocalPeerId ? body : NetworkAvatarRegistry.RemoteBodyForPeer(senderId);
        RecordDamageSource(body, source);
        ApplyPlayerDamage(body, packet);
    }

    private static void ClearReplicaBloodEffects(BodyScript body)
    {
        if (body == null) return;
        foreach (var transform in body.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null || transform == body.transform) continue;
            var name = transform.name;
            if (name != "gunshotwound" && !name.StartsWith("BloodSplash", StringComparison.Ordinal)) continue;
            Destroy(transform.gameObject);
        }
    }

    private void UpdateRemotePhysicsMode()
    {
        if (remoteAvatar == null) return;
        var player = PlayerScript.player;
        if (remoteBody != null && player != null && player.bodyScript != null)
            remoteBody.team = RemoteTeam(player.bodyScript);

        ApplyPlayerCollisionRule(player == null ? null : player.bodyScript);

        if (remotePhysicsModeKnown) return;
        remotePhysicsModeKnown = true;
        
        foreach (var body in remoteRigidbodies)
        {
            if (body == null) continue;
            body.simulated = true;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
        
        foreach (var pair in remoteColliderTriggers)
            if (pair.Key != null) pair.Key.isTrigger = pair.Value;
        
        if (MultiplayerSession.IsHost)
            foreach (var prop in FindObjectsOfType<Rigidbody2D>())
                if (prop != null && (prop.GetComponentInParent<CrateScript>() != null || prop.GetComponentInParent<DroppedWeapon>() != null))
                    IgnoreRemotePlayerPropCollisions(prop);
    }

    internal static void IgnoreRemotePlayerPropCollisions(Rigidbody2D prop)
    {
        if (prop == null || !MultiplayerSession.IsHost) return;
        var propColliders = prop.GetComponentsInChildren<Collider2D>(true);
        if (propColliders == null || propColliders.Length == 0) return;
        foreach (var replica in NetworkAvatarRegistry.replicas.Values)
        {
            if (replica == null) continue;
            foreach (var remoteCollider in replica.remoteColliderTriggers.Keys)
            {
                if (remoteCollider == null) continue;
                foreach (var propCollider in propColliders)
                    if (propCollider != null)
                        Physics2D.IgnoreCollision(remoteCollider, propCollider, true);
            }
        }
    }

    internal static void TryGrabRemotePlayer(LevitatorScript levitator)
    {
        if (instance == null || levitator == null || levitator.currentlyLevitating != null ||
            !MultiplayerSession.IsConnected || !MultiplayerSession.CanGrabPlayers) return;
        var camera = Camera.main;
        if (camera == null || levitator.refBody == null) return;
        var mouse = (Vector2)camera.ScreenToWorldPoint(Input.mousePosition);
        var origin = (Vector2)levitator.refBody.transform.position;
        foreach (var hit in Physics2D.LinecastAll(origin, mouse))
        {
            var collider = hit.collider;
            if (collider == null || collider.GetComponentInParent<BodyScript>() == levitator.refBody ||
                collider.gameObject.layer == LayerMask.NameToLayer("Cosmetic")) continue;
            var marker = collider.GetComponentInParent<NetworkReplica>();
            if (marker == null)
            {
                if (!collider.isTrigger) break;
                continue;
            }
            var remote = NetworkAvatarRegistry.ReplicaForBody(collider.GetComponentInParent<BodyScript>());
            if (remote == null || !CanGrabBody(remote.remoteBody)) continue;
            var rigidbody = hit.rigidbody == null ? collider.attachedRigidbody : hit.rigidbody;
            if (rigidbody == null) return;
            levitator.currentlyLevitating = rigidbody;
            levitator.point = hit.point;
            levitator.localGrabPoint = rigidbody.transform.InverseTransformPoint(hit.point);
            return;
        }
    }

    internal static void ValidateRemoteGrab(LevitatorScript levitator)
    {
        if (levitator == null || levitator.currentlyLevitating == null ||
            levitator.currentlyLevitating.GetComponentInParent<NetworkReplica>() == null) return;
        var body = levitator.currentlyLevitating.GetComponentInParent<BodyScript>();
        if (!MultiplayerSession.CanGrabPlayers || !CanGrabBody(body)) levitator.UnGrab();
    }

    internal static void QueueRemoteGrab(LevitatorScript levitator)
    {
        if (instance == null || !MultiplayerSession.IsConnected || levitator == null) return;
        var target = levitator.currentlyLevitating;
        var targetBody = target == null ? null : target.GetComponentInParent<BodyScript>();
        var replica = NetworkAvatarRegistry.ReplicaForBody(targetBody);
        byte kind;
        short index;
        if (target != null && replica != null && CanGrabBody(targetBody) &&
            replica.TryRemotePart(target, out kind, out index))
        {
            if (instance.outgoingGrabPeerId != 0 && instance.outgoingGrabPeerId != replica.remotePeerId)
                MultiplayerSession.Send(new PlayerGrabPacket(false), instance.outgoingGrabPeerId);
            MultiplayerSession.Send(new PlayerGrabPacket(true, kind, index, levitator.point.x,
                levitator.point.y, levitator.localGrabPoint.x, levitator.localGrabPoint.y), replica.remotePeerId);
            instance.outgoingGrabPeerId = replica.remotePeerId;
            return;
        }
        if (instance.outgoingGrabPeerId == 0) return;
        MultiplayerSession.Send(new PlayerGrabPacket(false), instance.outgoingGrabPeerId);
        instance.outgoingGrabPeerId = 0;
    }

    private void ReceivePlayerGrab(PlayerGrabPacket packet)
    {
        if (!MultiplayerSession.CanGrabPlayers || !packet.IsGrabbing)
        {
            incomingGrabUntil = 0f;
            return;
        }
        var command = new GrabCommand
        {
            Kind = packet.PartKind,
            Index = packet.PartIndex,
            Point = new Vector2(packet.PointX, packet.PointY),
            LocalPoint = new Vector2(packet.LocalPointX, packet.LocalPointY)
        };
        if (!IsFinite(command.Point.x) || !IsFinite(command.Point.y) ||
            !IsFinite(command.LocalPoint.x) || !IsFinite(command.LocalPoint.y)) return;
        incomingGrab = command;
        incomingGrabUntil = Time.unscaledTime + 0.15f;
    }

    private void ApplyIncomingGrab()
    {
        if (Time.unscaledTime > incomingGrabUntil || !MultiplayerSession.CanGrabPlayers) return;
        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        if (body == null || !CanGrabBody(body)) { incomingGrabUntil = 0f; return; }
        var rigidbody = ResolveLocalPart(body, incomingGrab.Kind, incomingGrab.Index);
        if (rigidbody == null || !rigidbody.simulated) return;
        var force = incomingGrab.Point - rigidbody.position;
        if (force.magnitude > 5f) force = force.normalized * 5f;
        rigidbody.AddForceAtPosition(force * 100f, rigidbody.transform.TransformPoint(incomingGrab.LocalPoint));
        rigidbody.angularVelocity *= 0.96f;
        if (force.magnitude > 2f && body.controlState == 0) body.EnterHalfControl();
    }

    private bool TryRemotePart(Rigidbody2D rigidbody, out byte kind, out short index)
    {
        kind = 0;
        index = 0;
        if (remoteBody == null) return false;
        if (rigidbody == remoteBody.rb) return true;
        var limbs = GetList(remoteBody, "limbs");
        for (var position = 0; position < limbs.Count; position++)
            if (((LimbScript)limbs[position]).rb == rigidbody)
            {
                kind = 1;
                index = (short)position;
                return true;
            }
        var tails = GetNetworkTailBodies(remoteBody);
        for (var position = 0; position < tails.Count; position++)
            if ((Rigidbody2D)tails[position] == rigidbody)
            {
                kind = 2;
                index = (short)position;
                return true;
            }
        return false;
    }

    private static Rigidbody2D ResolveLocalPart(
        BodyScript body,
        byte kind,
        int index)
    {
        if (body == null)
            return null;

        if (kind == 0)
            return body.rb;

        if (kind == 1)
        {
            var limbs = GetList(body, "limbs");

            if (index < 0 || index >= limbs.Count)
                return null;

            return ((LimbScript)limbs[index]).rb;
        }

        if (kind == 2)
        {
            var tails = GetNetworkTailBodies(body);

            if (index < 0 || index >= tails.Count)
                return null;

            return tails[index];
        }

        return null;
    }

    private static bool CanGrabBody(BodyScript body)
    {
        if (body == null || !MultiplayerSession.CanGrabPlayers) return false;
        if (!MultiplayerSession.GrabOnlyUnconscious) return true;
        var replica = NetworkAvatarRegistry.ReplicaForBody(body);
        if (replica != null) return replica.remoteCanBeGrabbed;
        return CanGrabOnlyState(body);
    }

    private static bool CanGrabOnlyState(BodyScript body)
    {
        if (body == null) return false;
        if (!body.isAlive) return true;
        if (body.inVehicle) return false;
        return !body.IsConsc() || !body.CanMove() || body.health < body.dyingStateTreshold;
    }

    internal static ShotState BeginWeaponShot(WeaponScript weapon)
    {
        var state = new ShotState
        {
            PreviousShooter = currentShooter,
            PreviousShotState = activeShotState,
            Weapon = weapon,
            AmmoBefore = weapon == null ? 0 : weapon.ammo,
            SpreadSeed = Interlocked.Increment(ref nextShotSpreadSeed)
        };
        activeShotState = state;
        currentShooter = weapon == null ? null : weapon.body;
        if (weapon != null && weapon.stats != null)
        {
            state.Origin = weapon.transform.TransformPoint(weapon.stats.barrelPosition);
            var facing = weapon.body != null && !weapon.body.isRight ? -1f : 1f;
            state.Direction = (Vector2)(weapon.transform.right * facing);
            state.Up = weapon.transform.up;
            state.WeaponSprite = SpriteId(weapon.stats.sprite);
        }
        GunsawMultiplayerPlugin.World?.enviroment?.CaptureDestroyedLampIds(state.DestroyedLampIds);
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || MultiplayerSession.PvpEnabled ||
            instance == null || player == null || currentShooter != player.bodyScript)
            return state;
        foreach (var replica in NetworkAvatarRegistry.replicas.Values)
            if (replica != null)
                foreach (var collider in replica.remoteColliderTriggers.Keys)
                {
                    if (collider == null || !collider.enabled) continue;
                    collider.enabled = false;
                    state.DisabledColliders.Add(collider);
                }
        return state;
    }

    internal static ShotState BeginMeleeAttack(BodyScript attacker)
    {
        var state = new ShotState { PreviousShooter = currentShooter };
        currentShooter = attacker;
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || MultiplayerSession.PvpEnabled ||
            instance == null || player == null || attacker != player.bodyScript)
            return state;
        foreach (var replica in NetworkAvatarRegistry.replicas.Values)
            if (replica != null)
                foreach (var collider in replica.remoteColliderTriggers.Keys)
                {
                    if (collider == null || !collider.enabled) continue;
                    collider.enabled = false;
                    state.DisabledColliders.Add(collider);
                }
        return state;
    }

    internal static void EndMeleeAttack(ShotState state)
    {
        EndWeaponShot(state);
    }

    internal static void PrepareNpcTarget(AIScript ai)
    {
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || instance == null ||
            ai == null || ai.body == null || ai.followPlayer) return;
        var player = PlayerScript.player;
        var localBody = player == null ? null : player.bodyScript;
        if (localBody == null) return;
        var current = ai.targetBody;
        if (current != null && current != localBody && NetworkAvatarRegistry.ReplicaForBody(current) == null) return;

        BodyScript best = null;
        var bestDistance = float.MaxValue;
        SelectNpcPlayerTarget(ai.body, localBody, ref best, ref bestDistance);
        foreach (var replica in NetworkAvatarRegistry.replicas.Values)
        {
            var remote = replica == null ? null : replica.remoteBody;
            if (remote == null) continue;
            remote.isPlayer = true;
            remote.team = RemoteTeam(localBody);
            SelectNpcPlayerTarget(ai.body, remote, ref best, ref bestDistance);
        }
        if (best != null) ai.targetBody = best;
    }

    private static void SelectNpcPlayerTarget(BodyScript npc, BodyScript candidate,
        ref BodyScript best, ref float bestDistance)
    {
        if (candidate == null || !candidate.isAlive || !candidate.gameObject.activeInHierarchy ||
            candidate.team == npc.team) return;
        var distance = Vector2.Distance(npc.transform.position, candidate.transform.position);
        if (distance > 40f || distance >= bestDistance) return;
        var from = npc.headTransform == null ? (Vector2)npc.transform.position : (Vector2)npc.headTransform.position;
        var to = candidate.headTransform == null
            ? (Vector2)candidate.transform.position
            : (Vector2)candidate.headTransform.position;
        if (distance >= 3.5f && Physics2D.Linecast(from, to, LayerMask.GetMask("Ground"))) return;
        best = candidate;
        bestDistance = distance;
    }

    private static string RemoteTeam(BodyScript localBody)
    {
        return MultiplayerSession.PvpEnabled ? PvpRemoteTeam : localBody.team;
    }

    internal static ShotState BeginProjectileExplosion(GameObject projectile)
    {
        var state = new ShotState
        {
            PreviousShooter = currentShooter,
            PreviousShotState = activeShotState,
            IsExplosion = true
        };
        activeShotState = state;
        currentShooter = ProjectileOwner(projectile);
        return state;
    }

    internal static RocketProjectile BeginRocketUpdate(RocketProjectile projectile)
    {
        var previous = activeRocketProjectile;
        activeRocketProjectile = projectile;
        return previous;
    }

    internal static void EndRocketUpdate(RocketProjectile previous)
    {
        activeRocketProjectile = previous;
    }

    internal static GameObject ResolveExplosionProjectile(GameObject projectile)
    {
        return projectile == null && activeRocketProjectile != null
            ? activeRocketProjectile.gameObject : projectile;
    }

    internal static void ReplicateProjectileImpact(GameObject projectile, Vector2 position)
    {
        if (!MultiplayerSession.IsConnected || projectile == null || !IsFinite(position.x) ||
            !IsFinite(position.y)) return;
        var rocket = projectile.GetComponentInChildren<RocketProjectile>(true);
        var grenade = projectile.GetComponentInChildren<GrenadeScript>(true);
        if (rocket == null && grenade == null) return;
        var shooter = ProjectileOwner(projectile);
        var player = PlayerScript.player;
        var localPlayerShot = shooter != null && player != null && shooter == player.bodyScript;
        var hostNpcShot = MultiplayerSession.IsHost && shooter != null && !shooter.isPlayer &&
            shooter.GetComponentInParent<NetworkReplica>() == null;
        if (!localPlayerShot && !hostNpcShot) return;
        var weapon = shooter == null ? null : shooter.weapon;
        MultiplayerSession.Send(new ProjectileImpactPacket(position.x, position.y,
            SpriteId(weapon == null || weapon.stats == null ? null : weapon.stats.sprite)));
    }

    internal static void ReplicateVelvetWeb(VelvetScript velvet)
    {
        if (!MultiplayerSession.IsConnected || velvet == null) return;
        var body = velvet.GetComponent<BodyScript>();
        var player = PlayerScript.player;
        if (body == null || player == null || body != player.bodyScript || body.headTransform == null) return;
        WebScript web = null;
        var origin = (Vector2)body.headTransform.position - (Vector2)body.headTransform.up * 0.2f;
        foreach (var candidate in FindObjectsOfType<WebScript>())
        {
            if (candidate == null || localVelvetWebs.Contains(candidate) ||
                ((Vector2)candidate.transform.position - origin).sqrMagnitude > 1f) continue;
            web = candidate;
            break;
        }
        if (web == null) return;
        localVelvetWebs.Add(web);
        localVelvetWebs.RemoveWhere(candidate => candidate == null);
        var direction = (Vector2)web.transform.right;
        if (direction.sqrMagnitude < 0.01f) return;
        var normalizedDirection = direction.normalized;
        MultiplayerSession.Send(new VelvetWebPacket(web.transform.position.x, web.transform.position.y,
            normalizedDirection.x, normalizedDirection.y));
    }

    internal static void ReplicateTeleportZone(TeleportZone zone, int activationId)
    {
        if (!MultiplayerSession.IsHost || zone == null || zone.id != activationId || zone.teleportPoint == null)
            return;
        var collider = zone.GetComponent<BoxCollider2D>();
        if (collider == null) return;
        var teleported = new HashSet<BodyScript>();
        foreach (var candidate in Physics2D.OverlapBoxAll(zone.transform.position, collider.size,
            zone.transform.eulerAngles.z))
        {
            BodyScript body;
            if (!candidate.TryGetComponent(out body))
            {
                LimbScript limb;
                if (!candidate.TryGetComponent(out limb) || limb == null) continue;
                body = limb.body;
            }
            if (body == null || !teleported.Add(body)) continue;
        }
        foreach (var replica in NetworkAvatarRegistry.replicas.Values)
        {
            if (replica == null || replica.remoteBody == null || replica.remotePeerId == 0) continue;
            if (!teleported.Contains(replica.remoteBody) &&
                !IsInsideTeleportZone(replica.remoteBody, zone.transform, collider)) continue;
            if (MultiplayerSession.IsHost)
                MultiplayerSession.Send(new PlayerTeleportPacket(zone.teleportPoint.position.x,
                    zone.teleportPoint.position.y), replica.remotePeerId);
        }
    }

    internal static List<SuppressedTeleportBody> SuppressRemoteTeleportEffects(TeleportZone zone, int activationId)
    {
        var suppressed = new List<SuppressedTeleportBody>();
        if (!MultiplayerSession.IsHost || zone == null || zone.id != activationId) return suppressed;
        var collider = zone.GetComponent<BoxCollider2D>();
        if (collider == null) return suppressed;
        foreach (var replica in NetworkAvatarRegistry.replicas.Values)
        {
            var body = replica == null ? null : replica.remoteBody;
            if (body == null || !body.isPlayer || !IsInsideTeleportZone(body, zone.transform, collider)) continue;
            suppressed.Add(new SuppressedTeleportBody(body));
            body.isPlayer = false;
        }
        return suppressed;
    }

    internal static void RestoreRemoteTeleportEffects(List<SuppressedTeleportBody> suppressed)
    {
        if (suppressed == null) return;
        foreach (var state in suppressed)
        {
            if (state.Body == null) continue;
            state.Body.transform.position = state.Position;
            state.Body.isPlayer = state.IsPlayer;
        }
    }

    private static bool IsInsideTeleportZone(BodyScript body, Transform zone, BoxCollider2D collider)
    {
        if (IsInsideTeleportZone(body.transform.position, zone, collider)) return true;
        foreach (var limb in body.GetComponentsInChildren<LimbScript>(true))
            if (limb != null && IsInsideTeleportZone(limb.transform.position, zone, collider)) return true;
        return false;
    }

    private static bool IsInsideTeleportZone(Vector3 point, Transform zone, BoxCollider2D collider)
    {
        var localPoint = (Vector2)zone.InverseTransformPoint(point) - collider.offset;
        var halfSize = collider.size * 0.5f;
        return Mathf.Abs(localPoint.x) <= halfSize.x && Mathf.Abs(localPoint.y) <= halfSize.y;
    }

    private static void ApplyRemoteTeleport(BodyScript body, PlayerTeleportPacket packet)
    {
        if (body == null || MultiplayerSession.IsHost) return;
        var position = new Vector2(packet.PositionX, packet.PositionY);
        if (!IsFinite(position.x) || !IsFinite(position.y)) return;
        body.transform.position = position;
        if (CameraFollow.cam != null) CameraFollow.cam.CenterToPlayer();
        if (ScreenFXManager.main != null) ScreenFXManager.main.Teleported();
        foreach (var unloader in FindObjectsOfType<ObjectUnloader>())
            if (unloader != null) unloader.CheckDistance();
        var sound = Resources.Load<AudioClip>("Sounds/Teleport");
        if (sound != null) Sound.Play(sound, position, false, false);
    }

    internal static void RouteVehicleImpact(BodyScript body, float impact, Vector2 position, bool ragdoll)
    {
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || body == null ||
            !IsFinite(impact) || impact <= 6f) return;
        var replica = NetworkAvatarRegistry.ReplicaForBody(body);
        if (replica == null || replica.remotePeerId == 0 || !replica.receivedFirstSnapshot ||
            KartPassengers.IsProtectedPassenger(body)) return;
        MultiplayerSession.Send(new VehicleImpactPacket(impact, position.x, position.y, ragdoll),
            replica.remotePeerId);
    }

    private static void ApplyVehicleImpact(BodyScript body, VehicleImpactPacket packet)
    {
        if (body == null || !body.isAlive || !IsFinite(packet.Impact) || packet.Impact <= 6f ||
            Time.unscaledTime < localRespawnProtectionUntil || KartPassengers.IsProtectedPassenger(body)) return;
        var impact = Mathf.Min(packet.Impact, 1000f);
        body.shockTime += 3f;
        
        if (packet.Ragdoll && body.controlState == BodyScript.RagdollState.FullControl) 
            body.EnterHalfControl();
        
        body.health -= impact * 2.5f;
        body.stamina -= impact * 3f;
        body.temporarySlowdown += impact * 0.1f;
        if (GameManager.main != null && IsFinite(packet.PositionX) && IsFinite(packet.PositionY))
            GameManager.main.DamageNumber(new Vector2(packet.PositionX, packet.PositionY), impact * 2.5f, body);
    }

    private static void ApplyVehicleEject(BodyScript body)
    {
        if (body != null && !MultiplayerSession.IsHost && body.inVehicle)
        {
            Vector2 yVelocity = (Vector2)body.curVehicle.mainPart.transform.up * 10f;
            body.ExitVehicle();
            body.lastMoveDir += yVelocity;
            body.EnterHalfControl();
            body.Damaged();
        }
          
    }

    private static void HandleTeleportRequest(ushort requesterId, TeleportRequestPacket request)
    {
        if (!MultiplayerSession.IsHost || MultiplayerSession.PvpEnabled || requesterId == 0) return;
        var target = request.TargetPeerId == MultiplayerSession.LocalPeerId
            ? (PlayerScript.player == null ? null : PlayerScript.player.bodyScript)
            : NetworkAvatarRegistry.RemoteBodyForPeer(request.TargetPeerId);
        if (target == null || !target.isAlive) return;
        var position = target.transform.position;
        if (!IsFinite(position.x) || !IsFinite(position.y)) return;
        MultiplayerSession.Send(new PlayerTeleportPacket(position.x, position.y), requesterId);
    }

    private void PlayRemoteVelvetWeb(VelvetWebPacket packet)
    {
        if (remoteBody == null) return;
        var origin = new Vector2(packet.PositionX, packet.PositionY);
        var direction = new Vector2(packet.DirectionX, packet.DirectionY);
        if (!IsFinite(origin.x) || !IsFinite(origin.y) || !IsFinite(direction.x) ||
            !IsFinite(direction.y) || direction.sqrMagnitude < 0.01f) return;
        var velvet = remoteBody.GetComponent<VelvetScript>();
        var prefab = velvet == null ? null : velvet.spawnPrefab;
        if (prefab == null) return;
        var visual = Instantiate(prefab, origin, Quaternion.identity);
        var web = visual.GetComponent<WebScript>();
        if (web == null)
        {
            Destroy(visual);
            return;
        }
        var speed = web.speed;
        var groundSprite = web.groundSprite;
        var bodySprite = web.bodySprite;
        visual.name = "MP Velvet Web";
        visual.transform.right = direction.normalized;
        foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        foreach (var collider in visual.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
        foreach (var rigidbody in visual.GetComponentsInChildren<Rigidbody2D>(true)) rigidbody.simulated = false;
        foreach (var source in visual.GetComponentsInChildren<AudioSource>(true)) source.enabled = false;
        var splash = Resources.Load<GameObject>("Spawnables/WebSplashSpit");
        if (splash != null)
        {
            var effect = Instantiate(splash, origin, Quaternion.identity);
            effect.transform.right = direction.normalized;
            Destroy(effect, 10f);
        }
        var sound = Resources.Load<AudioClip>("Sounds/spit");
        if (sound != null) Sound.Play(sound, origin);
        StartCoroutine(MoveRemoteVelvetWeb(visual, speed, groundSprite, bodySprite, remoteBody));
    }

    private static IEnumerator MoveRemoteVelvetWeb(GameObject visual, float speed, Sprite groundSprite, Sprite bodySprite, BodyScript ignoredBody)
    {
        var downwardVelocity = 0f;
        var remaining = 5f;
        while (visual != null && remaining > 0f)
        {
            Transform visualTransform = visual.transform;
            float deltaTime = Time.deltaTime;
            visualTransform.position += visualTransform.right * (speed * deltaTime);
            downwardVelocity += Physics2D.gravity.y * (0.1f * deltaTime);
            visualTransform.position += Vector3.up * (downwardVelocity * deltaTime);
            
            var velocity = ((Vector2)visual.transform.right * speed + Vector2.up * downwardVelocity).normalized;
            visual.transform.right = velocity;
            visual.transform.eulerAngles -= new Vector3(0f, 0f, Time.deltaTime * 10f);
            RaycastHit2D hit = default(RaycastHit2D);
            foreach (var candidate in Physics2D.RaycastAll(visual.transform.position, visual.transform.right, 1f))
            {
                if (candidate.collider == null || candidate.collider.isTrigger) continue;
                var body = candidate.collider.GetComponentInParent<BodyScript>();
                var limb = candidate.collider.GetComponentInParent<LimbScript>();
                var hitBody = body ?? (limb == null ? null : limb.body);
                if (hitBody != null && (hitBody == ignoredBody || hitBody.team == ignoredBody.team)) continue;
                if (candidate.collider.gameObject.layer != LayerMask.NameToLayer("Ground") &&
                    body == null && limb == null) continue;
                hit = candidate;
                break;
            }
            if (hit.collider != null)
            {
                CreateRemoteVelvetWebImpact(visual, hit, groundSprite, bodySprite);
                yield break;
            }
            remaining -= Time.deltaTime;
            yield return null;
        }
        if (visual != null) Destroy(visual);
    }

    private static void CreateRemoteVelvetWebImpact(GameObject visual, RaycastHit2D hit,
        Sprite groundSprite, Sprite bodySprite)
    {
        var splash = Resources.Load<GameObject>("Spawnables/WebSplashHit");
        if (splash != null) Destroy(Instantiate(splash, hit.point, Quaternion.identity), 10f);
        var renderer = visual.GetComponent<SpriteRenderer>();
        var limb = hit.collider.GetComponentInParent<LimbScript>();
        var onGround = hit.collider.gameObject.layer == LayerMask.NameToLayer("Ground");
        if (onGround)
        {
            visual.transform.position = hit.point;
            visual.transform.up = hit.normal;
            visual.transform.SetParent(hit.collider.transform, true);
            if (renderer != null)
            {
                renderer.sprite = groundSprite;
                renderer.flipX = UnityEngine.Random.Range(0f, 1f) > 0.5f;
            }
        }
        else if (limb != null)
        {
            visual.transform.SetParent(limb.transform, false);
            visual.transform.localPosition = new Vector2(UnityEngine.Random.Range(-0.1f, 0.1f),
                UnityEngine.Random.Range(-0.1f, 0.1f));
            visual.transform.localScale = new Vector2(UnityEngine.Random.Range(0.7f, 1.3f),
                UnityEngine.Random.Range(0.7f, 1.3f));
            visual.transform.eulerAngles = new Vector3(0f, 0f, UnityEngine.Random.Range(0f, 360f));
            if (renderer != null)
            {
                var limbRenderer = limb.GetComponent<SpriteRenderer>();
                renderer.sprite = bodySprite;
                renderer.flipX = UnityEngine.Random.Range(0f, 1f) > 0.5f;
                renderer.flipY = UnityEngine.Random.Range(0f, 1f) > 0.5f;
                if (limbRenderer != null)
                {
                    renderer.sortingLayerID = limbRenderer.sortingLayerID;
                    renderer.sortingOrder = limbRenderer.sortingOrder + 1;
                }
            }
        }
        else
        {
            Destroy(visual);
            return;
        }
        var sound = Resources.Load<AudioClip>("Sounds/webSplat");
        if (sound != null) Sound.Play(sound, visual.transform.position);
        Destroy(visual, 20f);
    }

    internal static void ReplicateExplosionImpulse(GameObject explosionObject, Vector2 position,
        float range, float force)
    {
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || !IsFinite(position.x) ||
            !IsFinite(position.y) || !IsFinite(range) || !IsFinite(force) || range <= 0f || force <= 0f)
            return;
        foreach (var replica in NetworkAvatarRegistry.replicas.Values)
        {
            if (replica == null || replica.remoteBody == null || replica.remotePeerId == 0 ||
                !BodyMayBeAffectedByExplosion(replica.remoteBody, explosionObject, position, range)) continue;
            MultiplayerSession.Send(PlayerDamagePacket.Explosion(position.x, position.y, range, force),
                replica.remotePeerId);
        }
    }

    private static bool BodyMayBeAffectedByExplosion(BodyScript body, GameObject explosionObject,
        Vector2 position, float range)
    {
        foreach (var collider in body.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider == null || collider.attachedRigidbody == null ||
                ((Vector2)collider.transform.position - position).sqrMagnitude > range * range) continue;
            var blocked = false;
            foreach (var hit in Physics2D.LinecastAll(position, collider.transform.position,
                LayerMask.GetMask("Ground")))
            {
                if (hit.collider == null || hit.collider.gameObject == explosionObject ||
                    hit.collider.gameObject == collider.attachedRigidbody.gameObject) continue;
                blocked = true;
                break;
            }
            if (!blocked) return true;
        }
        return false;
    }

    internal static void ConfigureProjectileCollisions(Component projectile, BodyScript shooter)
    {
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || MultiplayerSession.PvpEnabled ||
            instance == null || projectile == null || player == null ||
            shooter != player.bodyScript) return;
        foreach (var projectileCollider in projectile.GetComponentsInChildren<Collider2D>(true))
        foreach (var replica in NetworkAvatarRegistry.replicas.Values)
        foreach (var remoteCollider in replica.remoteColliderTriggers.Keys)
            if (projectileCollider != null && remoteCollider != null)
                Physics2D.IgnoreCollision(projectileCollider, remoteCollider, true);
    }

    private static BodyScript ProjectileOwner(GameObject projectile)
    {
        if (projectile == null) return null;
        var rocket = projectile.GetComponentInChildren<RocketProjectile>(true);
        if (rocket != null) return rocket.origBody;
        var grenade = projectile.GetComponentInChildren<GrenadeScript>(true);
        return grenade == null ? null : grenade.origBody;
    }

    internal static void CompleteWeaponShot(ShotState state, bool completed)
    {
        try
        {
            var player = PlayerScript.player;
            var shooter = state == null || state.Weapon == null ? null : state.Weapon.body;
            var localPlayerShot = player != null && shooter == player.bodyScript;
            var hostNpcShot = MultiplayerSession.IsHost && shooter != null && !shooter.isPlayer &&
                shooter.GetComponentInParent<NetworkReplica>() == null;
            if (!completed || state == null || state.Weapon == null || shooter == null ||
                (!localPlayerShot && !hostNpcShot) || state.Weapon.ammo >= state.AmmoBefore ||
                !MultiplayerSession.IsConnected) return;
            var targetPeers = new List<ushort>();
            foreach (var wound in state.Wounds)
                if (wound.TargetPeerId != 0 && !targetPeers.Contains(wound.TargetPeerId))
                    targetPeers.Add(wound.TargetPeerId);
            var directionCount = Math.Min(byte.MaxValue, state.ShotDirections.Count);
            var exactDirections = new ShotVisualDirection[directionCount];
            for (var index = 0; index < directionCount; index++)
            {
                var direction = state.ShotDirections[index];
                exactDirections[index] = new ShotVisualDirection(direction.x, direction.y);
            }
            GunsawMultiplayerPlugin.World?.enviroment?.CollectNewDestroyedLampIds(state.DestroyedLampIds,
                state.NewlyDestroyedLampIds);
            MultiplayerSession.Send(new ShotVisualPacket(state.Origin.x, state.Origin.y, state.Direction.x,
                state.Direction.y, state.Up.x, state.Up.y, state.WeaponSprite, hostNpcShot,
                targetPeers.ToArray(), state.SpreadSeed, exactDirections,
                state.NewlyDestroyedLampIds.ToArray()));
            foreach (var wound in state.Wounds)
                SendRemotePlayerWound(wound, wound.BaseDamage > 20f || wound.Critical);
        }
        finally { EndWeaponShot(state); }
    }

    internal static void RecordRemoteWound(WeaponScript weapon, LimbScript limb, Vector2 hitpoint,
        Vector2 direction, GameObject splash)
    {
        var replica = limb == null ? null : NetworkAvatarRegistry.ReplicaForBody(limb.body);
        if (!MultiplayerSession.IsConnected || activeShotState == null || replica == null ||
            weapon != activeShotState.Weapon) return;
        var limbs = GetList(replica.remoteBody, "limbs");
        var limbIndex = limbs.IndexOf(limb);
        if (limbIndex < 0 || limbIndex > short.MaxValue) return;
        var woundRenderer = FindLatestWound(limb, hitpoint);
        activeShotState.Wounds.Add(new PlayerWound
        {
            TargetPeerId = replica.remotePeerId,
            LimbIndex = (short)limbIndex,
            LocalPoint = limb.transform.InverseTransformPoint(hitpoint),
            Direction = direction,
            WeaponSprite = SpriteId(weapon == null || weapon.stats == null ? null : weapon.stats.sprite),
            WoundSprite = SpriteId(woundRenderer == null ? null : woundRenderer.sprite),
            HasSplash = splash != null,
            Critical = limb.isCritical,
            BaseDamage = TakeBaseDamage(activeShotState, replica.remotePeerId),
            BodyColliderHit = TakeBodyColliderHit(activeShotState, replica.remotePeerId, limb)
        });
    }

    private static void SendRemotePlayerWound(
        PlayerWound wound,
        bool createScreenCrack)
    {
        var type = MultiplayerSession.IsHost
            ? PacketType.PlayerDamage
            : PacketType.PvpDamage;

        MultiplayerSession.Send(
            new PlayerWoundPacket(
                type,
                wound.LimbIndex,
                wound.LocalPoint.x,
                wound.LocalPoint.y,
                wound.Direction.x,
                wound.Direction.y,
                wound.WeaponSprite,
                wound.WoundSprite,
                wound.HasSplash,
                createScreenCrack,
                wound.BaseDamage,
                wound.BodyColliderHit
            ),
            wound.TargetPeerId
        );
    }

    private void PlayRemoteShot(ShotVisualPacket packet)
    {
        var origin = new Vector2(packet.OriginX, packet.OriginY);
        var direction = new Vector2(packet.DirectionX, packet.DirectionY);
        var up = new Vector2(packet.UpX, packet.UpY);
        var sprite = packet.WeaponSprite;
        var npcShot = packet.IsNpcShot;
        var spreadSeed = packet.SpreadSeed;
        var exactDirections = packet.ExactDirections;
        
        if (!IsFinite(origin.x) || !IsFinite(origin.y) || !IsFinite(direction.x) ||
            !IsFinite(direction.y) || !IsFinite(up.x) || !IsFinite(up.y) ||
            direction.sqrMagnitude < 0.01f || up.sqrMagnitude < 0.01f) return;
        direction.Normalize();
        up.Normalize();
        var preset = FindWeaponPreset(sprite);
        if (preset == null && remoteBody != null && remoteBody.weapon != null)
            preset = remoteBody.weapon.stats;
        if (preset == null) return;

        GunsawMultiplayerPlugin.World?.enviroment?.ApplyRemoteDestroyedLamps(packet.DestroyedLampIds);
        
        if (preset.fireSound != null)
            Sound.Play(preset.fireSound, origin, false, false, null, 1f, 1f);
        
        if (preset.muzzleFlash != null)
        {
            var flash = Instantiate(preset.muzzleFlash, origin, Quaternion.identity);
            Destroy(flash, 0.4f);
        }
        
        if (preset.shootType == 1)
        {
            PlayRemoteProjectile(preset, origin, direction, !npcShot);
            return;
        }

        var count = Mathf.Clamp(preset.bulletAmount, 1, 12);
        for (var index = 0; index < count; index++)
        {
            Vector2 exactDirection = Vector2.zero;

            if (index < exactDirections.Length)
            {
                ShotVisualDirection dir = exactDirections[index];
                exactDirection = new Vector2(dir.X, dir.Y);
            }

            Vector2 shotDirection = exactDirection.sqrMagnitude > 0.01f
                ? exactDirection.normalized
                : (direction + up * (preset.bulletSpread * SpreadValue(spreadSeed, index))).normalized;
            
            CreateRemoteTracer(preset, origin, FindRemoteShotEnd(origin, shotDirection, !npcShot));
            CreateRemoteBulletImpact(preset, origin, shotDirection, !npcShot);
        }
    }

    private void PlayRemoteProjectile(WeaponPreset preset, Vector2 origin, Vector2 direction,
        bool ignoreRemoteAvatar)
    {
        GameObject visual = null;
        GameObject impactEffect = null;
        AudioClip explosionSound = null;
        var fireAmount = 0;
        var explosionRange = 0f;
        var speed = 22f;
        var speedIncrease = 0f;
        if (preset.tracerLine != null)
        {
            visual = Instantiate(preset.tracerLine, origin, Quaternion.identity);
            visual.name = "MP Projectile Visual";
            visual.transform.right = direction;
            var rocket = visual.GetComponentInChildren<RocketProjectile>(true);
            if (rocket != null && rocket.moveSpeed > 0f) speed = rocket.moveSpeed;
            if (rocket != null) speedIncrease = rocket.moveSpeedSpeedUp;
            if (rocket != null)
            {
                impactEffect = rocket.objOnDestroy;
                explosionSound = rocket.sound;
                fireAmount = rocket.fireAmount;
                explosionRange = rocket.range;
            }
            var grenade = visual.GetComponentInChildren<GrenadeScript>(true);
            if (grenade != null && grenade.startSpeed > 0f) speed = grenade.startSpeed;
            if (grenade != null)
            {
                impactEffect = grenade.objOnDestroy;
                explosionSound = grenade.explosionSound;
                fireAmount = grenade.fireAmount;
                explosionRange = grenade.range;
            }
            foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var collider in visual.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            foreach (var rigidbody in visual.GetComponentsInChildren<Rigidbody2D>(true)) rigidbody.simulated = false;
            foreach (var source in visual.GetComponentsInChildren<AudioSource>(true)) source.enabled = false;
        }
        if (visual == null)
        {
            visual = new GameObject("MP Projectile Visual");
            visual.transform.position = origin;
            visual.transform.right = direction;
            var line = AddFallbackTracer(visual);
            line.SetPosition(0, origin - direction * 0.7f);
            line.SetPosition(1, origin);
        }
        remoteProjectiles.Enqueue(new RemoteProjectileVisual
        {
            Visual = visual,
            ImpactEffect = impactEffect,
            ExplosionSound = explosionSound,
            FireAmount = fireAmount,
            Range = explosionRange,
            ExpiresAt = Time.unscaledTime + 5f
        });
        StartCoroutine(MoveRemoteProjectile(visual, direction,
            FindRemoteShotEnd(origin, direction, ignoreRemoteAvatar), speed, speedIncrease));
    }

    private static IEnumerator MoveRemoteProjectile(GameObject visual, Vector2 direction, Vector2 end,
        float speed, float speedIncrease)
    {
        var maximumLifetime = 2f;
        while (visual != null && maximumLifetime > 0f)
        {
            var current = (Vector2)visual.transform.position;
            var step = speed * Time.deltaTime;
            if (Vector2.Distance(current, end) <= step)
            {
                visual.transform.position = end;
                break;
            }
            visual.transform.position = current + direction * step;
            speed += speedIncrease * Time.deltaTime;
            maximumLifetime -= Time.deltaTime;
            yield return null;
        }
        if (visual != null) Destroy(visual);
    }

    private static void CreateRemoteProjectileImpact(Vector2 position, GameObject impactEffect,
        AudioClip explosionSound, int fireAmount, float range)
    {
        if (impactEffect != null)
        {
            var effect = Instantiate(impactEffect, position, Quaternion.identity);
            foreach (var projectile in effect.GetComponentsInChildren<RocketProjectile>(true)) projectile.enabled = false;
            foreach (var grenade in effect.GetComponentsInChildren<GrenadeScript>(true)) grenade.enabled = false;
            foreach (var collider in effect.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            foreach (var rigidbody in effect.GetComponentsInChildren<Rigidbody2D>(true)) rigidbody.simulated = false;
            Destroy(effect, 60f);
        }
        if (explosionSound != null) Sound.Play(explosionSound, position);
        CreateRemoteExplosionFires(position, fireAmount, range);
        CreateRemoteExplosionCracks(position);
    }

    private void PlayRemoteProjectileImpact(ProjectileImpactPacket packet)
    {
        var position = new Vector2(packet.PositionX, packet.PositionY);
        var sprite = packet.WeaponSpriteId;
        if (!IsFinite(position.x) || !IsFinite(position.y)) return;

        RemoteProjectileVisual projectile = null;
        while (remoteProjectiles.Count > 0 && remoteProjectiles.Peek().ExpiresAt < Time.unscaledTime)
            remoteProjectiles.Dequeue();
        if (remoteProjectiles.Count > 0) projectile = remoteProjectiles.Dequeue();
        if (projectile == null)
        {
            var preset = FindWeaponPreset(sprite);
            projectile = CreateRemoteProjectileVisualData(preset == null ? null : preset.tracerLine);
        }
        if (projectile == null) return;
        if (projectile.Visual != null) Destroy(projectile.Visual);
        CreateRemoteProjectileImpact(position, projectile.ImpactEffect, projectile.ExplosionSound,
            projectile.FireAmount, projectile.Range);
    }

    private static RemoteProjectileVisual CreateRemoteProjectileVisualData(GameObject projectile)
    {
        if (projectile == null) return null;
        var result = new RemoteProjectileVisual { ExpiresAt = Time.unscaledTime + 5f };
        var rocket = projectile.GetComponentInChildren<RocketProjectile>(true);
        if (rocket != null)
        {
            result.ImpactEffect = rocket.objOnDestroy;
            result.ExplosionSound = rocket.sound;
            result.FireAmount = rocket.fireAmount;
            result.Range = rocket.range;
        }
        var grenade = projectile.GetComponentInChildren<GrenadeScript>(true);
        if (grenade != null)
        {
            result.ImpactEffect = grenade.objOnDestroy;
            result.ExplosionSound = grenade.explosionSound;
            result.FireAmount = grenade.fireAmount;
            result.Range = grenade.range;
        }
        return rocket == null && grenade == null ? null : result;
    }

    private static void CreateRemoteExplosionFires(Vector2 position, int fireAmount, float range)
    {
        if (fireAmount < 1 || range <= 0f) return;
        var fire = Resources.Load<GameObject>("Spawnables/FireParticle");
        if (fire == null) return;
        for (var index = 0; index < fireAmount; index++)
        {
            var angle = (float)index * 360f / fireAmount + UnityEngine.Random.Range(-20f, 20f);
            var direction = (Vector2)(Quaternion.AngleAxis(angle, Vector3.forward) * Vector2.right);
            foreach (var hit in Physics2D.RaycastAll(position, direction, range, LayerMask.GetMask("Ground")))
            {
                if (hit.collider == null) continue;
                Instantiate(fire, hit.point, Quaternion.identity).transform.SetParent(hit.transform);
                break;
            }
        }
    }

    private static void CreateRemoteExplosionCracks(Vector2 position)
    {
        var backgroundCrack = Resources.Load<GameObject>("Spawnables/BackgroundCrack");
        foreach (var wall in GameManager.wallColls)
        {
            if (wall == null || !wall.OverlapPoint(position)) continue;
            var crack = backgroundCrack == null ? null : Instantiate(backgroundCrack, position,
                Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f)));
            SetRandomCrackFlip(crack);
            break;
        }

        var floorCrack = Resources.Load<GameObject>("Spawnables/FloorCrack");
        foreach (var hit in Physics2D.RaycastAll(position, Vector2.down, 10f, LayerMask.GetMask("Ground")))
        {
            if (hit.rigidbody != null) continue;
            var crack = floorCrack == null ? null : Instantiate(floorCrack, hit.point, Quaternion.identity);
            SetRandomCrackFlip(crack);
            break;
        }
    }

    private static void SetRandomCrackFlip(GameObject crack)
    {
        if (crack == null) return;
        var renderer = crack.GetComponent<SpriteRenderer>();
        if (renderer == null) return;
        renderer.flipX = UnityEngine.Random.Range(0f, 1f) > 0.5f;
        renderer.flipY = UnityEngine.Random.Range(0f, 1f) > 0.5f;
    }

    private void CreateRemoteTracer(WeaponPreset preset, Vector2 origin, Vector2 end)
    {
        GameObject visual = null;
        LineRenderer line = null;
        if (preset.tracerLine != null)
        {
            visual = Instantiate(preset.tracerLine, Vector3.zero, Quaternion.identity);
            line = visual.GetComponent<LineRenderer>();
            if (line == null) line = visual.GetComponentInChildren<LineRenderer>(true);
        }
        if (line == null)
        {
            if (visual != null) Destroy(visual);
            visual = new GameObject("MP Shot Tracer");
            line = AddFallbackTracer(visual);
        }
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.SetPosition(0, origin);
        line.SetPosition(1, end);
        Destroy(visual, 0.08f);
    }

    private static LineRenderer AddFallbackTracer(GameObject visual)
    {
        var line = visual.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = 0.035f;
        line.endWidth = 0.018f;
        line.startColor = new Color(1f, 0.85f, 0.35f, 0.95f);
        line.endColor = new Color(1f, 0.45f, 0.1f, 0.75f);
        if (fallbackTracerMaterial == null)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) fallbackTracerMaterial = new Material(shader);
        }
        if (fallbackTracerMaterial != null) line.sharedMaterial = fallbackTracerMaterial;
        line.sortingOrder = 100;
        return line;
    }

    private Vector2 FindRemoteShotEnd(Vector2 origin, Vector2 direction, bool ignoreRemoteAvatar)
    {
        var end = origin + direction * 100f;
        var closest = 100f;
        foreach (var hit in Physics2D.RaycastAll(origin, direction, 100f))
        {
            var collider = hit.collider;
            if (collider == null || collider.isTrigger || (ignoreRemoteAvatar && remoteAvatar != null &&
                collider.transform.IsChildOf(remoteAvatar.transform))) continue;
            if (hit.distance < closest)
            {
                closest = hit.distance;
                end = hit.point;
            }
        }
        return end;
    }

    private void CreateRemoteBulletImpact(WeaponPreset preset, Vector2 origin, Vector2 direction,
        bool ignoreRemoteAvatar)
    {
        if (preset == null || direction.sqrMagnitude < 0.01f) return;
        var penetration = preset.penetration;
        foreach (var hit in Physics2D.RaycastAll(origin, direction.normalized, preset.range,
            LayerMask.GetMask("Entity", "EntityStand", "Ground", "Default", "Water")))
        {
            var collider = hit.collider;
            if (collider == null || collider.isTrigger || (ignoreRemoteAvatar && remoteAvatar != null &&
                collider.transform.IsChildOf(remoteAvatar.transform))) continue;
            if (collider.transform.CompareTag("Water")) continue;
            if (collider.GetComponent<BodyScript>() != null)
            {
                penetration -= 2;
                if (penetration < 0) return;
                continue;
            }
            if (collider.GetComponent<LimbScript>() != null)
            {
                penetration--;
                if (penetration < 0) return;
                continue;
            }

            if (collider.transform.gameObject.CompareTag("Lamp")) return;

            if (preset.HitSounds != null && preset.HitSounds.Count > 0)
                Sound.Play(preset.HitSounds[UnityEngine.Random.Range(0, preset.HitSounds.Count)], hit.point);
            SpriteRenderer surface;
            if (collider.TryGetComponent<SpriteRenderer>(out surface))
            {
                var hole = new GameObject("MP BulletHole", typeof(SpriteRenderer));
                var renderer = hole.GetComponent<SpriteRenderer>();
                renderer.sprite = Resources.Load<Sprite>("Bhole/" + UnityEngine.Random.Range(1, 7));
                renderer.sortingOrder = surface.sortingOrder + 1;
                renderer.sortingLayerName = surface.sortingLayerName;
                renderer.material = Resources.Load<Material>("BaseSpriteMaterial");
                hole.transform.position = hit.point + hit.normal * UnityEngine.Random.Range(-0.03f, -0.25f);
                hole.transform.rotation = Quaternion.FromToRotation(Vector3.right, hit.normal);
                hole.transform.SetParent(collider.transform);
            }
            if (preset.hitSpark != null)
                Destroy(Instantiate(preset.hitSpark, hit.point, Quaternion.identity), 5f);
            return;
        }
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal static void EndWeaponShot(ShotState state)
    {
        if (state != null)
        {
            foreach (var collider in state.DisabledColliders)
                if (collider != null) collider.enabled = true;
            currentShooter = state.PreviousShooter;
            activeShotState = state.PreviousShotState;
        }
        else
        {
            currentShooter = null;
            activeShotState = null;
        }
    }

    private static float SpreadValue(int seed, int index)
    {
        if (seed == 0) return 0f;
        unchecked
        {
            var value = (uint)seed + 0x9E3779B9u * (uint)(index + 1);
            value ^= value >> 16;
            value *= 0x85EBCA6Bu;
            value ^= value >> 13;
            value *= 0xC2B2AE35u;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 8388607.5f - 1f;
        }
    }

    internal sealed class ShotState
    {
        internal BodyScript PreviousShooter;
        internal ShotState PreviousShotState;
        internal WeaponScript Weapon;
        internal bool IsExplosion;
        internal int AmmoBefore;
        internal int SpreadSeed;
        internal int SpreadIndex;
        internal readonly List<Vector2> ShotDirections = new();
        internal readonly HashSet<string> DestroyedLampIds = new();
        internal readonly List<string> NewlyDestroyedLampIds = new();
        internal Vector2 Origin;
        internal Vector2 Direction;
        internal Vector2 Up;
        internal string WeaponSprite = "";
        internal readonly List<PlayerWound> Wounds = new();
        internal readonly List<Collider2D> DisabledColliders = new();
        internal readonly Dictionary<ushort, Queue<float>> PendingBaseDamage = new();
        internal readonly Dictionary<ushort, Queue<LimbScript>> PendingBodyColliderHits = new();
    }

    internal sealed class TargetScreenEffectState
    {
        internal bool Suppress;
        internal float ScreenShake;
    }

    internal sealed class PlayerWound
    {
        internal ushort TargetPeerId;
        internal short LimbIndex;
        internal Vector2 LocalPoint;
        internal Vector2 Direction;
        internal string WeaponSprite;
        internal string WoundSprite;
        internal bool HasSplash;
        internal bool Critical;
        internal float BaseDamage;
        internal bool BodyColliderHit;
    }

    private sealed class GrabCommand
    {
        internal byte Kind;
        internal short Index;
        internal Vector2 Point;
        internal Vector2 LocalPoint;
    }

    private static void WriteBody(BinaryWriter writer, Rigidbody2D body)
    {
        if (body == null)
        {
            writer.Write(0f); writer.Write(0f); writer.Write(0f);
            return;
        }
        writer.Write(body.position.x); writer.Write(body.position.y); writer.Write(body.rotation);
    }

    private static void WriteTailTransform(BinaryWriter writer, Rigidbody2D reference,
        Transform transform, float rotation)
    {
        if (reference == null || transform == null)
        {
            writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(false);
            return;
        }
        var delta = (Vector2)transform.position - reference.position;
        writer.Write(delta.x);
        writer.Write(delta.y);
        writer.Write(Mathf.DeltaAngle(reference.rotation, rotation));
        var renderers = transform.GetComponentsInChildren<SpriteRenderer>(true);
        var sprite = renderers.Length == 0 ? null : renderers[0];
        writer.Write(sprite != null && sprite.transform.lossyScale.y < 0f);
        writer.Write((byte)renderers.Length);
        foreach (var renderer in renderers)
        {
            var color = (Color32)renderer.color;
            writer.Write(color.r); writer.Write(color.g); writer.Write(color.b); writer.Write(color.a);
        }
    }

    private static PlayerSnapshotTailState CreateTailBaseState(Rigidbody2D reference, Transform transform,
        float rotation)
    {
        if (reference == null || transform == null)
            return new PlayerSnapshotTailState(0f, 0f, 0f, false, null);

        var delta = (Vector2)transform.position - reference.position;
        var renderers = transform.GetComponentsInChildren<SpriteRenderer>(true);
        var colors = new PlayerSnapshotByteColor[renderers.Length];
        for (var index = 0; index < renderers.Length; index++)
        {
            var color = (Color32)renderers[index].color;
            colors[index] = new PlayerSnapshotByteColor(color.r, color.g, color.b, color.a);
        }
        var sprite = renderers.Length == 0 ? null : renderers[0];
        return new PlayerSnapshotTailState(delta.x, delta.y, Mathf.DeltaAngle(reference.rotation, rotation),
            sprite != null && sprite.transform.lossyScale.y < 0f, colors);
    }

    private Vector2 SetTarget(BinaryReader reader, Rigidbody2D body)
    {
        var position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        var rotation = Quaternion.Euler(0f, 0f, reader.ReadSingle());
        if (body == null) return position;
        SetTailTarget(body, position, rotation);
        return position;
    }

    private void SetTailTarget(Rigidbody2D body, Vector2 position, Quaternion rotation)
    {
        if (PlayerCarrySystem.SetRemoteArmRotation(remoteBody, body, rotation)) return;
        if (PlayerCarrySystem.MustLockRemoteCarryPose(remoteBody)) return;
        var now = Time.unscaledTime;
        TargetState previous;
        var hasPrevious = targets.TryGetValue(body, out previous);
        if (hasPrevious)
        {
            var previousAngle = previous.rotation.eulerAngles.z;
            var angle = rotation.eulerAngles.z;
            rotation = Quaternion.Euler(0f, 0f,
                previousAngle + Mathf.DeltaAngle(previousAngle, angle));
        }
        if (!receivedFirstSnapshot || !hasPrevious)
        {
            body.transform.position = position;
            body.transform.rotation = rotation;
            targets[body] = new TargetState
            {
                fromPosition = position,
                fromRotation = rotation,
                position = position,
                rotation = rotation,
                startedAt = now,
                receivedAt = now,
                duration = CurrentSnapshotInterval()
            };
            return;
        }

        var arrivalInterval = Mathf.Clamp(now - previous.receivedAt,
            CurrentSnapshotInterval(), 0.30f);
        targets[body] = new TargetState
        {
            fromPosition = BufferedRemoteInterpolation ? previous.position : body.transform.position,
            fromRotation = BufferedRemoteInterpolation ? previous.rotation : body.transform.rotation,
            position = position,
            rotation = rotation,
            startedAt = now,
            receivedAt = now,
            duration = arrivalInterval
        };
    }

    private void ReadTailTarget(
        BinaryReader reader,
        Rigidbody2D body,
        SpriteRenderer[] sprites,
        bool inVehicle,
        Vector2 sourceRoot,
        float sourceRootRotation)
    {
        var delta = new Vector2(
            reader.ReadSingle(),
            reader.ReadSingle());

        var deltaAngle = reader.ReadSingle();

        ApplyTailSpriteFlip(sprites, reader.ReadBoolean());
        ApplyTailSpriteColor(sprites, reader);

        if (body == null)
            return;

        if (inVehicle)
        {
            SetVehicleTailRotationTarget(body, deltaAngle);
            return;
        }

        SetTailTarget(
            body,
            lastAuthoritativePosition + delta,
            Quaternion.Euler(
                0f,
                0f,
                remoteBody.rb.rotation + deltaAngle));
    }
    
    private void SetVehicleTailRotationTarget(
        Rigidbody2D body,
        float localRotation)
    {
        var index = vehicleTailTargets.FindIndex(
            target => target.Body == body);

        if (index < 0)
            return;

        var state = vehicleTailTargets[index];

        var progress = Mathf.Clamp01(
            (Time.unscaledTime - state.StartedAt) /
            CurrentSnapshotInterval());

        state.FromLocalRotation = Mathf.LerpAngle(
            state.FromLocalRotation,
            state.LocalRotation,
            progress);

        state.LocalRotation = localRotation;
        state.StartedAt = Time.unscaledTime;

        vehicleTailTargets[index] = state;
    }

    private void SetVehicleTailTransformTarget(
        Transform transform,
        float localRotation)
    {
        if (transform == null)
            return;

        var now = Time.unscaledTime;

        var index =
            vehicleTailTransformTargets.FindIndex(
                target => target.Transform == transform);

        if (index < 0)
        {
            vehicleTailTransformTargets.Add(
                new VehicleTailTransformTarget
                {
                    Transform = transform,
                    LocalRotation = localRotation,
                    FromLocalRotation = localRotation,
                    StartedAt = now
                });

            return;
        }

        var state =
            vehicleTailTransformTargets[index];

        var progress = Mathf.Clamp01(
            (now - state.StartedAt) /
            CurrentSnapshotInterval());

        state.FromLocalRotation = Mathf.LerpAngle(
            state.FromLocalRotation,
            state.LocalRotation,
            progress);

        state.LocalRotation = localRotation;
        state.StartedAt = now;

        vehicleTailTransformTargets[index] = state;
    }

    private void ReadTailTarget(
        BinaryReader reader,
        Transform transform,
        SpriteRenderer[] sprites,
        bool inVehicle,
        Vector2 sourceRoot,
        float sourceRootRotation)
    {
        var delta = new Vector2(
            reader.ReadSingle(),
            reader.ReadSingle());

        var deltaAngle = reader.ReadSingle();

        ApplyTailSpriteFlip(sprites, reader.ReadBoolean());
        ApplyTailSpriteColor(sprites, reader);

        if (transform == null)
            return;

        if (inVehicle)
        {
            SetVehicleTailTransformTarget(
                transform,
                deltaAngle);

            return;
        }

        SetWorldTarget(transform, new TargetState
        {
            position = new Vector3(
                lastAuthoritativePosition.x + delta.x,
                lastAuthoritativePosition.y + delta.y,
                transform.position.z),

            rotation = Quaternion.Euler(
                0f,
                0f,
                remoteBody.rb.rotation + deltaAngle)
        });
    }
    
    private static void ApplyTailSpriteFlip(SpriteRenderer[] sprites, bool flipped)
    {
        var sprite = sprites == null || sprites.Length == 0 ? null : sprites[0];
        if (sprite == null) return;
        var scale = sprite.transform.localScale;
        if ((scale.y < 0f) == flipped) return;
        scale.y = -scale.y;
        sprite.transform.localScale = scale;
    }

    private static void ApplyTailSpriteColor(SpriteRenderer[] sprites, BinaryReader reader)
    {
        var count = reader.ReadByte();
        for (var index = 0; index < count; index++)
        {
            var color = (Color)new Color32(reader.ReadByte(), reader.ReadByte(),
                reader.ReadByte(), reader.ReadByte());
            if (sprites != null && index < sprites.Length && sprites[index] != null) sprites[index].color = color;
        }
    }

    private static void SkipBody(BinaryReader reader)
    {
        reader.ReadSingle(); reader.ReadSingle(); reader.ReadSingle();
    }

    private static IList GetList(BodyScript body, string name)
    {
        if (body == null) return new ArrayList();
        switch (name)
        {
            case "limbs": return body.limbs ?? new List<LimbScript>();
            case "tailBases": return body.tailBases ?? new List<Rigidbody2D>();
            case "weapons": return body.weapons ?? new List<WeaponPreset>();
            default: return new ArrayList();
        }
    }

    private static Transform[] GetTransforms(BodyScript body, string name)
    {
        return body != null && name == "tails" && body.tails != null ? body.tails : new Transform[0];
    }

    private static Transform GetTransform(BodyScript body, string name)
    {
        if (body == null) return null;
        return name == "gunTransform" ? body.gunTransform :
            name == "gunAnimTransform" ? body.gunAnimTransform : null;
    }


    private static void WriteWorldTransform(BinaryWriter writer, Transform transform)
    {
        if (transform == null) { writer.Write(0f); writer.Write(0f); writer.Write(0f); return; }
        writer.Write(transform.position.x);
        writer.Write(transform.position.y);
        writer.Write(transform.eulerAngles.z);
    }

    private static void WriteLocalTransform(BinaryWriter writer, Transform transform)
    {
        if (transform == null) { writer.Write(0f); writer.Write(0f); writer.Write(0f); return; }
        writer.Write(transform.localPosition.x);
        writer.Write(transform.localPosition.y);
        writer.Write(transform.localEulerAngles.z);
    }

    private void ReadWorldTransform(BinaryReader reader, Transform transform)
    {
        SetWorldTarget(transform, ReadWorldTarget(reader, transform));
    }

    private void ReadLocalTransform(BinaryReader reader, Transform transform)
    {
        if (transform == null)
        {
            SkipBody(reader);
            return;
        }
        var target = new TargetState
        {
            position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), transform.localPosition.z),
            rotation = Quaternion.Euler(0f, 0f, reader.ReadSingle())
        };
        SetLocalTarget(transform, target);
    }

    private static TargetState ReadWorldTarget(BinaryReader reader, Transform transform)
    {
        var z = transform == null ? 0f : transform.position.z;
        return new TargetState
        {
            position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), z),
            rotation = Quaternion.Euler(0f, 0f, reader.ReadSingle())
        };
    }

    private void SetWorldTarget(Transform transform, TargetState target)
    {
        if (PlayerCarrySystem.MustLockRemoteCarryPose(remoteBody))
        {
            if (transform == remoteBody.Arms) PlayerCarrySystem.SetRemoteArmsRotation(remoteBody, target.rotation);
            return;
        }
        if (transform == null) return;
        WorldTargetState previous;
        var firstTarget = !worldTargets.TryGetValue(transform, out previous);
        target.position.z = transform.position.z;
        if (!firstTarget)
        {
            var previousAngle = previous.rotation.eulerAngles.z;
            var angle = target.rotation.eulerAngles.z;
            target.rotation = Quaternion.Euler(0f, 0f,
                previousAngle + Mathf.DeltaAngle(previousAngle, angle));
        }
        var now = Time.unscaledTime;
        if (!receivedFirstSnapshot || firstTarget)
        {
            transform.position = target.position;
            transform.rotation = target.rotation;
            worldTargets[transform] = new WorldTargetState
            {
                fromPosition = target.position,
                fromRotation = target.rotation,
                position = target.position,
                rotation = target.rotation,
                startedAt = now,
                receivedAt = now,
                duration = CurrentSnapshotInterval()
            };
            return;
        }

        var arrivalInterval = Mathf.Clamp(now - previous.receivedAt,
            CurrentSnapshotInterval(), 0.30f);
        worldTargets[transform] = new WorldTargetState
        {
            fromPosition = BufferedRemoteInterpolation ? previous.position : transform.position,
            fromRotation = BufferedRemoteInterpolation ? previous.rotation : transform.rotation,
            position = target.position,
            rotation = target.rotation,
            startedAt = now,
            receivedAt = now,
            duration = arrivalInterval
        };
    }

    private void SetLocalTarget(Transform transform, TargetState target)
    {
        if (PlayerCarrySystem.MustLockRemoteCarryPose(remoteBody)) return;
        WorldTargetState previous;
        var firstTarget = !localTargets.TryGetValue(transform, out previous);
        var now = Time.unscaledTime;
        if (!receivedFirstSnapshot || firstTarget)
        {
            transform.localPosition = target.position;
            transform.localRotation = target.rotation;
            localTargets[transform] = new WorldTargetState
            {
                fromPosition = target.position,
                fromRotation = target.rotation,
                position = target.position,
                rotation = target.rotation,
                startedAt = now,
                receivedAt = now,
                duration = CurrentSnapshotInterval()
            };
            return;
        }

        var arrivalInterval = Mathf.Clamp(now - previous.receivedAt,
            CurrentSnapshotInterval(), 0.30f);
        localTargets[transform] = new WorldTargetState
        {
            fromPosition = BufferedRemoteInterpolation ? previous.position : transform.localPosition,
            fromRotation = BufferedRemoteInterpolation ? previous.rotation : transform.localRotation,
            position = target.position,
            rotation = target.rotation,
            startedAt = now,
            receivedAt = now,
            duration = arrivalInterval
        };
    }

    private static void WriteLineState(BinaryWriter writer, LineRenderer line)
    {
        var visible = line != null && line.enabled && line.gameObject.activeInHierarchy && line.positionCount > 0;
        writer.Write(visible);
        if (!visible) return;
        var count = Mathf.Min(line.positionCount, 16);
        writer.Write((byte)count);
        writer.Write(line.useWorldSpace);
        WriteColor(writer, line.startColor);
        WriteColor(writer, line.endColor);
        writer.Write(line.startWidth);
        writer.Write(line.endWidth);
        for (var index = 0; index < count; index++)
        {
            var point = line.GetPosition(index);
            writer.Write(point.x);
            writer.Write(point.y);
            writer.Write(point.z);
        }
    }

    private static PlayerSnapshotLineState CreateWeaponLaserState(LineRenderer line)
    {
        var visible = line != null && line.enabled && line.gameObject.activeInHierarchy && line.positionCount > 0;
        if (!visible) return new PlayerSnapshotLineState(false, false, default(PlayerSnapshotColor),
            default(PlayerSnapshotColor), 0f, 0f, new PlayerSnapshotVector3[0]);

        var count = Mathf.Min(line.positionCount, 16);
        var points = new PlayerSnapshotVector3[count];
        for (var index = 0; index < count; index++)
        {
            var point = line.GetPosition(index);
            points[index] = new PlayerSnapshotVector3(point.x, point.y, point.z);
        }
        var startColor = line.startColor;
        var endColor = line.endColor;
        return new PlayerSnapshotLineState(true, line.useWorldSpace,
            new PlayerSnapshotColor(startColor.r, startColor.g, startColor.b, startColor.a),
            new PlayerSnapshotColor(endColor.r, endColor.g, endColor.b, endColor.a), line.startWidth,
            line.endWidth, points);
    }

    private static void WriteLineState(BinaryWriter writer, PlayerSnapshotLineState state)
    {
        writer.Write(state.Visible);
        if (!state.Visible) return;
        writer.Write((byte)state.Points.Length);
        writer.Write(state.UsesWorldSpace);
        writer.Write(state.StartColor.Red); writer.Write(state.StartColor.Green);
        writer.Write(state.StartColor.Blue); writer.Write(state.StartColor.Alpha);
        writer.Write(state.EndColor.Red); writer.Write(state.EndColor.Green);
        writer.Write(state.EndColor.Blue); writer.Write(state.EndColor.Alpha);
        writer.Write(state.StartWidth); writer.Write(state.EndWidth);
        foreach (var point in state.Points)
        {
            writer.Write(point.X);
            writer.Write(point.Y);
            writer.Write(point.Z);
        }
    }

    private static void ReadLineState(BinaryReader reader, LineRenderer line, GameObject container)
    {
        var visible = reader.ReadBoolean();
        if (!visible)
        {
            if (line != null) line.enabled = false;
            if (container != null) container.SetActive(false);
            return;
        }

        var count = reader.ReadByte();
        var useWorldSpace = reader.ReadBoolean();
        var startColor = ReadColor(reader);
        var endColor = ReadColor(reader);
        var startWidth = reader.ReadSingle();
        var endWidth = reader.ReadSingle();
        var points = new Vector3[count];
        for (var index = 0; index < count; index++)
            points[index] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        if (line == null) return;

        if (container != null) container.SetActive(true);
        line.gameObject.SetActive(true);
        line.enabled = true;
        line.useWorldSpace = useWorldSpace;
        line.startColor = startColor;
        line.endColor = endColor;
        line.startWidth = startWidth;
        line.endWidth = endWidth;
        line.positionCount = count;
        line.SetPositions(points);
    }

    private static void WriteColor(BinaryWriter writer, Color color)
    {
        writer.Write(color.r);
        writer.Write(color.g);
        writer.Write(color.b);
        writer.Write(color.a);
    }

    private static Color ReadColor(BinaryReader reader)
    {
        return new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static void WriteVisualState(BinaryWriter writer, PlayerVisualState state)
    {
        var renderers = state == null ? new RendererVisualState[0] : state.Renderers;
        writer.Write((ushort)renderers.Length);
        for (var index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            writer.Write(renderer.Path);
            writer.Write(renderer.Visible);
            WriteColor(writer, renderer.Color);
            writer.Write(renderer.FlipX);
            writer.Write(renderer.FlipY);
        }

        var lights = state == null ? new LightVisualState[0] : state.Lights;
        writer.Write((ushort)lights.Length);
        for (var index = 0; index < lights.Length; index++)
        {
            var light = lights[index];
            writer.Write(light.Path);
            writer.Write(light.Visible);
            writer.Write(light.Intensity);
            WriteColor(writer, light.Color);
        }
        var expressions = state == null ? Array.Empty<byte>() : state.FacialExpressions;
        writer.Write((ushort)expressions.Length);
        for (var index = 0; index < expressions.Length; index++) writer.Write(expressions[index]);
    }

    private static PlayerVisualState ReadVisualState(BinaryReader reader)
    {
        var rendererCount = reader.ReadUInt16();
        var renderers = new RendererVisualState[rendererCount];
        for (var index = 0; index < rendererCount; index++)
            renderers[index] = new RendererVisualState(reader.ReadString(), reader.ReadBoolean(), ReadColor(reader),
                reader.ReadBoolean(), reader.ReadBoolean());

        var lightCount = reader.ReadUInt16();
        var lights = new LightVisualState[lightCount];
        for (var index = 0; index < lightCount; index++)
            lights[index] = new LightVisualState(reader.ReadString(), reader.ReadBoolean(), reader.ReadSingle(),
                ReadColor(reader));
        var expressionStates = new byte[reader.ReadUInt16()];
        for (var index = 0; index < expressionStates.Length; index++) expressionStates[index] = reader.ReadByte();
        return new PlayerVisualState(renderers, lights, expressionStates);
    }

    private void ApplyPlayerCollisionRule(BodyScript localBody)
    {
        var collisionsEnabled = MultiplayerSession.PlayerCollisions;
        if (localBody == null || (collisionRuleApplied && collisionRuleLocalBody == localBody &&
            collisionRulePlayerCollisions == collisionsEnabled)) return;

        var localColliders = localBody.GetComponentsInChildren<Collider2D>(true);
        foreach (var remoteCollider in remoteColliderTriggers.Keys)
        {
            if (remoteCollider == null) continue;
            foreach (var localCollider in localColliders)
                if (localCollider != null)
                    Physics2D.IgnoreCollision(remoteCollider, localCollider, !collisionsEnabled);
        }

        collisionRuleLocalBody = localBody;
        collisionRulePlayerCollisions = collisionsEnabled;
        collisionRuleApplied = true;
    }

    private static byte FacialExpressionState(FacialExpression expression)
    {
        if (expression == null || expression.head == null) return 0;
        var sprite = expression.head.sprite;
        if (sprite == expression.normalFace) return 1;
        if (sprite == expression.worriedFace) return 2;
        if (sprite == expression.deadFace) return 3;
        if (sprite == expression.halfClosedFace) return 4;
        if (sprite == expression.sadFace) return 5;
        if (sprite == expression.alertFace) return 6;
        if (sprite == expression.fightFace) return 7;
        return sprite == expression.specialSprite ? (byte)8 : (byte)0;
    }

    private static Sprite FacialExpressionSprite(FacialExpression expression, byte state)
    {
        if (expression == null) return null;
        switch (state)
        {
            case 1: return expression.normalFace;
            case 2: return expression.worriedFace;
            case 3: return expression.deadFace;
            case 4: return expression.halfClosedFace;
            case 5: return expression.sadFace;
            case 6: return expression.alertFace;
            case 7: return expression.fightFace;
            case 8: return expression.specialSprite;
            default: return null;
        }
    }

    private void ApplyVisualState(PlayerVisualState state, Transform root)
    {
        remoteVisualLayout = GetVisualLayout(remoteVisualLayout, root);
        foreach (var rendererState in state.Renderers)
        {
            SpriteRenderer renderer;
            if (!remoteVisualLayout.RenderersByPath.TryGetValue(rendererState.Path, out renderer) || renderer == null)
                continue;
            renderer.enabled = rendererState.Visible;
            renderer.color = rendererState.Color;
            renderer.flipX = rendererState.FlipX;
            renderer.flipY = rendererState.FlipY;
        }
        foreach (var lightState in state.Lights)
        {
            Component light;
            if (!remoteVisualLayout.LightsByPath.TryGetValue(lightState.Path, out light) || light == null) continue;
            var behaviour = light as Behaviour;
            if (behaviour != null) behaviour.enabled = lightState.Visible;
            var light2D = light as UnityEngine.Experimental.Rendering.Universal.Light2D;
            if (light2D != null)
            {
                light2D.intensity = lightState.Intensity;
                light2D.color = lightState.Color;
            }
        }
        var expressions = root.GetComponentsInChildren<FacialExpression>(true);
        for (var index = 0; index < expressions.Length && index < state.FacialExpressions.Length; index++)
        {
            var sprite = FacialExpressionSprite(expressions[index], state.FacialExpressions[index]);
            if (sprite != null && expressions[index].head != null) expressions[index].head.sprite = sprite;
        }
        HideChildrenOfDisabledHeadAccessories(root);
    }

    private static void HideChildrenOfDisabledHeadAccessories(Transform root)
    {
        if (root == null) return;
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer == null || !renderer.enabled) continue;
            var ancestor = renderer.transform.parent;
            while (ancestor != null && ancestor != root)
            {
                var ancestorRenderer = ancestor.GetComponent<SpriteRenderer>();
                if (ancestorRenderer != null && !ancestorRenderer.enabled &&
                    ancestor.parent != null && ancestor.parent.name == "Head")
                {
                    renderer.enabled = false;
                    break;
                }
                ancestor = ancestor.parent;
            }
        }
    }

    private static List<Component> FindCharacterLights(Transform root)
    {
        var lights = new List<Component>();
        foreach (var component in root.GetComponentsInChildren<Component>(true))
            if (component != null && component.GetType().FullName == "UnityEngine.Experimental.Rendering.Universal.Light2D")
                lights.Add(component);
        return lights;
    }

    private static VisualLayout GetVisualLayout(VisualLayout current, Transform root)
    {
        if (current != null && current.Root == root &&
            Time.unscaledTime < current.NextValidation) return current;
        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        var lights = FindCharacterLights(root);
        var layout = new VisualLayout
        {
            Root = root,
            Renderers = renderers,
            RendererPaths = new string[renderers.Length],
            Lights = lights.ToArray(),
            LightPaths = new string[lights.Count],
            RenderersByPath = new Dictionary<string, SpriteRenderer>(renderers.Length),
            LightsByPath = new Dictionary<string, Component>(lights.Count),
            NextValidation = Time.unscaledTime + 1f
        };
        for (var index = 0; index < renderers.Length; index++)
        {
            var path = RendererPath(root, renderers[index]);
            layout.RendererPaths[index] = path;
            layout.RenderersByPath[path] = renderers[index];
        }
        for (var index = 0; index < layout.Lights.Length; index++)
        {
            var path = HierarchyPath(root, layout.Lights[index].transform);
            layout.LightPaths[index] = path;
            layout.LightsByPath[path] = layout.Lights[index];
        }
        foreach (var renderer in renderers)
            if (renderer != null && renderer.name == "testGun")
            {
                layout.WeaponRenderer = renderer;
                break;
            }
        return layout;
    }

    private static string HierarchyPath(Transform root, Transform transform)
    {
        if (transform == root) return "";
        var indices = new List<int>();
        var current = transform;
        while (current != null && current != root)
        {
            indices.Add(current.GetSiblingIndex());
            current = current.parent;
        }
        indices.Reverse();
        return string.Join("/", indices);
    }

    private static string RendererPath(Transform root, SpriteRenderer renderer)
    {
        if (renderer == null) return "";
        var renderers = renderer.GetComponents<SpriteRenderer>();
        var componentIndex = 0;
        for (; componentIndex < renderers.Length; componentIndex++)
            if (renderers[componentIndex] == renderer) break;
        return HierarchyPath(root, renderer.transform) + "#" + componentIndex;
    }

    private sealed class VisualLayout
    {
        internal Transform Root;
        internal SpriteRenderer[] Renderers;
        internal string[] RendererPaths;
        internal Component[] Lights;
        internal string[] LightPaths;
        internal Dictionary<string, SpriteRenderer> RenderersByPath;
        internal Dictionary<string, Component> LightsByPath;
        internal SpriteRenderer WeaponRenderer;
        internal float NextValidation;
    }

    private readonly struct RendererVisualState
    {
        internal readonly string Path;
        internal readonly bool Visible;
        internal readonly Color Color;
        internal readonly bool FlipX;
        internal readonly bool FlipY;

        internal RendererVisualState(string path, bool visible, Color color, bool flipX, bool flipY)
        {
            Path = path ?? "";
            Visible = visible;
            Color = color;
            FlipX = flipX;
            FlipY = flipY;
        }
    }

    private readonly struct LightVisualState
    {
        internal readonly string Path;
        internal readonly bool Visible;
        internal readonly float Intensity;
        internal readonly Color Color;

        internal LightVisualState(string path, bool visible, float intensity, Color color)
        {
            Path = path ?? "";
            Visible = visible;
            Intensity = intensity;
            Color = color;
        }
    }

    private sealed class PlayerVisualState
    {
        internal readonly RendererVisualState[] Renderers;
        internal readonly LightVisualState[] Lights;
        internal readonly byte[] FacialExpressions;

        internal PlayerVisualState(RendererVisualState[] renderers, LightVisualState[] lights, byte[] facialExpressions)
        {
            Renderers = renderers ?? new RendererVisualState[0];
            Lights = lights ?? new LightVisualState[0];
            FacialExpressions = facialExpressions ?? Array.Empty<byte>();
        }

        internal static bool Equals(PlayerVisualState left, PlayerVisualState right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Renderers.Length != right.Renderers.Length ||
                left.Lights.Length != right.Lights.Length || left.FacialExpressions.Length != right.FacialExpressions.Length)
                return false;
            for (var index = 0; index < left.Renderers.Length; index++)
            {
                var a = left.Renderers[index]; var b = right.Renderers[index];
                if (a.Path != b.Path || a.Visible != b.Visible || a.Color != b.Color || a.FlipX != b.FlipX ||
                    a.FlipY != b.FlipY)
                    return false;
            }
            for (var index = 0; index < left.Lights.Length; index++)
            {
                var a = left.Lights[index]; var b = right.Lights[index];
                if (a.Path != b.Path || a.Visible != b.Visible || a.Intensity != b.Intensity || a.Color != b.Color)
                    return false;
            }
            for (var index = 0; index < left.FacialExpressions.Length; index++)
                if (left.FacialExpressions[index] != right.FacialExpressions[index]) return false;
            return true;
        }
    }

    private struct AvatarWireBreakdown
    {
        internal int Core;
        internal int Limbs;
        internal int Rig;
        internal int Weapons;
        internal int Effects;
        internal int Visual;
    }

    private static int TransformDepth(Transform transform)
    {
        var depth = 0;
        while (transform != null)
        {
            depth++;
            transform = transform.parent;
        }
        return depth;
    }

    private static PlayerSnapshotScarfState CreateScarfState(BodyScript body)
    {
        var scarf = body.GetComponentInChildren<ScarfPhysics>(true);
        var visible = scarf != null && scarf.gameObject.activeInHierarchy && scarf.pointRenderer != null;
        if (!visible) return new PlayerSnapshotScarfState(false, default(PlayerSnapshotColor),
            default(PlayerSnapshotColor));
        var startColor = scarf.pointRenderer.startColor;
        var endColor = scarf.pointRenderer.endColor;
        return new PlayerSnapshotScarfState(true, new PlayerSnapshotColor(startColor.r, startColor.g, startColor.b,
            startColor.a), new PlayerSnapshotColor(endColor.r, endColor.g, endColor.b, endColor.a));
    }

    private static void WriteScarfState(BinaryWriter writer, PlayerSnapshotScarfState state)
    {
        writer.Write(state.Visible);
        if (!state.Visible) return;
        writer.Write(state.StartColor.Red); writer.Write(state.StartColor.Green);
        writer.Write(state.StartColor.Blue); writer.Write(state.StartColor.Alpha);
        writer.Write(state.EndColor.Red); writer.Write(state.EndColor.Green);
        writer.Write(state.EndColor.Blue); writer.Write(state.EndColor.Alpha);
    }

    private void ReadScarfState(BinaryReader reader)
    {
        var visible = reader.ReadBoolean();
        if (!visible)
        {
            if (remoteScarf != null) Destroy(remoteScarf);
            if (remoteScarfHold != null) Destroy(remoteScarfHold);
            remoteScarf = null;
            remoteScarfHold = null;
            return;
        }

        var startColor = ReadColor(reader);
        var endColor = ReadColor(reader);
        if (remoteScarf == null || remoteScarfHold == null) CreateRemoteScarf();
        if (remoteScarf == null) return;
        var scarf = remoteScarf.GetComponent<ScarfPhysics>();
        var line = scarf == null ? remoteScarf.GetComponent<LineRenderer>() : scarf.pointRenderer;
        if (line == null) return;
        line.startColor = startColor;
        line.endColor = endColor;
        if (remoteScarfHold != null)
        {
            var holdRenderer = remoteScarfHold.GetComponent<SpriteRenderer>();
            if (holdRenderer != null) holdRenderer.color = startColor;
        }
    }

    private void CreateRemoteScarf()
    {
        if (remoteBody == null) return;
        var limbs = GetList(remoteBody, "limbs");
        if (limbs.Count < 2) return;
        var parent = ((LimbScript)limbs[1]).transform;
        if (remoteScarf == null)
        {
            var prefab = Resources.Load<GameObject>("Scarf");
            if (prefab == null) return;
            remoteScarf = Instantiate(prefab, parent);
            remoteScarf.name = "MP Remote Scarf";
            remoteScarf.transform.localRotation = Quaternion.identity;
            remoteScarf.transform.localPosition = new Vector3(-0.067f, 0.052f, 0f);
            var scarf = remoteScarf.GetComponent<ScarfPhysics>();
            if (scarf != null)
            {
                scarf.refbody = remoteBody;
                scarf.enabled = true;
            }
        }
        CreateRemoteScarfHold(parent);
    }

    private void CreateRemoteScarfHold(Transform parent)
    {
        if (parent == null || remoteScarfHold != null) return;
        var sprite = Resources.Load<Sprite>("scarfImage");
        if (sprite == null) return;
        remoteScarfHold = new GameObject("MP Remote Scarf Hold", typeof(SpriteRenderer));
        remoteScarfHold.transform.SetParent(parent, false);
        remoteScarfHold.transform.localPosition = Vector3.zero;
        remoteScarfHold.transform.localRotation = Quaternion.identity;
        remoteScarfHold.transform.localScale = Vector3.one;
        var renderer = remoteScarfHold.GetComponent<SpriteRenderer>();
        var parentRenderer = parent.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = parentRenderer == null ? null : parentRenderer.sharedMaterial;
        renderer.color = parentRenderer == null ? Color.white : parentRenderer.color;
        renderer.sortingLayerID = parentRenderer == null ? 0 : parentRenderer.sortingLayerID;
        renderer.sortingOrder = parentRenderer == null ? 1 : parentRenderer.sortingOrder + 1;
    }

    private static void RemoveReplicaScarfArtifacts(GameObject avatar)
    {
        if (avatar == null) return;
        foreach (var scarf in avatar.GetComponentsInChildren<ScarfPhysics>(true))
            if (scarf != null) DestroyImmediate(scarf.gameObject);
        foreach (var renderer in avatar.GetComponentsInChildren<SpriteRenderer>(true))
            if (renderer != null && renderer.gameObject.name == "ScarfHold") DestroyImmediate(renderer.gameObject);
    }

    private static bool IsBurning(LimbScript limb)
    {
        foreach (var fire in limb.GetComponentsInChildren<FireScript>(true))
            if (fire != null && fire.gameObject.activeInHierarchy && fire.GetComponentInParent<LimbScript>() == limb)
                return true;
        return false;
    }

    private void SetRemoteFire(int limbIndex, LimbScript limb, bool burning)
    {
        GameObject visual;
        remoteFires.TryGetValue(limbIndex, out visual);
        if (!burning)
        {
            if (visual != null) Destroy(visual);
            remoteFires.Remove(limbIndex);
            return;
        }
        if (visual != null) return;

        var prefab = Resources.Load<GameObject>("Spawnables/FireParticle");
        if (prefab == null) return;
        visual = Instantiate(prefab, limb.transform.position, Quaternion.identity);
        visual.name = "MP Remote Fire";
        visual.transform.SetParent(limb.transform, true);
        foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
        foreach (var behaviour in visual.GetComponentsInChildren<Behaviour>(true))
            if (behaviour.GetType().Name == "AudioSource") behaviour.enabled = false;
        remoteFires[limbIndex] = visual;
    }

    private void ApplyDismembermentVisuals()
    {
        if (remoteBody == null) return;
        foreach (var pair in originalDismemberSprites)
            if (pair.Key != null) pair.Key.sprite = pair.Value;
        foreach (var manager in remoteBody.GetComponentsInChildren<DismemberManager>(true))
        {
            var triggered = false;
            if (manager.dismemberLimbs != null)
            {
                foreach (var limb in manager.dismemberLimbs)
                {
                    if (limb == null || !limb.dismembered) continue;
                    triggered = true;
                    break;
                }
            }
            if (!triggered) continue;
            if (manager.dismemberJoint != null)
                foreach (var joint in manager.dismemberJoint)
                    if (joint != null) joint.enabled = false;
            if (manager.dismemberRender == null || manager.dismemberSprites == null) continue;
            var count = Mathf.Min(manager.dismemberRender.Length, manager.dismemberSprites.Length);
            for (var index = 0; index < count; index++)
                if (manager.dismemberRender[index] != null)
                    manager.dismemberRender[index].sprite = manager.dismemberSprites[index];
        }
    }

    private void CacheDismembermentVisuals()
    {
        originalDismemberSprites.Clear();
        if (remoteBody == null) return;
        foreach (var manager in remoteBody.GetComponentsInChildren<DismemberManager>(true))
        {
            if (manager.dismemberRender == null) continue;
            foreach (var renderer in manager.dismemberRender)
                if (renderer != null && !originalDismemberSprites.ContainsKey(renderer))
                    originalDismemberSprites.Add(renderer, renderer.sprite);
        }
    }

    private static void ApplyWeaponVisual(BodyScript body, ulong spriteName, int activeSlot, ulong[] inventorySprites)
    {
        var active = FindSprite(spriteName);
        var holstered = new Sprite[2];
        var holsterIndex = 0;
        for (var index = 0; index < inventorySprites.Length && holsterIndex < holstered.Length; index++)
        {
            if (index == activeSlot || inventorySprites[index] == 0UL) continue;
            holstered[holsterIndex++] = FindSprite(inventorySprites[index]);
        }
        foreach (var renderer in body.transform.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer.name == "testGun") renderer.sprite = active;
            else if (renderer.name == "BackWep1") renderer.sprite = holstered[0];
            else if (renderer.name == "BackWep2") renderer.sprite = holstered[1];
        }
    }

    private static string SpriteId(Sprite sprite)
    {
        if (sprite == null) return "";
        string cached;
        if (spriteIdCache.TryGetValue(sprite, out cached)) return cached;
        cached = BaseSpriteId(sprite) + "\n" + TextureSignature(sprite.texture);
        spriteIdCache[sprite] = cached;
        return cached;
    }

    private static string BaseSpriteId(Sprite sprite)
    {
        if (sprite == null) return "";
        var textureName = sprite.texture == null ? "" : sprite.texture.name;
        return sprite.name + "\n" + textureName + "\n" +
            sprite.rect.x.ToString(CultureInfo.InvariantCulture) + "," +
            sprite.rect.y.ToString(CultureInfo.InvariantCulture) + "," +
            sprite.rect.width.ToString(CultureInfo.InvariantCulture) + "," +
            sprite.rect.height.ToString(CultureInfo.InvariantCulture);
    }

    private static string TextureSignature(Texture2D texture)
    {
        if (texture == null) return "";
        string cached;
        if (textureSignatureCache.TryGetValue(texture, out cached)) return cached;
        try
        {
            var hash = 2166136261u;
            foreach (var pixel in texture.GetPixels32())
            {
                hash = unchecked((hash ^ pixel.r) * 16777619u);
                hash = unchecked((hash ^ pixel.g) * 16777619u);
                hash = unchecked((hash ^ pixel.b) * 16777619u);
                hash = unchecked((hash ^ pixel.a) * 16777619u);
            }
            cached = texture.width + "x" + texture.height + ":" + hash.ToString("X8");
        }
        catch (UnityException)
        {
            cached = "";
        }
        textureSignatureCache[texture] = cached;
        return cached;
    }

    private static Sprite FindSprite(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Sprite cached;
        if (spriteCache.TryGetValue(id, out cached) && cached != null) return cached;
        var separator = id.LastIndexOf('\n');
        if (separator < 1 || separator == id.Length - 1) return null;
        var baseId = id.Substring(0, separator);
        var textureSignature = id.Substring(separator + 1);
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (BaseSpriteId(sprite) != baseId || TextureSignature(sprite.texture) != textureSignature) continue;
            spriteCache[id] = sprite;
            return sprite;
        }
        return null;
    }

    private static WeaponPreset FindWeaponPreset(string spriteId)
    {
        if (string.IsNullOrEmpty(spriteId)) return null;
        WeaponPreset cached;
        if (weaponPresetCache.TryGetValue(spriteId, out cached) && cached != null) return cached;
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (preset != null && SpriteId(preset.sprite) == spriteId)
            {
                weaponPresetCache[spriteId] = preset;
                return preset;
            }
        return null;
    }

    private static Sprite FindSprite(ulong spriteId)
    {
        if (spriteId == 0UL) return null;
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            if (sprite != null && NetworkWireId.FromString(SpriteId(sprite)) == spriteId) return sprite;
        return null;
    }

    private static WeaponPreset FindWeaponPreset(ulong spriteId)
    {
        if (spriteId == 0UL) return null;
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (preset != null && NetworkWireId.FromString(SpriteId(preset.sprite)) == spriteId) return preset;
        return null;
    }

    private void SnapRemoteVehicleArmLimbs()
    {
        if (remoteBody == null || remoteBody.Arms == null ||
            remoteBody.limbs == null)
            return;

        foreach (var limb in remoteBody.limbs)
        {
            if (limb == null ||
                limb.dismembered ||
                limb.rb == null ||
                limb.transformToFollow == null)
                continue;

            var follow = limb.transformToFollow;
            
            if (follow != remoteBody.Arms &&
                !follow.IsChildOf(remoteBody.Arms))
                continue;

            limb.transform.position = follow.position;
            limb.transform.rotation = follow.rotation;

            limb.rb.position = follow.position;
            limb.rb.rotation = follow.eulerAngles.z;
            limb.rb.velocity = Vector2.zero;
            limb.rb.angularVelocity = 0f;
        }
    }
    
    private void ApplyVehicleReflection()
    {
        if (remoteBody == null)
            return;

        var scale = remoteBody.transform.localScale;
        var magnitude = Mathf.Abs(scale.x);

        scale.x =
            (magnitude < 0.0001f ? 1f : magnitude) *
            (remoteVehicleReflected ? -1f : 1f);

        remoteBody.transform.localScale = scale;
    }

    private void UpdateLocalVehicleLock()
    {
        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        var vehicle = body != null && body.inVehicle
            ? body.curVehicle
            : null;
        
        var valid =
            MultiplayerSession.IsConnected &&
            !MultiplayerSession.IsHost &&
            body != null &&
            body.rb != null &&
            vehicle != null &&
            vehicle.mainPart != null &&
            vehicle.mainPart.rb != null;
        

        if (!valid)
        {
            RestoreLocalVehiclePhysics();
            return;
        }

        if (localVehicleLocked &&
            (localVehicleBody != body || localVehicle != vehicle))
        {
            RestoreLocalVehiclePhysics();
        }

        if (!localVehicleLocked)
        {
            localVehicleBody = body;
            localVehicle = vehicle;
            localVehicleWasSimulated = body.rb.simulated;
            localVehicleLocked = true;

            body.rb.velocity = Vector2.zero;
            body.rb.angularVelocity = 0f;
            body.rb.simulated = false;
        }

        var seat = KartPassengers.SeatPosition(vehicle, body);
        var angle = vehicle.mainPart.rb.rotation;

        var position = body.transform.position;
        position.x = seat.x;
        position.y = seat.y;

        body.transform.SetPositionAndRotation(
            position,
            Quaternion.Euler(0f, 0f, angle));

        body.rb.position = seat;
        body.rb.rotation = angle;
        
        var vehicleRb = vehicle.mainPart.rb;
        var vehicleVelocity = vehicleRb.velocity;
        
        // Todo jitter
        body.rb.velocity = vehicleVelocity;
        body.rb.angularVelocity = vehicleRb.angularVelocity;
        body.lastMoveDir = vehicleVelocity;
    }

    private void RestoreLocalVehiclePhysics()
    {
        if (!localVehicleLocked)
            return;

        if (localVehicleBody != null && localVehicleBody.rb != null)
        {
            var rb = localVehicleBody.rb;

            rb.position = localVehicleBody.transform.position;
            rb.rotation = localVehicleBody.transform.eulerAngles.z;

            if (localVehicle != null &&
                localVehicle.mainPart != null &&
                localVehicle.mainPart.rb != null)
            {
                rb.velocity = localVehicle.mainPart.rb.velocity;
                rb.angularVelocity = localVehicle.mainPart.rb.angularVelocity;
            }

            rb.simulated = localVehicleWasSimulated;
        }

        localVehicleBody = null;
        localVehicle = null;
        localVehicleLocked = false;
    }
    
    private static List<Rigidbody2D> GetNetworkTailBodies(BodyScript body)
    {
        var result = new List<Rigidbody2D>();

        if (body == null || body.tails == null)
            return result;

        var added = new HashSet<Rigidbody2D>();

        foreach (var tailRoot in body.tails)
        {
            if (tailRoot == null)
                continue;

            CollectNetworkTailBodies(
                tailRoot,
                tailRoot,
                body.rb,
                result,
                added);
        }

        return result;
    }

    private static void CollectNetworkTailBodies(
        Transform current,
        Transform tailRoot,
        Rigidbody2D bodyRigidbody,
        List<Rigidbody2D> result,
        HashSet<Rigidbody2D> added)
    {
        for (var index = 0; index < current.childCount; index++)
        {
            var child = current.GetChild(index);
            var rigidbody = child.GetComponent<Rigidbody2D>();

            if (rigidbody != null &&
                rigidbody != bodyRigidbody &&
                rigidbody.transform != tailRoot &&
                added.Add(rigidbody))
            {
                result.Add(rigidbody);
            }

            CollectNetworkTailBodies(
                child,
                tailRoot,
                bodyRigidbody,
                result,
                added);
        }
    }
    
    internal static void RecordBodyColliderHit(BodyScript body, LimbScript limb)
    {
        if (!MultiplayerSession.IsConnected ||
            activeShotState == null ||
            activeShotState.Weapon == null ||
            body == null ||
            limb == null)
            return;

        var replica = NetworkAvatarRegistry.ReplicaForBody(body);

        if (replica == null || replica.remotePeerId == 0)
            return;

        if (!activeShotState.PendingBodyColliderHits.TryGetValue(replica.remotePeerId, out var queue))
        {
            queue = new Queue<LimbScript>();
            activeShotState.PendingBodyColliderHits[replica.remotePeerId] = queue;
        }

        queue.Enqueue(limb);
    }
    
    private static bool TakeBodyColliderHit(ShotState state, ushort targetPeerId, LimbScript limb)
    {
        if (state == null ||
            targetPeerId == 0 ||
            limb == null)
            return false;

        if (!state.PendingBodyColliderHits.TryGetValue(
                targetPeerId,
                out var queue))
            return false;

        if (queue.Count == 0)
            return false;

        if (queue.Peek() != limb)
            return false;

        queue.Dequeue();

        if (queue.Count == 0)
            state.PendingBodyColliderHits.Remove(targetPeerId);

        return true;
    }
    
    internal static void AddForceAtPositionWithPropAuthority(Rigidbody2D rb, Vector2 force, Vector2 position, ForceMode2D mode) 
    {
        rb.AddForceAtPosition(force, position, mode);
        TryTakePropAuthority(rb);
    }

    internal static void AddForceWithPropAuthority(Rigidbody2D rb, Vector2 force, ForceMode2D mode)
    {
        rb.AddForce(force, mode);
        TryTakePropAuthority(rb);
    }

    private static void TryTakePropAuthority(Rigidbody2D rb)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost || rb == null)
            return;

        var player = PlayerScript.player;
        if (player == null || currentShooter != player.bodyScript)
            return;

        GunsawMultiplayerPlugin.World?.QueueLevitated(rb);
    }
    
    private struct VehicleTailTarget
    {
        public Rigidbody2D Body;
        public float LocalRotation;
        public float FromLocalRotation;
        public float StartedAt;
    }

    private struct TargetState
    {
        public Vector3 fromPosition;
        public Quaternion fromRotation;
        public Vector3 position;
        public Quaternion rotation;
        public float startedAt;
        public float receivedAt;
        public float duration;
    }

    private struct WorldTargetState
    {
        public Vector3 fromPosition;
        public Quaternion fromRotation;
        public Vector3 position;
        public Quaternion rotation;
        public float startedAt;
        public float receivedAt;
        public float duration;
    }
    private sealed class RemoteProjectileVisual
    {
        public GameObject Visual;
        public GameObject ImpactEffect;
        public AudioClip ExplosionSound;
        public int FireAmount;
        public float Range;
        public float ExpiresAt;
    }
    
    private struct VehicleTailTransformTarget
    {
        public Transform Transform;
        public float LocalRotation;
        public float FromLocalRotation;
        public float StartedAt;
    }
    
    internal readonly struct SuppressedTeleportBody
    {
        internal readonly BodyScript Body;
        internal readonly Vector3 Position;
        internal readonly bool IsPlayer;

        internal SuppressedTeleportBody(BodyScript body)
        {
            Body = body;
            Position = body.transform.position;
            IsPlayer = body.isPlayer;
        }
    }
}