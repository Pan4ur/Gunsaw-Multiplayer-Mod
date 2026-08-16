using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class WorldReplication : MonoBehaviour
{
    internal static WorldReplication Instance;

    // пиздец почему это не енам
    internal const byte WeaponPickup = 1;
    internal const byte WeaponAmmoGet = 2;
    private const byte ButtonActivate = 3;
    private const byte DoorActivate = 4;
    private const byte ZoneActivate = 5;
    private const byte GlassDamage = 6;
    private const byte VehicleDamage = 7;
    private const byte DroneDamage = 8;
    private const byte WeaponDrop = 9;

    // Ill just leave it like this for now (It's becoming painful to drive the karts)
    private const float SnapshotInterval = 1f / 50f;

    private const float FullSnapshotInterval = 1f;
    private const float ClientAuthorityGrace = 0.35f;
    private const float ContactStateInterval = 0.1f;
    
    private readonly Dictionary<string, Rigidbody2D> bodies = new();
    private readonly Dictionary<Rigidbody2D, string> ids = new();
    private readonly Dictionary<string, ulong> wireIds = new();
    private readonly Dictionary<ulong, string> idsByWire = new();
    private readonly Dictionary<Rigidbody2D, DroppedWeapon> droppedWeapons = new ();
    private readonly Dictionary<Rigidbody2D, BodyLayout> bodyLayouts = new();
    private readonly HashSet<Rigidbody2D> interactivePropBodies = new();
    private readonly Dictionary<string, float> pendingDestroyedWeaponPickups = new();
    private readonly HashSet<string> clientDestroyedBodyIds = new();
    private readonly Dictionary<Rigidbody2D, State> received = new();
    private readonly Dictionary<Rigidbody2D, List<VehiclePathState>> vehiclePaths = new();
    private readonly List<Rigidbody2D> staleVehiclePaths = new();
    private readonly Dictionary<string, ClientBodyState> pushes = new();
    private readonly Dictionary<Rigidbody2D, float> locallyControlledUntil = new();
    private readonly Dictionary<string, PropAuthority> propAuthorities = new();
    private readonly ContactPoint2D[] contactBuffer = new ContactPoint2D[32];
    private readonly Dictionary<Rigidbody2D, float> nextContactStateAt = new();
    private readonly Dictionary<string, float> damage = new();
    private readonly Dictionary<string, float> nextDamage = new();
    private int nextRuntimeId;
    private int worldSnapshotSequence;
    private int lastReceivedWorldSnapshotSequence;
    private bool hasReceivedWorldSnapshotSequence;
    private readonly Dictionary<Rigidbody2D, LocalSettings> localSettings = new();
    private readonly HashSet<Rigidbody2D> clientCreatedBodies = [];
    private readonly HashSet<Rigidbody2D> clientBoundDroppedWeapons = [];
    private readonly HashSet<Rigidbody2D> networkCrateDebrisBodies = [];
    private readonly Dictionary<CrateScript, float> networkCrateDebrisDamageUntil = new();
    private readonly Dictionary<GameObject, bool> clientHiddenObjects = new();
    private readonly Dictionary<MonoBehaviour, bool> clientControllers = new();
    private readonly HashSet<Rigidbody2D> initializedBodies = [];
    private readonly Dictionary<string, ButtonScript> buttons = new();
    private readonly Dictionary<ButtonScript, string> buttonIds = new();
    private readonly Dictionary<string, uint> buttonActivations = new();
    private readonly Dictionary<string, uint> receivedButtonActivations = new();
    private readonly Dictionary<string, float> nextButtonActivation = new();
    private readonly Dictionary<string, QDoorOpen> proximityDoors = new();
    private readonly Dictionary<QDoorOpen, string> proximityDoorIds = new();
    private readonly Dictionary<string, float> nextDoorActivation = new();
    private readonly Dictionary<string, DoorScript> replicatedDoors = new();
    private readonly Dictionary<DoorScript, string> replicatedDoorIds = new();
    private readonly Dictionary<string, bool> hostDoorTargets = new();
    private readonly Dictionary<string, bool> hostDoorMoving = new();
    private readonly Dictionary<string, uint> hostDoorRevisions = new();
    private readonly Dictionary<string, uint> clientDoorRevisions = new();
    private readonly Dictionary<string, bool> clientDoorTargets = new();
    private readonly Dictionary<string, bool> clientDoorMoving = new();
    private readonly Dictionary<string, Vector2> clientDoorTargetPositions = new();
    private bool requestedDoorSnapshot;
    private readonly Dictionary<string, ActivateZoneScript> activationZones = new();
    private readonly Dictionary<ActivateZoneScript, string> activationZoneIds = new();
    private readonly Dictionary<string, float> nextZoneActivation = new();
    private readonly HashSet<string> activatedZoneIds = [];
    private readonly HashSet<string> localZonePrompts = [];
    private ActivateZoneScript promptZone;
    internal bool HasActivationPrompt => promptZone != null && MultiplayerSession.IsConnected;
    private readonly Dictionary<string, GlassScript> glasses = new();
    private readonly Dictionary<GlassScript, string> glassIds = new();
    private readonly HashSet<string> destroyedGlass = [];
    private readonly Dictionary<string, LampState> lamps = [];
    private readonly Dictionary<Collider2D, string> lampIds = new();
    private readonly HashSet<string> destroyedLamps = [];
    private readonly Dictionary<string, DroneScript> drones = new();
    private readonly Dictionary<DroneScript, string> droneIds = new();
    private readonly HashSet<string> destroyedDrones = [];
    private readonly HashSet<Rigidbody2D> droneBodies = [];
    private readonly Dictionary<FireScript, string> fireIds = new();
    private readonly Dictionary<string, FireScript> fires = new();
    private readonly Dictionary<FireScript, int> pendingRuntimeFires = new();
    private int nextRuntimeFireId;
    private readonly Dictionary<FireScript, FireLocalSettings> clientFireSettings = new();
    private readonly HashSet<FireScript> clientCreatedFires = [];
    private readonly HashSet<string> seenSnapshotFires = [];
    private readonly HashSet<SawScript> clientSaws = [];
    private byte[] lastSerializedWorld;
    private byte[] lastSerializedEnvironment;
    private byte[] lastReliableEnvironment;
    private readonly Dictionary<string, byte[]> lastSerializedBodyStates = new();
    private readonly Dictionary<string, BodyStateScratch> bodyStateScratch = new();
    private readonly Dictionary<string, float> lastChangedBodyAt = new();
    private float nextSnapshot;
    private float nextReliableEnvironment;
    private float nextFireRefresh;
    private float nextDroppedWeaponIndicatorUpdate;
    private float nextFullWorldSnapshot;
    private bool wasConnected;
    private bool wasHost;
    private bool discoveredScene;
    private int activeSceneHandle;
    private int lastSentPropCount;
    private int lastSentOtherCount;
    private int culledPropCount;
    private int culledOtherCount;
    private float nextActivitySample;
    private int sentPacketsWindow;
    private int sentStatesWindow;
    private int receivedPacketsWindow;
    private int receivedStatesWindow;
    private int sentPacketsPerSecond;
    private int sentStatesPerSecond;
    private int receivedPacketsPerSecond;
    private int receivedStatesPerSecond;
    private float clientFastSerializeState = 0f;
    private Transform localContactRoot;
    private Rigidbody2D[] localContactBodies = new Rigidbody2D[0];

    internal int TotalPropCount
    {
        get
        {
            var count = 0;
            foreach (var body in bodies.Values)
                if (body != null && IsInteractivePropBody(body)) count++;
            return count;
        }
    }

    internal int TotalOtherCount
    {
        get
        {
            var count = buttons.Count;
            foreach (var fire in fires.Values) if (fire != null) count++;
            foreach (var body in bodies.Values)
                if (body != null && !IsInteractivePropBody(body)) count++;
            return count;
        }
    }

    internal int LastSnapshotPropCount => lastSentPropCount;
    internal int LastSnapshotOtherCount => lastSentOtherCount;
    internal int CulledPropCount => culledPropCount;
    internal int CulledOtherCount => culledOtherCount;
    internal int SentPacketsPerSecond => sentPacketsPerSecond;
    internal int SentStatesPerSecond => sentStatesPerSecond;
    internal int ReceivedPacketsPerSecond => receivedPacketsPerSecond;
    internal int ReceivedStatesPerSecond => receivedStatesPerSecond;

    private void Awake()
    {
        Instance = this;
    }

    internal static void TrackDroppedWeapons()
    {
        var current = Instance;
        if (current == null || !MultiplayerSession.IsHost) return;
        foreach (var dropped in FindObjectsOfType<DroppedWeapon>())
            RegisterDroppedWeapon(dropped);
    }
    
    internal static void RegisterDroppedWeapon(DroppedWeapon dropped)
    {
        var current = Instance;
        if (current == null || !MultiplayerSession.IsHost || dropped == null) return;
        foreach (var body in dropped.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (body == null) continue;
            current.RegisterWorldBody(body);
            current.droppedWeapons[body] = dropped;
        }
    }

    internal void RegisterDestroyedCrateDebris(CrateScript crate, Rigidbody2D[] debrisBodies)
    {
        if (!MultiplayerSession.IsHost || crate == null || crate.breakType != CrateScript.BreakType.None ||
            debrisBodies == null || debrisBodies.Length == 0) return;
        var crateBody = crate.GetComponent<Rigidbody2D>();
        if (crateBody == null) return;
        var crateId = Id(crateBody);
        RegisterCrateDebrisBodies(crateId, debrisBodies, false);
    }

    internal void ApplyDistanceCulling()
    {
        if (!MultiplayerSession.IsHost) return;
        culledPropCount = 0;
        culledOtherCount = 0;
        foreach (var body in bodies.Values)
        {
            MultiplayerLoadDistance.ApplyWorldBody(body);
            if (!MultiplayerLoadDistance.IsSimulationCulled(body)) continue;
            if (IsInteractivePropBody(body)) culledPropCount++;
            else culledOtherCount++;
        }
    }

    private void Update()
    {
        var performanceStarted = MultiplayerPerformance.Start();
        try
        {
        SampleActivity();
        var scene = SceneManager.GetActiveScene();
        var isHost = MultiplayerSession.IsHost;
        var sceneChanged = activeSceneHandle != scene.handle;
        var roleChanged = wasConnected && wasHost != isHost;
        if (sceneChanged || roleChanged)
        {
            if (wasConnected) RestoreClientWorld();
            activeSceneHandle = scene.handle;
            nextSnapshot = 0f;
            discoveredScene = false;
        }

        if (!MultiplayerSession.IsConnected)
        {
            if (wasConnected) RestoreClientWorld();
            wasConnected = false;
            wasHost = isHost;
            return;
        }

        if (!wasConnected)
        {
            nextSnapshot = 0f;
            discoveredScene = false;
        }
        wasConnected = true;
        wasHost = isHost;
        if (!discoveredScene)
        {
            var discoveryStarted = MultiplayerPerformance.StartPhase();
            discoveredScene = true;
            RefreshWorldBodies();
            RefreshButtons();
            RefreshProximityDoors();
            RefreshReplicatedDoors();
            RefreshActivationZones();
            RefreshGlasses();
            RefreshDrones();
            DiscoverWorldFires();
            RefreshClientSaws();
            RefreshWorldControllers();
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldDiscovery, discoveryStarted);
        }
        if (Time.unscaledTime >= nextFireRefresh)
        {
            nextFireRefresh = Time.unscaledTime + 0.1f;
            var fireRefreshStarted = MultiplayerPerformance.StartPhase();
            RefreshKnownWorldFires();
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldFireRefresh, fireRefreshStarted);
        }
        ProcessPendingRuntimeFires();
        if (isHost)
        {
            var zonePromptStarted = MultiplayerPerformance.StartPhase();
            UpdateZonePrompt();
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldZonePrompt, zonePromptStarted);
            var inputStarted = MultiplayerPerformance.StartPhase();
            byte[] interaction;
            ushort interactionPeer;
            while (MultiplayerSession.TryTakeWorldInteraction(out interactionPeer, out interaction))
                ApplyWeaponInteraction(interactionPeer, interaction);
            ProcessDoorStatePackets();
            BroadcastChangedDoorStates();
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldInput, inputStarted);
            return;
        }

        ProcessDoorStatePackets();
        if (!requestedDoorSnapshot)
        {
            MultiplayerSession.Send(DoorStatePacket.RequestSnapshot(MultiplayerSession.SnapshotEpoch));
            requestedDoorSnapshot = true;
        }

        var clientZonePromptStarted = MultiplayerPerformance.StartPhase();
        UpdateZonePrompt();
        MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldZonePrompt, clientZonePromptStarted);

        byte[] snapshot;
        byte[] latestSnapshot = null;
        var latestSnapshotSequence = lastReceivedWorldSnapshotSequence;
        var queueStarted = MultiplayerPerformance.StartPhase();
        while (MultiplayerSession.TryTakeWorldSnapshot(out snapshot))
        {
            if (!TryReadWorldSnapshotSequence(snapshot, out var sequence)) continue;
            if (latestSnapshot == null || IsNewerWorldSnapshotSequence(sequence, latestSnapshotSequence))
            {
                latestSnapshot = snapshot;
                latestSnapshotSequence = sequence;
            }
        }
        MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotQueue, queueStarted);
        if (latestSnapshot != null)
        {
            var readStarted = MultiplayerPerformance.StartPhase();
            ReadSnapshot(latestSnapshot);
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotRead, readStarted);
        }
        byte[] environment;
        byte[] latestEnvironment = null;
        queueStarted = MultiplayerPerformance.StartPhase();
        while (MultiplayerSession.TryTakeWorldEnvironment(out environment)) latestEnvironment = environment;
        MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotQueue, queueStarted);
        if (latestEnvironment != null)
        {
            var readStarted = MultiplayerPerformance.StartPhase();
            ApplyEnvironment(latestEnvironment);
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotRead, readStarted);
        }
        var lodFreezeStarted = MultiplayerPerformance.StartPhase();
        FreezeFarClientProps();
        MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldClientLodFreeze, lodFreezeStarted);
        var sawsStarted = MultiplayerPerformance.StartPhase();
        AnimateClientSaws();
        MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldClientSaws, sawsStarted);
        var weaponIndicatorsStarted = MultiplayerPerformance.StartPhase();
        AnimateClientDroppedWeaponIndicators();
        MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldDroppedWeaponIndicators, weaponIndicatorsStarted);
        }
        finally
        {
            MultiplayerPerformance.AddWorld(performanceStarted);
        }
    }

    private void FixedUpdate()
    {
        var performanceStarted = MultiplayerPerformance.Start();
        try
        {
        if (!MultiplayerSession.IsConnected) return;
        if (MultiplayerSession.IsHost)
        {
            var inputStarted = MultiplayerPerformance.StartPhase();
            ushort inputPeer;
            WorldInputPacket input;
            while (MultiplayerSession.TryTakeWorldInput(out inputPeer, out input)) ApplyPushes(inputPeer, input);
            WorldDamagePacket damagePacket;
            while (MultiplayerSession.TryTakeWorldDamage(out damagePacket)) ApplyDamage(damagePacket);
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldInput, inputStarted);
            if (Time.unscaledTime >= nextSnapshot)
            {
                nextSnapshot = Time.unscaledTime + SnapshotInterval;
                var serializeStarted = MultiplayerPerformance.StartPhase();
                var snapshot = SerializeWorld();
                MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSerialize, serializeStarted);
                if (snapshot != null)
                { var snapshotReader = new PacketReader(snapshot); MultiplayerSession.Send(WorldSnapshotPacket.Read(ref snapshotReader)); }
                if (lastSerializedEnvironment != null &&
                    Time.unscaledTime >= nextReliableEnvironment &&
                    !BytesEqual(lastReliableEnvironment, lastSerializedEnvironment))
                {
                    nextReliableEnvironment = Time.unscaledTime + 0.1f;
                    MultiplayerSession.Send(new WorldEnvironmentPacket(lastSerializedEnvironment));
                    lastReliableEnvironment = lastSerializedEnvironment;
                }
            }
            return;
        }
        var contactsStarted = MultiplayerPerformance.StartPhase();
        CaptureLocalContacts();
        MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldContacts, contactsStarted);
        var authorityStarted = MultiplayerPerformance.StartPhase();
        MaintainMovingLocalAuthorities();
        MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldAuthorityMaintenance, authorityStarted);
        if (received.Count > 0)
        {
            var applyStarted = MultiplayerPerformance.StartPhase();
            foreach (var pair in received)
            {
                var body = pair.Key;
                if (body == null) continue;
                ApplyAuthoritativeState(body, pair.Value);
            }
            received.Clear();
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldStateApply, applyStarted);
        }
        TickVehiclePaths();
        AnimateClientDoors();
        if (clientFastSerializeState > 0f || Time.unscaledTime >= nextSnapshot)
        {
            var clientSendStarted = MultiplayerPerformance.StartPhase();
            clientFastSerializeState -= Time.fixedDeltaTime;
            nextSnapshot = Time.unscaledTime + SnapshotInterval;
            MultiplayerSession.Send(SerializePushes(), 1);
            MultiplayerSession.Send(SerializeDamage(), 1);
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldClientSend, clientSendStarted);
        }
        }
        finally
        {
            MultiplayerPerformance.AddWorld(performanceStarted);
        }
    }

    private void CaptureLocalContacts()
    {
        var player = PlayerScript.player;
        if (player == null || player.bodyScript == null) return;
        var localBody = player.bodyScript;
        var root = localBody.transform.root;
        if (root != localContactRoot)
        {
            localContactRoot = root;
            localContactBodies = root.GetComponentsInChildren<Rigidbody2D>();
            nextContactStateAt.Clear();
        }
        var now = Time.unscaledTime;
        foreach (var localRigidbody in localContactBodies)
        {
            if (localRigidbody == null || !localRigidbody.simulated) continue;
            var count = localRigidbody.GetContacts(contactBuffer);
            for (var index = 0; index < count; index++)
            {
                var contact = contactBuffer[index];
                var other = IsLocalPlayerCollider(contact.collider, localBody)
                    ? contact.otherCollider : contact.collider;
                if (other == null || IsLocalPlayerCollider(other, localBody)) continue;
                var body = other.attachedRigidbody;
                if (body == null || body.bodyType != RigidbodyType2D.Dynamic || !body.simulated ||
                    !ids.ContainsKey(body)) continue;

                QueueContactBodyState(body, now);
            }
        }
    }

    private static bool IsLocalPlayerCollider(Collider2D collider, BodyScript localBody)
    {
        return collider != null && collider.transform.root == localBody.transform.root;
    }

    private void FreezeFarClientProps()
    {
        if (MultiplayerSession.IsHost) return;
        var player = PlayerScript.player;
        var localBody = player == null ? null : player.bodyScript;
        if (localBody == null) return;
        var localPosition = localBody.rb == null ? (Vector2)localBody.transform.position : localBody.rb.position;
        foreach (var body in interactivePropBodies)
        {
            if (body == null || (body.position - localPosition).sqrMagnitude < MultiplayerLoadDistance.WorldDistanceSqr)
                continue;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = false;
        }
    }

    private void RefreshWorldBodies()
    {
        foreach (var body in FindObjectsOfType<Rigidbody2D>())
            RegisterWorldBody(body);
    }

    internal void RegisterRuntimeWorldBodies(GameObject runtimeObject)
    {
        if (!MultiplayerSession.IsHost || runtimeObject == null) return;
        foreach (var body in runtimeObject.GetComponentsInChildren<Rigidbody2D>(true))
            RegisterWorldBody(body);
    }

    private void RegisterWorldBody(Rigidbody2D body)
    {
        if (!IsWorldBody(body))
        {
            RemoveWorldBody(body);
            return;
        }
        var id = Id(body);
        bodies[id] = body;
        MultiplayerLoadDistance.RegisterWorldBody(body);
        WireId(id);
        if (!droppedWeapons.ContainsKey(body))
            droppedWeapons[body] = body.GetComponentInParent<DroppedWeapon>();
        if (!bodyLayouts.ContainsKey(body)) bodyLayouts[body] = CreateBodyLayout(body);
        var interactiveProp = IsInteractivePropBodyUncached(body);
        if (interactiveProp) interactivePropBodies.Add(body);
        else interactivePropBodies.Remove(body);
        if (MultiplayerSession.IsHost && interactiveProp)
            NetworkAvatarReplication.IgnoreRemotePlayerPropCollisions(body);
        if (!MultiplayerSession.IsHost) MakeClientControlled(body);
    }

    private void RemoveWorldBody(Rigidbody2D body)
    {
        if (body == null) return;
        MultiplayerLoadDistance.UnregisterWorldBody(body);
        string id;
        if (ids.TryGetValue(body, out id))
        {
            bodies.Remove(id);
            ids.Remove(body);
            propAuthorities.Remove(id);
        }
        droppedWeapons.Remove(body);
        bodyLayouts.Remove(body);
        interactivePropBodies.Remove(body);
        received.Remove(body);
        locallyControlledUntil.Remove(body);
        nextContactStateAt.Remove(body);
        localSettings.Remove(body);
        initializedBodies.Remove(body);
    }

    private static bool IsWorldBody(Rigidbody2D body)
    {
        if (body == null || !body.gameObject.scene.isLoaded) return false;
        if (body.GetComponentInParent<BodyScript>() != null ||
            body.GetComponentInParent<PlayerScript>() != null ||
            body.GetComponentInParent<NetworkReplica>() != null ||
            NpcReplication.IsNpcRigBody(body)) return false;


        var localPlayer = PlayerScript.player;
        if (localPlayer != null && localPlayer.bodyScript != null &&
            body.transform.root == localPlayer.bodyScript.transform.root) return false;

        if (IsInteractivePropBodyUncached(body)) return true;
        if (IsDroneBody(body)) return true;
        return !IsGameplayOwned(body) && IsMechanismBody(body);
    }

    private bool IsInteractivePropBody(Rigidbody2D body)
    {
        if (body == null) return false;
        if (interactivePropBodies.Contains(body)) return true;
        return !ids.ContainsKey(body) && IsInteractivePropBodyUncached(body);
    }

    private static bool IsInteractivePropBodyUncached(Rigidbody2D body)
    {
        return body != null && (body.GetComponentInParent<CrateScript>() != null ||
            body.GetComponentInParent<DroppedWeapon>() != null);
    }

    private static bool IsMechanismBody(Rigidbody2D body)
    {
        return body != null && (body.GetComponentInParent<DoorScript>() != null ||
            body.GetComponentInParent<VehiclePart>() != null ||
            IsSafetyRailingBody(body));
    }

    private static bool IsDoorBody(Rigidbody2D body)
    {
        return body != null && body.GetComponentInParent<DoorScript>() != null;
    }

    private static bool IsDroneBody(Rigidbody2D body)
    {
        return body != null && body.GetComponentInParent<DroneScript>() != null;
    }

    private static bool IsSafetyRailingBody(Rigidbody2D body)
    {
        if (body == null) return false;
        for (var current = body.transform; current != null; current = current.parent)
            if (current.name.StartsWith("SafetyRailing", StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool IsSafetyRailingAttached(BodyLayout layout)
    {
        if (!layout.SafetyRailing) return false;
        foreach (var joint in layout.Joints)
            if (joint != null && joint.enabled) return true;
        return false;
    }

    private BodyLayout BodyLayoutFor(Rigidbody2D body)
    {
        BodyLayout layout;
        if (bodyLayouts.TryGetValue(body, out layout)) return layout;
        layout = CreateBodyLayout(body);
        bodyLayouts[body] = layout;
        return layout;
    }

    private static BodyLayout CreateBodyLayout(Rigidbody2D body)
    {
        var crate = body.GetComponentInParent<CrateScript>();
        var vehiclePart = body.GetComponent<VehiclePart>();
        return new BodyLayout
        {
            Crate = crate,
            CratePrefabName = crate == null ? "" : CleanCloneName(crate.transform.root.name),
            SafetyRailing = IsSafetyRailingBody(body),
            Joints = body.GetComponents<Joint2D>(),
            VehiclePart = vehiclePart,
            Vehicle = vehiclePart == null ? null : vehiclePart.vehicle ?? vehiclePart.GetComponentInParent<VehicleBase>(),
            VehicleJoint = body.GetComponent<Joint2D>()
        };
    }

    private static void DetachSafetyRailing(Rigidbody2D body)
    {
        if (!IsSafetyRailingBody(body)) return;
        foreach (var joint in body.GetComponents<Joint2D>())
            if (joint != null) Destroy(joint);
    }

    private static bool IsGameplayOwned(Component component)
    {
        if (component == null) return false;
        if (component.GetComponentInParent<BodyScript>() != null ||
            component.GetComponentInParent<PlayerScript>() != null ||
            component.GetComponentInParent<NetworkReplica>() != null ||
            component.GetComponentInParent<WeaponScript>() != null ||
            component.GetComponentInParent<GrenadeScript>() != null ||
            component.GetComponentInParent<RocketProjectile>() != null) return true;

        var localPlayer = PlayerScript.player;
        return localPlayer != null && localPlayer.bodyScript != null &&
            component.transform.root == localPlayer.bodyScript.transform.root;
    }

    private void MakeClientControlled(Rigidbody2D body)
    {
        if (!localSettings.ContainsKey(body))
        {
            var crate = body.GetComponentInParent<CrateScript>();
            localSettings.Add(body, new LocalSettings
            {
                bodyType = body.bodyType,
                simulated = body.simulated,
                crate = crate,
                crateEnabled = crate != null && crate.enabled,
                droppedWeapon = body.GetComponentInParent<DroppedWeapon>(),
                droppedWeaponEnabled = body.GetComponentInParent<DroppedWeapon>() != null && body.GetComponentInParent<DroppedWeapon>().enabled
            });
        }

        var crateScript = body.GetComponentInParent<CrateScript>();
        if (crateScript != null) crateScript.enabled = false;
        var droppedWeapon = body.GetComponentInParent<DroppedWeapon>();
        if (droppedWeapon != null) droppedWeapon.enabled = false;
        if (IsMechanismBody(body) && !IsInteractivePropBody(body) && body.simulated)
            body.bodyType = RigidbodyType2D.Kinematic;
    }

    internal ulong VehicleWireId(VehicleBase vehicle)
    {
        if (vehicle == null || vehicle.mainPart == null || vehicle.mainPart.rb == null) return 0UL;
        return WireId(Id(vehicle.mainPart.rb));
    }

    internal VehicleBase FindVehicle(ulong wireId)
    {
        if (wireId == 0UL) return null;
        foreach (var vehicle in FindObjectsOfType<VehicleBase>())
        {
            if (vehicle == null || vehicle.mainPart == null || vehicle.mainPart.rb == null) continue;
            if (VehicleWireId(vehicle) == wireId) return vehicle;
        }
        return null;
    }

    private void RefreshWorldControllers()
    {
        if (MultiplayerSession.IsHost) return;
        RestoreGameplayControllers();
        DisableControllers(FindObjectsOfType<DoorScript>());
        DisableControllers(FindObjectsOfType<DelayedTrigger>());
        DisableControllers(FindObjectsOfType<TimedTrigger>());
        DisableControllers(FindObjectsOfType<MiniCrateSpawner>());
        DisableControllers(FindObjectsOfType<DroneScript>());
    }

    private void RefreshClientSaws()
    {
        clientSaws.Clear();
        foreach (var saw in FindObjectsOfType<SawScript>())
            if (saw != null && !IsGameplayOwned(saw)) clientSaws.Add(saw);
    }

    private void AnimateClientSaws()
    {
        foreach (var saw in clientSaws)
        {
            if (saw == null || saw.enabled || IsGameplayOwned(saw)) continue;
            var angles = saw.transform.eulerAngles;
            angles.z += saw.rotSpeed * Time.deltaTime;
            saw.transform.eulerAngles = angles;
        }
    }

    private void AnimateClientDroppedWeaponIndicators()
    {
        if (Time.unscaledTime < nextDroppedWeaponIndicatorUpdate) return;
        nextDroppedWeaponIndicatorUpdate = Time.unscaledTime + 0.1f;
        var player = PlayerScript.player;
        var localBody = player == null ? null : player.bodyScript;
        var localPosition = localBody == null ? Vector2.zero :
            (localBody.rb == null ? (Vector2)localBody.transform.position : localBody.rb.position);
        foreach (var dropped in droppedWeapons.Values)
        {
            if (dropped == null) continue;
            if (localBody != null && ((Vector2)dropped.transform.position - localPosition).sqrMagnitude > 256f)
                continue;
            SynchronizeDroppedWeaponAmmoIndicator(dropped);
        }
    }

    private void DisableControllers<T>(T[] controllers) where T : MonoBehaviour
    {
        foreach (var controller in controllers)
        {
            DisableController(controller);
        }
    }

    private void DisableController(MonoBehaviour controller)
    {
        if (controller == null || IsGameplayOwned(controller) || clientControllers.ContainsKey(controller)) return;
        clientControllers[controller] = controller.enabled;
        controller.enabled = false;
    }

    private void RestoreGameplayControllers()
    {
        var restore = new List<MonoBehaviour>();
        foreach (var pair in clientControllers)
        {
            if (pair.Key != null && IsGameplayOwned(pair.Key)) restore.Add(pair.Key);
        }
        foreach (var controller in restore)
        {
            controller.enabled = clientControllers[controller];
            clientControllers.Remove(controller);
        }
    }

    private void RestoreClientWorld()
    {
        nextRuntimeId = 0;
        foreach (var pair in localSettings)
        {
            if (pair.Key == null) continue;
            pair.Key.bodyType = pair.Value.bodyType;
            pair.Key.simulated = pair.Value.simulated;
            if (pair.Value.crate != null) pair.Value.crate.enabled = pair.Value.crateEnabled;
            if (pair.Value.droppedWeapon != null) pair.Value.droppedWeapon.enabled = pair.Value.droppedWeaponEnabled;
        }
        localSettings.Clear();
        foreach (var pair in clientControllers)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        clientControllers.Clear();
        bodies.Clear();
        droppedWeapons.Clear();
        bodyLayouts.Clear();
        interactivePropBodies.Clear();
        pendingDestroyedWeaponPickups.Clear();
        clientDestroyedBodyIds.Clear();
        received.Clear();
        vehiclePaths.Clear();
        pushes.Clear();
        locallyControlledUntil.Clear();
        nextContactStateAt.Clear();
        localContactRoot = null;
        localContactBodies = new Rigidbody2D[0];
        propAuthorities.Clear();
        damage.Clear();
        ids.Clear();
        initializedBodies.Clear();
        buttons.Clear();
        buttonIds.Clear();
        buttonActivations.Clear();
        receivedButtonActivations.Clear();
        nextButtonActivation.Clear();
        proximityDoors.Clear();
        proximityDoorIds.Clear();
        nextDoorActivation.Clear();
        replicatedDoors.Clear();
        replicatedDoorIds.Clear();
        hostDoorTargets.Clear();
        hostDoorMoving.Clear();
        hostDoorRevisions.Clear();
        clientDoorRevisions.Clear();
        clientDoorTargets.Clear();
        clientDoorMoving.Clear();
        clientDoorTargetPositions.Clear();
        requestedDoorSnapshot = false;
        activationZones.Clear();
        activationZoneIds.Clear();
        nextZoneActivation.Clear();
        activatedZoneIds.Clear();
        localZonePrompts.Clear();
        promptZone = null;
        glasses.Clear();
        glassIds.Clear();
        destroyedGlass.Clear();
        drones.Clear();
        droneIds.Clear();
        destroyedDrones.Clear();
        droneBodies.Clear();
        lamps.Clear();
        lampIds.Clear();
        destroyedLamps.Clear();
        foreach (var pair in clientFireSettings)
        {
            if (pair.Key == null) continue;
            pair.Key.gameObject.SetActive(pair.Value.active);
            pair.Key.enabled = pair.Value.enabled;
        }
        foreach (var fire in clientCreatedFires)
            if (fire != null) Destroy(fire.gameObject);
        foreach (var pair in clientHiddenObjects)
            if (pair.Key != null) pair.Key.SetActive(pair.Value);
        clientHiddenObjects.Clear();
        foreach (var body in clientCreatedBodies)
            if (body != null) Destroy(body.gameObject);
        clientCreatedBodies.Clear();
        clientBoundDroppedWeapons.Clear();
        networkCrateDebrisBodies.Clear();
        networkCrateDebrisDamageUntil.Clear();
        clientFireSettings.Clear();
        clientCreatedFires.Clear();
        pendingRuntimeFires.Clear();
        nextRuntimeFireId = 0;
        fireIds.Clear();
        fires.Clear();
        clientSaws.Clear();
        wireIds.Clear();
        idsByWire.Clear();
        lastSerializedWorld = null;
        worldSnapshotSequence = 0;
        lastReceivedWorldSnapshotSequence = 0;
        hasReceivedWorldSnapshotSequence = false;
        lastSerializedEnvironment = null;
        lastReliableEnvironment = null;
        lastSerializedBodyStates.Clear();
        foreach (var scratch in bodyStateScratch.Values) scratch.Dispose();
        bodyStateScratch.Clear();
        lastChangedBodyAt.Clear();
        nextFullWorldSnapshot = 0f;
        nextReliableEnvironment = 0f;
        nextFireRefresh = 0f;
        nextDroppedWeaponIndicatorUpdate = 0f;
        nextActivitySample = 0f;
        sentPacketsWindow = sentStatesWindow = receivedPacketsWindow = receivedStatesWindow = 0;
        sentPacketsPerSecond = sentStatesPerSecond = receivedPacketsPerSecond = receivedStatesPerSecond = 0;
    }

    private byte[] SerializeWorld()
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(MultiplayerSession.SnapshotEpoch);
            writer.Write(++worldSnapshotSequence);
            var fullSnapshot = Time.unscaledTime >= nextFullWorldSnapshot;
            if (fullSnapshot) nextFullWorldSnapshot = Time.unscaledTime + FullSnapshotInterval;
            var changedStates = new List<byte[]>();
            var changedPropCount = 0;
            var changedOtherBodyCount = 0;
            var bodySerializeStarted = MultiplayerPerformance.StartPhase();
            foreach (var pair in bodies)
            {
                var body = pair.Value;
                if (IsDoorBody(body)) continue;
                if (!fullSnapshot && body != null && !MultiplayerLoadDistance.IsWorldNearAnyPlayer(body)) continue;
                var awake = body != null && body.IsAwake();
                if (!fullSnapshot && body != null && !awake) continue;
                byte[] state;
                var stateChanged = SerializeBodyStateBuffered(pair.Key, body, fullSnapshot, awake, out state);
                if (fullSnapshot || stateChanged)
                {
                    changedStates.Add(state);
                    if (stateChanged) lastChangedBodyAt[pair.Key] = Time.unscaledTime;
                    if (body != null && IsInteractivePropBody(body)) changedPropCount++;
                    else changedOtherBodyCount++;
                }
            }
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSerializeBodies, bodySerializeStarted);
            writer.Write((ushort)changedStates.Count);
            foreach (var state in changedStates) writer.Write(state);
            var environmentSerializeStarted = MultiplayerPerformance.StartPhase();
            var environment = SerializeEnvironment();
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSerializeEnvironment, environmentSerializeStarted);
            var includeEnvironment = fullSnapshot || !BytesEqual(lastSerializedEnvironment, environment);
            writer.Write(includeEnvironment);
            if (includeEnvironment) writer.Write(environment);
            lastSerializedEnvironment = environment;
            var packet = stream.ToArray();
            if (!fullSnapshot && WorldSnapshotEquals(lastSerializedWorld, packet)) return null;
            lastSerializedWorld = packet;
            lastSentPropCount = changedPropCount;
            lastSentOtherCount = changedOtherBodyCount + (includeEnvironment ? buttons.Count + fires.Count : 0);
            sentPacketsWindow++;
            sentStatesWindow += changedStates.Count + (includeEnvironment ? buttons.Count + fires.Count : 0);
            return packet;
        }
    }

    private byte[] SerializeEnvironment()
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(MultiplayerSession.SnapshotEpoch);
            BinaryWriterRaw.WriteSingle(writer, Physics2D.gravity.x); BinaryWriterRaw.WriteSingle(writer, Physics2D.gravity.y);
            writer.Write((ushort)buttons.Count);
            foreach (var pair in buttons)
            {
                writer.Write(WireId(pair.Key)); writer.Write(pair.Value != null);
                uint activations; buttonActivations.TryGetValue(pair.Key, out activations); writer.Write(activations);
            }
            CaptureDestroyedGlass();
            writer.Write((ushort)Math.Min(ushort.MaxValue, destroyedGlass.Count));
            var writtenGlass = 0;
            foreach (var id in destroyedGlass)
            {
                if (writtenGlass++ >= ushort.MaxValue) break;
                writer.Write(WireId(id));
            }
            CaptureDestroyedLamps();
            writer.Write((ushort)Math.Min(ushort.MaxValue, destroyedLamps.Count));
            var writtenLamps = 0;
            foreach (var id in destroyedLamps)
            {
                if (writtenLamps++ >= ushort.MaxValue) break;
                writer.Write(WireId(id));
            }
            var fireCount = 0;
            foreach (var pair in fires) if (pair.Value != null && fireCount < ushort.MaxValue) fireCount++;
            writer.Write((ushort)fireCount);
            var writtenFires = 0;
            foreach (var pair in fires)
            {
                var fire = pair.Value;
                if (fire == null || writtenFires >= fireCount) continue;
                writer.Write(WireId(pair.Key)); BinaryWriterRaw.WriteSingle(writer, fire.transform.position.x);
                BinaryWriterRaw.WriteSingle(writer, fire.transform.position.y);
                BinaryWriterRaw.WriteSingle(writer, fire.transform.eulerAngles.z);
                BinaryWriterRaw.WriteSingle(writer, fire.fuel); writer.Write(fire.canIgnite);
                BinaryWriterRaw.WriteSingle(writer, fire.damageMult);
                BinaryWriterRaw.WriteSingle(writer, fire.fuelConsMult); writtenFires++;
            }
            CaptureDestroyedDrones();
            writer.Write((ushort)Math.Min(ushort.MaxValue, destroyedDrones.Count));
            var writtenDrones = 0;
            foreach (var id in destroyedDrones)
            {
                if (writtenDrones++ >= ushort.MaxValue) break;
                writer.Write(WireId(id));
            }
            var manager = GameManager.main;
            BinaryWriterRaw.WriteSingle(writer, manager == null ? 0f : manager.rainIntensity);
            BinaryWriterRaw.WriteSingle(writer, manager == null ? 0f : manager.snowIntensity);
            BinaryWriterRaw.WriteSingle(writer, manager == null ? 0f : manager.fogIntensity);
            var mission = MissionManager.main;
            writer.Write(mission == null ? -1 : mission.killAmount);
            writer.Write(mission == null ? -1 : mission.totalEnemyCount);
            return stream.ToArray();
        }
    }

    internal void DrawReplicationDebugOverlay()
    {
        foreach (var pair in bodies)
        {
            var body = pair.Value;
            if (body == null) continue;
            float changedAt;
            MultiplayerHud.DrawReplicationMarker(body.worldCenterOfMass,
                lastChangedBodyAt.TryGetValue(pair.Key, out changedAt) &&
                Time.unscaledTime - changedAt <= 1f);
        }
    }

    private bool SerializeBodyStateBuffered(string id, Rigidbody2D body, bool copyUnchanged, bool awake, out byte[] state)
    {
        BodyStateScratch scratch;
        if (!bodyStateScratch.TryGetValue(id, out scratch))
        {
            scratch = new BodyStateScratch(WireId(id));
            bodyStateScratch[id] = scratch;
        }
        var stream = scratch.Stream;
        var writer = scratch.Writer;
        stream.Position = 0;
        stream.SetLength(0);
        writer.Write(scratch.WireId);
        var destroyed = body == null;
        writer.Write(destroyed);
        if (!destroyed)
        {
            DroppedWeapon dropped;
            droppedWeapons.TryGetValue(body, out dropped);
            var layout = BodyLayoutFor(body);
            var crate = layout.Crate;
            writer.Write(dropped != null); writer.Write(crate != null);
            if (crate != null)
                writer.Write(networkCrateDebrisBodies.Contains(body) ? "" : layout.CratePrefabName);
            BinaryWriterRaw.WriteSingle(writer, body.position.x); BinaryWriterRaw.WriteSingle(writer, body.position.y);
            BinaryWriterRaw.WriteSingle(writer, body.rotation);
            BinaryWriterRaw.WriteSingle(writer, body.velocity.x); BinaryWriterRaw.WriteSingle(writer, body.velocity.y);
            BinaryWriterRaw.WriteSingle(writer, body.angularVelocity);
            BinaryWriterRaw.WriteSingle(writer, body.gravityScale); writer.Write((int)body.constraints);
            writer.Write((byte)body.bodyType); writer.Write(body.simulated); writer.Write(awake);
            var safetyRailing = layout.SafetyRailing;
            writer.Write(safetyRailing);
            writer.Write(safetyRailing && IsSafetyRailingAttached(layout));
            var vehiclePart = layout.VehiclePart;
            writer.Write(vehiclePart != null);
            if (vehiclePart != null)
            {
                var vehicle = vehiclePart.vehicle ?? layout.Vehicle;
                var joint = layout.VehicleJoint;
                BinaryWriterRaw.WriteSingle(writer, vehiclePart.health);
                BinaryWriterRaw.WriteSingle(writer, vehicle == null ? 0f : vehicle.health);
                writer.Write(vehicle != null && vehicle.engineDisabled);
                writer.Write(joint != null && joint.enabled);
            }
            if (dropped != null)
            {
                writer.Write(NetworkWireId.FromString(dropped.stats == null ? "" : dropped.stats.name));
                writer.Write(dropped.ammoAmount);
            }
        }
        writer.Flush();
        byte[] previous;
        var changed = !lastSerializedBodyStates.TryGetValue(id, out previous) || !StreamEquals(stream, previous);
        if (changed)
        {
            state = stream.ToArray();
            lastSerializedBodyStates[id] = state;
        }
        else state = copyUnchanged ? previous : null;
        return changed;
    }

    private static bool StreamEquals(MemoryStream stream, byte[] previous)
    {
        if (previous == null || stream.Length != previous.Length) return false;
        var buffer = stream.GetBuffer();
        for (var index = 0; index < previous.Length; index++) if (buffer[index] != previous[index]) return false;
        return true;
    }

    private ulong WireId(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0UL;
        ulong wire;
        if (wireIds.TryGetValue(id, out wire)) return wire;
        wire = NetworkWireId.FromString(id);
        wireIds[id] = wire;
        idsByWire[wire] = id;
        return wire;
    }

    private string ResolveWireId(ulong wire)
    {
        var started = MultiplayerPerformance.StartPhase();
        try
        {
            if (wire == 0UL) return "";
            string id;
            if (idsByWire.TryGetValue(wire, out id)) return id;
            id = FindKnownWireId(wire);
            if (id == null) id = "net/" + wire.ToString("X16");
            wireIds[id] = wire;
            idsByWire[wire] = id;
            return id;
        }
        finally
        {
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotWireResolve, started);
        }
    }

    private string FindKnownWireId(ulong wire)
    {
        foreach (var id in bodies.Keys) if (NetworkWireId.FromString(id) == wire) return id;
        foreach (var id in buttons.Keys) if (NetworkWireId.FromString(id) == wire) return id;
        foreach (var id in fires.Keys) if (NetworkWireId.FromString(id) == wire) return id;
        foreach (var id in replicatedDoors.Keys) if (NetworkWireId.FromString(id) == wire) return id;
        foreach (var id in proximityDoors.Keys) if (NetworkWireId.FromString(id) == wire) return id;
        foreach (var id in activationZones.Keys) if (NetworkWireId.FromString(id) == wire) return id;
        foreach (var id in glasses.Keys) if (NetworkWireId.FromString(id) == wire) return id;
        foreach (var id in drones.Keys) if (NetworkWireId.FromString(id) == wire) return id;
        return null;
    }

    private static bool TryReadWorldSnapshotSequence(byte[] data, out int sequence)
    {
        sequence = 0;
        if (data == null || data.Length < sizeof(int) * 2) return false;
        sequence = BitConverter.ToInt32(data, sizeof(int));
        return true;
    }

    private static bool IsNewerWorldSnapshotSequence(int sequence, int previous)
    {
        return unchecked(sequence - previous) > 0;
    }

    private static bool WorldSnapshotEquals(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        for (var index = sizeof(int) * 2; index < left.Length; index++)
            if (left[index] != right[index]) return false;
        return true;
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left == right) return true;
        if (left == null || right == null || left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
        return true;
    }

    private void SampleActivity()
    {
        if (Time.unscaledTime < nextActivitySample) return;
        nextActivitySample = Time.unscaledTime + 1f;
        sentPacketsPerSecond = sentPacketsWindow;
        sentStatesPerSecond = sentStatesWindow;
        receivedPacketsPerSecond = receivedPacketsWindow;
        receivedStatesPerSecond = receivedStatesWindow;
        sentPacketsWindow = sentStatesWindow = receivedPacketsWindow = receivedStatesWindow = 0;
    }

    private void ReadSnapshot(byte[] data)
    {
        try
        {
            var reader = new SnapshotReader(data);
            var sceneEpoch = reader.ReadInt32();
            if (!MultiplayerSession.IsSnapshotEpochCurrent(sceneEpoch)) return;
            var sequence = reader.ReadInt32();
            if (hasReceivedWorldSnapshotSequence && !IsNewerWorldSnapshotSequence(sequence, lastReceivedWorldSnapshotSequence)) return;
            lastReceivedWorldSnapshotSequence = sequence;
            hasReceivedWorldSnapshotSequence = true;
            var count = reader.ReadUInt16();
            receivedPacketsWindow++;
            receivedStatesWindow += count;
            var parseStarted = MultiplayerPerformance.StartPhase();
            for (var index = 0; index < count; index++)
            {
                var id = ResolveWireId(reader.ReadUInt64());
                var decodeStarted = MultiplayerPerformance.StartPhase();
                var destroyed = reader.ReadBoolean();
                Rigidbody2D body;
                if (destroyed)
                {
                    MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotDecode, decodeStarted);
                    var dispatchStarted = MultiplayerPerformance.StartPhase();
                    try
                    {
                        clientDestroyedBodyIds.Add(id);
                        float pendingUntil;
                        if (pendingDestroyedWeaponPickups.TryGetValue(id, out pendingUntil) &&
                            Time.unscaledTime >= pendingUntil)
                            pendingDestroyedWeaponPickups.Remove(id);
                        if (bodies.TryGetValue(id, out body) && body != null)
                        {
                            if (IsGameplayOwned(body))
                            {
                                bodies.Remove(id);
                                ids.Remove(body);
                                continue;
                            }

                            var crate = body.GetComponentInParent<CrateScript>();
                            if (crate != null && crate.objOnDestroy != null)
                            {
                                var debris = Instantiate(crate.objOnDestroy, crate.transform.position,
                                    crate.transform.rotation);
                                if (crate.breakType == CrateScript.BreakType.None)
                                    RegisterCrateDebrisBodies(id, debris.GetComponentsInChildren<Rigidbody2D>(true),
                                        true);
                            }

                            var objectToRemove = crate != null ? crate.gameObject : body.gameObject;
                            if (clientCreatedBodies.Remove(body))
                            {
                                if (objectToRemove != null) Destroy(objectToRemove);
                            }
                            else if (objectToRemove != null)
                            {
                                HideClientObjectHierarchy(objectToRemove);
                            }

                            ids.Remove(body);
                        }

                        bodies.Remove(id);
                    }
                    finally
                    {
                        MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotDispatch,
                            dispatchStarted);
                    }

                    continue;
                }

                var isDropped = reader.ReadBoolean();
                var isCrate = reader.ReadBoolean();
                var cratePrefabName = isCrate ? reader.ReadString() : "";
                var state = new State
                {
                    position = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    rotation = reader.ReadSingle(),
                    velocity = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    angularVelocity = reader.ReadSingle(),
                    gravityScale = reader.ReadSingle(),
                    constraints = (RigidbodyConstraints2D)reader.ReadInt32(),
                    bodyType = (RigidbodyType2D)reader.ReadByte(),
                    simulated = reader.ReadBoolean(),
                    awake = reader.ReadBoolean(),
                    safetyRailing = reader.ReadBoolean(),
                    safetyRailingAttached = reader.ReadBoolean(),
                    vehiclePart = reader.ReadBoolean()
                };
                if (state.vehiclePart)
                {
                    state.vehiclePartHealth = reader.ReadSingle();
                    state.vehicleHealth = reader.ReadSingle();
                    state.vehicleEngineDisabled = reader.ReadBoolean();
                    state.vehicleJointAttached = reader.ReadBoolean();
                }

                MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotDecode, decodeStarted);
                var stateDispatchStarted = MultiplayerPerformance.StartPhase();
                try
                {
                    if (clientDestroyedBodyIds.Contains(id)) continue;
                    if (isDropped)
                    {
                        var weaponId = reader.ReadUInt64();
                        var ammo = reader.ReadInt32();
                        float pendingUntil;
                        if (pendingDestroyedWeaponPickups.TryGetValue(id, out pendingUntil))
                        {
                            if (Time.unscaledTime < pendingUntil) continue;
                            pendingDestroyedWeaponPickups.Remove(id);
                        }

                        if (!bodies.TryGetValue(id, out body) || body == null)
                        {
                            body = FindExistingDroppedWeapon(id, weaponId, state.position);
                            if (body == null)
                            {
                                var objectStarted = MultiplayerPerformance.StartPhase();
                                body = CreateDroppedWeapon(id, weaponId, ammo, state.position, state.rotation);
                                MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotObjects,
                                    objectStarted);
                            }
                        }
                        else
                        {
                            DroppedWeapon dropped;
                            droppedWeapons.TryGetValue(body, out dropped);
                            SynchronizeDroppedWeapon(dropped, weaponId, ammo);
                        }
                    }
                    else if (isCrate && (!bodies.TryGetValue(id, out body) || body == null))
                    {
                        var objectStarted = MultiplayerPerformance.StartPhase();
                        body = CreateRuntimeCrate(id, cratePrefabName, state.position, state.rotation);
                        MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotObjects,
                            objectStarted);
                    }

                    if (bodies.TryGetValue(id, out body) && body != null)
                    {
                        received[body] = state;
                    }
                }
                finally
                {
                    MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotDispatch,
                        stateDispatchStarted);
                }
            }

            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotParse, parseStarted);
            if (reader.ReadBoolean())
            {
                var environmentStarted = MultiplayerPerformance.StartPhase();
                ApplyEnvironment(reader.ReadBytes(reader.ReadInt32()));
                MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldEnvironmentApply, environmentStarted);
            }
        }
        catch (EndOfStreamException)
        {
        }
    }

    private void ApplyEnvironment(byte[] data)
    {
        using (var reader = new BinaryReader(new MemoryStream(data)))
        {
            var sceneEpoch = reader.ReadInt32();
            if (!MultiplayerSession.IsSnapshotEpochCurrent(sceneEpoch)) return;
            Physics2D.gravity = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            var buttonCount = reader.ReadUInt16();
            for (var index = 0; index < buttonCount; index++)
                ApplyButtonState(ResolveWireId(reader.ReadUInt64()), reader.ReadBoolean(), reader.ReadUInt32());
            var glassCount = reader.ReadUInt16();
            for (var index = 0; index < glassCount; index++)
                ApplyGlassState(ResolveWireId(reader.ReadUInt64()));
            var lampCount = reader.ReadUInt16();
            for (var index = 0; index < lampCount; index++)
                ApplyLampState(ResolveWireId(reader.ReadUInt64()));
            seenSnapshotFires.Clear();
            var fireCount = reader.ReadUInt16();
            for (var index = 0; index < fireCount; index++)
            {
                var id = ResolveWireId(reader.ReadUInt64());
                var position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                var rotation = reader.ReadSingle(); var fuel = reader.ReadSingle(); var canIgnite = reader.ReadBoolean();
                var damageMult = reader.ReadSingle(); var fuelConsMult = reader.ReadSingle();
                seenSnapshotFires.Add(id); ApplyFireState(id, position, rotation, fuel, canIgnite, damageMult, fuelConsMult);
            }
            RemoveMissingFires(seenSnapshotFires);
            var droneCount = reader.ReadUInt16();
            for (var index = 0; index < droneCount; index++)
                ApplyDroneState(ResolveWireId(reader.ReadUInt64()));
            var rain = reader.ReadSingle();
            var snow = reader.ReadSingle();
            var fog = reader.ReadSingle();
            ApplyWeather(rain, snow, fog);
            if (reader.BaseStream.Length - reader.BaseStream.Position >= sizeof(int) * 2)
                ApplyMissionEnemyCount(reader.ReadInt32(), reader.ReadInt32());
        }
    }

    private static void ApplyMissionEnemyCount(int killed, int total)
    {
        if (killed < 0 || total < 0) return;
        var mission = MissionManager.main;
        if (mission == null) return;
        mission.killAmount = killed;
        mission.totalEnemyCount = total;
        if (mission.killsText != null)
            mission.killsText.text = "Enemies: " + Mathf.Max(0, total - killed) + "/" + total;
    }

    private static void ApplyWeather(float rain, float snow, float fog)
    {
        var manager = GameManager.main;
        if (manager == null) return;
        if (!Mathf.Approximately(manager.rainIntensity, rain))
        {
            manager.rainIntensity = rain;
            manager.UpdateRain();
        }
        if (!Mathf.Approximately(manager.snowIntensity, snow))
        {
            manager.snowIntensity = snow;
            manager.UpdateSnow();
        }
        if (!Mathf.Approximately(manager.fogIntensity, fog))
        {
            manager.fogIntensity = fog;
            manager.UpdateFog();
        }
    }

    private void ApplyAuthoritativeState(Rigidbody2D body, State state)
    {
        if (!MultiplayerSession.IsHost && IsDoorBody(body)) return;
        if (state.safetyRailing && !state.safetyRailingAttached)
            DetachSafetyRailing(body);
        ApplyVehicleState(body, state);
        if (!MultiplayerSession.IsHost && !state.vehiclePart && !MultiplayerLoadDistance.IsWorldNearLocalPlayer(body))
        {
            body.simulated = false;
            return;
        }

        var mechanism = IsMechanismBody(body) && !IsInteractivePropBody(body);
        float controlUntil;
        if (!mechanism && locallyControlledUntil.TryGetValue(body, out controlUntil))
        {
            if (Time.unscaledTime < controlUntil)
            {
                body.simulated = true;
                body.bodyType = RigidbodyType2D.Dynamic;
                body.WakeUp();
                return;
            }
            locallyControlledUntil.Remove(body);
        }
        body.gravityScale = state.gravityScale;
        body.constraints = state.constraints;
        body.simulated = state.simulated;
        body.bodyType = mechanism && state.simulated ? RigidbodyType2D.Kinematic : state.bodyType;
        if (!state.simulated) return;

        if (mechanism && state.vehiclePart)
        {
            if (!initializedBodies.Contains(body) ||
                (state.position - body.position).sqrMagnitude > 256f)
            {
                initializedBodies.Add(body);
                vehiclePaths.Remove(body);
                body.position = state.position;
                body.rotation = state.rotation;
                body.velocity = state.velocity;
                body.angularVelocity = state.angularVelocity;
                return;
            }

            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            List<VehiclePathState> path;
            if (!vehiclePaths.TryGetValue(body, out path))
                vehiclePaths[body] = path = new List<VehiclePathState>(12);
            var now = Time.unscaledTime;
            path.Add(new VehiclePathState
            {
                position = state.position,
                rotation = state.rotation,
                velocity = state.velocity,
                angularVelocity = state.angularVelocity,
                arrivedAt = now
            });
            while (path.Count > 2 && path[0].arrivedAt < now - 0.3f) path.RemoveAt(0);
            if (path.Count < 2)
            {
                body.velocity = state.velocity;
                body.angularVelocity = state.angularVelocity;
            }
            return;
        }

        if (mechanism) return;

        if (state.bodyType != RigidbodyType2D.Dynamic || !initializedBodies.Contains(body))
        {
            body.position = state.position;
            body.rotation = state.rotation;
            body.velocity = state.velocity;
            body.angularVelocity = state.angularVelocity;
            initializedBodies.Add(body);
        }
        else
        {
            body.position = state.position;
            body.rotation = state.rotation;
            body.velocity = state.velocity;
            body.angularVelocity = state.angularVelocity;
        }
        if (state.awake) body.WakeUp();
        else if (state.bodyType != RigidbodyType2D.Dynamic) body.Sleep();
    }

    private void TickVehiclePaths()
    {
        if (vehiclePaths.Count == 0) return;
        var renderTime = Time.unscaledTime - 0.1f;
        staleVehiclePaths.Clear();
        foreach (var pair in vehiclePaths)
        {
            var body = pair.Key;
            if (body == null) { staleVehiclePaths.Add(body); continue; }
            var path = pair.Value;
            if (path.Count < 2 || !body.simulated ||
                body.bodyType != RigidbodyType2D.Kinematic) continue;
            var segment = path.Count - 2;
            while (segment > 0 && path[segment].arrivedAt > renderTime) segment--;
            var from = path[segment];
            var to = path[segment + 1];
            var span = Mathf.Max(0.001f, to.arrivedAt - from.arrivedAt);
            var alpha = Mathf.Clamp01((renderTime - from.arrivedAt) / span);
            var targetPosition = Vector2.Lerp(from.position, to.position, alpha);
            var targetRotation = from.rotation +
                Mathf.DeltaAngle(from.rotation, to.rotation) * alpha;
            const float correctionGain = 5f;
            var correction = (targetPosition - body.position) * correctionGain;
            if (correction.sqrMagnitude > 25f) correction = correction.normalized * 5f;
            body.velocity = Vector2.Lerp(body.velocity, to.velocity + correction, 0.35f);
            var angularCorrection = Mathf.DeltaAngle(body.rotation, targetRotation) * correctionGain;
            body.angularVelocity = Mathf.Lerp(body.angularVelocity,
                to.angularVelocity + angularCorrection, 0.35f);
        }
        foreach (var body in staleVehiclePaths) vehiclePaths.Remove(body);
    }

    private static void ApplyVehicleState(Rigidbody2D body, State state)
    {
        if (!state.vehiclePart || body == null) return;
        var part = body.GetComponent<VehiclePart>();
        if (part == null) return;
        part.health = state.vehiclePartHealth;
        var vehicle = part.vehicle ?? part.GetComponentInParent<VehicleBase>();
        if (vehicle != null)
        {
            vehicle.health = state.vehicleHealth;
            vehicle.engineDisabled = state.vehicleEngineDisabled;
        }
        var joint = body.GetComponent<Joint2D>();
        if (joint != null) joint.enabled = state.vehicleJointAttached;
    }

    internal void QueuePush(LimbScript limb, Collision2D collision)
    {
        if (MultiplayerSession.IsHost || limb == null || limb.body == null || !limb.body.isPlayer || collision == null) return;
        var localPlayer = PlayerScript.player;
        if (localPlayer == null || limb.body != localPlayer.bodyScript) return;
        var body = collision.rigidbody ?? collision.gameObject.GetComponentInParent<Rigidbody2D>();
        if (!(IsInteractivePropBody(body) || droneBodies.Contains(body)) || limb.rb == null) return;
        QueueContactBodyState(body, Time.unscaledTime);
        var crate = body.GetComponentInParent<CrateScript>();
        if (crate != null && collision.relativeVelocity.magnitude >= crate.minDamageSpeed)
            QueueDamage(crate, collision.relativeVelocity.magnitude * crate.impactDamageMult);
    }

    private void QueueBodyState(Rigidbody2D body)
    {
        locallyControlledUntil[body] = Time.unscaledTime + ClientAuthorityGrace;
        clientFastSerializeState = ClientAuthorityGrace;
        body.simulated = true;
        body.bodyType = RigidbodyType2D.Dynamic;
        body.WakeUp();
        var id = Id(body);
        pushes[id] = CaptureBodyState(body);
    }

    private void QueueContactBodyState(Rigidbody2D body, float now)
    {
        float nextAt;
        if (nextContactStateAt.TryGetValue(body, out nextAt) && now < nextAt) return;
        nextContactStateAt[body] = now + ContactStateInterval;
        QueueBodyState(body);
    }


    private void MaintainMovingLocalAuthorities()
    {
        if (locallyControlledUntil.Count == 0) return;
        var now = Time.unscaledTime;
        var renew = new List<Rigidbody2D>();
        foreach (var pair in locallyControlledUntil)
        {
            var body = pair.Key;
            if (body == null || pair.Value >= now ||
                !(IsInteractivePropBody(body) || droneBodies.Contains(body))) continue;
            if (body.velocity.sqrMagnitude > 0.0004f || Mathf.Abs(body.angularVelocity) > 1f)
                renew.Add(body);
        }
        foreach (var body in renew)
            locallyControlledUntil[body] = now + ClientAuthorityGrace;
        clientFastSerializeState = ClientAuthorityGrace;
    }

    private static ClientBodyState CaptureBodyState(Rigidbody2D body)
    {
        return new ClientBodyState
        {
            position = body.position,
            rotation = body.rotation,
            velocity = body.velocity,
            angularVelocity = body.angularVelocity
        };
    }

    internal void QueueDamage(CrateScript crate, float amount)
    {
        if (MultiplayerSession.IsHost || crate == null || amount <= 0f) return;
        var body = crate.GetComponent<Rigidbody2D>();
        if (body == null) return;
        var id = Id(body);
        float allowedAt;
        if (nextDamage.TryGetValue(id, out allowedAt) && Time.unscaledTime < allowedAt) return;
        nextDamage[id] = Time.unscaledTime + 0.10f;
        damage[id] = Mathf.Min(100f, amount);
        clientFastSerializeState = Mathf.Max(clientFastSerializeState, 0.01f);
    }

    internal void QueueLevitated(Rigidbody2D body)
    {
        if (MultiplayerSession.IsHost || body == null ||
            !(IsInteractivePropBody(body) || droneBodies.Contains(body))) return;
        QueueBodyState(body);
    }

    internal void QueueWeaponInteraction(DroppedWeapon dropped, BodyScript body, byte operation)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost || dropped == null || body == null ||
            PlayerScript.player == null || body != PlayerScript.player.bodyScript) return;
        var rigidbody = dropped.GetComponent<Rigidbody2D>();
        if (rigidbody == null) rigidbody = dropped.GetComponentInChildren<Rigidbody2D>(true);
        if (rigidbody == null || !IsWorldBody(rigidbody)) return;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            var id = Id(rigidbody);
            writer.Write(operation);
            writer.Write(WireId(id));
            var slot = dropped.stats == null ? -1 : dropped.stats.slot;
            var oldWeapon = slot >= 0 && slot < body.weapons.Count ? body.weapons[slot] : null;
            var oldAmmo = slot >= 0 && slot < body.weaponAmmos.Count ? body.weaponAmmos[slot] : 0;
            if (operation == WeaponPickup && oldWeapon == null)
                pendingDestroyedWeaponPickups[id] = Time.unscaledTime + 1.5f;
            writer.Write(slot);
            writer.Write(NetworkWireId.FromString(oldWeapon == null ? "" : oldWeapon.name));
            writer.Write(oldAmmo);
            writer.Write(dropped.stats != null && body.weapons.Contains(dropped.stats));
            writer.Write(body.transform.position.x);
            writer.Write(body.transform.position.y);
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
    }

    internal static bool QueueLocalWeaponDrop(BodyScript body)
    {
        var current = Instance;
        var player = PlayerScript.player;
        if (current == null || !MultiplayerSession.IsConnected || MultiplayerSession.IsHost || body == null || player == null ||
            body != player.bodyScript || !body.isAlive || body.unarmed || body.weapons == null ||
            body.weaponAmmos == null) return false;

        var slot = body.currentWeapon;
        if (slot < 0 || slot >= body.weapons.Count || slot >= body.weaponAmmos.Count ||
            body.weapons[slot] == null) return false;

        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(WeaponDrop);
            writer.Write(0UL);
            writer.Write(slot);
            writer.Write(NetworkWireId.FromString(body.weapons[slot].name));
            writer.Write(body.weaponAmmos[slot]);
            writer.Write(false);
            writer.Write(body.transform.position.x);
            writer.Write(body.transform.position.y);
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
        return true;
    }

    internal void QueueButtonActivation(ButtonScript button)
    {
        if (MultiplayerSession.IsHost || button == null) return;
        var id = ButtonId(button);
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(ButtonActivate);
            writer.Write(WireId(id));
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
    }

    internal void QueueDoorActivation(QDoorOpen opener)
    {
        if (MultiplayerSession.IsHost || opener == null) return;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(DoorActivate);
            writer.Write(WireId(ProximityDoorId(opener)));
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
    }

    //TODO
    internal void QueueZoneActivation(ActivateZoneScript zone, bool manual = false)
    {
        if (MultiplayerSession.IsHost || zone == null) return;
        var id = ActivationZoneId(zone);
        if (!manual) localZonePrompts.Add(id);
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(ZoneActivate);
            writer.Write(WireId(id));
            writer.Write(manual);
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
    }

    internal void NotifyButtonActivated(ButtonScript button)
    {
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || button == null) return;
        var id = ButtonId(button);
        uint count;
        buttonActivations.TryGetValue(id, out count);
        buttonActivations[id] = count + 1;
    }

    internal void QueueGlassDamage(GlassScript glass, float damage, Vector3 bulletPosition)
    {
        if (MultiplayerSession.IsHost || glass == null || damage <= 0f) return;
        var id = GlassId(glass);
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(GlassDamage);
            writer.Write(WireId(id));
            writer.Write(damage);
            writer.Write(bulletPosition.x); writer.Write(bulletPosition.y); writer.Write(bulletPosition.z);
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
    }

    internal void QueueVehicleDamage(VehiclePart part, float amount, bool collision)
    {
        if (MultiplayerSession.IsHost || part == null || part.rb == null || amount <= 0f) return;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(VehicleDamage);
            writer.Write(WireId(Id(part.rb)));
            writer.Write(Mathf.Min(100f, amount));
            writer.Write(collision);
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
    }

    private WorldInputPacket SerializePushes()
    {
        var now = Time.unscaledTime;
        foreach (var pair in locallyControlledUntil)
        {
            var body = pair.Key;
            if (body == null || pair.Value < now) continue;
            pushes[Id(body)] = CaptureBodyState(body);
        }

        var states = new WorldInputState[pushes.Count];
        var index = 0;
        foreach (var pair in pushes)
        {
            var state = pair.Value;
            states[index++] = new WorldInputState(WireId(pair.Key), state.position.x, state.position.y,
                state.rotation, state.velocity.x, state.velocity.y, state.angularVelocity);
        }

        pushes.Clear();
        return new WorldInputPacket(states);
    }

    private void ApplyPushes(ushort peerId, WorldInputPacket packet)
    {
        var writer = new PacketWriter(2 + packet.States.Length * 36);
        packet.Write(ref writer);
        ApplyPushes(peerId, writer.ToArray());
    }

    private WorldDamagePacket SerializeDamage()
    {
        var entries = new WorldDamageEntry[damage.Count];
        var index = 0;
        foreach (var pair in damage)
        {
            entries[index++] = new WorldDamageEntry(WireId(pair.Key), pair.Value);
        }
        damage.Clear();
        return new WorldDamagePacket(entries);
    }

    private void ApplyPushes(ushort peerId, byte[] data)
    {
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                var count = reader.ReadUInt16();
                for (var index = 0; index < count; index++)
                {
                    var id = ResolveWireId(reader.ReadUInt64());
                    var predicted = new ClientBodyState
                    {
                        position = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                        rotation = reader.ReadSingle(),
                        velocity = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                        angularVelocity = reader.ReadSingle()
                    };
                    Rigidbody2D body;
                    if (!bodies.TryGetValue(id, out body) || body == null)
                    {
                        continue;
                    }
                    if (!IsInteractivePropBody(body) && !droneBodies.Contains(body))
                    {
                        continue;
                    }
                    PropAuthority authority;
                    if (propAuthorities.TryGetValue(id, out authority) &&
                        authority.expiresAt >= Time.unscaledTime && authority.peerId != peerId)
                    {
                        continue;
                    }
                    if ((body.position - predicted.position).sqrMagnitude > 9f)
                    {
                        continue;
                    }
                    propAuthorities[id] = new PropAuthority
                    {
                        peerId = peerId,
                        expiresAt = Time.unscaledTime + ClientAuthorityGrace
                    };
                    body.simulated = true;
                    body.bodyType = RigidbodyType2D.Dynamic;
                    body.position = predicted.position;
                    body.rotation = predicted.rotation;
                    body.velocity = predicted.velocity;
                    body.angularVelocity = predicted.angularVelocity;
                    body.WakeUp();
                }
            }
        }
        catch (EndOfStreamException) { }
    }

    private void ApplyDamage(WorldDamagePacket packet)
    {
        foreach (var entry in packet.Entries)
        {
            var id = ResolveWireId(entry.TargetId);
            var amount = Mathf.Clamp(entry.Amount, 0f, 100f);
            Rigidbody2D body;
            if (!bodies.TryGetValue(id, out body) || body == null) continue;
            var crate = body.GetComponentInParent<CrateScript>();
            if (crate != null && crate.enabled) crate.Damage(amount);
        }
    }

    private void ApplyWeaponInteraction(ushort peerId, byte[] data)
    {
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                var operation = reader.ReadByte();
                var id = ResolveWireId(reader.ReadUInt64());
                if (operation == ButtonActivate)
                {
                    ApplyButtonActivation(id, peerId);
                    return;
                }
                if (operation == DoorActivate)
                {
                    ApplyDoorActivation(id, peerId);
                    return;
                }
                if (operation == ZoneActivate)
                {
                    ApplyZoneActivation(id, peerId, reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean());
                    return;
                }
                if (operation == GlassDamage)
                {
                    ApplyGlassDamage(id, peerId, reader.ReadSingle(),
                        new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
                    return;
                }
                if (operation == VehicleDamage)
                {
                    ApplyVehicleDamage(id, peerId, reader.ReadSingle(), reader.ReadBoolean());
                    return;
                }
                if (operation == DroneDamage)
                {
                    ApplyDroneDamage(id, reader.ReadSingle());
                    return;
                }
                var slot = reader.ReadInt32();
                var oldWeaponId = reader.ReadUInt64();
                var oldAmmo = reader.ReadInt32();
                var clientOwnsWeapon = reader.ReadBoolean();
                var requestedPosition = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                Rigidbody2D rigidbody;
                var remoteBody = NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
                if (operation == WeaponDrop)
                {
                    var requestedWeapon = FindWeaponPreset(oldWeaponId);
                    if (remoteBody == null || !remoteBody.isAlive || requestedWeapon == null || slot < 0 ||
                        slot >= remoteBody.weapons.Count || slot >= remoteBody.weaponAmmos.Count ||
                        (requestedPosition - (Vector2)remoteBody.transform.position).sqrMagnitude > 25f)
                    {
                        return;
                    }
                    if (remoteBody.weapons[slot] != null &&
                        NetworkWireId.FromString(remoteBody.weapons[slot].name) != oldWeaponId)
                    {
                        return;
                    }
                    remoteBody.weapons[slot] = requestedWeapon;
                    remoteBody.weaponAmmos[slot] = Mathf.Max(0, oldAmmo);
                    remoteBody.ChangeWeapon(slot);
                    if (remoteBody.weapon != null) remoteBody.weapon.ammo = remoteBody.weaponAmmos[slot];
                    remoteBody.DropWeaponSingle();
                    return;
                }
                if ((operation != WeaponPickup && operation != WeaponAmmoGet) || remoteBody == null ||
                    !remoteBody.isAlive || !bodies.TryGetValue(id, out rigidbody) || rigidbody == null)
                {
                    return;
                }
                var dropped = rigidbody.GetComponentInParent<DroppedWeapon>();
                if (dropped == null || (requestedPosition - (Vector2)dropped.transform.position).sqrMagnitude > 25f)
                {
                    return;
                }
                if (operation == WeaponPickup && (slot < 0 || slot >= remoteBody.weapons.Count ||
                    slot >= remoteBody.weaponAmmos.Count || dropped.stats == null))
                {
                    return;
                }
                if (operation == WeaponAmmoGet && clientOwnsWeapon && dropped.stats != null &&
                    slot >= 0 && slot < remoteBody.weapons.Count)
                {
                    remoteBody.weapons[slot] = dropped.stats;
                }
                var wasPlayer = remoteBody.isPlayer;
                remoteBody.isPlayer = false;
                try
                {
                    if (operation == WeaponPickup)
                    {
                        var pickedWeapon = dropped.stats;
                        var previousWeapon = slot >= 0 && slot < remoteBody.weapons.Count
                            ? remoteBody.weapons[slot] : null;
                        remoteBody.weapons[slot] = FindWeaponPreset(oldWeaponId);
                        remoteBody.weaponAmmos[slot] = Mathf.Max(0, oldAmmo);
                        ReplaceDroppedWeaponWithPrevious(dropped, remoteBody, pickedWeapon);
                        remoteBody.weapons[slot] = pickedWeapon;
                        remoteBody.weaponAmmos[slot] = 0;
                        remoteBody.ChangeWeapon(slot);
                        if (previousWeapon == null)
                            UnityEngine.Object.Destroy(dropped.gameObject);
                    }
                    else if (clientOwnsWeapon) dropped.AmmoGet(remoteBody);
                    else UnloadDroppedWeapon(dropped);
                }
                finally { remoteBody.isPlayer = wasPlayer; }
            }
        }
        catch (EndOfStreamException) { }
    }

    private void ApplyVehicleDamage(string id, ushort peerId, float amount, bool collision)
    {
        Rigidbody2D body;
        var remoteBody = NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
        if (remoteBody == null || !remoteBody.isAlive || !bodies.TryGetValue(id, out body) || body == null)
            return;
        var part = body.GetComponent<VehiclePart>();
        if (part == null || (remoteBody.transform.position - part.transform.position).sqrMagnitude > 36f)
            return;
        amount = Mathf.Clamp(amount, 0f, 100f);
        if (collision) part.health -= amount;
        part.Damage(amount);
    }


    private void RefreshButtons()
    {
        foreach (var button in FindObjectsOfType<ButtonScript>())
        {
            if (button == null || buttonIds.ContainsKey(button)) continue;
            var id = ButtonId(button);
            buttonIds[button] = id;
            buttons[id] = button;
            if (!buttonActivations.ContainsKey(id)) buttonActivations[id] = 0;
        }
    }

    private void RefreshProximityDoors()
    {
        foreach (var opener in FindObjectsOfType<QDoorOpen>())
        {
            if (opener == null || proximityDoorIds.ContainsKey(opener)) continue;
            var id = ProximityDoorId(opener);
            proximityDoorIds[opener] = id;
            proximityDoors[id] = opener;
        }
    }

    private void RefreshReplicatedDoors()
    {
        foreach (var door in FindObjectsOfType<DoorScript>())
        {
            if (door == null || IsGameplayOwned(door)) continue;
            if (!replicatedDoorIds.TryGetValue(door, out var id))
            {
                id = ComponentId(door);
                replicatedDoorIds[door] = id;
                replicatedDoors[id] = door;
            }
            if (MultiplayerSession.IsHost)
            {
                if (hostDoorTargets.ContainsKey(id)) continue;
                hostDoorTargets[id] = door.followingFirstPoint;
                hostDoorMoving[id] = IsDoorMoving(door);
                hostDoorRevisions[id] = 1;
            }
            else
            {
                var source = door.GetComponent<AudioSource>();
                if (source != null) source.Stop();
            }
        }
    }

    private void ProcessDoorStatePackets()
    {
        ushort peerId;
        DoorStatePacket packet;
        while (MultiplayerSession.TryTakeDoorState(out peerId, out packet))
        {
            if (!MultiplayerSession.IsSnapshotEpochCurrent(packet.SceneEpoch)) continue;
            if (MultiplayerSession.IsHost)
            {
                if (packet.Message == DoorStateMessage.RequestSnapshot) SendDoorStateSnapshot(peerId);
                continue;
            }
            if (packet.Message != DoorStateMessage.States) continue;
            foreach (var state in packet.States) ApplyDoorState(state, packet.IncludesPositions);
        }
    }

    private void BroadcastChangedDoorStates()
    {
        var changed = new List<DoorStateEntry>();
        foreach (var pair in replicatedDoors)
        {
            var door = pair.Value;
            if (door == null) continue;
            bool previous;
            var moving = IsDoorMoving(door);
            bool wasMoving;
            hostDoorMoving.TryGetValue(pair.Key, out wasMoving);
            if (hostDoorTargets.TryGetValue(pair.Key, out previous) && previous == door.followingFirstPoint &&
                wasMoving == moving) continue;
            hostDoorTargets[pair.Key] = door.followingFirstPoint;
            hostDoorMoving[pair.Key] = moving;
            uint revision;
            hostDoorRevisions.TryGetValue(pair.Key, out revision);
            revision++;
            hostDoorRevisions[pair.Key] = revision;
            var target = DoorTarget(door);
            changed.Add(new DoorStateEntry(WireId(pair.Key), revision, door.followingFirstPoint, moving,
                target.x, target.y));
        }
        if (changed.Count > 0)
            MultiplayerSession.Send(DoorStatePacket.StatesUpdate(MultiplayerSession.SnapshotEpoch, false, changed.ToArray()));
    }

    private void SendDoorStateSnapshot(ushort peerId)
    {
        var states = new List<DoorStateEntry>(replicatedDoors.Count);
        foreach (var pair in replicatedDoors)
        {
            var door = pair.Value;
            if (door == null) continue;
            uint revision;
            if (!hostDoorRevisions.TryGetValue(pair.Key, out revision)) revision = 1;
            var position = door.transform.position;
            var target = DoorTarget(door);
            states.Add(new DoorStateEntry(WireId(pair.Key), revision, door.followingFirstPoint, IsDoorMoving(door),
                target.x, target.y, position.x, position.y, door.transform.eulerAngles.z));
        }
        MultiplayerSession.Send(DoorStatePacket.StatesUpdate(MultiplayerSession.SnapshotEpoch, true, states.ToArray()), peerId);
    }

    private void ApplyDoorState(DoorStateEntry state, bool includesPosition)
    {
        var id = ResolveWireId(state.Id);
        uint previousRevision;
        if (clientDoorRevisions.TryGetValue(id, out previousRevision) && state.Revision < previousRevision) return;
        DoorScript door;
        if (!replicatedDoors.TryGetValue(id, out door) || door == null) return;
        clientDoorRevisions[id] = state.Revision;
        clientDoorTargets[id] = state.FollowingFirstPoint;
        bool wasMoving;
        clientDoorMoving.TryGetValue(id, out wasMoving);
        clientDoorMoving[id] = state.IsMoving;
        clientDoorTargetPositions[id] = new Vector2(state.TargetX, state.TargetY);
        door.followingFirstPoint = state.FollowingFirstPoint;
        var source = door.GetComponent<AudioSource>();
        if (includesPosition && source != null)
        {
            if (state.IsMoving && source.clip != null) source.Play();
            else source.Stop();
        }
        if (!includesPosition && wasMoving != state.IsMoving)
        {
            if (state.IsMoving) door.StartMoving();
            else
            {
                if (source != null) source.Stop();
                if (door.endSound != null) Sound.Play(door.endSound, door.transform.position, false, false, door.transform);
            }
        }
        if (!includesPosition) return;
        var body = door.GetComponent<Rigidbody2D>();
        if (body == null) return;
        body.position = new Vector2(state.PositionX, state.PositionY);
        body.rotation = state.Rotation;
        body.velocity = Vector2.zero;
        body.angularVelocity = 0f;
    }

    private void AnimateClientDoors()
    {
        if (MultiplayerSession.IsHost) return;
        foreach (var pair in clientDoorTargets)
        {
            DoorScript door;
            if (!replicatedDoors.TryGetValue(pair.Key, out door) || door == null) continue;
            var body = door.GetComponent<Rigidbody2D>();
            Vector2 target;
            if (!clientDoorTargetPositions.TryGetValue(pair.Key, out target) || body == null || door.speed <= 0f) continue;
            var next = Vector2.MoveTowards(body.position, target, door.speed * Time.fixedDeltaTime);
            body.MovePosition(next);
            if ((next - target).sqrMagnitude <= 0.0001f)
            {
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                var source = door.GetComponent<AudioSource>();
                if (source != null) source.Stop();
            }
        }
    }

    private static bool IsDoorMoving(DoorScript door)
    {
        if (door == null) return false;
        var target = DoorTarget(door);
        return Vector2.Distance(door.transform.position, target) >= door.speed * 0.05f;
    }

    private static Vector2 DoorTarget(DoorScript door)
    {
        var target = door != null && door.followingFirstPoint ? door.point1 : door == null ? null : door.point2;
        return target == null ? Vector2.zero : target.position;
    }

    private void RefreshActivationZones()
    {
        foreach (var zone in FindObjectsOfType<ActivateZoneScript>())
        {
            if (zone == null || activationZoneIds.ContainsKey(zone)) continue;
            var id = ActivationZoneId(zone);
            activationZoneIds[zone] = id;
            activationZones[id] = zone;
        }
    }

    private void DiscoverWorldFires()
    {
        if (MultiplayerSession.IsHost) fires.Clear();
        foreach (var fire in FindObjectsOfType<FireScript>())
            RegisterWorldFireInternal(fire);
    }

    private void RegisterWorldFireInternal(FireScript fire)
    {
        if (fire == null || IsGameplayOwned(fire)) return;
        string id;
        if (!fireIds.TryGetValue(fire, out id))
        {
            id = ComponentId(fire);
            fireIds[fire] = id;
        }
        fires[id] = fire;
    }

    internal static void RegisterRuntimeWorldFire(FireScript fire)
    {
        if (fire == null || !MultiplayerSession.IsHost) return;
        var instance = Instance;
        if (instance == null || !instance.discoveredScene || instance.fireIds.ContainsKey(fire)) return;
        instance.pendingRuntimeFires[fire] = Time.frameCount;
    }

    private void ProcessPendingRuntimeFires()
    {
        if (!MultiplayerSession.IsHost || pendingRuntimeFires.Count == 0) return;
        var ready = new List<FireScript>();
        foreach (var pair in pendingRuntimeFires)
        {
            var fire = pair.Key;
            if (Time.frameCount <= pair.Value) continue;
            ready.Add(fire);
            if (fire == null || IsGameplayOwned(fire) || fireIds.ContainsKey(fire)) continue;
            var id = "runtime-fire/" + (++nextRuntimeFireId).ToString();
            fireIds[fire] = id;
            fires[id] = fire;
        }
        foreach (var fire in ready) pendingRuntimeFires.Remove(fire);
    }

    private void RefreshKnownWorldFires()
    {
        if (MultiplayerSession.IsHost) return;
        foreach (var pair in fires)
        {
            var fire = pair.Value;
            if (fire == null) continue;
            if (!clientFireSettings.ContainsKey(fire)) clientFireSettings[fire] = new FireLocalSettings
            {
                enabled = fire.enabled,
                active = fire.gameObject.activeSelf
            };

            fire.enabled = ShouldTickClientFire(fire);
        }
    }

    internal static bool ShouldTickClientFire(FireScript fire)
    {
        if (fire == null) return false;
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;

        if (fire.GetComponentInParent<BodyScript>() != null) return true;

        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        if (body == null) return false;

        const float activationDistanceSqr = 9f;
        foreach (var limb in body.GetComponentsInChildren<LimbScript>(true))
        {
            if (limb != null && ((Vector2)limb.transform.position -
                (Vector2)fire.transform.position).sqrMagnitude <= activationDistanceSqr)
                return true;
        }
        return body.rb != null && (body.rb.position - (Vector2)fire.transform.position)
            .sqrMagnitude <= activationDistanceSqr;
    }

    private void RefreshGlasses()
    {
        foreach (var glass in FindObjectsOfType<GlassScript>())
        {
            if (glass == null || glassIds.ContainsKey(glass)) continue;
            var id = GlassId(glass);
            glassIds[glass] = id;
            glasses[id] = glass;
        }
        RefreshLamps();
    }

    internal void RegisterLevelLoaderWorldObjects()
    {
        if (!MultiplayerSession.IsConnected) return;
        RefreshGlasses();
        nextSnapshot = 0f;
    }

    private void RefreshLamps()
    {
        foreach (var collider in FindObjectsOfType<Collider2D>())
        {
            if (collider == null || lampIds.ContainsKey(collider)) continue;
            var light = collider.GetComponentInParent<UnityEngine.Experimental.Rendering.Universal.Light2D>();
            if (light == null) continue;
            if (!collider.CompareTag("Lamp") &&
                !collider.gameObject.name.StartsWith("Lamp (") &&
                !light.CompareTag("Lamp") &&
                !light.gameObject.name.StartsWith("Lamp (")) continue;
            var id = ComponentId(collider);
            lampIds[collider] = id;
            lamps[id] = new LampState { Object = light.gameObject, Light = light, Collider = collider };
        }
    }

    private void CaptureDestroyedLamps()
    {
        foreach (var pair in lamps)
            if (LampIsDestroyed(pair.Value)) destroyedLamps.Add(pair.Key);
    }

    internal void CaptureDestroyedLampIds(ISet<string> ids)
    {
        if (ids == null) return;
        foreach (var pair in lamps)
            if (LampIsDestroyed(pair.Value)) ids.Add(pair.Key);
    }

    internal void CollectNewDestroyedLampIds(ISet<string> before, List<string> result)
    {
        if (before == null || result == null) return;
        foreach (var pair in lamps)
            if (LampIsDestroyed(pair.Value) && !before.Contains(pair.Key)) result.Add(pair.Key);
    }

    private static bool LampIsDestroyed(LampState lamp)
    {
        return lamp == null || lamp.Object == null || !lamp.Object.activeSelf ||
            lamp.Light == null || !lamp.Light.enabled || lamp.Collider == null || !lamp.Collider.enabled;
    }

    internal void ApplyRemoteDestroyedLamps(IList<string> ids)
    {
        if (ids == null) return;
        foreach (var id in ids)
            if (!string.IsNullOrEmpty(id)) ApplyLampState(id);
    }

    private void ApplyLampState(string id)
    {
        LampState lamp;
        if (!lamps.TryGetValue(id, out lamp))
        {
            RefreshLamps();
            if (!lamps.TryGetValue(id, out lamp)) return;
        }
        BreakLamp(id, lamp, lamp.Object == null ? Vector2.zero : lamp.Object.transform.position);
    }

    private void BreakLamp(string id, LampState lamp, Vector2 hitPoint)
    {
        if (lamp == null) return;
        var lampObject = lamp.Object;
        if (lampObject != null)
        {
            var position = (Vector2)lampObject.transform.position;
            Destroy(lampObject);
            Sound.Play(Resources.Load<AudioClip>("Sounds/LightBreak"), hitPoint);
            Instantiate(Resources.Load("Spawnables/LampShards"), hitPoint, Quaternion.identity);
            Destroy(Instantiate(Resources.Load("Spawnables/Shock"), position, Quaternion.identity), 15f);
        }
        destroyedLamps.Add(id);
    }

    private sealed class LampState
    {
        internal GameObject Object;
        internal Behaviour Light;
        internal Collider2D Collider;
    }

    private void RefreshDrones()
    {
        foreach (var drone in FindObjectsOfType<DroneScript>())
        {
            if (drone == null || droneIds.ContainsKey(drone)) continue;
            var body = drone.GetComponent<Rigidbody2D>();
            if (body == null) continue;
            var id = Id(body);
            droneIds[drone] = id;
            drones[id] = drone;
            droneBodies.Add(body);
        }
    }

    private void CaptureDestroyedDrones()
    {
        foreach (var pair in drones)
            if (pair.Value == null) destroyedDrones.Add(pair.Key);
    }

    internal void QueueDroneDamage(DroneScript drone, float amount)
    {
        if (MultiplayerSession.IsHost || drone == null || amount <= 0f) return;
        var body = drone.GetComponent<Rigidbody2D>();
        if (body == null) return;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(DroneDamage);
            writer.Write(WireId(Id(body)));
            writer.Write(Mathf.Min(100f, amount));
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
    }

    private void ApplyDroneDamage(string id, float amount)
    {
        DroneScript drone;
        if (!drones.TryGetValue(id, out drone) || drone == null) return;
        drone.Damage(amount);
    }

    private void ApplyDroneState(string id)
    {
        DroneScript drone;
        if (!drones.TryGetValue(id, out drone) || drone == null) return;
        var renderer = drone.GetComponent<SpriteRenderer>();
        if (renderer != null && drone.deadSprite != null) renderer.sprite = drone.deadSprite;
        if (drone.deactiveOnDeath != null)
            foreach (var child in drone.deactiveOnDeath)
                if (child != null) child.SetActive(false);
        var source = drone.GetComponent<AudioSource>();
        if (source != null) source.Stop();
        if (drone.breakSound != null) Sound.Play(drone.breakSound, drone.transform.position);
        var shock = Resources.Load<GameObject>("Spawnables/Shock");
        if (shock != null) Destroy(Instantiate(shock, drone.transform), 20f);
        drones.Remove(id);
        droneIds.Remove(drone);
        Destroy(drone);
    }

    private string GlassId(GlassScript glass)
    {
        string id;
        if (glassIds.TryGetValue(glass, out id)) return id;
        id = ComponentId(glass);
        glassIds[glass] = id;
        glasses[id] = glass;
        return id;
    }

    private void CaptureDestroyedGlass()
    {
        foreach (var pair in glasses)
            if (IsGlassBroken(pair.Value)) destroyedGlass.Add(pair.Key);
    }

    private static bool IsGlassBroken(GlassScript glass)
    {
        if (glass == null) return true;
        return glass.health <= 0f;
    }

    private void ApplyGlassDamage(string id, ushort peerId, float damage, Vector3 bulletPosition)
    {
        GlassScript glass;
        var remoteBody = NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
        if (!glasses.TryGetValue(id, out glass) || glass == null || remoteBody == null ||
            !remoteBody.isAlive || ((Vector2)remoteBody.transform.position - (Vector2)glass.transform.position).sqrMagnitude > 10000f)
            return;
        glass.Damage(Mathf.Max(0f, damage), bulletPosition);
        if (IsGlassBroken(glass)) destroyedGlass.Add(id);
    }

    private void ApplyGlassState(string id)
    {
        GlassScript glass;
        if (!glasses.TryGetValue(id, out glass) || glass == null)
        {
            RefreshGlasses();
            if (!glasses.TryGetValue(id, out glass) || glass == null) return;
        }
        if (IsGlassBroken(glass)) return;
        MultiplayerGlassDamagePatch.ApplyingNetworkState = true;
        try { glass.Damage(float.MaxValue, glass.transform.position); }
        finally { MultiplayerGlassDamagePatch.ApplyingNetworkState = false; }
    }

    private void ApplyFireState(string id, Vector2 position, float rotation, float fuel,
        bool canIgnite, float damageMult, float fuelConsMult)
    {
        FireScript fire;
        if (!fires.TryGetValue(id, out fire) || fire == null)
        {
            foreach (var candidate in FindObjectsOfType<FireScript>())
            {
                if (candidate == null || candidate.GetComponentInParent<BodyScript>() != null ||
                    fireIds.ContainsKey(candidate) ||
                    ((Vector2)candidate.transform.position - position).sqrMagnitude > 0.25f) continue;
                fire = candidate;
                break;
            }
            if (fire == null)
            {
                var prefab = Resources.Load<GameObject>("Spawnables/FireParticle");
                var created = prefab == null ? null : Instantiate(prefab, position,
                    Quaternion.Euler(0f, 0f, rotation));
                fire = created == null ? null : created.GetComponent<FireScript>();
                if (fire == null)
                {
                    if (created != null) Destroy(created);
                    return;
                }
                clientCreatedFires.Add(fire);
            }
            else if (!clientFireSettings.ContainsKey(fire))
            {
                clientFireSettings[fire] = new FireLocalSettings
                {
                    enabled = fire.enabled,
                    active = fire.gameObject.activeSelf
                };
            }
            fireIds[fire] = id;
            fires[id] = fire;
        }
        fire.gameObject.SetActive(true);
        fire.transform.position = position;
        fire.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
        fire.fuel = fuel;
        fire.canIgnite = canIgnite;
        fire.damageMult = damageMult;
        fire.fuelConsMult = fuelConsMult;
        fire.enabled = ShouldTickClientFire(fire);
        var particles = fire.GetComponent<ParticleSystem>();
        if (particles != null && !particles.isPlaying) particles.Play();
    }

    private void RemoveMissingFires(HashSet<string> seen)
    {
        var missing = new List<string>();
        foreach (var pair in fires)
            if (!seen.Contains(pair.Key)) missing.Add(pair.Key);
        foreach (var id in missing)
        {
            var fire = fires[id];
            fires.Remove(id);
            if (fire == null) continue;
            fireIds.Remove(fire);
            if (clientCreatedFires.Remove(fire)) Destroy(fire.gameObject);
            else fire.gameObject.SetActive(false);
        }
    }

    private string ButtonId(ButtonScript button)
    {
        string id;
        if (buttonIds.TryGetValue(button, out id)) return id;
        id = ComponentId(button);
        buttonIds[button] = id;
        buttons[id] = button;
        return id;
    }

    private string ProximityDoorId(QDoorOpen opener)
    {
        string id;
        if (proximityDoorIds.TryGetValue(opener, out id)) return id;
        id = ComponentId(opener);
        proximityDoorIds[opener] = id;
        proximityDoors[id] = opener;
        return id;
    }

    private void ApplyDoorActivation(string id, ushort peerId)
    {
        QDoorOpen opener;
        var remotePlayer = NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
        float allowedAt;
        if (!proximityDoors.TryGetValue(id, out opener) || opener == null || remotePlayer == null ||
            !remotePlayer.isAlive ||
            ((Vector2)remotePlayer.transform.position - (Vector2)opener.transform.position).sqrMagnitude >= 784f ||
            (nextDoorActivation.TryGetValue(id, out allowedAt) && Time.unscaledTime < allowedAt)) return;
        var door = opener.GetComponent<DoorScript>();
        if (door == null) return;
        nextDoorActivation[id] = Time.unscaledTime + 0.2f;
        Destroy(opener);
        door.Activate(69);
    }

    private string ActivationZoneId(ActivateZoneScript zone)
    {
        string id;
        if (activationZoneIds.TryGetValue(zone, out id)) return id;
        id = ComponentId(zone);
        activationZoneIds[zone] = id;
        activationZones[id] = zone;
        return id;
    }

    internal void ActivateLocalZone(ActivateZoneScript zone, bool manual)
    {
        if (zone == null || !MultiplayerSession.IsHost) return;
        var id = ActivationZoneId(zone);
        if (!manual && activatedZoneIds.Contains(id)) localZonePrompts.Add(id);
        ApplyZoneActivation(id, MultiplayerSession.LocalPeerId, manual);
    }

    private void ApplyZoneActivation(string id, ushort peerId, bool manual)
    {
        ActivateZoneScript zone;
        var localPlayer = PlayerScript.player;
        var remotePlayer = peerId == MultiplayerSession.LocalPeerId
            ? (localPlayer == null ? null : localPlayer.bodyScript)
            : NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
        float allowedAt;
        if (!activationZones.TryGetValue(id, out zone) || zone == null || remotePlayer == null ||
            !remotePlayer.isAlive || (!manual && activatedZoneIds.Contains(id)) ||
            (nextZoneActivation.TryGetValue(id, out allowedAt) && Time.unscaledTime < allowedAt)) return;
        var zoneCollider = zone.GetComponent<Collider2D>();
        if (zoneCollider == null || zoneCollider.bounds.SqrDistance(remotePlayer.transform.position) > 4f) return;
        var hostPlayer = PlayerScript.player;
        var hostBody = hostPlayer == null ? null : hostPlayer.bodyScript;
        if (!string.IsNullOrEmpty(zone.team) && (hostBody == null || zone.team != hostBody.team)) return;
        nextZoneActivation[id] = Time.unscaledTime + 0.2f;
        activatedZoneIds.Add(id);
        foreach (var target in GameObject.FindGameObjectsWithTag("Activateable"))
            target.SendMessage("Activate", zone.id, SendMessageOptions.DontRequireReceiver);
    }

    // hacky fix but i hope it doesnt fuckup the level logic
    private void UpdateZonePrompt()
    {
        promptZone = null;
        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        if (body == null || !body.isAlive) return;
        foreach (var pair in activationZones)
        {
            var zone = pair.Value;
            var collider = zone == null ? null : zone.GetComponent<Collider2D>();
            if (collider == null || !localZonePrompts.Contains(pair.Key) || collider.bounds.SqrDistance(body.transform.position) > 4f) continue;
            promptZone = zone;
            break;
        }
        if (promptZone == null || !Input.GetKeyDown(player.keys["Use"])) return;
        if (MultiplayerSession.IsHost) ActivateLocalZone(promptZone, true);
        else QueueZoneActivation(promptZone, true);
    }

    private void ApplyButtonActivation(string id, ushort peerId)
    {
        ButtonScript button;
        var remotePlayer = NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
        float allowedAt;
        if (!buttons.TryGetValue(id, out button) || button == null || remotePlayer == null ||
            !remotePlayer.isAlive || (remotePlayer.transform.position - button.transform.position).sqrMagnitude > 25f ||
            (nextButtonActivation.TryGetValue(id, out allowedAt) && Time.unscaledTime < allowedAt)) return;
        nextButtonActivation[id] = Time.unscaledTime + 0.15f;
        button.Activated();
        nextSnapshot = 0f; // Sending new world state
    }

    private void ApplyButtonState(string id, bool exists, uint activations)
    {
        ButtonScript button;
        buttons.TryGetValue(id, out button);
        uint previous;
        var hadPrevious = receivedButtonActivations.TryGetValue(id, out previous);
        receivedButtonActivations[id] = activations;
        if (hadPrevious && activations > previous && button != null && button.activateSound != null)
            Sound.Play(button.activateSound, button.transform.position, false, false, null, 1f, 1f);
        if (!exists && button != null) SetButtonInactive(button);
    }

    private static void SetButtonInactive(ButtonScript button)
    {
        if (button.transform.childCount > 0)
        {
            var child = button.transform.GetChild(0);
            var renderer = child.GetComponent<SpriteRenderer>();
            var inactive = Resources.Load<Sprite>("Spawnables/buttonInactive");
            if (renderer != null && inactive != null) renderer.sprite = inactive;
            foreach (var light in child.GetComponents<UnityEngine.Experimental.Rendering.Universal.Light2D>())
                light.color = Color.red;
        }
        Destroy(button);
    }

    private static string CleanCloneName(string name)
    {
        return string.IsNullOrEmpty(name) ? "" : name.Replace("(Clone)", "").Trim();
    }

    private static string ComponentId(Component component)
    {
        var path = new StringBuilder(component.gameObject.scene.name);
        var hierarchy = new List<Transform>();
        for (var current = component.transform; current != null; current = current.parent)
            hierarchy.Add(current);
        for (var index = hierarchy.Count - 1; index >= 0; index--)
        {
            var current = hierarchy[index];
            path.Append('/').Append(current.name).Append('#').Append(SameNameSiblingIndex(current));
        }
        var components = component.GetComponents(component.GetType());
        for (var index = 0; index < components.Length; index++)
            if (components[index] == component) { path.Append(':').Append(component.GetType().Name).Append('#').Append(index); break; }
        return path.ToString();
    }

    private void RegisterCrateDebrisBodies(string crateId, Rigidbody2D[] debrisBodies, bool clientCreated)
    {
        if (string.IsNullOrEmpty(crateId) || debrisBodies == null) return;
        Array.Sort(debrisBodies, CompareCrateDebrisBodies);
        for (var index = 0; index < debrisBodies.Length; index++)
        {
            var body = debrisBodies[index];
            if (body == null) continue;
            var id = crateId + "/debris#" + index;
            bodies[id] = body;
            ids[body] = id;
            var wire = NetworkWireId.FromString(id);
            wireIds[id] = wire;
            idsByWire[wire] = id;
            droppedWeapons[body] = null;
            if (IsInteractivePropBodyUncached(body)) interactivePropBodies.Add(body);
            networkCrateDebrisBodies.Add(body);
            var debrisCrate = body.GetComponentInParent<CrateScript>();
            if (debrisCrate != null)
            {
                networkCrateDebrisDamageUntil[debrisCrate] = Time.unscaledTime + 0.75f;
            }
            if (!clientCreated) continue;
            clientCreatedBodies.Add(body);
            if (debrisCrate != null) debrisCrate.enabled = false;
        }
    }

    internal bool IsNetworkCrateDebris(CrateScript crate)
    {
        if (crate == null) return false;
        foreach (var body in crate.GetComponentsInChildren<Rigidbody2D>(true))
            if (body != null && networkCrateDebrisBodies.Contains(body)) return true;
        return false;
    }

    internal bool TryProtectNetworkCrateDebrisDamage(CrateScript crate, float damageAmount)
    {
        if (crate == null) return false;
        float until;
        if (!networkCrateDebrisDamageUntil.TryGetValue(crate, out until)) return false;
        var now = Time.unscaledTime;
        var protect = now < until;
        if (protect) networkCrateDebrisDamageUntil[crate] = now + 0.75f;
        return protect;
    }

    private static int CompareCrateDebrisBodies(Rigidbody2D left, Rigidbody2D right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        var byName = string.CompareOrdinal(left.name, right.name);
        return byName != 0 ? byName : left.transform.GetSiblingIndex().CompareTo(right.transform.GetSiblingIndex());
    }

    private void HideClientObjectHierarchy(GameObject value)
    {
        if (value == null) return;
        var transforms = value.GetComponentsInChildren<Transform>(true);
        foreach (var transform in transforms)
        {
            if (transform == null || transform.gameObject == null) continue;
            var child = transform.gameObject;
            if (!clientHiddenObjects.ContainsKey(child))
                clientHiddenObjects[child] = child.activeSelf;
        }
        for (var index = transforms.Length - 1; index >= 0; index--)
            if (transforms[index] != null) transforms[index].gameObject.SetActive(false);
    }

    private string Id(Rigidbody2D body)
    {
        string id;
        if (ids.TryGetValue(body, out id)) return id;
        var dropped = body.GetComponentInParent<DroppedWeapon>();
        var crate = body.GetComponentInParent<CrateScript>();
        if ((dropped != null && IsRuntimeDroppedWeapon(dropped)) ||
            (crate != null && crate.GetComponentInParent<RuntimeSpawnedCrate>() != null))
        {
            id = "runtime/" + (++nextRuntimeId);
            ids[body] = id;
            return id;
        }
        var path = new StringBuilder(body.gameObject.scene.name);
        var hierarchy = new List<Transform>();
        for (var current = body.transform; current != null; current = current.parent)
            hierarchy.Add(current);
        for (var index = hierarchy.Count - 1; index >= 0; index--)
        {
            var current = hierarchy[index];
            path.Append('/').Append(current.name).Append('#').Append(SameNameSiblingIndex(current));
        }
        var components = body.GetComponents<Rigidbody2D>();
        for (var index = 0; index < components.Length; index++)
            if (components[index] == body) { path.Append(":rb#").Append(index); break; }
        id = path.ToString();
        ids[body] = id;
        return id;
    }

    private static bool IsRuntimeDroppedWeapon(DroppedWeapon dropped)
    {
        if (dropped == null) return false;
        var root = dropped.transform.root;
        return (root != null && root.name.Contains("(Clone)")) ||
            dropped.gameObject.name.Contains("(Clone)");
    }

    private static int SameNameSiblingIndex(Transform transform)
    {
        var ordinal = 0;
        if (transform.parent != null)
        {
            for (var index = 0; index < transform.GetSiblingIndex(); index++)
                if (transform.parent.GetChild(index).name == transform.name) ordinal++;
            return ordinal;
        }

        foreach (var root in transform.gameObject.scene.GetRootGameObjects())
        {
            if (root.transform == transform) break;
            if (root.name == transform.name) ordinal++;
        }
        return ordinal;
    }

    private Rigidbody2D CreateDroppedWeapon(string id, ulong weaponId, int ammo, Vector2 position, float rotation)
    {
        var prefab = Resources.Load<GameObject>("Spawnables/PickupWeapon");
        if (prefab == null) return null;
        var weapon = FindWeaponPreset(weaponId);
        if (weapon == null) return null;
        var dropped = Instantiate(prefab, position, Quaternion.Euler(0f, 0f, rotation)).GetComponent<DroppedWeapon>();
        if (dropped == null) return null;
        dropped.ChangeWeapon(weapon, ammo);
        var body = dropped.GetComponent<Rigidbody2D>();
        if (body == null) { Destroy(dropped.gameObject); return null; }
        bodies[id] = body;
        ids[body] = id;
        clientCreatedBodies.Add(body);
        clientBoundDroppedWeapons.Add(body);
        droppedWeapons[body] = dropped;
        interactivePropBodies.Add(body);
        MakeClientControlled(body);
        return body;
    }

    private Rigidbody2D FindExistingDroppedWeapon(string id, ulong weaponId, Vector2 position)
    {
        Rigidbody2D best = null;
        var bestDistance = 4f;
        foreach (var pair in droppedWeapons)
        {
            var body = pair.Key;
            var dropped = pair.Value;
            if (body == null || dropped == null || clientCreatedBodies.Contains(body) ||
                clientBoundDroppedWeapons.Contains(body) || dropped.stats == null ||
                NetworkWireId.FromString(dropped.stats.name) != weaponId) continue;
            var distance = (body.position - position).sqrMagnitude;
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = body;
        }
        if (best == null) return null;
        string previousId;
        if (ids.TryGetValue(best, out previousId)) bodies.Remove(previousId);
        bodies[id] = best;
        ids[best] = id;
        var wire = NetworkWireId.FromString(id);
        wireIds[id] = wire;
        idsByWire[wire] = id;
        clientBoundDroppedWeapons.Add(best);
        interactivePropBodies.Add(best);
        return best;
    }

    private Rigidbody2D CreateRuntimeCrate(string id, string prefabName, Vector2 position, float rotation)
    {
        if (string.IsNullOrEmpty(prefabName)) return null;
        var prefab = Resources.Load<GameObject>("Spawnables/" + prefabName) ??
            Resources.Load<GameObject>("Objects/" + prefabName) ??
            Resources.Load<GameObject>(prefabName);
        if (prefab == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null || candidate.scene.IsValid() ||
                    CleanCloneName(candidate.name) != prefabName ||
                    candidate.GetComponentInChildren<CrateScript>(true) == null) continue;
                prefab = candidate;
                break;
            }
        }
        if (prefab == null) return null;
        var created = Instantiate(prefab, position, Quaternion.Euler(0f, 0f, rotation));
        var body = created.GetComponentInChildren<Rigidbody2D>();
        if (body == null)
        {
            Destroy(created);
            return null;
        }
        bodies[id] = body;
        ids[body] = id;
        clientCreatedBodies.Add(body);
        droppedWeapons[body] = null;
        interactivePropBodies.Add(body);
        MakeClientControlled(body);
        return body;
    }

    private static void SynchronizeDroppedWeapon(DroppedWeapon dropped, ulong weaponId, int ammo)
    {
        if (dropped == null) return;
        var weapon = dropped.stats;
        var changed = weapon == null || NetworkWireId.FromString(weapon.name) != weaponId;
        if (changed)
        {
            weapon = FindWeaponPreset(weaponId);
            if (weapon == null) return;
            dropped.ChangeWeapon(weapon, ammo);
        }
        if (dropped.ammoAmount != ammo)
        {
            dropped.ammoAmount = ammo;
            changed = true;
        }
        if (!changed) return;
        SynchronizeDroppedWeaponAmmoIndicator(dropped);
        if (ammo <= 0 && weapon.magExtractedSprite != null)
        {
            var renderer = dropped.GetComponent<SpriteRenderer>();
            if (renderer != null) renderer.sprite = weapon.magExtractedSprite;
        }
    }

    private static void UnloadDroppedWeapon(DroppedWeapon dropped)
    {
        if (dropped == null || dropped.stats == null || dropped.ammoAmount <= 0) return;
        dropped.ammoAmount = 0;
        var renderer = dropped.GetComponent<SpriteRenderer>();
        if (renderer != null && dropped.stats.magExtractedSprite != null)
            renderer.sprite = dropped.stats.magExtractedSprite;
        SynchronizeDroppedWeaponAmmoIndicator(dropped);
        var rigidbody = dropped.GetComponent<Rigidbody2D>();
        if (rigidbody != null)
        {
            rigidbody.AddForce(new Vector2(UnityEngine.Random.Range(-1.5f, 1.5f),
                UnityEngine.Random.Range(-1.5f, 1.5f)), ForceMode2D.Impulse);
            rigidbody.AddTorque(UnityEngine.Random.Range(-1.5f, 1.5f), ForceMode2D.Impulse);
        }
    }

    private static void ReplaceDroppedWeaponWithPrevious(DroppedWeapon dropped, BodyScript body,
        WeaponPreset pickedWeapon)
    {
        if (dropped == null || body == null || pickedWeapon == null) return;
        if (body.currentWeapon == pickedWeapon.slot && body.weapon != null && body.weapon.isReloading)
            body.weapon.CancelReload();
        var previousWeapon = body.weapons[pickedWeapon.slot];
        var previousAmmo = body.weaponAmmos[pickedWeapon.slot];
        dropped.pickupCool = 0.5f;
        dropped.ChangeWeapon(previousWeapon, previousAmmo);
        if (previousWeapon == null) return;
        dropped.ammoAmount = previousAmmo;
        var rigidbody = dropped.GetComponent<Rigidbody2D>();
        if (rigidbody == null) return;
        if (body.currentWeapon == pickedWeapon.slot && body.weapon != null)
        {
            dropped.transform.position = body.weapon.transform.position;
            dropped.transform.rotation = body.weapon.transform.rotation;
            if (body.isRight)
            {
                rigidbody.velocity = body.weapon.transform.right * 6f;
                dropped.transform.localScale = Vector2.one;
            }
            else
            {
                rigidbody.velocity = -body.weapon.transform.right * 6f;
                dropped.transform.localScale = new Vector2(-1f, 1f);
            }
            rigidbody.angularVelocity = UnityEngine.Random.Range(-50f, 50f);
        }
        else if (body.mainTorso != null)
        {
            if (body.isRight)
            {
                dropped.transform.position = body.mainTorso.transform.position - body.mainTorso.transform.right * 0.3f;
                dropped.transform.localScale = Vector2.one;
                dropped.transform.eulerAngles = body.mainTorso.transform.eulerAngles - new Vector3(0f, 0f, 90f);
            }
            else
            {
                dropped.transform.position = body.mainTorso.transform.position + body.mainTorso.transform.right * 0.3f;
                dropped.transform.localScale = new Vector2(-1f, 1f);
                dropped.transform.eulerAngles = body.mainTorso.transform.eulerAngles + new Vector3(0f, 0f, 90f);
            }
        }
    }

    internal static void SynchronizeDroppedWeaponAmmoIndicator(DroppedWeapon dropped)
    {
        if (dropped == null) return;
        var ammoSprite = dropped.ammoSprite;
        if (ammoSprite == null) return;
        var weapon = dropped.stats;
        var player = PlayerScript.player;
        if (weapon != null && player != null && player.ammoImages != null &&
            weapon.ammoType >= 0 && weapon.ammoType < player.ammoImages.Length)
            ammoSprite.sprite = player.ammoImages[weapon.ammoType];
        ammoSprite.transform.position = dropped.transform.position + Vector3.up * 0.6f;
        ammoSprite.transform.rotation = Quaternion.identity;
        ammoSprite.enabled = dropped.ammoAmount > 0 && Mathf.PingPong(Time.time, 0.3f) > 0.15f;
    }

    private static WeaponPreset FindWeaponPreset(ulong weaponId)
    {
        if (weaponId == 0UL) return null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (candidate != null && NetworkWireId.FromString(candidate.name) == weaponId) return candidate;
        return null;
    }

    private sealed class BodyStateScratch : IDisposable
    {
        internal readonly MemoryStream Stream = new(96);
        internal readonly BinaryWriter Writer;

        internal readonly ulong WireId;

        internal BodyStateScratch(ulong wireId)
        {
            WireId = wireId;
            Writer = new BinaryWriter(Stream, Encoding.UTF8, true);
        }

        public void Dispose()
        {
            Writer.Dispose();
            Stream.Dispose();
        }
    }

    private struct State
    {
        public Vector2 position;
        public float rotation;
        public Vector2 velocity;
        public float angularVelocity;
        public float gravityScale;
        public RigidbodyConstraints2D constraints;
        public RigidbodyType2D bodyType;
        public bool simulated;
        public bool awake;
        public bool safetyRailing;
        public bool safetyRailingAttached;
        public bool vehiclePart;
        public float vehiclePartHealth;
        public float vehicleHealth;
        public bool vehicleEngineDisabled;
        public bool vehicleJointAttached;
    }

    private class VehiclePathState
    {
        public Vector2 position;
        public float rotation;
        public Vector2 velocity;
        public float angularVelocity;
        public float arrivedAt;
    }

    private struct SnapshotReader
    {
        private readonly byte[] data;
        private int offset;

        public SnapshotReader(byte[] source)
        {
            data = source;
            offset = 0;
        }

        public byte ReadByte()
        {
            Require(1);
            return data[offset++];
        }

        public bool ReadBoolean()
        {
            return ReadByte() != 0;
        }

        public ushort ReadUInt16()
        {
            Require(2);
            var value = (ushort)(data[offset] | data[offset + 1] << 8);
            offset += 2;
            return value;
        }

        public int ReadInt32()
        {
            Require(4);
            var value = data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 |
                data[offset + 3] << 24;
            offset += 4;
            return value;
        }

        public ulong ReadUInt64()
        {
            Require(8);
            ulong value = data[offset] | (ulong)data[offset + 1] << 8 | (ulong)data[offset + 2] << 16 |
                (ulong)data[offset + 3] << 24 | (ulong)data[offset + 4] << 32 |
                (ulong)data[offset + 5] << 40 | (ulong)data[offset + 6] << 48 | (ulong)data[offset + 7] << 56;
            offset += 8;
            return value;
        }

        public float ReadSingle()
        {
            Require(4);
            var value = BitConverter.ToSingle(data, offset);
            offset += 4;
            return value;
        }

        public string ReadString()
        {
            var length = 0;
            var shift = 0;
            byte value;
            do
            {
                value = ReadByte();
                length |= (value & 0x7F) << shift;
                shift += 7;
                if (shift > 35) throw new FormatException();
            } while ((value & 0x80) != 0);
            Require(length);
            var result = Encoding.UTF8.GetString(data, offset, length);
            offset += length;
            return result;
        }

        public byte[] ReadBytes(int length)
        {
            Require(length);
            var result = new byte[length];
            Buffer.BlockCopy(data, offset, result, 0, length);
            offset += length;
            return result;
        }

        private void Require(int count)
        {
            if (count < 0 || offset > data.Length - count) throw new EndOfStreamException();
        }
    }

    private sealed class BodyLayout
    {
        public CrateScript Crate;
        public string CratePrefabName = "";
        public bool SafetyRailing;
        public Joint2D[] Joints = new Joint2D[0];
        public VehiclePart VehiclePart;
        public VehicleBase Vehicle;
        public Joint2D VehicleJoint;
    }

    private struct LocalSettings
    {
        public RigidbodyType2D bodyType;
        public bool simulated;
        public CrateScript crate;
        public bool crateEnabled;
        public DroppedWeapon droppedWeapon;
        public bool droppedWeaponEnabled;
    }

    private struct ClientBodyState
    {
        public Vector2 position;
        public float rotation;
        public Vector2 velocity;
        public float angularVelocity;
    }

    private struct PropAuthority
    {
        public ushort peerId;
        public float expiresAt;
    }

    private struct FireLocalSettings
    {
        public bool enabled;
        public bool active;
    }
}

internal sealed class RuntimeSpawnedCrate : MonoBehaviour { }
