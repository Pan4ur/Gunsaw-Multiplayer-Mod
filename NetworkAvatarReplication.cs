using System;
using System.IO;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class NetworkAvatarReplication : MonoBehaviour
{
    private const bool TailDiagnosticsEnabled = false;
    private const float SnapshotInterval = 1f / 50f;
    private const float VisualSnapshotInterval = 1f / 50f;
    private const string PvpRemoteTeam = "gunsaw_mp_remote_player";
    private static readonly List<string> knownCharacterPrefabs = new List<string>();
    private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    private static readonly Dictionary<string, WeaponPreset> weaponPresetCache =
        new Dictionary<string, WeaponPreset>();
    private static string selectedCharacterPrefab = "";
    private BodyScript remoteBody;
    private GameObject remoteAvatar;
    private string remotePrefabPath = "";
    private string remoteName = "Player";
    private string identitySent = "";
    private float nextIdentity;
    private float nextSnapshot;
    private float nextVisualSnapshot;
    private string localName;
    private readonly Dictionary<Rigidbody2D, TargetState> targets = new Dictionary<Rigidbody2D, TargetState>();
    private readonly Dictionary<Transform, WorldTargetState> worldTargets = new Dictionary<Transform, WorldTargetState>();
    private readonly Dictionary<Transform, TailAttachmentState> tailAttachments =
        new Dictionary<Transform, TailAttachmentState>();
    private readonly Dictionary<SpriteRenderer, TailVisualState> tailVisuals =
        new Dictionary<SpriteRenderer, TailVisualState>();
    private readonly List<ProceduralTailBoneState> proceduralTailBones =
        new List<ProceduralTailBoneState>();
    private GameObject tailVisualRoot;
    private Vector2 proceduralTailVelocity;
    private Vector2 lastProceduralTailPosition;
    private float proceduralTailTime;
    private float proceduralTailForce;
    private bool hasProceduralTailPosition;
    private string tailDiagnosticPath = "";
    private bool localTailFacingKnown;
    private bool lastLocalTailFacing;
    private int localTailDebugFrames;
    private int remoteTailDebugFrames;
    private int tailDebugSequence;
    private bool receivedFirstSnapshot;
    private int appliedWeapon = -1;
    private string appliedWeaponSprite = "";
    private string appliedInventory = "";
    private LineRenderer remoteLevitLine;
    private GameObject remoteScarf;
    private GUIStyle remoteNameTagStyle;
    private GUIStyle remoteNameTagShadowStyle;
    private readonly Dictionary<int, GameObject> remoteFires = new Dictionary<int, GameObject>();
    private readonly Dictionary<Collider2D, bool> remoteColliderTriggers = new Dictionary<Collider2D, bool>();
    private readonly Dictionary<SpriteRenderer, Sprite> originalDismemberSprites = new Dictionary<SpriteRenderer, Sprite>();
    private readonly List<Transform> staleWorldTargets = new List<Transform>();
    private readonly List<KeyValuePair<Transform, WorldTargetState>> orderedWorldTargets =
        new List<KeyValuePair<Transform, WorldTargetState>>();
    private Rigidbody2D[] remoteRigidbodies = new Rigidbody2D[0];
    private bool remotePhysicsModeKnown;
    private bool lastRemoteSimulated;
    private bool lastPassiveGrabProxy;
    private static NetworkAvatarReplication instance;
    private static readonly Dictionary<ushort, NetworkAvatarReplication> replicas =
        new Dictionary<ushort, NetworkAvatarReplication>();
    private static readonly Dictionary<int, BodyScript> lastDamageSources =
        new Dictionary<int, BodyScript>();
    private static readonly HashSet<int> announcedDeaths = new HashSet<int>();
    private bool coordinator;
    private ushort remotePeerId;
    private float lastRemoteHealth;
    private bool lastRemoteAlive = true;
    private static BodyScript currentShooter;
    private static ShotState activeShotState;
    private static bool applyingNetworkPlayerDamage;
    private static Material fallbackTracerMaterial;
    private static readonly MethodInfo DoWoundMethod = AccessTools.Method(typeof(WeaponScript), "DoWound");
    private static readonly FieldInfo PlayerSingletonField = AccessTools.Field(typeof(PlayerScript), "player");
    private static readonly FieldInfo GlobalBodyField = AccessTools.Field(typeof(PlayerScript), "globalBody");
    private static readonly FieldInfo PlayerAmmoTextsField = AccessTools.Field(typeof(PlayerScript), "ammoTexts");
    private static readonly FieldInfo PlayerButtonsField = AccessTools.Field(typeof(PlayerScript), "buttons");
    private static int suppressedTargetScreenEffects;
    private static float suppressedCameraUntil = -1f;
    private static PlayerScript localPlayerInstance;
    private static Transform localGlobalBody;
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
    private GUIStyle respawnStyle;

    internal static BodyScript RemoteBody
    {
        get
        {
            foreach (var replica in replicas.Values)
                if (replica != null && replica.remoteBody != null) return replica.remoteBody;
            return null;
        }
    }

    internal static BodyScript RemoteBodyForPeer(ushort peerId)
    {
        NetworkAvatarReplication replica;
        return replicas.TryGetValue(peerId, out replica) && replica != null ? replica.remoteBody : null;
    }

    internal static bool IsRemoteAvatarBody(BodyScript body)
    {
        return ReplicaForBody(body) != null;
    }

    internal static void ForceRefreshRemotePhysics()
    {
        foreach (var replica in replicas.Values)
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

        var buttons = PlayerButtonsField == null ? null : PlayerButtonsField.GetValue(player) as ButtonScript[];
        if (buttons != null)
            foreach (var button in buttons)
                if (button == null)
                {
                    PlayerButtonsField.SetValue(player, null);
                    break;
                }
        return true;
    }

    internal static void CompleteVanillaBodySwitch(BodyScript body)
    {
        if (instance == null || body == null) return;
        instance.localRespawnGeneration++;
        instance.respawnAt = -1f;
        instance.localRespawnPending = false;
        instance.localWasAlive = true;
        instance.localDeathPosition = body.transform.position;
        localRespawnProtectionUntil = Time.unscaledTime + RespawnProtectionSeconds;
        localPlayerInstance = PlayerScript.player;
        localGlobalBody = body.transform;
        RestoreLocalPlayerSingleton();
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
        newBody.isWalking = false;
        newBody.isAlive = true;
        newBody.health = Mathf.Max(1f, newBody.maxHealth);
        newBody.dropWeapon = false;
        newBody.CurrentState = 0;
        newBody.controlState = 0;
        newBody.noLegs = false;
        newBody.deHeaded = false;
        newBody.onScreen = true;
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

    internal static string RemoteNameForBody(BodyScript body)
    {
        var replica = ReplicaForBody(body);
        return replica == null ? "Player" : replica.remoteName;
    }

    internal static void RecordDamageSource(BodyScript victim)
    {
        if (victim == null) return;
        if (victim.isAlive) announcedDeaths.Remove(victim.GetInstanceID());
        if (currentShooter == null || currentShooter == victim) return;
        lastDamageSources[victim.GetInstanceID()] = currentShooter;
    }

    internal static void RecordDamageSource(BodyScript victim, BodyScript source)
    {
        if (victim == null) return;
        if (victim.isAlive) announcedDeaths.Remove(victim.GetInstanceID());
        if (source != null && source != victim) lastDamageSources[victim.GetInstanceID()] = source;
    }

    internal static BodyScript DamageSourceFor(BodyScript victim)
    {
        if (victim == null) return null;
        BodyScript source;
        return lastDamageSources.TryGetValue(victim.GetInstanceID(), out source) ? source : null;
    }

    internal static bool BeginDeathAnnouncement(BodyScript victim)
    {
        if (victim == null) return false;
        var id = victim.GetInstanceID();
        if (victim.isAlive)
        {
            announcedDeaths.Remove(id);
            return false;
        }
        return announcedDeaths.Add(id);
    }

    internal static RemotePlayerInfo[] RemotePlayers()
    {
        var result = new List<RemotePlayerInfo>(replicas.Count);
        foreach (var pair in replicas)
            if (pair.Value != null)
                result.Add(new RemotePlayerInfo
                {
                    PeerId = pair.Key,
                    Name = pair.Value.remoteName,
                    Body = pair.Value.remoteBody,
                    PingMs = MultiplayerSession.PeerPing(pair.Key)
                });
        result.Sort((left, right) => left.PeerId.CompareTo(right.PeerId));
        return result.ToArray();
    }

    internal static string RemoteNameTag(BodyScript body)
    {
        var replica = ReplicaForBody(body);
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
        if (!TailDiagnosticsEnabled) return;
        tailDiagnosticPath = Path.Combine(Paths.BepInExRootPath,
            "tail-debug-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".log");
        try
        {
            File.WriteAllText(tailDiagnosticPath,
                "Gunsaw Multiplayer tail diagnostics 0.1.40\n" +
                "Process=" + System.Diagnostics.Process.GetCurrentProcess().Id + "\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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

    private static NetworkAvatarReplication GetOrCreateReplica(ushort peerId)
    {
        if (instance == null || peerId == 0 || peerId == MultiplayerSession.LocalPeerId) return null;
        NetworkAvatarReplication replica;
        if (replicas.TryGetValue(peerId, out replica) && replica != null) return replica;
        replica = instance.gameObject.AddComponent<NetworkAvatarReplication>();
        replica.remotePeerId = peerId;
        replica.localName = instance.localName;
        replicas[peerId] = replica;
        return replica;
    }

    private static NetworkAvatarReplication ReplicaForBody(BodyScript body)
    {
        if (body == null) return null;
        foreach (var replica in replicas.Values)
            if (replica != null && replica.remoteBody == body) return replica;
        return null;
    }

    private static void CleanupDisconnectedReplicas()
    {
        var stale = new List<ushort>();
        foreach (var pair in replicas)
            if (pair.Value == null || !MultiplayerSession.HasPeer(pair.Key)) stale.Add(pair.Key);
        foreach (var peerId in stale)
        {
            NetworkAvatarReplication replica;
            if (replicas.TryGetValue(peerId, out replica) && replica != null)
            {
                replica.DestroyRemote();
                Destroy(replica);
            }
            replicas.Remove(peerId);
        }
    }

    private static void DestroyAllReplicas()
    {
        foreach (var replica in new List<NetworkAvatarReplication>(replicas.Values))
            if (replica != null)
            {
                replica.DestroyRemote();
                Destroy(replica);
            }
        replicas.Clear();
    }

    private void Update()
    {
        if (!coordinator)
        {
            TickRemote();
            return;
        }
        if (!MultiplayerSession.IsHosting && !MultiplayerSession.IsConnected)
        {
            DestroyAllReplicas();
            return;
        }
        CleanupDisconnectedReplicas();
        if (!MultiplayerSession.IsConnected) return;

        EnsureLocalPlayerSingleton();
        MultiplayerSession.UpdatePing();
        var player = PlayerScript.player;
        if (player == null || player.bodyScript == null) return;
        localPlayerInstance = player;
        localGlobalBody = PlayerScript.globalBody == null
            ? player.bodyScript.transform : PlayerScript.globalBody;
        UpdateLocalRespawn(player);
        player = PlayerScript.player;
        if (player == null || player.bodyScript == null) return;

        ushort senderId;
        byte[] playerDamage;
        while (MultiplayerSession.TryTakePlayerDamage(out senderId, out playerDamage))
            ApplyPlayerDamage(player.bodyScript, playerDamage);
        byte[] pvpDamage;
        while (MultiplayerSession.TryTakePvpDamage(out senderId, out pvpDamage))
            ApplyPvpDamage(player.bodyScript, senderId, pvpDamage);
        byte[] shotVisual;
        while (MultiplayerSession.TryTakeShotVisual(out senderId, out shotVisual))
        {
            var shooter = GetOrCreateReplica(senderId);
            if (shooter != null) shooter.PlayRemoteShot(shotVisual);
        }
        byte[] playerGrab;
        while (MultiplayerSession.TryTakePlayerGrab(out senderId, out playerGrab))
            ReceivePlayerGrab(playerGrab);
        UpdateLocalRespawn(player);
        player = PlayerScript.player;
        if (player == null) return;
        if (player.bodyScript == null) return;

        var prefab = ResolveCharacterPrefab(player.bodyScript);
        var currentIdentity = localName + "\n" + prefab;
        if (identitySent != currentIdentity || Time.unscaledTime >= nextIdentity)
        {
            identitySent = currentIdentity;
            nextIdentity = Time.unscaledTime + 2f;
            MultiplayerSession.SendIdentity(localName, prefab);
        }

        string identity;
        while (MultiplayerSession.TryTakeIdentity(out senderId, out identity))
        {
            var replica = GetOrCreateReplica(senderId);
            if (replica != null) replica.CreateRemote(identity, player.bodyScript);
        }

        if (Time.unscaledTime >= nextSnapshot)
        {
            nextSnapshot = Time.unscaledTime + CurrentSnapshotInterval();
            MultiplayerSession.SendSnapshot(Serialize(player.bodyScript));
        }

        byte[] snapshot;
        while (MultiplayerSession.TryTakeSnapshot(out senderId, out snapshot))
        {
            NetworkAvatarReplication replica;
            if (replicas.TryGetValue(senderId, out replica) && replica != null) replica.Apply(snapshot);
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
        UpdateRemotePhysicsMode();

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

        foreach (var pair in targets)
        {
            var body = pair.Key;
            var target = pair.Value;
            if (body == null) continue;
            var alpha = Mathf.Clamp01((Time.unscaledTime - target.startedAt) /
                CurrentSnapshotInterval());
            body.transform.position = Vector3.Lerp(target.fromPosition, target.position, alpha);
            body.transform.rotation = Quaternion.Lerp(target.fromRotation, target.rotation, alpha);
        }
    }

    private void LateUpdate()
    {
        if (!MultiplayerSession.IsConnected || remoteAvatar == null) return;
        if (remoteTailDebugFrames > 0) WriteTailDiagnostics("REMOTE FRAME PRE", remoteBody, true);
        UpdateProceduralTail();
        UpdateTailVisuals();
        if (remoteTailDebugFrames > 0)
        {
            WriteTailDiagnostics("REMOTE FRAME POST", remoteBody, true);
            remoteTailDebugFrames--;
        }
    }

    private void OnGUI()
    {
        if (coordinator)
        {
            if (MultiplayerSession.IsHosting || MultiplayerSession.IsConnected) DrawRespawnCountdown();
            return;
        }
        if (!MultiplayerSession.IsConnected) return;
        if (remoteBody == null || remoteBody.rb == null) return;
        var camera = Camera.main;
        if (camera == null) return;
        var scale = Mathf.Clamp(Mathf.Abs(remoteBody.characterScale), 0.7f, 1.8f);
        var world = (Vector3)remoteBody.rb.position + Vector3.up * (1.35f * scale);
        var screen = camera.WorldToScreenPoint(world);
        if (screen.z <= 0f || screen.x < -200f || screen.x > Screen.width + 200f ||
            screen.y < -80f || screen.y > Screen.height + 80f) return;

        EnsureNameTagStyles();
        var content = new GUIContent(RemoteNameTag(remoteBody));
        var size = remoteNameTagStyle.CalcSize(content);
        var width = Mathf.Min(size.x + 14f, Screen.width - 12f);
        var rect = new Rect(
            Mathf.Clamp(screen.x - width * 0.5f, 6f, Screen.width - width - 6f),
            Screen.height - screen.y - 12f,
            width,
            Mathf.Max(24f, size.y + 4f));
        remoteNameTagStyle.normal.textColor = !remoteBody.isAlive
            ? new Color(1f, 0.28f, 0.28f, 1f)
            : !remoteBody.IsConsc() ? new Color(1f, 0.72f, 0.22f, 1f) : Color.white;

        var previousDepth = GUI.depth;
        GUI.depth = 10;
        GUI.Label(new Rect(rect.x - 1f, rect.y, rect.width, rect.height), content, remoteNameTagShadowStyle);
        GUI.Label(new Rect(rect.x + 1f, rect.y, rect.width, rect.height), content, remoteNameTagShadowStyle);
        GUI.Label(new Rect(rect.x, rect.y - 1f, rect.width, rect.height), content, remoteNameTagShadowStyle);
        GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, rect.height), content, remoteNameTagShadowStyle);
        GUI.Label(rect, content, remoteNameTagStyle);
        GUI.depth = previousDepth;
    }

    private void EnsureNameTagStyles()
    {
        if (remoteNameTagStyle != null) return;
        remoteNameTagStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            clipping = TextClipping.Overflow
        };
        remoteNameTagShadowStyle = new GUIStyle(remoteNameTagStyle);
        remoteNameTagShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.95f);
    }

    private void DrawRespawnCountdown()
    {
        var player = PlayerScript.player;
        if (!MultiplayerSession.AllowRespawn || respawnAt < 0f || player == null ||
            player.bodyScript == null || player.bodyScript.isAlive) return;
        if (respawnStyle == null)
        {
            respawnStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 22,
                fontStyle = FontStyle.Bold
            };
            respawnStyle.normal.textColor = Color.white;
        }
        var seconds = Mathf.Max(0, Mathf.CeilToInt(respawnAt - Time.unscaledTime));
        GUI.Label(new Rect(Screen.width * 0.5f - 140f, Screen.height * 0.35f, 280f, 36f),
            "RESPAWN IN " + seconds, respawnStyle);
    }

    private void FixedUpdate()
    {
        if (coordinator) ApplyIncomingGrab();
    }

    private void OnDestroy()
    {
        if (coordinator) return;
        NetworkAvatarReplication current;
        if (replicas.TryGetValue(remotePeerId, out current) && current == this)
            replicas.Remove(remotePeerId);
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
        var avatar = Instantiate(prefab, localBody.transform.position + new Vector3(2f, 0f, 0f), Quaternion.identity);

        foreach (var remotePlayer in avatar.GetComponentsInChildren<PlayerScript>(true))
            DestroyImmediate(remotePlayer);
        RestoreLocalPlayerSingleton();
        avatar.AddComponent<NetworkReplica>();
        remoteAvatar = avatar;
        remotePrefabPath = prefabPath;
        remoteBody = avatar.GetComponentInChildren<BodyScript>();
        if (remoteBody == null) { Destroy(avatar); return; }
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
        foreach (var behaviour in avatar.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
        foreach (var animator in avatar.GetComponentsInChildren<Animator>()) animator.enabled = false;
        UpdateRemotePhysicsMode();
        CacheDismembermentVisuals();
        CreateTailVisuals();
        CreateRemoteLevitLine(avatar.transform);
        WriteTailDiagnostics("REMOTE SPAWN", remoteBody, true);
        Debug.Log("[Gunsaw MP] Spawned remote avatar for " + name + ".");
    }

    private void DestroyRemote()
    {
        if (remoteAvatar != null)
        {
            foreach (var remotePlayer in remoteAvatar.GetComponentsInChildren<PlayerScript>(true))
                DestroyImmediate(remotePlayer);
            Destroy(remoteAvatar);
            RestoreLocalPlayerSingleton();
        }
        remoteAvatar = null;
        remoteBody = null;
        remotePrefabPath = "";
        remoteName = "Player";
        remoteLevitLine = null;
        remoteScarf = null;
        remoteFires.Clear();
        remoteRigidbodies = new Rigidbody2D[0];
        remotePhysicsModeKnown = false;
        remoteColliderTriggers.Clear();
        originalDismemberSprites.Clear();
        targets.Clear();
        worldTargets.Clear();
        tailAttachments.Clear();
        tailVisuals.Clear();
        proceduralTailBones.Clear();
        proceduralTailVelocity = Vector2.zero;
        proceduralTailTime = 0f;
        proceduralTailForce = 0f;
        hasProceduralTailPosition = false;
        remoteTailDebugFrames = 0;
        if (tailVisualRoot != null) Destroy(tailVisualRoot);
        tailVisualRoot = null;
        receivedFirstSnapshot = false;
        appliedWeapon = -1;
        appliedWeaponSprite = "";
        appliedInventory = "";
        remoteDeathDropSpawned = false;
        appliedDismembermentHash = int.MinValue;
        pendingRemoteDamage = 0f;
        remoteCanBeGrabbed = false;
        incomingGrabUntil = 0f;
    }

    private static void RestoreLocalPlayerSingleton()
    {
        if (PlayerSingletonField != null) PlayerSingletonField.SetValue(null, localPlayerInstance);
        if (GlobalBodyField != null) GlobalBodyField.SetValue(null, localGlobalBody);
    }

    internal static void CaptureCharacterMenu(MainMenuManager menu)
    {
        if (menu == null) return;
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var charactersField = typeof(MainMenuManager).GetField("characters", flags);
        var indexField = typeof(MainMenuManager).GetField("charIndex", flags);
        var pathField = typeof(SelectableCharacter).GetField("prefabPath", flags);
        var characters = charactersField == null ? null : charactersField.GetValue(menu) as IList;
        if (characters == null || pathField == null) return;

        for (var index = 0; index < characters.Count; index++)
        {
            var path = pathField.GetValue(characters[index]) as string;
            if (!string.IsNullOrEmpty(path) && !knownCharacterPrefabs.Contains(path))
                knownCharacterPrefabs.Add(path);
        }

        var selectedIndex = indexField == null ? -1 : (int)indexField.GetValue(menu);
        if (selectedIndex >= 0 && selectedIndex < characters.Count)
        {
            var path = pathField.GetValue(characters[selectedIndex]) as string;
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

    private byte[] Serialize(BodyScript body)
    {
        ObserveLocalTail(body);
        var includeVisualState = Time.unscaledTime >= nextVisualSnapshot;
        if (includeVisualState) nextVisualSnapshot = Time.unscaledTime + VisualSnapshotInterval;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(body.isRight);
            WriteBody(writer, body.rb);
            var limbs = GetList(body, "limbs");
            writer.Write((ushort)limbs.Count);
            foreach (LimbScript limb in limbs)
            {
                WriteBody(writer, limb.rb);
                writer.Write(limb.dismembered);
                writer.Write(IsBurning(limb));
            }

            // TAIL POS TODO
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            //

            WriteWorldTransform(writer, body.Arms);
            WriteWorldTransform(writer, GetTransform(body, "gunTransform"));
            WriteWorldTransform(writer, GetTransform(body, "gunAnimTransform"));
            WriteWorldTransform(writer, body.weapon == null ? null : body.weapon.transform);
            writer.Write(body.health);
            writer.Write(body.isAlive);
            writer.Write(body.stamina);
            writer.Write((byte)body.controlState);
            writer.Write(CanGrabOnlyState(body));
            writer.Write(body.burnIntensity);
            writer.Write(body.noLegs);
            writer.Write(body.deHeaded);
            writer.Write(body.unarmed ? -1 : body.currentWeapon);
            writer.Write(body.weapon == null ? 0 : body.weapon.ammo);
            var weapons = GetList(body, "weapons");
            var activeRenderer = FindVisibleWeaponRenderer(body.transform);
            writer.Write(SpriteId(activeRenderer == null ? null : activeRenderer.sprite));
            writer.Write((ushort)weapons.Count);
            foreach (WeaponPreset preset in weapons)
                writer.Write(preset == null ? "" : SpriteId(preset.sprite));
            WriteLineState(writer, GetFieldValue<LineRenderer>(body, "wepLaserLine"));
            var player = PlayerScript.player;
            WriteLineState(writer, player == null ? null : player.levitLine);
            WriteScarfState(writer, body);
            writer.Write(includeVisualState);
            if (includeVisualState) WriteVisualState(writer, body.transform);
            return stream.ToArray();
        }
    }

    private void Apply(byte[] data)
    {
        if (remoteBody == null || remoteBody.rb == null) return;
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                var isRight = reader.ReadBoolean();
                if (remoteBody.isRight != isRight)
                {
                    WriteTailDiagnostics("REMOTE FLIP BEFORE", remoteBody, true);
                    remoteBody.SwitchDir(true);
                    WriteTailDiagnostics("REMOTE FLIP AFTER", remoteBody, true);
                    remoteTailDebugFrames = 12;
                }
                SetTarget(reader, remoteBody.rb);
                var limbs = GetList(remoteBody, "limbs");
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
                    SetTarget(reader, limb.rb);
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
                for (var index = 0; index < tailCount; index++) SkipBody(reader);
                var tailRootCount = reader.ReadUInt16();
                for (var index = 0; index < tailRootCount; index++) SkipBody(reader);
                ReadWorldTransform(reader, remoteBody.Arms);
                ReadWorldTransform(reader, GetTransform(remoteBody, "gunTransform"));
                ReadWorldTransform(reader, GetTransform(remoteBody, "gunAnimTransform"));
                var weaponTransformState = ReadWorldTarget(reader, null);
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
                remoteBody.stamina = reader.ReadSingle();
                remoteBody.controlState = (BodyScript.RagdollState)reader.ReadByte();
                remoteCanBeGrabbed = reader.ReadBoolean();
                lastRemoteHealth = remoteBody.health;
                lastRemoteAlive = remoteBody.isAlive;
                if (MultiplayerSession.IsHost && wasRemoteAlive && !lastRemoteAlive)
                    ClientNpcDeathPatch.Announce(remoteBody);
                if (lastRemoteAlive && !wasRemoteAlive)
                {
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
                var weaponSprite = reader.ReadString();
                var inventoryCount = reader.ReadUInt16();
                var inventorySprites = new string[inventoryCount];
                for (var index = 0; index < inventoryCount; index++) inventorySprites[index] = reader.ReadString();
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
                    appliedWeaponSprite = "";
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
                    SetWorldTarget(remoteBody.weapon.transform, weaponTransformState);
                }
                var remoteLaser = GetFieldValue<LineRenderer>(remoteBody, "wepLaserLine");
                ReadLineState(reader, remoteLaser, GetFieldValue<GameObject>(remoteBody, "wepLaser"));
                ReadLineState(reader, remoteLevitLine, remoteLevitLine == null ? null : remoteLevitLine.gameObject);
                ReadScarfState(reader);
                if (reader.ReadBoolean()) ReadVisualState(reader, remoteBody.transform);
                receivedFirstSnapshot = true;
            }
        }
        catch (EndOfStreamException) { }
    }

    internal static bool HandleHostRemoteDamaged(BodyScript body, bool critical)
    {
        var replica = ReplicaForBody(body);
        if (!MultiplayerSession.IsConnected || replica == null || !replica.receivedFirstSnapshot) return false;
        var amount = Mathf.Clamp(replica.lastRemoteHealth - body.health, 0f, 1000f);
        body.health = replica.lastRemoteHealth;
        body.isAlive = replica.lastRemoteAlive;
        if (amount > 0.001f) RouteRemotePlayerDamage(replica, amount, critical);
        return true;
    }

    internal static bool HandleHostRemoteDeath(BodyScript body)
    {
        var replica = ReplicaForBody(body);
        if (!MultiplayerSession.IsConnected || replica == null || !replica.receivedFirstSnapshot) return false;
        body.health = replica.lastRemoteHealth;
        body.isAlive = replica.lastRemoteAlive;
        RouteRemotePlayerDamage(replica, Mathf.Max(1f, replica.lastRemoteHealth + 1f), true);
        return true;
    }

    private static void RouteRemotePlayerDamage(NetworkAvatarReplication replica, float amount, bool critical)
    {
        if (MultiplayerSession.IsHost)
        {
            if (currentShooter == null || !currentShooter.isPlayer || MultiplayerSession.PvpEnabled)
            {
                replica.SpawnRemoteDeathDropIfNeeded(amount);
                SendRemotePlayerDamage(replica.remotePeerId, amount, critical);
            }
            return;
        }
        if (currentShooter == null) return;
        var localPlayer = PlayerScript.player;

        if (localPlayer == null || currentShooter != localPlayer.bodyScript) return;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(amount);
            writer.Write(critical);
            MultiplayerSession.SendPvpDamage(replica.remotePeerId, stream.ToArray());
        }
    }

    private static void SendRemotePlayerDamage(ushort targetPeerId, float amount, bool critical)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(amount);
            writer.Write(critical);
            MultiplayerSession.SendPlayerDamage(targetPeerId, stream.ToArray());
        }
    }

    private static void ApplyPlayerDamage(BodyScript body, byte[] data)
    {
        if (body == null) return;

        if (Time.unscaledTime < localRespawnProtectionUntil) return;
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                var amount = Mathf.Clamp(reader.ReadSingle(), 0f, 1000f);
                var critical = reader.ReadBoolean();
                if (amount > 0f && body.isAlive)
                {
                    body.health -= amount;
                    applyingNetworkPlayerDamage = true;
                    try { body.Damaged(critical); }
                    finally { applyingNetworkPlayerDamage = false; }
                }
                if (reader.BaseStream.Position >= reader.BaseStream.Length || reader.ReadByte() != 1) return;
                ApplyNetworkWound(body, reader);
            }
        }
        catch (EndOfStreamException) { }
    }

    internal static bool BlockLocalRespawnDeath(BodyScript body)
    {
        if (body == null || Time.unscaledTime >= localRespawnProtectionUntil) return false;
        var player = PlayerScript.player;
        if (player == null || player.bodyScript != body) return false;

        ReviveRespawnBody(body);
        return true;
    }

    private static void ApplyNetworkWound(BodyScript body, BinaryReader reader)
    {
        var localPlayer = PlayerScript.player;

        if (localPlayer == null || localPlayer.bodyScript == null || body != localPlayer.bodyScript)
            return;
        var limbIndex = reader.ReadInt16();
        var localPoint = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        var direction = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        var weaponSprite = reader.ReadString();
        var woundSprite = reader.ReadString();
        var hasSplash = reader.ReadBoolean();
        var createScreenCrack = reader.ReadBoolean();
        var limbs = GetList(body, "limbs");
        if (limbIndex >= 0 && limbIndex < limbs.Count)
        {
            var limb = limbs[limbIndex] as LimbScript;
            var preset = FindWeaponPreset(weaponSprite);
            if (limb != null && preset != null && DoWoundMethod != null)
            {
                GameObject sourceObject = null;
                try
                {
                    sourceObject = new GameObject("MP Wound Source");
                    sourceObject.SetActive(false);
                    var sourceWeapon = sourceObject.AddComponent<WeaponScript>();
                    sourceWeapon.stats = preset;
                    sourceWeapon.body = body;
                    var hitPoint = (Vector2)limb.transform.TransformPoint(localPoint);
                    DoWoundMethod.Invoke(sourceWeapon, new object[]
                    {
                        limb, hitPoint, direction, hasSplash ? preset.bloodSplash : null
                    });
                    if (!string.IsNullOrEmpty(woundSprite))
                    {
                        var wound = FindLatestWound(limb, hitPoint);
                        var sprite = FindSprite(woundSprite);
                        if (wound != null && sprite != null) wound.sprite = sprite;
                    }
                }
                catch (TargetInvocationException exception)
                {
                    Debug.LogWarning("[Gunsaw MP] Could not replay player wound: " +
                        (exception.InnerException == null ? exception.Message : exception.InnerException.Message));
                }
                finally
                {
                    if (sourceObject != null) Destroy(sourceObject);
                }
            }
        }
        if (createScreenCrack && CameraFollow.cam != null)
            CameraFollow.cam.CreateScreenCrack();
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
        if (!MultiplayerSession.IsConnected || player == null || body == null) return false;
        if (body != player.bodyScript && !IsRemoteAvatarBody(body)) return false;
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

    private void RespawnLocalPlayer(PlayerScript player, BodyScript oldBody)
    {
        if (localRespawnPending) return;
        localRespawnPending = true;
        var generation = ++localRespawnGeneration;
        respawnAt = -1f;
        localRespawnProtectionUntil = Time.unscaledTime + RespawnProtectionSeconds;
        var prefabPath = ResolveCharacterPrefab(oldBody);
        var prefab = string.IsNullOrEmpty(prefabPath) ? null : Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            localRespawnPending = false;
            Debug.LogError("[Gunsaw MP] Could not respawn player: character prefab is missing.");
            return;
        }
        EnsureRespawnWeaponSlots(oldBody);

        var position = ResolveRespawnPosition(oldBody);
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
            localPlayerInstance.UnDie();
            if (CameraFollow.cam != null) CameraFollow.cam.target = newBody.transform;
            localRespawnProtectionUntil = Time.unscaledTime + RespawnProtectionSeconds;
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

    private Vector3 ResolveRespawnPosition(BodyScript oldBody)
    {
        var candidate = MultiplayerSession.RespawnAtStart ? localSpawnPosition : localDeathPosition;
        if (!IsRespawnPositionBlocked(candidate, oldBody)) return candidate;
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
        body.controlState = 0;
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

    internal static void EnsurePlayerAmmoDisplaySlots(PlayerScript player)
    {
        if (player == null || player.bodyScript == null) return;
        var ammoTexts = PlayerAmmoTextsField == null
            ? null : PlayerAmmoTextsField.GetValue(player);
        var ammoArray = ammoTexts as Array;
        var ammoCollection = ammoTexts as ICollection;
        var required = ammoArray != null ? ammoArray.Length
            : ammoCollection == null ? 0 : ammoCollection.Count;
        if (required <= 0) return;
        if (player.bodyScript.ammoAmount == null)
            player.bodyScript.ammoAmount = new List<int>();
        while (player.bodyScript.ammoAmount.Count < required)
            player.bodyScript.ammoAmount.Add(0);
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

    private static void ApplyPvpDamage(BodyScript body, ushort senderId, byte[] data)
    {
        if (!MultiplayerSession.PvpEnabled) return;
        var source = senderId == MultiplayerSession.LocalPeerId
            ? body : RemoteBodyForPeer(senderId);
        RecordDamageSource(body, source);
        ApplyPlayerDamage(body, data);
    }

    private void UpdateRemotePhysicsMode()
    {
        if (remoteAvatar == null) return;
        var player = PlayerScript.player;
        if (remoteBody != null && player != null && player.bodyScript != null)
            remoteBody.team = RemoteTeam(player.bodyScript);

        var simulated = MultiplayerSession.IsHost || MultiplayerSession.PvpEnabled ||
            MultiplayerSession.CanGrabPlayers;
        var passiveGrabProxy = !MultiplayerSession.IsHost && !MultiplayerSession.PvpEnabled &&
            MultiplayerSession.CanGrabPlayers;
        if (remotePhysicsModeKnown && simulated == lastRemoteSimulated &&
            passiveGrabProxy == lastPassiveGrabProxy) return;
        remotePhysicsModeKnown = true;
        lastRemoteSimulated = simulated;
        lastPassiveGrabProxy = passiveGrabProxy;
        foreach (var body in remoteRigidbodies)
        {
            if (body == null) continue;
            body.simulated = simulated;
            if (simulated) body.bodyType = RigidbodyType2D.Kinematic;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
        foreach (var pair in remoteColliderTriggers)
            if (pair.Key != null) pair.Key.isTrigger = pair.Value || passiveGrabProxy ||
                (!MultiplayerSession.PvpEnabled && !MultiplayerSession.IsHost);
        if (MultiplayerSession.IsHost)
            foreach (var prop in FindObjectsOfType<Rigidbody2D>())
                if (prop != null && (prop.GetComponentInParent<CrateScript>() != null ||
                    prop.GetComponentInParent<DroppedWeapon>() != null))
                    IgnoreRemotePlayerPropCollisions(prop);
    }

    internal static void IgnoreRemotePlayerPropCollisions(Rigidbody2D prop)
    {
        if (prop == null || !MultiplayerSession.IsHost) return;
        var propColliders = prop.GetComponentsInChildren<Collider2D>(true);
        if (propColliders == null || propColliders.Length == 0) return;
        foreach (var replica in replicas.Values)
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
            var remote = ReplicaForBody(collider.GetComponentInParent<BodyScript>());
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
        var replica = ReplicaForBody(targetBody);
        byte kind;
        short index;
        if (target != null && replica != null && CanGrabBody(targetBody) &&
            replica.TryRemotePart(target, out kind, out index))
        {
            if (instance.outgoingGrabPeerId != 0 && instance.outgoingGrabPeerId != replica.remotePeerId)
                MultiplayerSession.SendPlayerGrab(instance.outgoingGrabPeerId, new byte[] { 0 });
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(true);
                writer.Write(kind);
                writer.Write(index);
                writer.Write(levitator.point.x);
                writer.Write(levitator.point.y);
                writer.Write(levitator.localGrabPoint.x);
                writer.Write(levitator.localGrabPoint.y);
                MultiplayerSession.SendPlayerGrab(replica.remotePeerId, stream.ToArray());
            }
            instance.outgoingGrabPeerId = replica.remotePeerId;
            return;
        }
        if (instance.outgoingGrabPeerId == 0) return;
        MultiplayerSession.SendPlayerGrab(instance.outgoingGrabPeerId, new byte[] { 0 });
        instance.outgoingGrabPeerId = 0;
    }

    private void ReceivePlayerGrab(byte[] data)
    {
        if (data == null || data.Length < 1 || !MultiplayerSession.CanGrabPlayers)
        {
            incomingGrabUntil = 0f;
            return;
        }
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                if (!reader.ReadBoolean()) { incomingGrabUntil = 0f; return; }
                var command = new GrabCommand
                {
                    Kind = reader.ReadByte(),
                    Index = reader.ReadInt16(),
                    Point = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    LocalPoint = new Vector2(reader.ReadSingle(), reader.ReadSingle())
                };
                if (!IsFinite(command.Point.x) || !IsFinite(command.Point.y) ||
                    !IsFinite(command.LocalPoint.x) || !IsFinite(command.LocalPoint.y)) return;
                incomingGrab = command;
                incomingGrabUntil = Time.unscaledTime + 0.15f;
            }
        }
        catch (EndOfStreamException) { }
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
        var tails = GetList(remoteBody, "tailBases");
        for (var position = 0; position < tails.Count; position++)
            if ((Rigidbody2D)tails[position] == rigidbody)
            {
                kind = 2;
                index = (short)position;
                return true;
            }
        return false;
    }

    private static Rigidbody2D ResolveLocalPart(BodyScript body, byte kind, int index)
    {
        if (kind == 0) return body.rb;
        var parts = kind == 1 ? GetList(body, "limbs") : kind == 2 ? GetList(body, "tailBases") : null;
        if (parts == null || index < 0 || index >= parts.Count) return null;
        return kind == 1 ? ((LimbScript)parts[index]).rb : (Rigidbody2D)parts[index];
    }

    private static bool CanGrabBody(BodyScript body)
    {
        if (body == null || !MultiplayerSession.CanGrabPlayers) return false;
        if (!MultiplayerSession.GrabOnlyUnconscious) return true;
        var replica = ReplicaForBody(body);
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
            AmmoBefore = weapon == null ? 0 : weapon.ammo
        };
        activeShotState = state;
        currentShooter = weapon == null ? null : weapon.body;
        if (weapon != null && weapon.stats != null)
        {
            state.Origin = weapon.transform.TransformPoint(weapon.stats.barrelPosition);
            var facing = weapon.body != null && !weapon.body.isRight ? -1f : 1f;
            state.Direction = (Vector2)(weapon.transform.right * facing);
            state.WeaponSprite = SpriteId(weapon.stats.sprite);
        }
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || MultiplayerSession.PvpEnabled ||
            instance == null || player == null || currentShooter != player.bodyScript)
            return state;
        foreach (var replica in replicas.Values)
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
        foreach (var replica in replicas.Values)
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
        if (current != null && current != localBody && ReplicaForBody(current) == null) return;

        BodyScript best = null;
        var bestDistance = float.MaxValue;
        SelectNpcPlayerTarget(ai.body, localBody, ref best, ref bestDistance);
        foreach (var replica in replicas.Values)
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
        var state = new ShotState { PreviousShooter = currentShooter };
        currentShooter = ProjectileOwner(projectile);
        return state;
    }

    internal static void ConfigureProjectileCollisions(Component projectile, BodyScript shooter)
    {
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || MultiplayerSession.PvpEnabled ||
            instance == null || projectile == null || player == null ||
            shooter != player.bodyScript) return;
        foreach (var projectileCollider in projectile.GetComponentsInChildren<Collider2D>(true))
        foreach (var replica in replicas.Values)
        foreach (var remoteCollider in replica.remoteColliderTriggers.Keys)
            if (projectileCollider != null && remoteCollider != null)
                Physics2D.IgnoreCollision(projectileCollider, remoteCollider, true);
    }

    private static BodyScript ProjectileOwner(GameObject projectile)
    {
        if (projectile == null) return null;
        foreach (var component in projectile.GetComponents<Component>())
        {
            if (component == null) continue;
            var field = AccessTools.Field(component.GetType(), "origBody");
            if (field != null && typeof(BodyScript).IsAssignableFrom(field.FieldType))
                return field.GetValue(component) as BodyScript;
        }
        return null;
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
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(state.Origin.x);
                writer.Write(state.Origin.y);
                writer.Write(state.Direction.x);
                writer.Write(state.Direction.y);
                writer.Write(state.WeaponSprite ?? "");
                writer.Write(hostNpcShot);

                var targetPeers = new List<ushort>();
                foreach (var wound in state.Wounds)
                    if (wound.TargetPeerId != 0 && !targetPeers.Contains(wound.TargetPeerId))
                        targetPeers.Add(wound.TargetPeerId);
                writer.Write((ushort)targetPeers.Count);
                foreach (var targetPeer in targetPeers) writer.Write(targetPeer);
                MultiplayerSession.SendShotVisual(stream.ToArray());
            }
            foreach (var wound in state.Wounds)
                SendRemotePlayerWound(wound, wound.Critical);
        }
        finally { EndWeaponShot(state); }
    }

    internal static void RecordRemoteWound(WeaponScript weapon, LimbScript limb, Vector2 hitpoint,
        Vector2 direction, GameObject splash)
    {
        var replica = limb == null ? null : ReplicaForBody(limb.body);
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
            Critical = limb.isCritical
        });
    }

    private static void SendRemotePlayerWound(PlayerWound wound, bool createScreenCrack)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0f);
            writer.Write(false);
            writer.Write((byte)1);
            writer.Write(wound.LimbIndex);
            writer.Write(wound.LocalPoint.x);
            writer.Write(wound.LocalPoint.y);
            writer.Write(wound.Direction.x);
            writer.Write(wound.Direction.y);
            writer.Write(wound.WeaponSprite ?? "");
            writer.Write(wound.WoundSprite ?? "");
            writer.Write(wound.HasSplash);
            writer.Write(createScreenCrack);
            if (MultiplayerSession.IsHost)
                MultiplayerSession.SendPlayerDamage(wound.TargetPeerId, stream.ToArray());
            else MultiplayerSession.SendPvpDamage(wound.TargetPeerId, stream.ToArray());
        }
    }

    private void PlayRemoteShot(byte[] data)
    {
        if (data == null) return;
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                var origin = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                var direction = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                var sprite = reader.ReadString();
                var npcShot = reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean();
                var targetPeers = new List<ushort>();
                if (reader.BaseStream.Position + sizeof(ushort) <= reader.BaseStream.Length)
                {
                    var targetCount = reader.ReadUInt16();
                    for (var index = 0; index < targetCount &&
                        reader.BaseStream.Position + sizeof(ushort) <= reader.BaseStream.Length; index++)
                        targetPeers.Add(reader.ReadUInt16());
                }
                if (npcShot && !targetPeers.Contains(MultiplayerSession.LocalPeerId)) return;
                if (!IsFinite(origin.x) || !IsFinite(origin.y) || !IsFinite(direction.x) ||
                    !IsFinite(direction.y) || direction.sqrMagnitude < 0.01f) return;
                direction.Normalize();
                var preset = FindWeaponPreset(sprite);
                if (preset == null && remoteBody != null && remoteBody.weapon != null)
                    preset = remoteBody.weapon.stats;
                if (preset == null) return;

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
                    var shotDirection = (direction + Vector2.Perpendicular(direction) *
                        preset.bulletSpread * UnityEngine.Random.Range(-1f, 1f)).normalized;
                    CreateRemoteTracer(preset, origin,
                        FindRemoteShotEnd(origin, shotDirection, !npcShot));
                }
            }
        }
        catch (EndOfStreamException) { }
        catch (IOException) { }
    }

    private void PlayRemoteProjectile(WeaponPreset preset, Vector2 origin, Vector2 direction,
        bool ignoreRemoteAvatar)
    {
        var visual = new GameObject("MP Projectile Visual");
        visual.transform.position = origin;
        visual.transform.right = direction;
        var template = preset.tracerLine == null ? null : preset.tracerLine.GetComponentInChildren<SpriteRenderer>(true);
        if (template != null)
        {
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = template.sprite;
            renderer.sharedMaterial = template.sharedMaterial;
            renderer.color = template.color;
            renderer.sortingLayerID = template.sortingLayerID;
            renderer.sortingOrder = template.sortingOrder;
            visual.transform.localScale = template.transform.lossyScale;
        }
        else
        {
            var line = AddFallbackTracer(visual);
            line.SetPosition(0, origin - direction * 0.7f);
            line.SetPosition(1, origin);
        }
        StartCoroutine(MoveRemoteProjectile(visual, direction,
            FindRemoteShotEnd(origin, direction, ignoreRemoteAvatar), 22f));
    }

    private static IEnumerator MoveRemoteProjectile(GameObject visual, Vector2 direction, Vector2 end, float speed)
    {
        var maximumLifetime = 2f;
        while (visual != null && maximumLifetime > 0f)
        {
            var current = (Vector2)visual.transform.position;
            var step = speed * Time.unscaledDeltaTime;
            if (Vector2.Distance(current, end) <= step) break;
            visual.transform.position = current + direction * step;
            maximumLifetime -= Time.unscaledDeltaTime;
            yield return null;
        }
        if (visual != null) Destroy(visual);
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

    internal sealed class ShotState
    {
        internal BodyScript PreviousShooter;
        internal ShotState PreviousShotState;
        internal WeaponScript Weapon;
        internal int AmmoBefore;
        internal Vector2 Origin;
        internal Vector2 Direction;
        internal string WeaponSprite = "";
        internal readonly List<PlayerWound> Wounds = new List<PlayerWound>();
        internal readonly List<Collider2D> DisabledColliders = new List<Collider2D>();
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
        writer.Write(body.position.x); writer.Write(body.position.y); writer.Write(body.rotation);
    }

    private void SetTarget(BinaryReader reader, Rigidbody2D body)
    {
        var position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        var rotation = reader.ReadSingle();
        if (body == null) return;
        var target = new TargetState
        {
            fromPosition = body.transform.position,
            fromRotation = body.transform.rotation,
            position = position,
            rotation = Quaternion.Euler(0f, 0f, rotation),
            startedAt = Time.unscaledTime
        };
        targets[body] = target;
        if (!receivedFirstSnapshot)
        {
            body.transform.position = position;
            body.transform.rotation = target.rotation;
        }
    }

    private static void SkipBody(BinaryReader reader)
    {
        reader.ReadSingle(); reader.ReadSingle(); reader.ReadSingle();
    }

    private static IList GetList(BodyScript body, string name)
    {
        var field = typeof(BodyScript).GetField(name);
        return field == null ? new ArrayList() : (IList)field.GetValue(body);
    }

    private static Transform[] GetTransforms(BodyScript body, string name)
    {
        var field = typeof(BodyScript).GetField(name);
        return field == null ? new Transform[0] : (Transform[])field.GetValue(body);
    }

    private static Transform GetTransform(BodyScript body, string name)
    {
        var field = typeof(BodyScript).GetField(name);
        return field == null ? null : (Transform)field.GetValue(body);
    }

    private static void WriteWorldTransforms(BinaryWriter writer, Transform[] transforms)
    {
        writer.Write((ushort)transforms.Length);
        foreach (var transform in transforms) WriteWorldTransform(writer, transform);
    }

    private void ReadWorldTransforms(BinaryReader reader, Transform[] transforms)
    {
        var count = reader.ReadUInt16();
        for (var index = 0; index < count; index++)
        {
            if (index < transforms.Length) ReadWorldTransform(reader, transforms[index]);
            else SkipBody(reader);
        }
    }

    private static void WriteWorldTransform(BinaryWriter writer, Transform transform)
    {
        if (transform == null) { writer.Write(0f); writer.Write(0f); writer.Write(0f); return; }
        writer.Write(transform.position.x);
        writer.Write(transform.position.y);
        writer.Write(transform.eulerAngles.z);
    }

    private void ReadWorldTransform(BinaryReader reader, Transform transform)
    {
        SetWorldTarget(transform, ReadWorldTarget(reader, transform));
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
        if (transform == null) return;
        WorldTargetState previous;
        var firstTarget = !worldTargets.TryGetValue(transform, out previous);
        target.position.z = transform.position.z;
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

        worldTargets[transform] = new WorldTargetState
        {
            fromPosition = transform.position,
            fromRotation = transform.rotation,
            position = target.position,
            rotation = target.rotation,
            startedAt = now,
            receivedAt = now,
                duration = CurrentSnapshotInterval()
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

    private static void WriteVisualState(BinaryWriter writer, Transform root)
    {
        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        writer.Write((ushort)renderers.Length);
        foreach (var renderer in renderers)
        {
            writer.Write(HierarchyPath(root, renderer.transform));
            writer.Write(renderer.enabled && renderer.gameObject.activeInHierarchy);
            writer.Write(SpriteId(renderer.sprite));
            WriteColor(writer, renderer.color);
            writer.Write(renderer.flipX);
            writer.Write(renderer.flipY);
        }

        var lights = FindCharacterLights(root);
        writer.Write((ushort)lights.Count);
        foreach (var light in lights)
        {
            writer.Write(HierarchyPath(root, light.transform));
            var behaviour = light as Behaviour;
            writer.Write(behaviour == null || behaviour.enabled && light.gameObject.activeInHierarchy);
            writer.Write(GetComponentProperty(light, "intensity", 0f));
            WriteColor(writer, GetComponentProperty(light, "color", Color.white));
        }
    }

    private void ReadVisualState(BinaryReader reader, Transform root)
    {
        var renderers = new Dictionary<string, SpriteRenderer>();
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            renderers[HierarchyPath(root, renderer.transform)] = renderer;

        var rendererCount = reader.ReadUInt16();
        for (var index = 0; index < rendererCount; index++)
        {
            var path = reader.ReadString();
            var visible = reader.ReadBoolean();
            var spriteId = reader.ReadString();
            var color = ReadColor(reader);
            var flipX = reader.ReadBoolean();
            var flipY = reader.ReadBoolean();
            SpriteRenderer renderer;
            if (!renderers.TryGetValue(path, out renderer) || renderer == null) continue;
            TailVisualState tailVisual;
            var targetRenderer = renderer;
            if (tailVisuals.TryGetValue(renderer, out tailVisual))
            {
                tailVisual.visible = visible;
                renderer.enabled = false;
                targetRenderer = tailVisual.renderer;
            }
            else renderer.enabled = visible;
            if (targetRenderer == null) continue;
            targetRenderer.enabled = visible;
            if (SpriteId(targetRenderer.sprite) != spriteId) targetRenderer.sprite = FindSprite(spriteId);
            targetRenderer.color = color;
            targetRenderer.flipX = flipX;
            targetRenderer.flipY = flipY;
        }

        var lights = new Dictionary<string, Component>();
        foreach (var light in FindCharacterLights(root))
            lights[HierarchyPath(root, light.transform)] = light;
        var lightCount = reader.ReadUInt16();
        for (var index = 0; index < lightCount; index++)
        {
            var path = reader.ReadString();
            var visible = reader.ReadBoolean();
            var intensity = reader.ReadSingle();
            var color = ReadColor(reader);
            Component light;
            if (!lights.TryGetValue(path, out light) || light == null) continue;
            var behaviour = light as Behaviour;
            if (behaviour != null) behaviour.enabled = visible;
            SetComponentProperty(light, "intensity", intensity);
            SetComponentProperty(light, "color", color);
        }
    }

    private void CreateTailVisuals()
    {
        tailAttachments.Clear();
        tailVisuals.Clear();
        proceduralTailBones.Clear();
        if (tailVisualRoot != null) Destroy(tailVisualRoot);
        tailVisualRoot = null;
        if (remoteBody == null) return;

        var tails = GetTransforms(remoteBody, "tails");
        if (tails.Length == 0) return;
        var tailAnchor = remoteBody.mainTorso != null ? remoteBody.mainTorso.transform :
            remoteBody.rb == null ? null : remoteBody.rb.transform;
        foreach (var tail in tails)
            CacheProceduralTailRoot(tail, tailAnchor);

        var tailBodies = GetList(remoteBody, "tailBases");
        for (var index = 0; index < tailBodies.Count; index++)
        {
            var body = tailBodies[index] as Rigidbody2D;
            if (body != null) CacheProceduralTailBody(body, index, tailBodies.Count);
        }
        proceduralTailTime = 0f;
        proceduralTailForce = 0f;
        proceduralTailVelocity = Vector2.zero;
        hasProceduralTailPosition = false;

        tailVisualRoot = new GameObject("MP Remote Tail Visuals");
        var claimed = new HashSet<SpriteRenderer>();
        var claimedBones = new HashSet<Transform>();
        var boneIndex = 1;
        foreach (var tail in tails)
        {
            if (tail == null) continue;
            foreach (var source in tail.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (source == null || !claimed.Add(source)) continue;
                if (claimedBones.Add(source.transform))
                {
                    proceduralTailBones.Add(new ProceduralTailBoneState
                    {
                        transform = source.transform,
                        index = boneIndex++,
                        baseAngle = remoteBody.isRight ? 0f : 180f
                    });
                }
                var visual = new GameObject("MP Tail " + source.name);
                visual.layer = source.gameObject.layer;
                visual.transform.SetParent(tailVisualRoot.transform, false);
                var renderer = visual.AddComponent<SpriteRenderer>();
                renderer.sharedMaterial = source.sharedMaterial;
                renderer.sprite = source.sprite;
                renderer.color = source.color;
                renderer.flipX = source.flipX;
                renderer.flipY = source.flipY;
                renderer.sortingLayerID = source.sortingLayerID;
                renderer.sortingOrder = source.sortingOrder;
                tailVisuals[source] = new TailVisualState
                {
                    renderer = renderer,
                    visible = source.enabled && source.gameObject.activeInHierarchy,
                    tailRoot = tail,
                    initialFacingRight = remoteBody.isRight,
                    initiallyReflected = TailTransformIsReflected(source.transform)
                };
                source.enabled = false;
            }
        }
        UpdateTailVisuals();
    }

    private void CacheProceduralTailRoot(Transform transform, Transform anchor)
    {
        if (transform == null) return;
        TailAttachmentState attachment;
        if (!tailAttachments.TryGetValue(transform, out attachment))
        {
            attachment = new TailAttachmentState
            {
                body = transform.GetComponent<Rigidbody2D>(),
                localPosition = transform.localPosition,
                segmentIndex = -1,
                segmentCount = 1
            };
            tailAttachments[transform] = attachment;
        }
        if (anchor == null) return;
        attachment.anchor = anchor;
        attachment.anchorLocalPosition = anchor.InverseTransformPoint(transform.position);
        attachment.anchorLocalRotation = Quaternion.Inverse(anchor.rotation) * transform.rotation;
    }

    private void CacheProceduralTailBody(Rigidbody2D body, int index, int count)
    {
        TailAttachmentState attachment;
        if (!tailAttachments.TryGetValue(body.transform, out attachment))
        {
            attachment = new TailAttachmentState
            {
                localPosition = body.transform.localPosition
            };
            tailAttachments[body.transform] = attachment;
        }
        attachment.body = body;
        attachment.segmentIndex = index;
        attachment.segmentCount = Mathf.Max(1, count);
    }

    private void UpdateProceduralTail()
    {
        if (remoteBody == null || remoteBody.rb == null || tailAttachments.Count == 0) return;
        var deltaTime = Mathf.Clamp(Time.unscaledDeltaTime, 0.0001f, 0.05f);
        var currentPosition = remoteBody.rb.position;
        if (hasProceduralTailPosition)
        {
            var measuredVelocity = (currentPosition - lastProceduralTailPosition) / deltaTime;
            if (measuredVelocity.sqrMagnitude < 2500f)
            {
                var velocityAlpha = 1f - Mathf.Exp(-deltaTime * 8f);
                proceduralTailVelocity = Vector2.Lerp(proceduralTailVelocity, measuredVelocity, velocityAlpha);
            }
        }
        else hasProceduralTailPosition = true;
        lastProceduralTailPosition = currentPosition;

        var bobSpeed = remoteBody.tailBobSpeed;
        proceduralTailTime += deltaTime * bobSpeed * (remoteBody.isCrouching ? 0.5f : 1f);
        if (Mathf.Abs(proceduralTailVelocity.y) < 0.75f)
            proceduralTailForce += deltaTime;
        proceduralTailForce = Mathf.Clamp01(proceduralTailForce);

        var torsoAngle = remoteBody.mainTorso != null
            ? remoteBody.mainTorso.rotation
            : remoteBody.rb.rotation;
        var inverseTorso = Quaternion.Euler(0f, 0f, -torsoAngle);
        var localVelocity3 = inverseTorso * new Vector3(proceduralTailVelocity.x, proceduralTailVelocity.y, 0f);
        var localVelocity = new Vector2(localVelocity3.x, localVelocity3.y);
        var stale = new List<Transform>();
        var ordered = new List<KeyValuePair<Transform, TailAttachmentState>>(tailAttachments);
        ordered.Sort((left, right) => TransformDepth(left.Key).CompareTo(TransformDepth(right.Key)));
        foreach (var pair in ordered)
        {
            var transform = pair.Key;
            var attachment = pair.Value;
            if (transform == null || attachment == null)
            {
                stale.Add(transform);
                continue;
            }

            if (attachment.anchor != null)
                transform.position = attachment.anchor.TransformPoint(attachment.anchorLocalPosition);
            else transform.localPosition = attachment.localPosition;

            var anchoredRotation = attachment.anchor != null
                ? attachment.anchor.rotation * attachment.anchorLocalRotation
                : Quaternion.Euler(0f, 0f, torsoAngle);
            transform.rotation = anchoredRotation;
            if (attachment.body != null)
            {
                attachment.body.position = transform.position;
                attachment.body.rotation = transform.eulerAngles.z;
            }
        }
        foreach (var transform in stale) tailAttachments.Remove(transform);

        UpdateOriginalTailBob(deltaTime, localVelocity);
    }

    // FIX TAIL TODO
    private void UpdateOriginalTailBob(float deltaTime, Vector2 localVelocity)
    {
        if (proceduralTailBones.Count == 0 || proceduralTailForce <= 0.5f) return;

        var bobForce = Mathf.Max(0f, Mathf.Abs(remoteBody.tailBobForce));
        var rotationAlpha = Mathf.Clamp01(4f * deltaTime * bobForce * proceduralTailForce);
        var walkLag = Mathf.Clamp(-localVelocity.y * 2.5f, -12f, 12f);

        for (var position = 0; position < proceduralTailBones.Count; position++)
        {
            var bone = proceduralTailBones[position];
            if (bone == null || bone.transform == null) continue;

            var delay = bone.index > 3 ? 2f : 0f;
            float phase;
            if (bone.index % 3 == 0) phase = proceduralTailTime - 1.35f + delay;
            else if (bone.index == 0) phase = proceduralTailTime - 0.5f + delay;
            else phase = proceduralTailTime + delay;

            var wave = (Mathf.PingPong(phase, 2f) - 1f) * 50f;
            var lagRatio = proceduralTailBones.Count <= 1
                ? 0f
                : position / (float)(proceduralTailBones.Count - 1);
            var targetAngle = bone.baseAngle + wave + walkLag * lagRatio;
            var currentAngle = bone.transform.eulerAngles.z;
            var smoothedAngle = Mathf.LerpAngle(currentAngle, targetAngle, rotationAlpha);
            bone.transform.rotation = Quaternion.Euler(0f, 0f, smoothedAngle);
        }
    }

    private void UpdateTailVisuals()
    {
        if (tailVisuals.Count == 0) return;
        var stale = new List<SpriteRenderer>();
        foreach (var pair in tailVisuals)
        {
            var source = pair.Key;
            var visual = pair.Value;
            if (source == null || visual == null || visual.renderer == null)
            {
                stale.Add(source);
                continue;
            }
            source.enabled = false;
            visual.renderer.enabled = visual.visible;
            var mirrorHorizontally = remoteBody != null &&
                remoteBody.isRight != visual.initialFacingRight;
            visual.lastReflected = TailTransformIsReflected(source.transform) ^ mirrorHorizontally;
            ApplyTailVisualTransform2D(
                source.transform,
                visual.renderer.transform,
                visual.tailRoot,
                mirrorHorizontally);
            visual.renderer.sortingLayerID = source.sortingLayerID;
            visual.renderer.sortingOrder = source.sortingOrder;
        }
        foreach (var source in stale) tailVisuals.Remove(source);
    }

    private static bool TailTransformIsReflected(Transform transform)
    {
        var right = transform.TransformVector(Vector3.right);
        var up = transform.TransformVector(Vector3.up);
        return right.x * up.y - right.y * up.x < 0f;
    }

    private static void ApplyTailVisualTransform2D(
        Transform source,
        Transform visual,
        Transform tailRoot,
        bool mirrorHorizontally)
    {
        var right = source.TransformVector(Vector3.right);
        var up = source.TransformVector(Vector3.up);
        var position = source.position;

        if (mirrorHorizontally)
        {
            var pivotX = tailRoot != null ? tailRoot.position.x : position.x;
            position.x = pivotX - (position.x - pivotX);
            right.x = -right.x;
            up.x = -up.x;
        }

        var scaleX = new Vector2(right.x, right.y).magnitude;
        var scaleY = new Vector2(up.x, up.y).magnitude;
        var determinant = right.x * up.y - right.y * up.x;
        var angle = scaleX > 0.0001f
            ? Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg
            : Mathf.Atan2(-up.x, up.y) * Mathf.Rad2Deg;
        if (determinant < 0f) scaleY = -scaleY;

        visual.position = position;
        visual.rotation = Quaternion.Euler(0f, 0f, angle);
        visual.localScale = new Vector3(scaleX, scaleY, 1f);
    }

    private void ObserveLocalTail(BodyScript body)
    {
        if (body == null) return;
        if (!localTailFacingKnown)
        {
            localTailFacingKnown = true;
            lastLocalTailFacing = body.isRight;
            localTailDebugFrames = 4;
            WriteTailDiagnostics("LOCAL INITIAL", body, false);
        }
        else if (lastLocalTailFacing != body.isRight)
        {
            WriteTailDiagnostics("LOCAL FLIP", body, false);
            lastLocalTailFacing = body.isRight;
            localTailDebugFrames = 12;
        }
        if (localTailDebugFrames <= 0) return;
        WriteTailDiagnostics("LOCAL FRAME", body, false);
        localTailDebugFrames--;
    }

    private void WriteTailDiagnostics(string label, BodyScript body, bool includeReplicaVisuals)
    {
        if (string.IsNullOrEmpty(tailDiagnosticPath) || body == null) return;
        var builder = new StringBuilder(4096);
        builder.Append("\n--- #").Append(++tailDebugSequence)
            .Append(' ').Append(label)
            .Append(" frame=").Append(Time.frameCount)
            .Append(" time=").Append(Time.unscaledTime.ToString("F3"))
            .Append(" host=").Append(MultiplayerSession.IsHost)
            .Append(" isRight=").Append(body.isRight)
            .Append(" prefab=").Append(body.transform.root.name)
            .AppendLine(" ---");
        builder.Append("procedural velocity=").Append(FormatVector2(proceduralTailVelocity))
            .Append(" phase=").Append(proceduralTailTime.ToString("F3"))
            .Append(" attachments=").Append(tailAttachments.Count)
            .Append(" visuals=").Append(tailVisuals.Count)
            .AppendLine();
        AppendTailTransform(builder, "body", body.transform);
        if (body.rb != null)
            builder.Append("body.rb pos=").Append(FormatVector2(body.rb.position))
                .Append(" rot=").Append(body.rb.rotation.ToString("F3"))
                .Append(" vel=").Append(FormatVector2(body.rb.velocity))
                .AppendLine();
        if (body.mainTorso != null)
        {
            AppendTailTransform(builder, "torso", body.mainTorso.transform);
            builder.Append("torso.rb pos=").Append(FormatVector2(body.mainTorso.position))
                .Append(" rot=").Append(body.mainTorso.rotation.ToString("F3"))
                .AppendLine();
        }

        var root = body.transform.root;
        var tails = GetTransforms(body, "tails");
        builder.Append("tailRoots=").Append(tails.Length).AppendLine();
        for (var index = 0; index < tails.Length; index++)
        {
            var transform = tails[index];
            if (transform == null)
            {
                builder.Append(" root[").Append(index).AppendLine("]=null");
                continue;
            }
            builder.Append(" root[").Append(index).Append("] path=")
                .Append(HierarchyPath(root, transform)).AppendLine();
            AppendTailTransform(builder, "  transform", transform);
        }

        var bases = GetList(body, "tailBases");
        builder.Append("tailBases=").Append(bases.Count).AppendLine();
        for (var index = 0; index < bases.Count; index++)
        {
            var rigidbody = bases[index] as Rigidbody2D;
            if (rigidbody == null)
            {
                builder.Append(" bone[").Append(index).AppendLine("]=null");
                continue;
            }
            builder.Append(" bone[").Append(index).Append("] name=").Append(rigidbody.name)
                .Append(" path=").Append(HierarchyPath(root, rigidbody.transform))
                .Append(" rbPos=").Append(FormatVector2(rigidbody.position))
                .Append(" rbRot=").Append(rigidbody.rotation.ToString("F3"))
                .Append(" rbType=").Append(rigidbody.bodyType)
                .Append(" simulated=").Append(rigidbody.simulated);
            TailAttachmentState attachment;
            if (tailAttachments.TryGetValue(rigidbody.transform, out attachment) && attachment != null)
                builder.Append(" target=").Append(attachment.lastTargetAngle.ToString("F3"))
                    .Append(" wave=").Append(attachment.lastWave.ToString("F3"))
                    .Append(" inertia=").Append(attachment.lastInertia.ToString("F3"))
                    .Append(" alpha=").Append(attachment.lastRotationAlpha.ToString("F3"));
            builder.AppendLine();
            AppendTailTransform(builder, "  transform", rigidbody.transform);
        }

        if (includeReplicaVisuals)
        {
            var visualIndex = 0;
            foreach (var pair in tailVisuals)
            {
                var source = pair.Key;
                var visual = pair.Value;
                builder.Append(" visual[").Append(visualIndex++).Append("] source=")
                    .Append(source == null ? "null" : source.name);
                if (source != null)
                    builder.Append(" sourceDet=").Append(TailTransformDeterminant(source.transform).ToString("F4"));
                if (visual != null)
                    builder.Append(" initialFacing=").Append(visual.initialFacingRight)
                        .Append(" initialReflected=").Append(visual.initiallyReflected)
                        .Append(" desiredReflected=").Append(visual.lastReflected);
                builder.AppendLine();
                if (source != null) AppendTailTransform(builder, "  source", source.transform);
                if (visual != null && visual.renderer != null)
                    AppendTailTransform(builder, "  output", visual.renderer.transform);
            }
        }

        try { File.AppendAllText(tailDiagnosticPath, builder.ToString()); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void AppendTailTransform(StringBuilder builder, string label, Transform transform)
    {
        if (transform == null)
        {
            builder.Append(label).AppendLine("=null");
            return;
        }
        builder.Append(label)
            .Append(" name=").Append(transform.name)
            .Append(" parent=").Append(transform.parent == null ? "null" : transform.parent.name)
            .Append(" pos=").Append(FormatVector3(transform.position))
            .Append(" localPos=").Append(FormatVector3(transform.localPosition))
            .Append(" euler=").Append(FormatVector3(transform.eulerAngles))
            .Append(" localEuler=").Append(FormatVector3(transform.localEulerAngles))
            .Append(" rot=").Append(FormatQuaternion(transform.rotation))
            .Append(" localRot=").Append(FormatQuaternion(transform.localRotation))
            .Append(" scale=").Append(FormatVector3(transform.localScale))
            .Append(" lossy=").Append(FormatVector3(transform.lossyScale))
            .Append(" det2D=").Append(TailTransformDeterminant(transform).ToString("F4"))
            .AppendLine();
    }

    private static float TailTransformDeterminant(Transform transform)
    {
        var right = transform.TransformVector(Vector3.right);
        var up = transform.TransformVector(Vector3.up);
        return right.x * up.y - right.y * up.x;
    }

    private static string FormatVector2(Vector2 value)
    {
        return "(" + value.x.ToString("F3") + "," + value.y.ToString("F3") + ")";
    }

    private static string FormatVector3(Vector3 value)
    {
        return "(" + value.x.ToString("F3") + "," + value.y.ToString("F3") + "," +
            value.z.ToString("F3") + ")";
    }

    private static string FormatQuaternion(Quaternion value)
    {
        return "(" + value.x.ToString("F4") + "," + value.y.ToString("F4") + "," +
            value.z.ToString("F4") + "," + value.w.ToString("F4") + ")";
    }

    private static List<Component> FindCharacterLights(Transform root)
    {
        var lights = new List<Component>();
        foreach (var component in root.GetComponentsInChildren<Component>(true))
            if (component != null && component.GetType().FullName == "UnityEngine.Experimental.Rendering.Universal.Light2D")
                lights.Add(component);
        return lights;
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

    private static T GetComponentProperty<T>(Component component, string name, T fallback)
    {
        var property = component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property == null || !property.CanRead) return fallback;
        var value = property.GetValue(component, null);
        return value is T typed ? typed : fallback;
    }

    private static void SetComponentProperty<T>(Component component, string name, T value)
    {
        var property = component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.CanWrite) property.SetValue(component, value, null);
    }

    private static void WriteScarfState(BinaryWriter writer, BodyScript body)
    {
        var scarf = body.GetComponentInChildren<ScarfPhysics>(true);
        var visible = scarf != null && scarf.gameObject.activeInHierarchy && scarf.pointRenderer != null;
        writer.Write(visible);
        if (!visible) return;
        WriteColor(writer, scarf.pointRenderer.startColor);
        WriteColor(writer, scarf.pointRenderer.endColor);
    }

    private void ReadScarfState(BinaryReader reader)
    {
        var visible = reader.ReadBoolean();
        if (!visible)
        {
            if (remoteScarf != null) Destroy(remoteScarf);
            remoteScarf = null;
            return;
        }

        var startColor = ReadColor(reader);
        var endColor = ReadColor(reader);
        if (remoteScarf == null) CreateRemoteScarf();
        if (remoteScarf == null) return;
        var scarf = remoteScarf.GetComponent<ScarfPhysics>();
        var line = scarf == null ? remoteScarf.GetComponent<LineRenderer>() : scarf.pointRenderer;
        if (line == null) return;
        line.startColor = startColor;
        line.endColor = endColor;
    }

    private void CreateRemoteScarf()
    {
        if (remoteBody == null) return;
        var limbs = GetList(remoteBody, "limbs");
        if (limbs.Count < 2) return;
        var prefab = Resources.Load<GameObject>("Scarf");
        if (prefab == null) return;
        var parent = ((LimbScript)limbs[1]).transform;
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

    private static T GetFieldValue<T>(object instance, string name) where T : class
    {
        if (instance == null) return null;
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field == null ? null : field.GetValue(instance) as T;
    }

    private static void ApplyWeaponVisual(BodyScript body, string spriteName, int activeSlot, string[] inventorySprites)
    {
        var active = FindSprite(spriteName);
        var holstered = new Sprite[2];
        var holsterIndex = 0;
        for (var index = 0; index < inventorySprites.Length && holsterIndex < holstered.Length; index++)
        {
            if (index == activeSlot || string.IsNullOrEmpty(inventorySprites[index])) continue;
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
        var textureName = sprite.texture == null ? "" : sprite.texture.name;
        return sprite.name + "\n" + textureName + "\n" + sprite.rect.x + "," + sprite.rect.y + "," + sprite.rect.width + "," + sprite.rect.height;
    }

    private static Sprite FindSprite(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Sprite cached;
        if (spriteCache.TryGetValue(id, out cached) && cached != null) return cached;
        var parts = id.Split('\n');
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (SpriteId(sprite) != id) continue;
            spriteCache[id] = sprite;
            return sprite;
        }
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite.name != parts[0]) continue;
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

    private static SpriteRenderer FindVisibleWeaponRenderer(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            if (renderer.name == "testGun" && renderer.sprite != null) return renderer;
        return null;
    }


    private struct TargetState
    {
        public Vector3 fromPosition;
        public Quaternion fromRotation;
        public Vector3 position;
        public Quaternion rotation;
        public float startedAt;
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
    private sealed class TailVisualState
    {
        public SpriteRenderer renderer;
        public bool visible;
        public Transform tailRoot;
        public bool initialFacingRight;
        public bool initiallyReflected;
        public bool lastReflected;
    }
    private sealed class TailAttachmentState
    {
        public Rigidbody2D body;
        public Vector3 localPosition;
        public Transform anchor;
        public Vector3 anchorLocalPosition;
        public Quaternion anchorLocalRotation;
        public int segmentIndex;
        public int segmentCount;
        public float lastWave;
        public float lastInertia;
        public float lastTargetAngle;
        public float lastRotationAlpha;
    }
    private sealed class ProceduralTailBoneState
    {
        public Transform transform;
        public int index;
        public float baseAngle;
    }
}


internal sealed class NetworkReplica : MonoBehaviour { }

// It's like mixins for minecraft lol

[HarmonyPatch(typeof(MainMenuManager), "UpdateCharacter")]
internal static class CharacterSelectionReplicationPatch
{
    private static void Postfix(MainMenuManager __instance)
    {
        NetworkAvatarReplication.CaptureCharacterMenu(__instance);
    }
}

internal sealed class RemotePlayerInfo
{
    internal ushort PeerId;
    internal string Name = "Player";
    internal BodyScript Body;
    internal int PingMs = -1;
}

[HarmonyPatch(typeof(PlayerScript), "Start")]
internal static class LocalCharacterCreationPatch
{
    private static void Prefix()
    {
        NetworkAvatarReplication.RestoreCharacterSelection();
    }
}

[HarmonyPatch(typeof(WeaponBackShow), "WepChanged")]
internal static class WeaponBackShowSlotGuardPatch
{
    private static void Prefix(BodyScript ___body)
    {
        NetworkAvatarReplication.EnsureRespawnWeaponSlots(___body);
    }
}

[HarmonyPatch(typeof(PlayerScript), "BodyAmmoChanged")]
internal static class PlayerAmmoDisplaySlotGuardPatch
{
    private static void Prefix(PlayerScript __instance)
    {
        NetworkAvatarReplication.EnsurePlayerAmmoDisplaySlots(__instance);
    }
}
