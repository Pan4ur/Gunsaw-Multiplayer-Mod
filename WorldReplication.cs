using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class WorldReplication : MonoBehaviour
{
    internal const byte WeaponPickup = 1;
    internal const byte WeaponAmmoGet = 2;
    internal const byte ButtonActivate = 3;
    internal const byte DoorActivate = 4;
    internal const byte ZoneActivate = 5;

    private const float SnapshotInterval = 1f / 50f;
    private const float ClientAuthorityGrace = 0.35f;
    private const float DiscoveryInterval = 0.5f;

    private const bool DiagnosticsEnabled = false;
    private readonly Dictionary<string, Rigidbody2D> bodies = new Dictionary<string, Rigidbody2D>();
    private readonly Dictionary<Rigidbody2D, string> ids = new Dictionary<Rigidbody2D, string>();
    private readonly Dictionary<Rigidbody2D, DroppedWeapon> droppedWeapons =
        new Dictionary<Rigidbody2D, DroppedWeapon>();
    private readonly Dictionary<Rigidbody2D, State> received = new Dictionary<Rigidbody2D, State>();
    private readonly Dictionary<string, ClientBodyState> pushes = new Dictionary<string, ClientBodyState>();
    private readonly Dictionary<Rigidbody2D, float> locallyControlledUntil = new Dictionary<Rigidbody2D, float>();
    private readonly Dictionary<string, PropAuthority> propAuthorities = new Dictionary<string, PropAuthority>();
    private readonly Dictionary<string, PropTrace> propTraces = new Dictionary<string, PropTrace>();
    private readonly ContactPoint2D[] contactBuffer = new ContactPoint2D[32];
    private readonly Dictionary<string, float> damage = new Dictionary<string, float>();
    private readonly Dictionary<string, float> nextDamage = new Dictionary<string, float>();
    private int nextRuntimeId;
    private readonly Dictionary<Rigidbody2D, LocalSettings> localSettings = new Dictionary<Rigidbody2D, LocalSettings>();
    private readonly HashSet<Rigidbody2D> clientCreatedBodies = new HashSet<Rigidbody2D>();
    private readonly Dictionary<GameObject, bool> clientHiddenObjects = new Dictionary<GameObject, bool>();
    private readonly Dictionary<MonoBehaviour, bool> clientControllers = new Dictionary<MonoBehaviour, bool>();
    private readonly HashSet<Rigidbody2D> initializedBodies = new HashSet<Rigidbody2D>();
    private readonly Dictionary<string, ButtonScript> buttons = new Dictionary<string, ButtonScript>();
    private readonly Dictionary<ButtonScript, string> buttonIds = new Dictionary<ButtonScript, string>();
    private readonly Dictionary<string, uint> buttonActivations = new Dictionary<string, uint>();
    private readonly Dictionary<string, uint> receivedButtonActivations = new Dictionary<string, uint>();
    private readonly Dictionary<string, float> nextButtonActivation = new Dictionary<string, float>();
    private readonly Dictionary<string, QDoorOpen> proximityDoors = new Dictionary<string, QDoorOpen>();
    private readonly Dictionary<QDoorOpen, string> proximityDoorIds = new Dictionary<QDoorOpen, string>();
    private readonly Dictionary<string, float> nextDoorActivation = new Dictionary<string, float>();
    private readonly Dictionary<string, ActivateZoneScript> activationZones =
        new Dictionary<string, ActivateZoneScript>();
    private readonly Dictionary<ActivateZoneScript, string> activationZoneIds =
        new Dictionary<ActivateZoneScript, string>();
    private readonly Dictionary<string, float> nextZoneActivation = new Dictionary<string, float>();
    private readonly Dictionary<FireScript, string> fireIds = new Dictionary<FireScript, string>();
    private readonly Dictionary<string, FireScript> fires = new Dictionary<string, FireScript>();
    private readonly Dictionary<FireScript, FireLocalSettings> clientFireSettings = new Dictionary<FireScript, FireLocalSettings>();
    private readonly HashSet<FireScript> clientCreatedFires = new HashSet<FireScript>();
    private readonly Dictionary<string, AudioSource> mechanismAudio = new Dictionary<string, AudioSource>();
    private readonly Dictionary<AudioSource, string> mechanismAudioIds = new Dictionary<AudioSource, string>();
    private readonly Dictionary<AudioSource, bool> clientAudioWasPlaying = new Dictionary<AudioSource, bool>();
    private readonly HashSet<string> seenSnapshotFires = new HashSet<string>();
    private readonly HashSet<string> seenSnapshotAudio = new HashSet<string>();
    private float nextSnapshot;
    private float nextDiscovery;
    private float nextHostLoadRadiusUpdate;
    private float nextDiagnostics;
    private float nextManifest;
    private bool wasConnected;
    private bool wasHost;
    private string activeScene = "";
    private string diagnosticPath = "";
    private int callbackContacts;
    private int scannedContacts;
    private int levitatedStates;
    private int queuedStates;
    private int sentStates;
    private int receivedInputPackets;
    private int appliedInputStates;
    private int missingInputBodies;
    private int rejectedInputStates;
    private int rejectedForeignAuthority;
    private int rejectedInputDistance;
    private int weaponRequestsSent;
    private int weaponRequestsApplied;
    private int weaponRequestsRejected;
    private int receivedSnapshotPackets;
    private int receivedSnapshotStates;
    private int missingSnapshotBodies;
    private string lastQueuedId = "";
    private string lastMissingInputId = "";
    private string lastRejectedInputId = "";
    private string lastMissingSnapshotId = "";

    private void Awake()
    {
        if (!DiagnosticsEnabled) return;
        diagnosticPath = Path.Combine(Paths.BepInExRootPath,
            "world-sync-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".log");
        try { File.WriteAllText(diagnosticPath, "Gunsaw Multiplayer world sync diagnostics\n"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Update()
    {
        if (DiagnosticsEnabled) WriteDiagnostics();
        var scene = SceneManager.GetActiveScene().name;
        var isHost = MultiplayerSession.IsHost;
        var sceneChanged = activeScene != scene;
        var roleChanged = wasConnected && wasHost != isHost;
        if (sceneChanged || roleChanged)
        {
            if (wasConnected) RestoreClientWorld();
            activeScene = scene;
            nextDiscovery = 0f;
            nextSnapshot = 0f;
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
            nextDiscovery = 0f;
            nextSnapshot = 0f;
        }
        wasConnected = true;
        wasHost = isHost;
        if (Time.unscaledTime >= nextDiscovery)
        {
            nextDiscovery = Time.unscaledTime + DiscoveryInterval;
            RefreshWorldBodies();
            RefreshButtons();
            RefreshProximityDoors();
            RefreshActivationZones();
            RefreshWorldFires();
            RefreshWorldControllers();
            RefreshMechanismAudio();
        }
        if (isHost)
        {
            byte[] interaction;
            ushort interactionPeer;
            while (MultiplayerSession.TryTakeWorldInteraction(out interactionPeer, out interaction))
                ApplyWeaponInteraction(interactionPeer, interaction);
            return;
        }

        byte[] snapshot;
        byte[] latestSnapshot = null;
        while (MultiplayerSession.TryTakeWorldSnapshot(out snapshot)) latestSnapshot = snapshot;
        if (latestSnapshot != null) ReadSnapshot(latestSnapshot);
        AnimateClientSaws();
    }

    private void FixedUpdate()
    {
        if (!MultiplayerSession.IsConnected) return;
        if (MultiplayerSession.IsHost)
        {
            ushort inputPeer;
            byte[] input;
            while (MultiplayerSession.TryTakeWorldInput(out inputPeer, out input)) ApplyPushes(inputPeer, input);
            byte[] damageInput;
            while (MultiplayerSession.TryTakeWorldDamage(out damageInput)) ApplyDamage(damageInput);
            if (Time.unscaledTime >= nextSnapshot)
            {
                nextSnapshot = Time.unscaledTime + SnapshotInterval;
                MultiplayerSession.SendWorldSnapshot(SerializeWorld());
            }
            return;
        }
        CaptureLocalContacts();
        MaintainMovingLocalAuthorities();
        if (received.Count > 0)
        {
            foreach (var pair in received)
            {
                var body = pair.Key;
                if (body == null) continue;
                ApplyAuthoritativeState(body, pair.Value);
            }
            received.Clear();
        }
        if (Time.unscaledTime >= nextSnapshot)
        {
            nextSnapshot = Time.unscaledTime + SnapshotInterval;
            MultiplayerSession.SendWorldInput(SerializePushes());
            MultiplayerSession.SendWorldDamage(SerializeDamage());
        }
    }

    private void CaptureLocalContacts()
    {
        var player = PlayerScript.player;
        if (player == null || player.bodyScript == null) return;

        foreach (var pair in bodies)
        {
            var body = pair.Value;
            if (body == null || body.bodyType != RigidbodyType2D.Dynamic || !body.simulated) continue;
            var count = body.GetContacts(contactBuffer);
            for (var index = 0; index < count; index++)
            {
                var contact = contactBuffer[index];
                if (!IsLocalPlayerCollider(contact.collider, player.bodyScript) &&
                    !IsLocalPlayerCollider(contact.otherCollider, player.bodyScript)) continue;
                scannedContacts++;
                QueueBodyState(body);
                break;
            }
        }
    }

    private static bool IsLocalPlayerCollider(Collider2D collider, BodyScript localBody)
    {
        return collider != null && collider.transform.root == localBody.transform.root;
    }

    private void RefreshWorldBodies()
    {
        foreach (var body in FindObjectsOfType<Rigidbody2D>())
        {
            if (!IsWorldBody(body))
            {
                RemoveWorldBody(body);
                continue;
            }
            bodies[Id(body)] = body;
            if (!droppedWeapons.ContainsKey(body))
                droppedWeapons[body] = body.GetComponentInParent<DroppedWeapon>();
            if (MultiplayerSession.IsHost && IsInteractivePropBody(body))
                NetworkAvatarReplication.IgnoreRemotePlayerPropCollisions(body);
            if (!MultiplayerSession.IsHost) MakeClientControlled(body);
        }
    }

    private void RemoveWorldBody(Rigidbody2D body)
    {
        if (body == null) return;
        string id;
        if (ids.TryGetValue(body, out id))
        {
            bodies.Remove(id);
            ids.Remove(body);
            propAuthorities.Remove(id);
            propTraces.Remove(id);
        }
        droppedWeapons.Remove(body);
        received.Remove(body);
        locallyControlledUntil.Remove(body);
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

        if (IsInteractivePropBody(body)) return true;
        return !IsGameplayOwned(body) && IsMechanismBody(body);
    }

    internal void MaintainHostLoadRadius()
    {
        if (!MultiplayerSession.IsHost || Time.unscaledTime < nextHostLoadRadiusUpdate) return;
        nextHostLoadRadiusUpdate = Time.unscaledTime + 0.1f;
        var remotePlayers = NetworkAvatarReplication.RemotePlayers();
        if (remotePlayers.Length == 0) return;
        var loadDistance = Mathf.Max(0f, PlayerPrefs.GetFloat("loadDistance", 1800f));
        foreach (var unloader in Resources.FindObjectsOfTypeAll<ObjectUnloader>())
        {
            if (unloader == null || !unloader.gameObject.scene.isLoaded ||
                IsGameplayOwned(unloader)) continue;
            var position = (Vector2)unloader.transform.position;
            var nearRemotePlayer = false;
            foreach (var remote in remotePlayers)
            {
                if (remote == null || remote.Body == null || !remote.Body.isAlive) continue;
                if (((Vector2)remote.Body.transform.position - position).sqrMagnitude >= loadDistance) continue;
                nearRemotePlayer = true;
                break;
            }
            if (!nearRemotePlayer) continue;
            var body = unloader.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.simulated = true;
            }
            var sprite = unloader.GetComponent<SpriteRenderer>();
            if (sprite != null) sprite.enabled = true;
            var crate = unloader.GetComponent<CrateScript>();
            if (crate != null) crate.enabled = true;
        }

        foreach (var spawner in Resources.FindObjectsOfTypeAll<MiniCrateSpawner>())
        {
            if (spawner == null || !spawner.gameObject.scene.isLoaded ||
                IsGameplayOwned(spawner)) continue;
            var position = (Vector2)spawner.transform.position;
            var nearRemotePlayer = false;
            foreach (var remote in remotePlayers)
            {
                if (remote == null || remote.Body == null || !remote.Body.isAlive) continue;
                if (((Vector2)remote.Body.transform.position - position).sqrMagnitude >= loadDistance) continue;
                nearRemotePlayer = true;
                break;
            }
            if (!nearRemotePlayer) continue;
            if (!spawner.gameObject.activeSelf) spawner.gameObject.SetActive(true);
            spawner.enabled = true;
        }
    }


    private static bool IsInteractivePropBody(Rigidbody2D body)
    {
        return body != null && (body.GetComponentInParent<CrateScript>() != null ||
            body.GetComponentInParent<DroppedWeapon>() != null);
    }

    private static bool IsMechanismBody(Rigidbody2D body)
    {
        return body != null && (body.GetComponentInParent<DoorScript>() != null ||
            body.GetComponentInParent<MovingBelt>() != null ||
            body.GetComponentInParent<RbMoveToObj>() != null ||
            body.GetComponentInParent<SawScript>() != null ||
            body.GetComponentInParent<CustJoint>() != null);
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

    private void RefreshWorldControllers()
    {
        if (MultiplayerSession.IsHost) return;
        RestoreGameplayControllers();
        DisableControllers(FindObjectsOfType<DoorScript>());
        DisableControllers(FindObjectsOfType<MovingBelt>());
        DisableControllers(FindObjectsOfType<RbMoveToObj>());
        foreach (var joint in FindObjectsOfType<CustJoint>())
            if (joint != null && !IsGameplayOwned(joint) &&
                !IsInteractivePropBody(joint.GetComponentInParent<Rigidbody2D>()))
                DisableController(joint);
        DisableControllers(FindObjectsOfType<DelayedTrigger>());
        DisableControllers(FindObjectsOfType<TimedTrigger>());
        DisableControllers(FindObjectsOfType<MiniCrateSpawner>());
    }

    private static void AnimateClientSaws()
    {
        foreach (var saw in FindObjectsOfType<SawScript>())
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

    private void RefreshMechanismAudio()
    {
        if (MultiplayerSession.IsHost) mechanismAudio.Clear();
        CollectMechanismAudio(FindObjectsOfType<DoorScript>());
        CollectMechanismAudio(FindObjectsOfType<MovingBelt>());
        CollectMechanismAudio(FindObjectsOfType<RbMoveToObj>());
        CollectMechanismAudio(FindObjectsOfType<SawScript>());
        CollectMechanismAudio(FindObjectsOfType<CustJoint>());
    }

    private void CollectMechanismAudio<T>(T[] controllers) where T : MonoBehaviour
    {
        foreach (var controller in controllers)
        {
            if (controller == null || IsGameplayOwned(controller)) continue;
            foreach (var source in controller.GetComponentsInChildren<AudioSource>(true))
                RegisterMechanismAudio(source);
            var parentSource = controller.GetComponentInParent<AudioSource>();
            RegisterMechanismAudio(parentSource);
            var body = controller.GetComponentInParent<Rigidbody2D>();
            if (body == null) continue;
            foreach (var source in body.GetComponentsInChildren<AudioSource>(true))
                RegisterMechanismAudio(source);
        }
    }

    private void RegisterMechanismAudio(AudioSource source)
    {
        if (source == null || IsGameplayOwned(source)) return;
        string id;
        if (!mechanismAudioIds.TryGetValue(source, out id))
        {
            id = ComponentId(source);
            mechanismAudioIds[source] = id;
        }
        mechanismAudio[id] = source;
        if (MultiplayerSession.IsHost || clientAudioWasPlaying.ContainsKey(source)) return;
        clientAudioWasPlaying[source] = source.isPlaying;
        source.Stop();
    }

    private void ApplyMechanismAudio(string id, bool playing, bool loop, float volume, float pitch)
    {
        AudioSource source;
        if (!mechanismAudio.TryGetValue(id, out source) || source == null) return;
        source.loop = loop;
        source.volume = Mathf.Clamp01(volume);
        source.pitch = Mathf.Clamp(pitch, -3f, 3f);
        if (playing)
        {
            if (!source.isPlaying && source.clip != null) source.Play();
        }
        else if (source.isPlaying) source.Stop();
    }

    private void StopMissingMechanismAudio(HashSet<string> seen)
    {
        foreach (var pair in mechanismAudio)
        {
            if (pair.Value != null && !seen.Contains(pair.Key) && pair.Value.isPlaying)
                pair.Value.Stop();
        }
    }

    private void RestoreClientWorld()
    {
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
        received.Clear();
        pushes.Clear();
        locallyControlledUntil.Clear();
        propAuthorities.Clear();
        propTraces.Clear();
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
        activationZones.Clear();
        activationZoneIds.Clear();
        nextZoneActivation.Clear();
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
        clientFireSettings.Clear();
        clientCreatedFires.Clear();
        fireIds.Clear();
        fires.Clear();
        foreach (var pair in clientAudioWasPlaying)
        {
            if (pair.Key == null) continue;
            if (pair.Value && pair.Key.clip != null) pair.Key.Play();
            else pair.Key.Stop();
        }
        clientAudioWasPlaying.Clear();
        mechanismAudioIds.Clear();
        mechanismAudio.Clear();
    }

    private byte[] SerializeWorld()
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            var states = new List<KeyValuePair<string, Rigidbody2D>>(bodies);
            writer.Write((ushort)states.Count);
            foreach (var pair in states)
            {
                var body = pair.Value;
                writer.Write(pair.Key);
                var destroyed = body == null;
                writer.Write(destroyed);
                if (destroyed) continue;
                DroppedWeapon dropped;
                droppedWeapons.TryGetValue(body, out dropped);
                var crate = body.GetComponentInParent<CrateScript>();
                writer.Write(dropped != null);
                writer.Write(crate != null);
                if (crate != null) writer.Write(CleanCloneName(crate.transform.root.name));
                writer.Write(body.position.x); writer.Write(body.position.y);
                writer.Write(body.rotation);
                writer.Write(body.velocity.x); writer.Write(body.velocity.y);
                writer.Write(body.angularVelocity);
                writer.Write(body.gravityScale);
                writer.Write((int)body.constraints);
                writer.Write((byte)body.bodyType);
                writer.Write(body.simulated);
                writer.Write(body.IsAwake());
                if (dropped != null)
                {
                    writer.Write(dropped.stats == null ? "" : dropped.stats.name);
                    writer.Write(dropped.ammoAmount);
                }
            }
            writer.Write(Physics2D.gravity.x);
            writer.Write(Physics2D.gravity.y);
            writer.Write((ushort)buttons.Count);
            foreach (var pair in buttons)
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value != null);
                uint activations;
                buttonActivations.TryGetValue(pair.Key, out activations);
                writer.Write(activations);
            }
            var fireCount = 0;
            foreach (var pair in fires)
                if (pair.Value != null && fireCount < ushort.MaxValue) fireCount++;
            writer.Write((ushort)fireCount);
            var writtenFires = 0;
            foreach (var pair in fires)
            {
                var fire = pair.Value;
                if (fire == null || writtenFires >= fireCount) continue;
                writer.Write(pair.Key);
                writer.Write(fire.transform.position.x);
                writer.Write(fire.transform.position.y);
                writer.Write(fire.transform.eulerAngles.z);
                writer.Write(fire.fuel);
                writer.Write(fire.canIgnite);
                writer.Write(fire.damageMult);
                writer.Write(fire.fuelConsMult);
                writtenFires++;
            }
            var audioCount = 0;
            foreach (var pair in mechanismAudio)
                if (pair.Value != null && audioCount < ushort.MaxValue) audioCount++;
            writer.Write((ushort)audioCount);
            var writtenAudio = 0;
            foreach (var pair in mechanismAudio)
            {
                if (pair.Value == null || writtenAudio >= audioCount) continue;
                writer.Write(pair.Key);
                writer.Write(pair.Value.isPlaying);
                writer.Write(pair.Value.loop);
                writer.Write(pair.Value.volume);
                writer.Write(pair.Value.pitch);
                writtenAudio++;
            }
            return stream.ToArray();
        }
    }

    private void ReadSnapshot(byte[] data)
    {
        receivedSnapshotPackets++;
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                var count = reader.ReadUInt16();
                receivedSnapshotStates += count;
                for (var index = 0; index < count; index++)
                {
                    var id = reader.ReadString();
                    var destroyed = reader.ReadBoolean();
                    Rigidbody2D body;
                    if (destroyed)
                    {
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
                                Instantiate(crate.objOnDestroy, crate.transform.position, crate.transform.rotation);
                            if (clientCreatedBodies.Remove(body))
                            {
                                if (body.gameObject != null) Destroy(body.gameObject);
                            }
                            else if (body.gameObject != null)
                            {
                                if (!clientHiddenObjects.ContainsKey(body.gameObject))
                                    clientHiddenObjects[body.gameObject] = body.gameObject.activeSelf;
                                body.gameObject.SetActive(false);
                            }
                            ids.Remove(body);
                        }
                        bodies.Remove(id);
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
                        awake = reader.ReadBoolean()
                    };
                    if (isDropped)
                    {
                        var weaponName = reader.ReadString();
                        var ammo = reader.ReadInt32();
                        if (!bodies.TryGetValue(id, out body) || body == null)
                            body = CreateDroppedWeapon(id, weaponName, ammo, state.position, state.rotation);
                        else
                            SynchronizeDroppedWeapon(body.GetComponentInParent<DroppedWeapon>(), weaponName, ammo);
                    }
                    else if (isCrate && (!bodies.TryGetValue(id, out body) || body == null))
                    {
                        body = CreateRuntimeCrate(id, cratePrefabName, state.position, state.rotation);
                    }
                    if (bodies.TryGetValue(id, out body) && body != null)
                    {
                        received[body] = state;
                    }
                    else
                    {
                        missingSnapshotBodies++;
                        lastMissingSnapshotId = id;
                    }
                }
                Physics2D.gravity = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                var buttonCount = reader.ReadUInt16();
                for (var index = 0; index < buttonCount; index++)
                {
                    var id = reader.ReadString();
                    var exists = reader.ReadBoolean();
                    var activations = reader.ReadUInt32();
                    ApplyButtonState(id, exists, activations);
                }
                seenSnapshotFires.Clear();
                var fireCount = reader.ReadUInt16();
                for (var index = 0; index < fireCount; index++)
                {
                    var id = reader.ReadString();
                    var position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    var rotation = reader.ReadSingle();
                    var fuel = reader.ReadSingle();
                    var canIgnite = reader.ReadBoolean();
                    var damageMult = reader.ReadSingle();
                    var fuelConsMult = reader.ReadSingle();
                    seenSnapshotFires.Add(id);
                    ApplyFireState(id, position, rotation, fuel, canIgnite, damageMult, fuelConsMult);
                }
                RemoveMissingFires(seenSnapshotFires);
                seenSnapshotAudio.Clear();
                var audioCount = reader.ReadUInt16();
                for (var index = 0; index < audioCount; index++)
                {
                    var id = reader.ReadString();
                    var playing = reader.ReadBoolean();
                    var loop = reader.ReadBoolean();
                    var volume = reader.ReadSingle();
                    var pitch = reader.ReadSingle();
                    seenSnapshotAudio.Add(id);
                    ApplyMechanismAudio(id, playing, loop, volume, pitch);
                }
                StopMissingMechanismAudio(seenSnapshotAudio);
            }
        }
        catch (EndOfStreamException) { }
    }

    private void ApplyAuthoritativeState(Rigidbody2D body, State state)
    {
        var mechanism = IsMechanismBody(body) && !IsInteractivePropBody(body);
        float controlUntil;
        if (!mechanism && locallyControlledUntil.TryGetValue(body, out controlUntil))
        {
            if (Time.unscaledTime < controlUntil)
            {
                body.simulated = true;
                body.bodyType = RigidbodyType2D.Dynamic;
                body.WakeUp();
                TraceSnapshot(body, state, "ignored");
                return;
            }
            locallyControlledUntil.Remove(body);
        }
        body.gravityScale = state.gravityScale;
        body.constraints = state.constraints;
        body.simulated = state.simulated;
        body.bodyType = mechanism && state.simulated ? RigidbodyType2D.Kinematic : state.bodyType;
        if (!state.simulated) return;

        if (mechanism)
        {
            if (!initializedBodies.Contains(body) || (state.position - body.position).sqrMagnitude > 256f)
            {
                body.position = state.position;
                body.rotation = state.rotation;
                initializedBodies.Add(body);
            }
            else
            {
                var tickAlpha = Mathf.Clamp01(Time.fixedDeltaTime / SnapshotInterval);
                body.position = Vector2.Lerp(body.position, state.position, tickAlpha);
                body.rotation = Mathf.LerpAngle(body.rotation, state.rotation, tickAlpha);
            }
            body.velocity = state.velocity;
            body.angularVelocity = state.angularVelocity;
            return;
        }

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
            var positionError = state.position - body.position;
            var rotationError = Mathf.DeltaAngle(body.rotation, state.rotation);
            if (positionError.sqrMagnitude > 256f || Mathf.Abs(rotationError) > 135f)
            {
                body.position = state.position;
                body.rotation = state.rotation;
                body.velocity = state.velocity;
                body.angularVelocity = state.angularVelocity;
            }
            else
            {
                const float correction = 0.35f;
                if (positionError.sqrMagnitude > 0.0001f) body.position += positionError * correction;
                if (Mathf.Abs(rotationError) > 0.1f) body.rotation += rotationError * correction;
                body.velocity = Vector2.Lerp(body.velocity, state.velocity, correction);
                body.angularVelocity = Mathf.Lerp(body.angularVelocity, state.angularVelocity, correction);
            }
        }
        if (state.awake) body.WakeUp();
        else if (state.bodyType != RigidbodyType2D.Dynamic) body.Sleep();
        TraceSnapshot(body, state, "applied");
    }

    internal void QueuePush(LimbScript limb, Collision2D collision)
    {
        callbackContacts++;
        if (MultiplayerSession.IsHost || limb == null || limb.body == null || !limb.body.isPlayer || collision == null) return;
        var localPlayer = PlayerScript.player;
        if (localPlayer == null || limb.body != localPlayer.bodyScript) return;
        var body = collision.rigidbody ?? collision.gameObject.GetComponentInParent<Rigidbody2D>();
        if (!IsInteractivePropBody(body) || limb.rb == null) return;
        QueueBodyState(body);
        var crate = body.GetComponentInParent<CrateScript>();
        if (crate != null && collision.relativeVelocity.magnitude >= crate.minDamageSpeed)
            QueueDamage(crate, collision.relativeVelocity.magnitude * crate.impactDamageMult);
    }

    private void QueueBodyState(Rigidbody2D body)
    {
        locallyControlledUntil[body] = Time.unscaledTime + 0.35f;
        body.simulated = true;
        body.bodyType = RigidbodyType2D.Dynamic;
        body.WakeUp();
        var id = Id(body);
        pushes[id] = CaptureBodyState(body);
        lastQueuedId = id;
        TraceLocal(body);
        queuedStates++;
    }


    private void MaintainMovingLocalAuthorities()
    {
        if (locallyControlledUntil.Count == 0) return;
        var now = Time.unscaledTime;
        var renew = new List<Rigidbody2D>();
        foreach (var pair in locallyControlledUntil)
        {
            var body = pair.Key;
            if (body == null || pair.Value >= now || !IsInteractivePropBody(body)) continue;
            if (body.velocity.sqrMagnitude > 0.0004f || Mathf.Abs(body.angularVelocity) > 1f)
                renew.Add(body);
        }
        foreach (var body in renew)
            locallyControlledUntil[body] = now + ClientAuthorityGrace;
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
    }

    internal void QueueLevitated(Rigidbody2D body)
    {
        if (MultiplayerSession.IsHost || body == null || !IsInteractivePropBody(body)) return;
        levitatedStates++;
        QueueBodyState(body);
    }

    internal void QueueWeaponInteraction(DroppedWeapon dropped, BodyScript body, byte operation)
    {
        if (MultiplayerSession.IsHost || dropped == null || body == null ||
            PlayerScript.player == null || body != PlayerScript.player.bodyScript) return;
        var rigidbody = dropped.GetComponent<Rigidbody2D>();
        if (rigidbody == null || !IsWorldBody(rigidbody)) return;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(operation);
            writer.Write(Id(rigidbody));
            var slot = dropped.stats == null ? -1 : dropped.stats.slot;
            var oldWeapon = slot >= 0 && slot < body.weapons.Count ? body.weapons[slot] : null;
            var oldAmmo = slot >= 0 && slot < body.weaponAmmos.Count ? body.weaponAmmos[slot] : 0;
            writer.Write(slot);
            writer.Write(oldWeapon == null ? "" : oldWeapon.name);
            writer.Write(oldAmmo);
            writer.Write(dropped.stats != null && body.weapons.Contains(dropped.stats));
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
            weaponRequestsSent++;
        }
    }

    internal void QueueButtonActivation(ButtonScript button)
    {
        if (MultiplayerSession.IsHost || button == null) return;
        var id = ButtonId(button);
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(ButtonActivate);
            writer.Write(id);
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
            writer.Write(ProximityDoorId(opener));
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
    }

    internal void QueueZoneActivation(ActivateZoneScript zone)
    {
        if (MultiplayerSession.IsHost || zone == null) return;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(ZoneActivate);
            writer.Write(ActivationZoneId(zone));
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

    private byte[] SerializePushes()
    {
        var now = Time.unscaledTime;
        foreach (var pair in locallyControlledUntil)
        {
            var body = pair.Key;
            if (body == null || pair.Value < now) continue;
            pushes[Id(body)] = CaptureBodyState(body);
        }

        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)pushes.Count);
            sentStates += pushes.Count;
            foreach (var pair in pushes)
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value.position.x); writer.Write(pair.Value.position.y);
                writer.Write(pair.Value.rotation);
                writer.Write(pair.Value.velocity.x); writer.Write(pair.Value.velocity.y);
                writer.Write(pair.Value.angularVelocity);
            }
            pushes.Clear();
            return stream.ToArray();
        }
    }

    private byte[] SerializeDamage()
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)damage.Count);
            foreach (var pair in damage) { writer.Write(pair.Key); writer.Write(pair.Value); }
            damage.Clear();
            return stream.ToArray();
        }
    }

    private void ApplyPushes(ushort peerId, byte[] data)
    {
        receivedInputPackets++;
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                var count = reader.ReadUInt16();
                for (var index = 0; index < count; index++)
                {
                    var id = reader.ReadString();
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
                        missingInputBodies++;
                        lastMissingInputId = id;
                        continue;
                    }
                    if (!IsInteractivePropBody(body))
                    {
                        rejectedInputStates++;
                        lastRejectedInputId = id + ":not-prop";
                        continue;
                    }
                    PropAuthority authority;
                    if (propAuthorities.TryGetValue(id, out authority) &&
                        authority.expiresAt >= Time.unscaledTime && authority.peerId != peerId)
                    {
                        rejectedInputStates++;
                        rejectedForeignAuthority++;
                        lastRejectedInputId = id + ":owner";
                        continue;
                    }
                    if ((body.position - predicted.position).sqrMagnitude > 9f)
                    {
                        rejectedInputStates++;
                        rejectedInputDistance++;
                        lastRejectedInputId = id + ":distance";
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
                    TraceHostInput(id, peerId, body, predicted);
                    appliedInputStates++;
                }
            }
        }
        catch (EndOfStreamException) { }
    }

    private void WriteDiagnostics()
    {
        if (Time.unscaledTime < nextDiagnostics || string.IsNullOrEmpty(diagnosticPath)) return;
        nextDiagnostics = Time.unscaledTime + 1f;
        var inventory = BuildInventorySummary();
        var line = DateTime.Now.ToString("HH:mm:ss.fff") +
            " role=" + (MultiplayerSession.IsHost ? "host" : "client") +
            " connected=" + MultiplayerSession.IsConnected +
            " scene=" + activeScene +
            " bodies=" + bodies.Count +
            " inventory=" + inventory +
            " callbackContacts=" + callbackContacts +
            " scannedContacts=" + scannedContacts +
            " levitated=" + levitatedStates +
            " queued=" + queuedStates +
            " sent=" + sentStates +
            " inputPackets=" + receivedInputPackets +
            " applied=" + appliedInputStates +
            " missing=" + missingInputBodies +
            " lastMissingInput=" + ShortDiagnosticId(lastMissingInputId) +
            " rejected=" + rejectedInputStates +
            " rejectedOwner=" + rejectedForeignAuthority +
            " rejectedDistance=" + rejectedInputDistance +
            " lastRejected=" + ShortDiagnosticId(lastRejectedInputId) +
            " snapshots=" + receivedSnapshotPackets + "/" + receivedSnapshotStates +
            " missingSnapshot=" + missingSnapshotBodies +
            " lastMissingSnapshot=" + ShortDiagnosticId(lastMissingSnapshotId) +
            " lastQueued=" + ShortDiagnosticId(lastQueuedId) +
            " localAuthority=" + locallyControlledUntil.Count +
            " hostAuthority=" + propAuthorities.Count +
            " traces=" + FormatPropTraces() +
            " weaponSent=" + weaponRequestsSent +
            " weaponApplied=" + weaponRequestsApplied +
            " weaponRejected=" + weaponRequestsRejected + Environment.NewLine;
        try { File.AppendAllText(diagnosticPath, line); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        WriteBodyManifest(inventory);
    }

    private string BuildInventorySummary()
    {
        var identifiers = new List<string>();
        var props = 0;
        var mechanisms = 0;
        foreach (var pair in bodies)
        {
            var body = pair.Value;
            if (body == null || !body.gameObject.scene.isLoaded) continue;
            var kind = IsInteractivePropBody(body) ? "P" :
                (IsMechanismBody(body) ? "M" : "W");
            if (kind == "P") props++;
            else if (kind == "M") mechanisms++;
            identifiers.Add(kind + ":" + pair.Key);
        }
        identifiers.Sort(StringComparer.Ordinal);
        uint hash = 2166136261u;
        foreach (var identifier in identifiers)
        {
            for (var index = 0; index < identifier.Length; index++)
            {
                hash ^= identifier[index];
                hash *= 16777619u;
            }
        }
        return identifiers.Count + ":P" + props + ":M" + mechanisms + ":H" + hash.ToString("X8");
    }

    private void WriteBodyManifest(string inventory)
    {
        if (Time.unscaledTime < nextManifest || string.IsNullOrEmpty(diagnosticPath)) return;
        nextManifest = Time.unscaledTime + 3f;
        var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        var manifestPath = Path.Combine(Paths.BepInExRootPath,
            "world-manifest-" + processId + ".txt");
        var entries = new List<string>();
        foreach (var pair in bodies)
        {
            var body = pair.Value;
            if (body == null || !body.gameObject.scene.isLoaded) continue;
            var kind = IsInteractivePropBody(body) ? "prop" :
                (IsMechanismBody(body) ? "mechanism" : "world");
            entries.Add(pair.Key + "\t" + kind + "\t" + body.gameObject.activeInHierarchy +
                "\t" + body.simulated + "\t" + body.bodyType);
        }
        entries.Sort(StringComparer.Ordinal);
        var content = "scene=" + activeScene + " role=" +
            (MultiplayerSession.IsHost ? "host" : "client") + " inventory=" + inventory +
            Environment.NewLine + string.Join(Environment.NewLine, entries.ToArray());
        try { File.WriteAllText(manifestPath, content); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string ShortDiagnosticId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "-";
        return id.Length <= 72 ? id : id.Substring(id.Length - 72);
    }

    private void ApplyDamage(byte[] data)
    {
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                var count = reader.ReadUInt16();
                for (var index = 0; index < count; index++)
                {
                    var id = reader.ReadString();
                    var amount = Mathf.Clamp(reader.ReadSingle(), 0f, 100f);
                    Rigidbody2D body;
                    if (!bodies.TryGetValue(id, out body) || body == null) continue;
                    var crate = body.GetComponentInParent<CrateScript>();
                    if (crate != null && crate.enabled) crate.Damage(amount);
                }
            }
        }
        catch (EndOfStreamException) { }
    }

    private void ApplyWeaponInteraction(ushort peerId, byte[] data)
    {
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                var operation = reader.ReadByte();
                var id = reader.ReadString();
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
                    ApplyZoneActivation(id, peerId);
                    return;
                }
                var slot = reader.ReadInt32();
                var oldWeaponName = reader.ReadString();
                var oldAmmo = reader.ReadInt32();
                var clientOwnsWeapon = reader.ReadBoolean();
                Rigidbody2D rigidbody;
                var remoteBody = NetworkAvatarReplication.RemoteBodyForPeer(peerId);
                if ((operation != WeaponPickup && operation != WeaponAmmoGet) || remoteBody == null ||
                    !remoteBody.isAlive || !bodies.TryGetValue(id, out rigidbody) || rigidbody == null)
                {
                    weaponRequestsRejected++;
                    return;
                }
                var dropped = rigidbody.GetComponentInParent<DroppedWeapon>();
                if (dropped == null || (remoteBody.transform.position - dropped.transform.position).sqrMagnitude > 25f)
                {
                    weaponRequestsRejected++;
                    return;
                }
                if (operation == WeaponPickup && slot >= 0 && slot < remoteBody.weapons.Count &&
                    slot < remoteBody.weaponAmmos.Count)
                {
                    remoteBody.weapons[slot] = FindWeaponPreset(oldWeaponName);
                    remoteBody.weaponAmmos[slot] = Mathf.Max(0, oldAmmo);
                }
                else if (operation == WeaponAmmoGet && clientOwnsWeapon && dropped.stats != null &&
                    slot >= 0 && slot < remoteBody.weapons.Count)
                {
                    remoteBody.weapons[slot] = dropped.stats;
                }
                var wasPlayer = remoteBody.isPlayer;
                remoteBody.isPlayer = false;
                try
                {
                    if (operation == WeaponPickup) dropped.PickupWeapon(remoteBody);
                    else if (clientOwnsWeapon) dropped.AmmoGet(remoteBody);
                    else UnloadDroppedWeapon(dropped);
                }
                finally { remoteBody.isPlayer = wasPlayer; }
                weaponRequestsApplied++;
            }
        }
        catch (EndOfStreamException) { }
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

    private void RefreshWorldFires()
    {
        if (MultiplayerSession.IsHost) fires.Clear();
        foreach (var fire in FindObjectsOfType<FireScript>())
        {
            if (fire == null || IsGameplayOwned(fire)) continue;
            string id;
            if (!fireIds.TryGetValue(fire, out id))
            {
                id = ComponentId(fire);
                fireIds[fire] = id;
            }
            fires[id] = fire;
            if (MultiplayerSession.IsHost || clientFireSettings.ContainsKey(fire)) continue;
            clientFireSettings[fire] = new FireLocalSettings
            {
                enabled = fire.enabled,
                active = fire.gameObject.activeSelf
            };
            fire.enabled = false;
        }
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
        fire.enabled = false;
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
        var remotePlayer = NetworkAvatarReplication.RemoteBodyForPeer(peerId);
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

    private void ApplyZoneActivation(string id, ushort peerId)
    {
        ActivateZoneScript zone;
        var remotePlayer = NetworkAvatarReplication.RemoteBodyForPeer(peerId);
        float allowedAt;
        if (!activationZones.TryGetValue(id, out zone) || zone == null || remotePlayer == null ||
            !remotePlayer.isAlive ||
            (nextZoneActivation.TryGetValue(id, out allowedAt) && Time.unscaledTime < allowedAt)) return;
        var zoneCollider = zone.GetComponent<Collider2D>();
        if (zoneCollider == null || zoneCollider.bounds.SqrDistance(remotePlayer.transform.position) > 4f) return;
        var hostPlayer = PlayerScript.player;
        var hostBody = hostPlayer == null ? null : hostPlayer.bodyScript;
        if (!string.IsNullOrEmpty(zone.team) && (hostBody == null || zone.team != hostBody.team)) return;
        nextZoneActivation[id] = Time.unscaledTime + 0.2f;
        foreach (var target in GameObject.FindGameObjectsWithTag("Activateable"))
            target.SendMessage("Activate", zone.id, SendMessageOptions.DontRequireReceiver);
        if (!zone.activateOnce) return;
        Destroy(zone);
    }

    private void ApplyButtonActivation(string id, ushort peerId)
    {
        ButtonScript button;
        var remotePlayer = NetworkAvatarReplication.RemoteBodyForPeer(peerId);
        float allowedAt;
        if (!buttons.TryGetValue(id, out button) || button == null || remotePlayer == null ||
            !remotePlayer.isAlive || (remotePlayer.transform.position - button.transform.position).sqrMagnitude > 25f ||
            (nextButtonActivation.TryGetValue(id, out allowedAt) && Time.unscaledTime < allowedAt)) return;
        nextButtonActivation[id] = Time.unscaledTime + 0.15f;
        button.Activated();
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
            foreach (var component in child.GetComponents<Component>())
            {
                if (component == null || component.GetType().Name != "Light2D") continue;
                var property = component.GetType().GetProperty("color",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.PropertyType == typeof(Color) && property.CanWrite)
                    property.SetValue(component, Color.red, null);
            }
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

    private string Id(Rigidbody2D body)
    {
        string id;
        if (ids.TryGetValue(body, out id)) return id;
        if (body.GetComponentInParent<DroppedWeapon>() != null)
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

    private Rigidbody2D CreateDroppedWeapon(string id, string weaponName, int ammo, Vector2 position, float rotation)
    {
        var prefab = Resources.Load<GameObject>("Spawnables/PickupWeapon");
        if (prefab == null) return null;
        var weapon = FindWeaponPreset(weaponName);
        if (weapon == null) return null;
        var dropped = Instantiate(prefab, position, Quaternion.Euler(0f, 0f, rotation)).GetComponent<DroppedWeapon>();
        if (dropped == null) return null;
        dropped.ChangeWeapon(weapon, ammo);
        var body = dropped.GetComponent<Rigidbody2D>();
        if (body == null) { Destroy(dropped.gameObject); return null; }
        bodies[id] = body;
        ids[body] = id;
        clientCreatedBodies.Add(body);
        MakeClientControlled(body);
        return body;
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
        MakeClientControlled(body);
        return body;
    }

    private static void SynchronizeDroppedWeapon(DroppedWeapon dropped, string weaponName, int ammo)
    {
        if (dropped == null) return;
        var weapon = FindWeaponPreset(weaponName);
        if (weapon == null) return;
        if (dropped.stats != weapon || dropped.ammoAmount != ammo)
            dropped.ChangeWeapon(weapon, ammo);
        dropped.ammoAmount = ammo;
        var ammoSpriteField = typeof(DroppedWeapon).GetField("ammoSprite",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var ammoSprite = ammoSpriteField == null ? null : ammoSpriteField.GetValue(dropped) as SpriteRenderer;
        if (ammoSprite != null) ammoSprite.enabled = ammo > 0;
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
        var ammoSpriteField = typeof(DroppedWeapon).GetField("ammoSprite",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var ammoSprite = ammoSpriteField == null ? null : ammoSpriteField.GetValue(dropped) as SpriteRenderer;
        if (ammoSprite != null) ammoSprite.enabled = false;
        var rigidbody = dropped.GetComponent<Rigidbody2D>();
        if (rigidbody != null)
        {
            rigidbody.AddForce(new Vector2(UnityEngine.Random.Range(-1.5f, 1.5f),
                UnityEngine.Random.Range(-1.5f, 1.5f)), ForceMode2D.Impulse);
            rigidbody.AddTorque(UnityEngine.Random.Range(-1.5f, 1.5f), ForceMode2D.Impulse);
        }
    }

    private static WeaponPreset FindWeaponPreset(string weaponName)
    {
        foreach (var candidate in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (candidate != null && candidate.name == weaponName) return candidate;
        return null;
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

    private void TraceLocal(Rigidbody2D body)
    {
        if (!DiagnosticsEnabled || body == null) return;
        var id = Id(body);
        PropTrace trace;
        propTraces.TryGetValue(id, out trace);
        trace.id = id;
        trace.localPosition = body.position;
        trace.localVelocity = body.velocity;
        trace.localType = body.bodyType;
        trace.localSamples++;
        propTraces[id] = trace;
    }

    private void TraceHostInput(string id, ushort peerId, Rigidbody2D body, ClientBodyState input)
    {
        if (!DiagnosticsEnabled) return;
        PropTrace trace;
        propTraces.TryGetValue(id, out trace);
        trace.id = id;
        trace.peerId = peerId;
        trace.inputPosition = input.position;
        trace.inputVelocity = input.velocity;
        trace.hostPosition = body.position;
        trace.hostType = body.bodyType;
        trace.hostSamples++;
        propTraces[id] = trace;
    }

    private void TraceSnapshot(Rigidbody2D body, State state, string action)
    {
        if (!DiagnosticsEnabled || body == null) return;
        string id;
        if (!ids.TryGetValue(body, out id) || !propTraces.ContainsKey(id)) return;
        var trace = propTraces[id];
        trace.id = id;
        trace.snapshotPosition = state.position;
        trace.snapshotVelocity = state.velocity;
        trace.snapshotType = state.bodyType;
        trace.actualPosition = body.position;
        trace.actualVelocity = body.velocity;
        trace.snapshotAction = action;
        trace.snapshotSamples++;
        propTraces[id] = trace;
    }

    private string FormatPropTraces()
    {
        if (propTraces.Count == 0) return "none";
        var builder = new StringBuilder();
        var written = 0;
        foreach (var pair in propTraces)
        {
            if (written++ >= 4) break;
            var trace = pair.Value;
            if (builder.Length > 0) builder.Append('|');
            var label = trace.id ?? pair.Key;
            var slash = label.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < label.Length) label = label.Substring(slash + 1);
            if (label.Length > 36) label = label.Substring(label.Length - 36);
            builder.Append(label).Append(" local=").Append(TraceVector(trace.localPosition))
                .Append('/').Append(trace.localType).Append('#').Append(trace.localSamples)
                .Append(" in=").Append(TraceVector(trace.inputPosition)).Append('#').Append(trace.hostSamples)
                .Append(" host=").Append(TraceVector(trace.hostPosition)).Append('/').Append(trace.hostType)
                .Append(" snap=").Append(TraceVector(trace.snapshotPosition)).Append('/').Append(trace.snapshotType)
                .Append('/').Append(trace.snapshotAction ?? "-").Append('#').Append(trace.snapshotSamples)
                .Append(" actual=").Append(TraceVector(trace.actualPosition));
        }
        return builder.ToString();
    }

    private static string TraceVector(Vector2 value)
    {
        return value.x.ToString("F2") + "," + value.y.ToString("F2");
    }

    private struct PropTrace
    {
        public string id;
        public ushort peerId;
        public Vector2 localPosition;
        public Vector2 localVelocity;
        public RigidbodyType2D localType;
        public int localSamples;
        public Vector2 inputPosition;
        public Vector2 inputVelocity;
        public Vector2 hostPosition;
        public RigidbodyType2D hostType;
        public int hostSamples;
        public Vector2 snapshotPosition;
        public Vector2 snapshotVelocity;
        public RigidbodyType2D snapshotType;
        public Vector2 actualPosition;
        public Vector2 actualVelocity;
        public string snapshotAction;
        public int snapshotSamples;
    }

    private struct FireLocalSettings
    {
        public bool enabled;
        public bool active;
    }

}
