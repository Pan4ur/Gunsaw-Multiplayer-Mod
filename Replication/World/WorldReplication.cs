using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class WorldReplication : MonoBehaviour
{
    internal static WorldReplication Instance;
    
    internal enum WorldInteraction : byte
    {
        WeaponPickup = 1,
        WeaponAmmoGet = 2,
        ButtonActivate = 3,
        DoorActivate = 4,
        ZoneActivate = 5,
        GlassDamage = 6,
        VehicleDamage = 7,
        DroneDamage = 8,
        WeaponDrop = 9
    }

    // Ill just leave it like this for now (It's becoming painful to drive the karts)
    private const float SnapshotInterval = 1f / 50f;

    private const float FullSnapshotInterval = 3f;
    internal const float ClientAuthorityGrace = 0.35f;
    
    internal readonly Dictionary<Rigidbody2D, DroppedWeapon> droppedWeapons = new ();
    internal readonly Dictionary<Rigidbody2D, BodyLayout> bodyLayouts = new();
    internal readonly Dictionary<string, float> pendingDestroyedWeaponPickups = new();
    private readonly HashSet<string> clientDestroyedBodyIds = new();
    internal readonly Dictionary<Rigidbody2D, float> nextContactStateAt = new();
    private readonly Dictionary<string, float> damage = new();
    private readonly Dictionary<string, float> nextDamage = new();
    private int nextRuntimeId;
    private int worldSnapshotSequence;
    private int lastReceivedWorldSnapshotSequence;
    private bool hasReceivedWorldSnapshotSequence;
    internal readonly HashSet<Rigidbody2D> clientCreatedBodies = [];
    internal readonly HashSet<Rigidbody2D> clientBoundDroppedWeapons = [];
    private readonly HashSet<Rigidbody2D> networkCrateDebrisBodies = [];
    private readonly Dictionary<CrateScript, float> networkCrateDebrisDamageUntil = new();
    private readonly Dictionary<GameObject, bool> clientHiddenObjects = new();
    private readonly Dictionary<MonoBehaviour, bool> clientControllers = new();
    internal readonly HashSet<Rigidbody2D> initializedBodies = [];
    internal readonly Dictionary<string, ButtonScript> buttons = new();
    internal readonly Dictionary<ButtonScript, string> buttonIds = new();
    internal readonly Dictionary<string, uint> buttonActivations = new();
    internal readonly Dictionary<string, uint> receivedButtonActivations = new();
    internal readonly Dictionary<string, float> nextButtonActivation = new();
    internal readonly Dictionary<string, QDoorOpen> proximityDoors = new();
    internal readonly Dictionary<QDoorOpen, string> proximityDoorIds = new();
    internal readonly Dictionary<string, float> nextDoorActivation = new();
    internal readonly Dictionary<string, ActivateZoneScript> activationZones = new();
    internal readonly Dictionary<ActivateZoneScript, string> activationZoneIds = new();
    internal readonly Dictionary<string, float> nextZoneActivation = new();
    internal readonly HashSet<string> activatedZoneIds = [];
    internal readonly HashSet<string> localZonePrompts = [];
    internal ActivateZoneScript promptZone;
    internal bool HasActivationPrompt => promptZone != null && MultiplayerSession.IsConnected;
    internal readonly Dictionary<string, GlassScript> glasses = new();
    internal readonly Dictionary<GlassScript, string> glassIds = new();
    internal readonly HashSet<string> destroyedGlass = [];
    internal readonly Dictionary<string, LampState> lamps = [];
    internal readonly Dictionary<Collider2D, string> lampIds = new();
    internal readonly HashSet<string> destroyedLamps = [];
    internal readonly Dictionary<string, DroneScript> drones = new();
    internal readonly Dictionary<DroneScript, string> droneIds = new();
    internal readonly HashSet<string> destroyedDrones = [];
    internal readonly HashSet<Rigidbody2D> droneBodies = [];
    internal readonly Dictionary<FireScript, string> fireIds = new();
    internal readonly Dictionary<string, FireScript> fires = new();
    internal readonly Dictionary<FireScript, int> pendingRuntimeFires = new();
    internal int nextRuntimeFireId;
    internal readonly Dictionary<FireScript, FireLocalSettings> clientFireSettings = new();
    internal readonly HashSet<FireScript> clientCreatedFires = [];
    internal readonly Dictionary<string, AudioSource> mechanismAudio = new();
    internal readonly Dictionary<AudioSource, string> mechanismAudioIds = new();
    internal readonly Dictionary<AudioSource, bool> clientAudioWasPlaying = new();
    internal readonly Dictionary<AudioSource, DoorScript> doorAudioSources = new();
    internal readonly Dictionary<AudioSource, Rigidbody2D> doorAudioBodies = new();
    internal readonly Dictionary<AudioSource, float> clientDoorAudioStartedAt = new();
    internal readonly List<AudioSource> staleClientDoorAudio = new();
    private readonly HashSet<SawScript> clientSaws = [];
    private byte[] lastSerializedWorld;
    private byte[] lastSerializedEnvironment;
    private byte[] lastReliableEnvironment;
    private readonly Dictionary<string, byte[]> lastSerializedBodyStates = new();
    private readonly Dictionary<string, BodyStateScratch> bodyStateScratch = new();
    private readonly Dictionary<string, float> lastChangedBodyAt = new();
    internal float nextSnapshot;
    private float nextReliableEnvironment;
    private float nextFireRefresh;
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
    internal float clientFastSerializeState = 0f;
    internal Transform localContactRoot;

    internal int TotalPropCount
    {
        get
        {
            var count = 0;
            foreach (var body in bodies.bodies.Values)
                if (body != null && bodies.IsInteractivePropBody(body)) count++;
            return count;
        }
    }

    internal int TotalOtherCount
    {
        get
        {
            var count = buttons.Count + mechanismAudio.Count;
            foreach (var fire in fires.Values) if (fire != null) count++;
            foreach (var body in bodies.bodies.Values)
                if (body != null && !bodies.IsInteractivePropBody(body)) count++;
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
    
    internal WorldBodyReplication bodies;
    internal WorldEnvironmentReplication enviroment;
    internal DroppedWeaponReplication weapons;


    private void Awake()
    {
        Instance = this;
        bodies = new WorldBodyReplication();
        enviroment = new WorldEnvironmentReplication();
        weapons = new DroppedWeaponReplication();
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
        foreach (var body in bodies.bodies.Values)
        {
            LoadDistanceSystem.ApplyWorldBody(body);
            if (!LoadDistanceSystem.IsSimulationCulled(body)) continue;
            if (bodies.IsInteractivePropBody(body)) culledPropCount++;
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
                if (isHost && !discoveredScene) DiscoverScene();
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
            if (!discoveredScene) DiscoverScene();

            if (Time.unscaledTime >= nextFireRefresh)
            {
                nextFireRefresh = Time.unscaledTime + 0.1f;
                var fireRefreshStarted = MultiplayerPerformance.StartPhase();
                enviroment.RefreshKnownWorldFires();
                MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldFireRefresh, fireRefreshStarted);
            }

            enviroment.ProcessPendingRuntimeFires();
            if (isHost)
            {
                var zonePromptStarted = MultiplayerPerformance.StartPhase();
                enviroment.UpdateZonePrompt();
                MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldZonePrompt, zonePromptStarted);
                var inputStarted = MultiplayerPerformance.StartPhase();
                byte[] interaction;
                ushort interactionPeer;
                while (MultiplayerSession.TryTakeWorldInteraction(out interactionPeer, out interaction))
                    ApplyWeaponInteraction(interactionPeer, interaction);
                MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldInput, inputStarted);
                return;
            }

            var clientZonePromptStarted = MultiplayerPerformance.StartPhase();
            enviroment.UpdateZonePrompt();
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
                enviroment.ApplyEnvironment(latestEnvironment);
                MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotRead, readStarted);
            }

            var lodFreezeStarted = MultiplayerPerformance.StartPhase();
            bodies.FreezeFarClientProps();
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldClientLodFreeze, lodFreezeStarted);
            var sawsStarted = MultiplayerPerformance.StartPhase();
            AnimateClientSaws();
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldClientSaws, sawsStarted);
            var weaponIndicatorsStarted = MultiplayerPerformance.StartPhase();
            weapons.AnimateClientDroppedWeaponIndicators();
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldDroppedWeaponIndicators,
                weaponIndicatorsStarted);
            enviroment.StopSettledClientDoorAudio();
        }
        finally
        {
            MultiplayerPerformance.AddWorld(performanceStarted);
        }
    }

    private void DiscoverScene()
    {
        var discoveryStarted = MultiplayerPerformance.StartPhase();
        discoveredScene = true;
        bodies.RefreshWorldBodies();
        enviroment.RefreshButtons();
        enviroment.RefreshProximityDoors();
        enviroment.RefreshActivationZones();
        enviroment.RefreshGlasses();
        enviroment.RefreshDrones();
        enviroment.DiscoverWorldFires();
        RefreshClientSaws();
        RefreshWorldControllers();
        enviroment.RefreshMechanismAudio();
        MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldDiscovery, discoveryStarted);
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
                    {
                        var snapshotReader = new PacketReader(snapshot);
                        MultiplayerSession.Send(WorldSnapshotPacket.Read(ref snapshotReader));
                    }

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
            bodies.CaptureLocalContacts();
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldContacts, contactsStarted);
            var authorityStarted = MultiplayerPerformance.StartPhase();
            bodies.MaintainMovingLocalAuthorities();
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldAuthorityMaintenance, authorityStarted);
            if (bodies.received.Count > 0)
            {
                var applyStarted = MultiplayerPerformance.StartPhase();
                foreach (var pair in bodies.received)
                {
                    var body = pair.Key;
                    if (body == null) continue;
                    bodies.ApplyAuthoritativeState(body, pair.Value);
                }

                bodies.received.Clear();
                MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldStateApply, applyStarted);
            }

            bodies.TickVehiclePaths();
            if (clientFastSerializeState > 0f || Time.unscaledTime >= nextSnapshot)
            {
                var clientSendStarted = MultiplayerPerformance.StartPhase();
                clientFastSerializeState -= Time.fixedDeltaTime;
                nextSnapshot = Time.unscaledTime + SnapshotInterval;
                var pushes = SerializePushes();
                if (pushes.States.Length != 0) MultiplayerSession.Send(pushes, 1);
                var damagePacket = SerializeDamage();
                if (damagePacket.Entries.Length != 0) MultiplayerSession.Send(damagePacket, 1);
                MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldClientSend, clientSendStarted);
            }
        }
        finally
        {
            MultiplayerPerformance.AddWorld(performanceStarted);
        }
    }
    
    internal static bool IsInteractivePropBodyUncached(Rigidbody2D body)
    {
        return body != null && (body.GetComponentInParent<CrateScript>() != null ||
            body.GetComponentInParent<DroppedWeapon>() != null);
    }

    private static bool IsSafetyRailingAttached(BodyLayout layout)
    {
        if (!layout.SafetyRailing) return false;
        foreach (var joint in layout.Joints)
            if (joint != null && joint.enabled) return true;
        return false;
    }

    internal static bool IsGameplayOwned(Component component)
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
        DisableControllers(FindObjectsOfType<RbMoveToObj>());
        foreach (var joint in FindObjectsOfType<CustJoint>())
            if (joint != null && !IsGameplayOwned(joint) &&
                !bodies.IsInteractivePropBody(joint.GetComponentInParent<Rigidbody2D>()))
                DisableController(joint);
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
        foreach (var pair in bodies.localSettings)
        {
            if (pair.Key == null) continue;
            pair.Key.bodyType = pair.Value.bodyType;
            pair.Key.simulated = pair.Value.simulated;
            if (pair.Value.crate != null) pair.Value.crate.enabled = pair.Value.crateEnabled;
            if (pair.Value.droppedWeapon != null) pair.Value.droppedWeapon.enabled = pair.Value.droppedWeaponEnabled;
        }
        bodies. localSettings.Clear();
        foreach (var pair in clientControllers)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        clientControllers.Clear();
        bodies.bodies.Clear();
        droppedWeapons.Clear();
        bodyLayouts.Clear();
        bodies.interactivePropBodies.Clear();
        pendingDestroyedWeaponPickups.Clear();
        clientDestroyedBodyIds.Clear();
        bodies.received.Clear();
        bodies.vehiclePaths.Clear();
        bodies.pushes.Clear();
        bodies.locallyControlledUntil.Clear();
        nextContactStateAt.Clear();
        localContactRoot = null;
        bodies.localContactBodies = new Rigidbody2D[0];
        bodies.propAuthorities.Clear();
        damage.Clear();
        bodies.ids.Clear();
        initializedBodies.Clear();
        buttons.Clear();
        buttonIds.Clear();
        buttonActivations.Clear();
        receivedButtonActivations.Clear();
        nextButtonActivation.Clear();
        proximityDoors.Clear();
        proximityDoorIds.Clear();
        nextDoorActivation.Clear();
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
        foreach (var pair in clientAudioWasPlaying)
        {
            if (pair.Key == null) continue;
            if (pair.Value && pair.Key.clip != null) pair.Key.Play();
            else pair.Key.Stop();
        }
        clientAudioWasPlaying.Clear();
        doorAudioSources.Clear();
        doorAudioBodies.Clear();
        clientDoorAudioStartedAt.Clear();
        staleClientDoorAudio.Clear();
        mechanismAudioIds.Clear();
        mechanismAudio.Clear();
        clientSaws.Clear();
        bodies.wireIds.Clear();
        bodies.idsByWire.Clear();
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
        weapons.nextDroppedWeaponIndicatorUpdate = 0f;
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
            foreach (var pair in bodies.bodies)
            {
                var body = pair.Value;
                if (!fullSnapshot && body != null && !LoadDistanceSystem.IsWorldNearAnyPlayer(body)) continue;
                var awake = body != null && body.IsAwake();
                if (!fullSnapshot && body != null && !awake) continue;
                byte[] state;
                var stateChanged = SerializeBodyStateBuffered(pair.Key, body, fullSnapshot, awake, out state);
                if (fullSnapshot || stateChanged)
                {
                    changedStates.Add(state);
                    if (stateChanged) lastChangedBodyAt[pair.Key] = Time.unscaledTime;
                    if (body != null && bodies.IsInteractivePropBody(body)) changedPropCount++;
                    else changedOtherBodyCount++;
                }
            }
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSerializeBodies, bodySerializeStarted);
            writer.Write((ushort)changedStates.Count);
            foreach (var state in changedStates) writer.Write(state);
            var environmentSerializeStarted = MultiplayerPerformance.StartPhase();
            var environment = enviroment.SerializeEnvironment();
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSerializeEnvironment, environmentSerializeStarted);
            var includeEnvironment = fullSnapshot || !BytesEqual(lastSerializedEnvironment, environment);
            writer.Write(includeEnvironment);
            if (includeEnvironment) writer.Write(environment);
            lastSerializedEnvironment = environment;
            var packet = stream.ToArray();
            if (!fullSnapshot && WorldSnapshotEquals(lastSerializedWorld, packet)) return null;
            lastSerializedWorld = packet;
            lastSentPropCount = changedPropCount;
            lastSentOtherCount = changedOtherBodyCount + (includeEnvironment ? buttons.Count + fires.Count + mechanismAudio.Count : 0);
            sentPacketsWindow++;
            sentStatesWindow += changedStates.Count + (includeEnvironment ? buttons.Count + fires.Count + mechanismAudio.Count : 0);
            return packet;
        }
    }

    internal void DrawReplicationDebugOverlay()
    {
        foreach (var pair in bodies.bodies)
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
            var layout = bodies.BodyLayoutFor(body);
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

    internal ulong WireId(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0UL;
        ulong wire;
        if (bodies.wireIds.TryGetValue(id, out wire)) return wire;
        wire = NetworkWireId.FromString(id);
        bodies.wireIds[id] = wire;
        bodies.idsByWire[wire] = id;
        return wire;
    }

    internal string ResolveWireId(ulong wire)
    {
        var started = MultiplayerPerformance.StartPhase();
        try
        {
            if (wire == 0UL) return "";
            string id;
            if (bodies.idsByWire.TryGetValue(wire, out id)) return id;
            id = FindKnownWireId(wire);
            if (id == null) id = "net/" + wire.ToString("X16");
            bodies.wireIds[id] = wire;
            bodies.idsByWire[wire] = id;
            return id;
        }
        finally
        {
            MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotWireResolve, started);
        }
    }

    private string FindKnownWireId(ulong wire)
    {
        foreach (var id in bodies.bodies.Keys) if (NetworkWireId.FromString(id) == wire) return id;
        foreach (var id in buttons.Keys) if (NetworkWireId.FromString(id) == wire) return id;
        foreach (var id in fires.Keys) if (NetworkWireId.FromString(id) == wire) return id;
        foreach (var id in mechanismAudio.Keys) if (NetworkWireId.FromString(id) == wire) return id;
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
                        if (bodies.bodies.TryGetValue(id, out body) && body != null)
                        {
                            if (IsGameplayOwned(body))
                            {
                                bodies.bodies.Remove(id);
                                bodies.ids.Remove(body);
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

                            bodies.ids.Remove(body);
                        }

                        bodies.bodies.Remove(id);
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

                        if (!bodies.bodies.TryGetValue(id, out body) || body == null)
                        {
                            body = weapons.FindExistingDroppedWeapon(id, weaponId, state.position);
                            if (body == null)
                            {
                                var objectStarted = MultiplayerPerformance.StartPhase();
                                body = weapons.CreateDroppedWeapon(id, weaponId, ammo, state.position, state.rotation);
                                MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotObjects,
                                    objectStarted);
                            }
                        }
                        else
                        {
                            DroppedWeapon dropped;
                            droppedWeapons.TryGetValue(body, out dropped);
                            weapons.SynchronizeDroppedWeapon(dropped, weaponId, ammo);
                        }
                    }
                    else if (isCrate && (!bodies.bodies.TryGetValue(id, out body) || body == null))
                    {
                        var objectStarted = MultiplayerPerformance.StartPhase();
                        body = CreateRuntimeCrate(id, cratePrefabName, state.position, state.rotation);
                        MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldSnapshotObjects,
                            objectStarted);
                    }

                    if (bodies.bodies.TryGetValue(id, out body) && body != null)
                    {
                        bodies.received[body] = state;
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
                enviroment.ApplyEnvironment(reader.ReadBytes(reader.ReadInt32()));
                MultiplayerPerformance.AddPhase(MultiplayerPerformancePhase.WorldEnvironmentApply, environmentStarted);
            }
        }
        catch (EndOfStreamException)
        {
        }
    }

    internal void QueuePush(LimbScript limb, Collision2D collision)
    {
        if (limb == null || limb.rb == null) return;
        QueueBodyPush(limb.body, collision);
    }

    internal void QueueBodyPush(BodyScript pushingBody, Collision2D collision)
    {
        if (MultiplayerSession.IsHost || pushingBody == null || !pushingBody.isPlayer || collision == null) return;
        var localPlayer = PlayerScript.player;
        if (localPlayer == null || pushingBody != localPlayer.bodyScript) return;
        var body = collision.rigidbody ?? collision.gameObject.GetComponentInParent<Rigidbody2D>();
        if (!(bodies.IsInteractivePropBody(body) || droneBodies.Contains(body) || WorldBodyReplication.IsClientAuthorityJointBody(body))) return;
        bodies.QueueContactBodyState(body, Time.unscaledTime);
        var crate = body.GetComponentInParent<CrateScript>();
        if (crate != null && collision.relativeVelocity.magnitude >= crate.minDamageSpeed)
            QueueDamage(crate, collision.relativeVelocity.magnitude * crate.impactDamageMult);
    }

    internal void QueueBodyState(Rigidbody2D body)
    {
        bodies.locallyControlledUntil[body] = Time.unscaledTime + ClientAuthorityGrace;
        clientFastSerializeState = ClientAuthorityGrace;
        body.simulated = true;
        body.bodyType = RigidbodyType2D.Dynamic;
        body.WakeUp();
        var id = Id(body);
        bodies.pushes[id] = WorldBodyReplication.CaptureBodyState(body);
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
            !(bodies.IsInteractivePropBody(body) || droneBodies.Contains(body) ||
              WorldBodyReplication.IsClientAuthorityJointBody(body))) return;
        QueueBodyState(body);
    }
    

    internal void QueueButtonActivation(ButtonScript button)
    {
        if (MultiplayerSession.IsHost || button == null) return;
        var id = ButtonId(button);
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte) WorldInteraction.ButtonActivate);
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
            writer.Write((byte) WorldInteraction.DoorActivate);
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
            writer.Write((byte) WorldInteraction.ZoneActivate);
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
            writer.Write((byte) WorldInteraction.GlassDamage);
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
            writer.Write((byte) WorldInteraction.VehicleDamage);
            writer.Write(WireId(Id(part.rb)));
            writer.Write(Mathf.Min(100f, amount));
            writer.Write(collision);
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
    }

    private WorldInputPacket SerializePushes()
    {
        var now = Time.unscaledTime;
        foreach (var pair in bodies.locallyControlledUntil)
        {
            var body = pair.Key;
            if (body == null || pair.Value < now) continue;
            bodies.pushes[Id(body)] = WorldBodyReplication.CaptureBodyState(body);
        }

        var states = new WorldInputState[bodies.pushes.Count];
        var index = 0;
        foreach (var pair in bodies.pushes)
        {
            var state = pair.Value;
            states[index++] = new WorldInputState(WireId(pair.Key), state.position.x, state.position.y,
                state.rotation, state.velocity.x, state.velocity.y, state.angularVelocity);
        }

        bodies.pushes.Clear();
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
                    if (!bodies.bodies.TryGetValue(id, out body) || body == null)
                    {
                        continue;
                    }
                    if (!bodies.IsInteractivePropBody(body) && !droneBodies.Contains(body) && !WorldBodyReplication.IsClientAuthorityJointBody(body))
                    {
                        continue;
                    }
                    PropAuthority authority;
                    if (bodies.propAuthorities.TryGetValue(id, out authority) &&
                        authority.expiresAt >= Time.unscaledTime && authority.peerId != peerId)
                    {
                        continue;
                    }
                    if ((body.position - predicted.position).sqrMagnitude > 9f)
                    {
                        continue;
                    }
                    bodies.propAuthorities[id] = new PropAuthority
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
            if (!bodies.bodies.TryGetValue(id, out body) || body == null) continue;
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
                var operation = (WorldInteraction) reader.ReadByte();
                var id = ResolveWireId(reader.ReadUInt64());
                if (operation == WorldInteraction.ButtonActivate)
                {
                    enviroment.ApplyButtonActivation(id, peerId);
                    return;
                }
                if (operation == WorldInteraction.DoorActivate)
                {
                    enviroment.ApplyDoorActivation(id, peerId);
                    return;
                }
                if (operation == WorldInteraction.ZoneActivate)
                {
                    enviroment.ApplyZoneActivation(id, peerId, reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean());
                    return;
                }
                if (operation == WorldInteraction.GlassDamage)
                {
                    enviroment.ApplyGlassDamage(id, peerId, reader.ReadSingle(),
                        new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
                    return;
                }
                if (operation == WorldInteraction.VehicleDamage)
                {
                    ApplyVehicleDamage(id, peerId, reader.ReadSingle(), reader.ReadBoolean());
                    return;
                }
                if (operation == WorldInteraction.DroneDamage)
                {
                    enviroment.ApplyDroneDamage(id, reader.ReadSingle());
                    return;
                }
                var slot = reader.ReadInt32();
                var oldWeaponId = reader.ReadUInt64();
                var oldAmmo = reader.ReadInt32();
                var clientOwnsWeapon = reader.ReadBoolean();
                var requestedPosition = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                Rigidbody2D rigidbody;
                var remoteBody = NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
                if (operation == WorldInteraction.WeaponDrop)
                {
                    var requestedWeapon = weapons.FindWeaponPreset(oldWeaponId);
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
                if ((operation != WorldInteraction.WeaponPickup && operation != WorldInteraction.WeaponAmmoGet) || remoteBody == null ||
                    !remoteBody.isAlive || !bodies.bodies.TryGetValue(id, out rigidbody) || rigidbody == null)
                {
                    return;
                }
                var dropped = rigidbody.GetComponentInParent<DroppedWeapon>();
                if (dropped == null || (requestedPosition - (Vector2)dropped.transform.position).sqrMagnitude > 25f)
                {
                    return;
                }
                if (operation == WorldInteraction.WeaponPickup && (slot < 0 || slot >= remoteBody.weapons.Count ||
                                                                   slot >= remoteBody.weaponAmmos.Count || dropped.stats == null))
                {
                    return;
                }
                if (operation == WorldInteraction.WeaponAmmoGet && clientOwnsWeapon && dropped.stats != null &&
                    slot >= 0 && slot < remoteBody.weapons.Count)
                {
                    remoteBody.weapons[slot] = dropped.stats;
                }
                var wasPlayer = remoteBody.isPlayer;
                remoteBody.isPlayer = false;
                try
                {
                    if (operation == WorldInteraction.WeaponPickup)
                    {
                        var pickedWeapon = dropped.stats;
                        var previousWeapon = slot >= 0 && slot < remoteBody.weapons.Count
                            ? remoteBody.weapons[slot] : null;
                        remoteBody.weapons[slot] = weapons.FindWeaponPreset(oldWeaponId);
                        remoteBody.weaponAmmos[slot] = Mathf.Max(0, oldAmmo);
                        weapons.ReplaceDroppedWeaponWithPrevious(dropped, remoteBody, pickedWeapon);
                        remoteBody.weapons[slot] = pickedWeapon;
                        remoteBody.weaponAmmos[slot] = 0;
                        remoteBody.ChangeWeapon(slot);
                        if (previousWeapon == null)
                            Destroy(dropped.gameObject);
                    }
                    else if (clientOwnsWeapon) dropped.AmmoGet(remoteBody);
                    else weapons.UnloadDroppedWeapon(dropped);
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
        if (remoteBody == null || !remoteBody.isAlive || !bodies.bodies.TryGetValue(id, out body) || body == null)
            return;
        var part = body.GetComponent<VehiclePart>();
        if (part == null || (remoteBody.transform.position - part.transform.position).sqrMagnitude > 36f)
            return;
        amount = Mathf.Clamp(amount, 0f, 100f);
        if (collision) part.health -= amount;
        part.Damage(amount);
    }

    internal static void RegisterRuntimeWorldFire(FireScript fire)
    {
        if (fire == null || !MultiplayerSession.IsHost) return;
        var instance = Instance;
        if (instance == null || !instance.discoveredScene || instance.fireIds.ContainsKey(fire)) return;
        instance.pendingRuntimeFires[fire] = Time.frameCount;
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

    internal void RegisterLevelLoaderWorldObjects()
    {
        if (!MultiplayerSession.IsConnected) return;
        enviroment.RefreshGlasses();
        nextSnapshot = 0f;
    }

    internal void QueueDroneDamage(DroneScript drone, float amount)
    {
        if (MultiplayerSession.IsHost || drone == null || amount <= 0f) return;
        var body = drone.GetComponent<Rigidbody2D>();
        if (body == null) return;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte) WorldInteraction.DroneDamage);
            writer.Write(WireId(Id(body)));
            writer.Write(Mathf.Min(100f, amount));
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
    }

    internal string GlassId(GlassScript glass)
    {
        string id;
        if (glassIds.TryGetValue(glass, out id)) return id;
        id = ComponentId(glass);
        glassIds[glass] = id;
        glasses[id] = glass;
        return id;
    }
    
    internal string ButtonId(ButtonScript button)
    {
        string id;
        if (buttonIds.TryGetValue(button, out id)) return id;
        id = ComponentId(button);
        buttonIds[button] = id;
        buttons[id] = button;
        return id;
    }

    internal string ProximityDoorId(QDoorOpen opener)
    {
        string id;
        if (proximityDoorIds.TryGetValue(opener, out id)) return id;
        id = ComponentId(opener);
        proximityDoorIds[opener] = id;
        proximityDoors[id] = opener;
        return id;
    }

    internal string ActivationZoneId(ActivateZoneScript zone)
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
        enviroment.ApplyZoneActivation(id, MultiplayerSession.LocalPeerId, manual);
    }

    internal static string CleanCloneName(string name)
    {
        return string.IsNullOrEmpty(name) ? "" : name.Replace("(Clone)", "").Trim();
    }

    internal string ComponentId(Component component)
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
            bodies.bodies[id] = body;
            bodies.ids[body] = id;
            var wire = NetworkWireId.FromString(id);
            bodies.wireIds[id] = wire;
            bodies.idsByWire[wire] = id;
            droppedWeapons[body] = null;
            if (IsInteractivePropBodyUncached(body)) bodies.interactivePropBodies.Add(body);
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

    internal string Id(Rigidbody2D body)
    {
        string id;
        if (bodies.ids.TryGetValue(body, out id)) return id;
        var dropped = body.GetComponentInParent<DroppedWeapon>();
        var crate = body.GetComponentInParent<CrateScript>();
        if ((dropped != null && weapons.IsRuntimeDroppedWeapon(dropped)) ||
            (crate != null && crate.GetComponentInParent<RuntimeSpawnedCrate>() != null))
        {
            id = "runtime/" + (++nextRuntimeId);
            bodies.ids[body] = id;
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
        bodies.ids[body] = id;
        return id;
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
        bodies.bodies[id] = body;
        bodies.ids[body] = id;
        clientCreatedBodies.Add(body);
        droppedWeapons[body] = null;
        bodies.interactivePropBodies.Add(body);
        bodies.MakeClientControlled(body);
        return body;
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

    internal struct State
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

    internal sealed class BodyLayout
    {
        public CrateScript Crate;
        public string CratePrefabName = "";
        public bool SafetyRailing;
        public Joint2D[] Joints = new Joint2D[0];
        public VehiclePart VehiclePart;
        public VehicleBase Vehicle;
        public Joint2D VehicleJoint;
    }
    
    internal sealed class LampState
    {
        internal GameObject Object;
        internal Behaviour Light;
        internal Collider2D Collider;
    }

    internal struct ClientBodyState
    {
        public Vector2 position;
        public float rotation;
        public Vector2 velocity;
        public float angularVelocity;
    }

    internal struct PropAuthority
    {
        public ushort peerId;
        public float expiresAt;
    }

    internal struct FireLocalSettings
    {
        public bool enabled;
        public bool active;
    }
}

internal sealed class RuntimeSpawnedCrate : MonoBehaviour { }
