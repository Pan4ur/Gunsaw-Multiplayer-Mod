using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class NpcReplication : MonoBehaviour
{
    private const byte ProtocolVersion = 4;

    private const float SnapshotInterval = 1f / 50f;
    private const float DiscoveryInterval = 1f;
    private const bool DiagnosticsEnabled = false;
    private static readonly MethodInfo LimbRenderCallback = typeof(LimbScript).GetMethod(
        "OnWillRenderObject", BindingFlags.Instance | BindingFlags.NonPublic);
    private readonly Dictionary<BodyScript, string> hostIds = new Dictionary<BodyScript, string>();
    private readonly Dictionary<string, BodyScript> hostNpcs = new Dictionary<string, BodyScript>();
    private readonly Dictionary<BodyScript, HostNpcLayout> hostLayouts =
        new Dictionary<BodyScript, HostNpcLayout>();
    private readonly Dictionary<string, NpcProxy> clientNpcs = new Dictionary<string, NpcProxy>();
    private readonly Dictionary<BodyScript, NpcProxy> clientBodies = new Dictionary<BodyScript, NpcProxy>();
    private readonly HashSet<NpcProxy> clientProxies = new HashSet<NpcProxy>();
    private readonly HashSet<string> locallyPossessedNpcIds = new HashSet<string>();
    private readonly HashSet<string> remotelyPossessedNpcIds = new HashSet<string>();
    private float possessionRenderGuardUntil;
    private readonly Dictionary<string, float> prefabRetryAt = new Dictionary<string, float>();
    private float nextSnapshot;
    private float nextDiscovery;
    private int sendSequence;
    private int receivedSequence = -1;
    private bool wasConnected;
    private bool wasHost;
    private string activeScene = "";
    private string diagnosticPath = "";
    private float nextDiagnostics;
    private int lastPacketBytes;
    private int lastStateCount;
    private int createdFromNetwork;
    private int missingPrefabs;
    private int reboundExisting;
    private int applyFailures;
    private int rigMisses;
    private string lastApplyError = "none";
    internal static NpcReplication Instance;
    internal static bool ApplyingAuthoritativeDeath;

    private void Awake()
    {
        Instance = this;
        if (!DiagnosticsEnabled) return;
        diagnosticPath = Path.Combine(Paths.BepInExRootPath,
            "npc-sync-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".log");
        try { File.WriteAllText(diagnosticPath, "Gunsaw Multiplayer NPC sync diagnostics\n"); }
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
            ResetReplication(wasConnected && !wasHost);
            activeScene = scene;
            nextDiscovery = 0f;
        }

        if (!MultiplayerSession.IsConnected)
        {
            if (wasConnected) ResetReplication(true);
            wasConnected = false;
            wasHost = isHost;
            return;
        }
        if (!wasConnected) nextDiscovery = 0f;
        wasConnected = true;
        wasHost = isHost;

        var player = PlayerScript.player;
        if (player == null || player.bodyScript == null) return;
        var refreshDiscovery = Time.unscaledTime >= nextDiscovery;
        if (refreshDiscovery) nextDiscovery = Time.unscaledTime + DiscoveryInterval;
        if (isHost)
        {
            if (refreshDiscovery) RefreshHostNpcs(player.bodyScript);
            byte[] possessionPacket;
            ushort possessionPeer;
            while (MultiplayerSession.TryTakeNpcPossession(out possessionPeer, out possessionPacket))
                ApplyRemotePossession(possessionPeer, possessionPacket);
            byte[] damagePacket;
            ushort damagePeer;
            while (MultiplayerSession.TryTakeNpcDamage(out damagePeer, out damagePacket))
                ApplyClientDamage(damagePeer, damagePacket);
            byte[] grabPacket;
            ushort grabPeer;
            while (MultiplayerSession.TryTakeNpcGrab(out grabPeer, out grabPacket))
                ApplyClientGrab(grabPeer, grabPacket);
            if (Time.unscaledTime >= nextSnapshot)
            {
                nextSnapshot = Time.unscaledTime + SnapshotInterval;
                AnimateHostOffscreenNpcs();
                MultiplayerSession.SendNpcSnapshot(SerializeSnapshot());
            }
            return;
        }

        if (refreshDiscovery) RefreshClientNpcs(player.bodyScript);
        byte[] packet;
        byte[] latestPacket = null;
        while (MultiplayerSession.TryTakeNpcSnapshot(out packet)) latestPacket = packet;
        if (latestPacket != null) ApplySnapshot(latestPacket);
    }

    private void LateUpdate()
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return;
        InterpolateClientNpcs();
        foreach (var proxy in clientProxies)
            if (proxy != null && proxy.Root != null && !proxy.NetworkVisible && proxy.Root.activeSelf)
                proxy.Root.SetActive(false);
    }

    private void RefreshHostNpcs(BodyScript localBody)
    {
        var seen = new HashSet<string>();
        foreach (var body in Resources.FindObjectsOfTypeAll<BodyScript>())
        {
            if (!IsNpc(body, localBody)) continue;
            var id = StableId(body);
            if (remotelyPossessedNpcIds.Contains(id)) continue;
            var root = NpcRoot(body);
            if (!root.activeSelf) root.SetActive(true);
            foreach (var ai in root.GetComponentsInChildren<AIScript>(true))
            {
                if (ai == null || ai.body != body) continue;
                if (!ai.gameObject.activeSelf) ai.gameObject.SetActive(true);
                ai.enabled = true;
                body.onScreen = true;
            }
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (animator == null) continue;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;
            }
            if (!hostIds.TryGetValue(body, out id))
            {
                id = StableId(body);
                hostIds[body] = id;
            }
            hostNpcs[id] = body;
            seen.Add(id);
        }
        var stale = new List<string>();
        foreach (var pair in hostNpcs)
            if (pair.Value == null || !seen.Contains(pair.Key)) stale.Add(pair.Key);
        foreach (var id in stale)
        {
            BodyScript body;
            if (hostNpcs.TryGetValue(id, out body)) hostLayouts.Remove(body);
            hostNpcs.Remove(id);
        }
    }

    private void RefreshClientNpcs(BodyScript localBody)
    {
        foreach (var body in Resources.FindObjectsOfTypeAll<BodyScript>())
        {
            if (!IsNpc(body, localBody) || clientBodies.ContainsKey(body)) continue;
            var root = NpcRoot(body);
            var duplicateRoot = false;
            foreach (var proxy in clientBodies.Values)
                if (proxy.Root == root) { duplicateRoot = true; break; }
            if (duplicateRoot) continue;
            var id = StableId(body);
            if (clientNpcs.ContainsKey(id)) id += ":local#" + clientBodies.Count;
            var created = CreateProxy(id, body, root, false);
            clientNpcs[id] = created;
            clientBodies[body] = created;
        }
    }

    private static bool IsNpc(BodyScript body, BodyScript localBody)
    {
        if (body == null || !body.gameObject.scene.isLoaded) return false;
        if (body.transform.root == localBody.transform.root) return false;
        if (body.isPlayer || body.GetComponentInParent<PlayerScript>() != null) return false;
        if (body.GetComponentInParent<NetworkReplica>() != null) return false;
        return true;
    }

    private void ApplyRemotePossession(ushort peerId, byte[] packet)
    {
        if (packet == null || packet.Length == 0) return;
        var id = Encoding.UTF8.GetString(packet);
        if (string.IsNullOrEmpty(id)) return;
        remotelyPossessedNpcIds.Add(id);
        BodyScript body;
        if (hostNpcs.TryGetValue(id, out body) && body != null)
        {
            hostNpcs.Remove(id);
            hostIds.Remove(body);
            hostLayouts.Remove(body);
            var root = NpcRoot(body);
            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
                return;
            }
        }

        foreach (var candidate in Resources.FindObjectsOfTypeAll<BodyScript>())
        {
            if (candidate == null || StableId(candidate) != id) continue;
            var candidateRoot = NpcRoot(candidate);
            if (candidateRoot != null) UnityEngine.Object.Destroy(candidateRoot);
            break;
        }
    }

    private void AnimateHostOffscreenNpcs()
    {
        var manager = GameManager.main;
        if (manager == null || LimbRenderCallback == null) return;
        foreach (var body in hostNpcs.Values)
        {
            if (body == null || !body.gameObject.activeInHierarchy ||
                manager.IsOnscreen((Vector2)body.transform.position)) continue;
            foreach (var item in GetList(body, "limbs"))
            {
                var limb = item as LimbScript;
                if (limb == null || !limb.gameObject.activeInHierarchy) continue;
                try { LimbRenderCallback.Invoke(limb, null); }
                catch (TargetInvocationException) { }
            }
        }
    }

    internal static bool IsNpcRigBody(Rigidbody2D rigidbody)
    {
        var instance = Instance;
        if (instance == null || rigidbody == null) return false;
        foreach (var npc in instance.hostNpcs.Values)
            if (BelongsToNpcRoot(rigidbody, npc)) return true;
        foreach (var npc in instance.clientBodies.Keys)
            if (BelongsToNpcRoot(rigidbody, npc)) return true;
        return false;
    }

    private static bool BelongsToNpcRoot(Rigidbody2D rigidbody, BodyScript npc)
    {
        if (npc == null || rigidbody == null) return false;
        var root = NpcRoot(npc);
        return root != null && (rigidbody.transform == root.transform ||
            rigidbody.transform.IsChildOf(root.transform));
    }

    private static GameObject NpcRoot(BodyScript body)
    {
        var current = body.transform;
        while (current.parent != null)
        {
            var parent = current.parent;
            var bodies = parent.GetComponentsInChildren<BodyScript>(true);
            if (bodies.Length != 1 || bodies[0] != body) break;
            current = parent;
        }
        return current.gameObject;
    }

    private byte[] SerializeSnapshot()
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(ProtocolVersion);
            writer.Write(++sendSequence);
            writer.Write((ushort)hostNpcs.Count);
            foreach (var pair in hostNpcs)
            {
                var body = pair.Value;
                var layout = HostLayout(body);
                writer.Write(pair.Key);
                writer.Write(CleanCloneName(NpcRoot(body).name));
                writer.Write(body.characterName ?? "");
                writer.Write(body.speciesName ?? "");
                writer.Write(NpcRoot(body).activeSelf);
                writer.Write(body.isRight);
                writer.Write((int)body.CurrentState);
                writer.Write((int)body.controlState);
                writer.Write(body.health);
                writer.Write(body.stamina);
                writer.Write(body.isAlive);
                writer.Write(body.grounded);
                writer.Write(body.isInWater);
                writer.Write(body.noLegs);
                writer.Write(body.deHeaded);
                writer.Write(body.burnIntensity);
                var destroyOnDeath = layout.DestroyOnDeath;
                writer.Write((ushort)destroyOnDeath.Count);
                foreach (GameObject item in destroyOnDeath) writer.Write(item != null && item.activeSelf);
                WritePose(writer, body.rb);

                var rigBodies = layout.RigBodies;
                writer.Write((ushort)rigBodies.Length);
                for (var rigIndex = 0; rigIndex < rigBodies.Length; rigIndex++)
                {
                    var rigBody = rigBodies[rigIndex];
                    writer.Write(layout.RigIds[rigIndex]);
                    WritePose(writer, rigBody);
                }

                var limbs = layout.Limbs;
                writer.Write((ushort)limbs.Count);
                foreach (LimbScript limb in limbs)
                {
                    WritePose(writer, limb.rb);
                    writer.Write(limb.dismembered);
                    writer.Write(IsBurning(limb));
                }

                var tailBases = layout.TailBases;
                writer.Write((ushort)tailBases.Count);
                foreach (Rigidbody2D tailBase in tailBases) WritePose(writer, tailBase);
                WriteTransforms(writer, layout.Tails);
                WriteTransform(writer, layout.GunTransform);
                WriteTransform(writer, layout.GunAnimationTransform);
                WriteTransform(writer, body.weapon == null ? null : body.weapon.transform);

                writer.Write(body.currentWeapon);
                writer.Write(body.weapon == null ? 0 : body.weapon.ammo);
                var weapons = GetList(body, "weapons");
                writer.Write((ushort)weapons.Count);
                foreach (WeaponPreset preset in weapons) writer.Write(preset == null ? "" : preset.name);
                WriteLine(writer, GetFieldValue<LineRenderer>(body, "wepLaserLine"));
            }
            var packet = stream.ToArray();
            lastPacketBytes = packet.Length;
            lastStateCount = hostNpcs.Count;
            return packet;
        }
    }

    private void ApplySnapshot(byte[] packet)
    {
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(packet)))
            {
                if (reader.ReadByte() != ProtocolVersion) return;
                var sequence = reader.ReadInt32();
                if (sequence <= receivedSequence) return;
                var count = reader.ReadUInt16();
                var states = new List<NpcState>(count);
                for (var index = 0; index < count; index++) states.Add(ReadState(reader));
                receivedSequence = sequence;
                lastPacketBytes = packet.Length;
                lastStateCount = count;
                ApplyStates(states);
            }
        }
        catch (EndOfStreamException) { }
    }

    private static NpcState ReadState(BinaryReader reader)
    {
        var state = new NpcState
        {
            Id = reader.ReadString(),
            RootName = reader.ReadString(),
            CharacterName = reader.ReadString(),
            SpeciesName = reader.ReadString(),
            Active = reader.ReadBoolean(),
            IsRight = reader.ReadBoolean(),
            CurrentState = reader.ReadInt32(),
            ControlState = reader.ReadInt32(),
            Health = reader.ReadSingle(),
            Stamina = reader.ReadSingle(),
            IsAlive = reader.ReadBoolean(),
            Grounded = reader.ReadBoolean(),
            IsInWater = reader.ReadBoolean(),
            NoLegs = reader.ReadBoolean(),
            DeHeaded = reader.ReadBoolean(),
            BurnIntensity = reader.ReadSingle()
        };
        var deathObjectCount = reader.ReadUInt16();
        state.DeathObjects = new bool[deathObjectCount];
        for (var index = 0; index < deathObjectCount; index++) state.DeathObjects[index] = reader.ReadBoolean();
        state.Body = ReadPose(reader);
        var rigBodyCount = reader.ReadUInt16();
        state.RigBodies = new RigPose[rigBodyCount];
        for (var index = 0; index < rigBodyCount; index++)
            state.RigBodies[index] = new RigPose { Id = reader.ReadUInt64(), Pose = ReadPose(reader) };
        var limbCount = reader.ReadUInt16();
        state.Limbs = new LimbState[limbCount];
        for (var index = 0; index < limbCount; index++)
            state.Limbs[index] = new LimbState
            {
                Pose = ReadPose(reader),
                Dismembered = reader.ReadBoolean(),
                Burning = reader.ReadBoolean()
            };
        var tailBaseCount = reader.ReadUInt16();
        state.TailBases = new Pose[tailBaseCount];
        for (var index = 0; index < tailBaseCount; index++) state.TailBases[index] = ReadPose(reader);
        state.Tails = ReadTransforms(reader);
        state.Gun = ReadTransform(reader);
        state.GunAnimation = ReadTransform(reader);
        state.Weapon = ReadTransform(reader);
        state.WeaponSlot = reader.ReadInt32();
        state.WeaponAmmo = reader.ReadInt32();
        var weaponCount = reader.ReadUInt16();
        state.Weapons = new string[weaponCount];
        for (var index = 0; index < weaponCount; index++) state.Weapons[index] = reader.ReadString();
        state.Laser = ReadLine(reader);
        return state;
    }

    private void ApplyStates(List<NpcState> states)
    {
        foreach (var proxy in clientProxies)
            if (proxy != null) proxy.NetworkVisible = false;
        var claimed = new HashSet<NpcProxy>();
        foreach (var state in states)
        {
            if (locallyPossessedNpcIds.Contains(state.Id)) continue;
            var proxy = FindOrCreateProxy(state, claimed);
            if (proxy == null) continue;
            claimed.Add(proxy);
            proxy.NetworkVisible = state.Active;
            proxy.Root.SetActive(state.Active);
            if (!state.Active) continue;
            try { ApplyState(proxy, state); }
            catch (Exception exception)
            {
                applyFailures++;
                lastApplyError = exception.GetType().Name + ":" + exception.Message.Replace('\n', ' ');
            }
        }

        foreach (var proxy in clientProxies)
            if (!claimed.Contains(proxy) && proxy.Root != null) proxy.Root.SetActive(false);
    }

    private NpcProxy FindOrCreateProxy(NpcState state, HashSet<NpcProxy> claimed)
    {
        NpcProxy exact;
        if (clientNpcs.TryGetValue(state.Id, out exact) && exact != null &&
            (exact.Body == null || exact.Root == null))
        {
            clientNpcs.Remove(state.Id);
            exact = null;
        }
        if (clientNpcs.TryGetValue(state.Id, out exact) && exact != null && !claimed.Contains(exact))
        {
            if (DescriptorScore(exact, state) >= 0f) return exact;
            clientNpcs.Remove(state.Id);
            exact.NetworkId = state.Id + ":unmatched#" + exact.Body.GetInstanceID();
            clientNpcs[exact.NetworkId] = exact;
        }

        NpcProxy best = null;
        var bestScore = float.MinValue;
        foreach (var proxy in clientProxies)
        {
            if (proxy == null || proxy.Body == null || proxy.Root == null || claimed.Contains(proxy)) continue;
            var typeScore = DescriptorScore(proxy, state);
            if (typeScore < 0f) continue;
            var distance = proxy.Body.rb == null ? 1000f : Vector2.Distance(proxy.Body.rb.position, state.Body.Position);
            var score = typeScore - distance;
            if (score <= bestScore) continue;
            bestScore = score;
            best = proxy;
        }
        if (best != null)
        {
            clientNpcs.Remove(best.NetworkId);
            best.NetworkId = state.Id;
            clientNpcs[state.Id] = best;
            reboundExisting++;
            return best;
        }

        var retryKey = state.RootName + "\n" + state.SpeciesName;
        float retryAt;
        if (prefabRetryAt.TryGetValue(retryKey, out retryAt) && Time.unscaledTime < retryAt) return null;
        prefabRetryAt[retryKey] = Time.unscaledTime + 2f;
        var prefab = FindNpcPrefab(state);
        if (prefab == null)
        {
            missingPrefabs++;
            return null;
        }
        var root = Instantiate(prefab, state.Body.Position, Quaternion.Euler(0f, 0f, state.Body.Rotation));
        var body = FindMatchingBody(root, state);
        if (body == null)
        {
            Destroy(root);
            return null;
        }
        var created = CreateProxy(state.Id, body, root, true);
        createdFromNetwork++;
        clientNpcs[state.Id] = created;
        clientBodies[body] = created;
        return created;
    }

    private static float DescriptorScore(NpcProxy proxy, NpcState state)
    {
        if (proxy == null || proxy.Body == null || proxy.Root == null) return -1f;
        var score = 0f;
        if (!string.IsNullOrEmpty(state.SpeciesName))
        {
            if (proxy.SpeciesName != state.SpeciesName) return -1f;
            score += 100f;
        }
        if (!string.IsNullOrEmpty(state.RootName))
        {
            if (proxy.RootName != state.RootName) return -1f;
            score += 500f;
        }
        return score > 0f ? score : -1f;
    }

    private static GameObject FindNpcPrefab(NpcState state)
    {
        var direct = Resources.Load<GameObject>("Enemies/" + state.RootName) ??
            Resources.Load<GameObject>(state.RootName) ??
            Resources.Load<GameObject>("Spawnables/" + state.RootName);
        if (direct != null && FindMatchingBody(direct, state) != null) return direct;

        foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate == null || candidate.scene.IsValid()) continue;
            if (CleanCloneName(candidate.name) != state.RootName) continue;
            if (FindMatchingBody(candidate, state) != null) return candidate;
        }
        return null;
    }

    private static BodyScript FindMatchingBody(GameObject root, NpcState state)
    {
        foreach (var body in root.GetComponentsInChildren<BodyScript>(true))
        {
            if (!string.IsNullOrEmpty(state.SpeciesName) && body.speciesName != state.SpeciesName) continue;
            return body;
        }
        return null;
    }

    private NpcProxy CreateProxy(string id, BodyScript body, GameObject root, bool networkCreated)
    {
        var proxy = new NpcProxy
        {
            NetworkId = id,
            Body = body,
            Root = root,
            RootName = CleanCloneName(root == null ? "" : root.name),
            SpeciesName = body == null ? "" : body.speciesName,
            NetworkCreated = networkCreated,
            OriginalRootActive = root.activeSelf,
        };
        clientProxies.Add(proxy);
        CacheDismembermentVisuals(proxy);
        CacheFireVisuals(proxy);
        CacheRigBodies(proxy);
        FreezeProxy(proxy);
        return proxy;
    }

    private static void FreezeProxy(NpcProxy proxy)
    {
        if (proxy.Root == null) return;
        if (proxy.Marker == null)
        {
            proxy.Marker = proxy.Root.GetComponent<NpcNetworkReplica>();
            if (proxy.Marker == null) proxy.Marker = proxy.Root.AddComponent<NpcNetworkReplica>();
        }
        foreach (var behaviour in proxy.Root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour is NpcNetworkReplica || behaviour is ScarfPhysics) continue;
            if (!proxy.Behaviours.ContainsKey(behaviour)) proxy.Behaviours.Add(behaviour, behaviour.enabled);
            behaviour.enabled = false;
        }
        foreach (var behaviour in proxy.Root.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour == null) continue;
            var typeName = behaviour.GetType().Name;
            if (typeName != "Animator" && typeName != "AudioSource") continue;
            if (!proxy.OtherBehaviours.ContainsKey(behaviour))
                proxy.OtherBehaviours.Add(behaviour, behaviour.enabled);
            behaviour.enabled = false;
        }
        var keepLocalPhysics = proxy.LocalPhysics && Time.unscaledTime <= proxy.LocalPhysicsUntil;
        foreach (var body in proxy.Root.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (!proxy.RigidbodySettings.ContainsKey(body))
                proxy.RigidbodySettings.Add(body, new RigidbodySettings
                {
                    BodyType = body.bodyType,
                    Simulated = body.simulated
                });
            if (body.simulated && !keepLocalPhysics) body.bodyType = RigidbodyType2D.Kinematic;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    private void ApplyState(NpcProxy proxy, NpcState state)
    {
        var body = proxy.Body;
        body.characterName = state.CharacterName;
        body.speciesName = state.SpeciesName;
        ApplyLifeTransition(body, state.IsAlive);
        if (body.isRight != state.IsRight) body.SwitchDir(true);
        body.CurrentState = (BodyScript.EntityState)state.CurrentState;
        body.controlState = (BodyScript.RagdollState)state.ControlState;
        body.health = state.Health;
        proxy.LastHostHealth = state.Health;
        proxy.LastHostAlive = state.IsAlive;
        body.stamina = state.Stamina;
        body.isAlive = state.IsAlive;
        body.grounded = state.Grounded;
        body.isInWater = state.IsInWater;
        body.noLegs = state.NoLegs;
        body.deHeaded = state.DeHeaded;
        body.burnIntensity = state.BurnIntensity;
        if (body.limbMat != null) body.limbMat.SetFloat("BurnIntensity", state.BurnIntensity);
        var destroyOnDeath = GetList(body, "destroyOnDeath");
        for (var index = 0; index < state.DeathObjects.Length && index < destroyOnDeath.Count; index++)
        {
            var item = destroyOnDeath[index] as GameObject;
            if (item != null) item.SetActive(state.DeathObjects[index]);
        }
        SetTarget(proxy, body.rb, state.Body);
        foreach (var rigState in state.RigBodies)
        {
            Rigidbody2D rigBody;
            if (proxy.RigBodies.TryGetValue(rigState.Id, out rigBody) && rigBody != null)
            {
                SetTarget(proxy, rigBody, rigState.Pose);
                SetTransformTarget(proxy, rigBody.transform, TransformPose.From(rigState.Pose));
            }
            else
                rigMisses++;
        }

        var limbs = GetList(body, "limbs");
        for (var index = 0; index < state.Limbs.Length; index++)
        {
            if (index >= limbs.Count) continue;
            var limb = limbs[index] as LimbScript;
            if (limb == null) continue;
            SetTarget(proxy, limb.rb, state.Limbs[index].Pose);
            limb.dismembered = state.Limbs[index].Dismembered;
            SetRemoteFire(proxy, index, limb, state.Limbs[index].Burning);
        }

        var tailBases = GetList(body, "tailBases");
        for (var index = 0; index < state.TailBases.Length && index < tailBases.Count; index++)
        {
            var tailBase = tailBases[index] as Rigidbody2D;
            if (tailBase != null) SetTarget(proxy, tailBase, state.TailBases[index]);
        }
        SetTransformTargets(proxy, GetTransforms(body, "tails"), state.Tails);
        SetTransformTarget(proxy, GetTransform(body, "gunTransform"), state.Gun);
        SetTransformTarget(proxy, GetTransform(body, "gunAnimTransform"), state.GunAnimation);
        ApplyWeapon(proxy, state);
        SetTransformTarget(proxy, body.weapon == null ? null : body.weapon.transform, state.Weapon);
        ApplyLine(state.Laser, GetFieldValue<LineRenderer>(body, "wepLaserLine"),
            GetFieldValue<GameObject>(body, "wepLaser"));
        ApplyDismembermentVisuals(proxy);
        proxy.ReceivedFirstState = true;
    }

    private static void ApplyLifeTransition(BodyScript body, bool hostAlive)
    {
        if (hostAlive)
        {
            if (!body.isAlive)
            {
                body.health = Mathf.Max(1f, body.maxHealth);
                body.CurrentState = 0;
                body.controlState = 0;
                body.isAlive = true;
                body.WakeUp();
            }
            return;
        }
        if (!body.isAlive) return;
        var wasPlayer = body.isPlayer;
        body.isPlayer = true;
        body.dropWeapon = false;
        ApplyingAuthoritativeDeath = true;
        try { body.Death(); }
        finally
        {
            ApplyingAuthoritativeDeath = false;
            body.isPlayer = wasPlayer;
            body.dropWeapon = false;
        }
    }

    internal static bool IsClientProxy(BodyScript body)
    {
        return MultiplayerSession.IsConnected && !MultiplayerSession.IsHost && Instance != null &&
            body != null && Instance.clientBodies.ContainsKey(body);
    }

    internal static bool IsLocallyPossessedBody(BodyScript body)
    {
        var current = Instance;
        return MultiplayerSession.IsConnected && !MultiplayerSession.IsHost && current != null &&
            body != null && current.locallyPossessedNpcIds.Contains(StableId(body));
    }

    internal static bool IsPossessionRenderGuard(BodyScript body)
    {
        var current = Instance;
        return current != null && body != null && Time.unscaledTime < current.possessionRenderGuardUntil &&
            current.locallyPossessedNpcIds.Contains(StableId(body));
    }

    internal static void PrepareAuthoritativeNpcDeath(BodyScript body)
    {
        if (!MultiplayerSession.IsHosting || body == null || body.isPlayer || !body.isAlive ||
            body.weapon == null || body.unarmed) return;
        body.dropWeapon = true;
    }

    internal static bool TryPossessLocalPlayer(LimbScript limb)
    {
        var current = Instance;
        var player = PlayerScript.player;
        var oldBody = player == null ? null : player.bodyScript;
        var target = limb == null ? null : limb.body;
        if (current == null || player == null || oldBody == null || target == null ||
            target == oldBody || !MultiplayerSession.IsConnected || target.isPlayer)
            return false;

        string id = null;
        NpcProxy proxy = null;
        if (MultiplayerSession.IsHost)
        {
            if (!current.hostIds.TryGetValue(target, out id) || !current.hostNpcs.ContainsKey(id))
            {
                id = StableId(target);
                BodyScript mapped;
                if (!current.hostNpcs.TryGetValue(id, out mapped) || mapped != target) return false;
                current.hostIds[target] = id;
            }
        }
        else if (!current.clientBodies.TryGetValue(target, out proxy) || proxy == null)
        {
            return false;
        }
        else
        {
            id = proxy.NetworkId;
        }

        var sourceRoot = NpcRoot(target);
        if (sourceRoot == null || !sourceRoot.scene.IsValid()) return false;
        var sourcePosition = sourceRoot.transform.position;
        var sourceRotation = sourceRoot.transform.rotation;
        var sourceParent = sourceRoot.transform.parent;
        var clone = UnityEngine.Object.Instantiate(sourceRoot);
        if (clone == null) return false;

        clone.SetActive(false);
        foreach (var customJoint in clone.GetComponentsInChildren<CustJoint>(true))
            if (customJoint != null) UnityEngine.Object.DestroyImmediate(customJoint);

        foreach (var chatter in clone.GetComponentsInChildren<Chatter>(true))
            if (chatter != null) UnityEngine.Object.DestroyImmediate(chatter);
        foreach (var ai in clone.GetComponentsInChildren<AIScript>(true))
            if (ai != null) UnityEngine.Object.DestroyImmediate(ai);
        foreach (var marker in clone.GetComponentsInChildren<NpcNetworkReplica>(true))
            if (marker != null) UnityEngine.Object.DestroyImmediate(marker);
        clone.name = sourceRoot.name + " [Player]";
        if (sourceParent != null) clone.transform.SetParent(sourceParent, true);
        clone.transform.position = sourcePosition;
        clone.transform.rotation = sourceRotation;
        clone.transform.localScale = sourceRoot.transform.localScale;
        clone.SetActive(true);

        var newBody = FindBodyForPossession(clone, target);
        if (newBody == null || newBody.limbs == null || newBody.limbs.Count == 0)
        {
            UnityEngine.Object.Destroy(clone);
            return false;
        }

        foreach (var behaviour in clone.GetComponentsInChildren<Behaviour>(true))
            if (behaviour != null && !(behaviour is PlayerScript)) behaviour.enabled = true;
        foreach (var child in clone.GetComponentsInChildren<Transform>(true))
            if (child != null) child.gameObject.SetActive(true);
        newBody.noLegs = false;
        newBody.deHeaded = false;
        foreach (var limbItem in GetList(newBody, "limbs"))
        {
            var limbScript = limbItem as LimbScript;
            if (limbScript != null) limbScript.dismembered = false;
        }
        foreach (var renderer in clone.GetComponentsInChildren<SpriteRenderer>(true))
            if (renderer != null) renderer.enabled = true;

        if (!NetworkAvatarReplication.ReplaceLocalPlayerBody(oldBody, newBody))
        {
            UnityEngine.Object.Destroy(clone);
            return false;
        }

        if (!MultiplayerSession.IsHost) MultiplayerSession.SendNpcPossession(id);
        current.locallyPossessedNpcIds.Add(id);
        current.possessionRenderGuardUntil = Time.unscaledTime + 0.5f;
        if (proxy != null)
        {
            current.clientBodies.Remove(target);
            current.clientNpcs.Remove(proxy.NetworkId);
            current.clientProxies.Remove(proxy);
        }
        else
        {
            current.hostIds.Remove(target);
            current.hostNpcs.Remove(id);
            current.hostLayouts.Remove(target);
        }

        var oldRoot = oldBody.transform.root == null ? null : oldBody.transform.root.gameObject;
        if (oldRoot != null && oldRoot != clone) UnityEngine.Object.Destroy(oldRoot);
        if (sourceRoot != null && sourceRoot != clone) UnityEngine.Object.Destroy(sourceRoot);
        return true;
    }

    private static BodyScript FindBodyForPossession(GameObject root, BodyScript source)
    {
        var bodies = root.GetComponentsInChildren<BodyScript>(true);
        foreach (var body in bodies)
            if (body != null && source != null && body.characterName == source.characterName &&
                body.speciesName == source.speciesName) return body;
        return bodies.Length == 0 ? null : bodies[0];
    }

    private static void RemoveDetachedPossessionParts(BodyScript body)
    {
        if (body == null) return;
        foreach (var limb in Resources.FindObjectsOfTypeAll<LimbScript>())
        {
            if (limb == null || limb.body != body || limb.transform.root == body.transform.root) continue;
            var root = limb.transform.root;
            if (root != null && root.gameObject.scene.IsValid()) root.gameObject.SetActive(false);
        }
    }

    internal static void TryGrabClientCorpse(LevitatorScript levitator)
    {
        if (MultiplayerSession.IsHost || Instance == null || levitator == null ||
            levitator.currentlyLevitating != null || levitator.refBody == null) return;
        var camera = Camera.main;
        if (camera == null) return;
        var origin = (Vector2)levitator.refBody.transform.position;
        var mouse = (Vector2)camera.ScreenToWorldPoint(Input.mousePosition);
        foreach (var hit in Physics2D.LinecastAll(origin, mouse))
        {
            var collider = hit.collider;
            if (collider == null || collider.GetComponentInParent<BodyScript>() == levitator.refBody ||
                collider.gameObject.layer == LayerMask.NameToLayer("Cosmetic")) continue;
            var body = collider.GetComponentInParent<BodyScript>();
            NpcProxy proxy;
            if (body != null && Instance.clientBodies.TryGetValue(body, out proxy) && proxy != null &&
                proxy.ReceivedFirstState && !proxy.LastHostAlive)
            {
                var rigidbody = hit.rigidbody == null ? collider.attachedRigidbody : hit.rigidbody;
                if (rigidbody == null) return;
                EnableLocalCorpsePhysics(proxy);
                levitator.currentlyLevitating = rigidbody;
                levitator.point = hit.point;
                levitator.localGrabPoint = rigidbody.transform.InverseTransformPoint(hit.point);
                return;
            }
            if (!collider.isTrigger) break;
        }
    }

    internal static void QueueClientCorpseGrab(LevitatorScript levitator)
    {
        if (MultiplayerSession.IsHost || Instance == null || levitator == null) return;
        var rigidbody = levitator.currentlyLevitating;
        if (rigidbody == null) return;
        var body = rigidbody.GetComponentInParent<BodyScript>();
        NpcProxy proxy;
        if (body == null || !Instance.clientBodies.TryGetValue(body, out proxy) || proxy == null ||
            !proxy.ReceivedFirstState || proxy.LastHostAlive) return;
        ulong rigId = 0;
        var found = false;
        foreach (var pair in proxy.RigBodies)
            if (pair.Value == rigidbody) { rigId = pair.Key; found = true; break; }
        if (!found) return;

        EnableLocalCorpsePhysics(proxy);
        proxy.LocalPhysicsUntil = Time.unscaledTime + 0.15f;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(proxy.NetworkId);
            writer.Write(rigId);
            writer.Write(levitator.point.x);
            writer.Write(levitator.point.y);
            writer.Write(levitator.localGrabPoint.x);
            writer.Write(levitator.localGrabPoint.y);
            MultiplayerSession.SendNpcGrab(stream.ToArray());
        }
    }

    private static void EnableLocalCorpsePhysics(NpcProxy proxy)
    {
        proxy.LocalPhysics = true;
        proxy.LocalPhysicsUntil = Time.unscaledTime + 0.15f;
        foreach (var body in proxy.Root.GetComponentsInChildren<Rigidbody2D>(true))
        {
            body.simulated = true;
            body.bodyType = RigidbodyType2D.Dynamic;
            body.WakeUp();
        }
    }

    internal static bool HandleClientDamaged(BodyScript body, bool critical)
    {
        if (!IsClientProxy(body)) return false;
        NpcProxy proxy;
        if (!Instance.clientBodies.TryGetValue(body, out proxy) || proxy == null) return false;
        if (!proxy.ReceivedFirstState)
        {
            body.isAlive = true;
            return true;
        }
        var amount = Mathf.Clamp(proxy.LastHostHealth - body.health, 0f, 1000f);
        body.health = proxy.LastHostHealth;
        body.isAlive = proxy.LastHostAlive;
        if (amount > 0.001f) Instance.SendClientDamage(proxy.NetworkId, amount, critical);
        return true;
    }

    internal static bool HandleClientDeath(BodyScript body)
    {
        if (ApplyingAuthoritativeDeath || !IsClientProxy(body)) return false;
        NpcProxy proxy;
        if (!Instance.clientBodies.TryGetValue(body, out proxy) || proxy == null) return false;
        if (!proxy.ReceivedFirstState)
        {
            body.isAlive = true;
            return true;
        }
        body.health = proxy.LastHostHealth;
        body.isAlive = proxy.LastHostAlive;
        Instance.SendClientDamage(proxy.NetworkId, Mathf.Max(1f, proxy.LastHostHealth + 1f), true);
        return true;
    }

    private void SendClientDamage(string id, float amount, bool critical)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(id);
            writer.Write(amount);
            writer.Write(critical);
            MultiplayerSession.SendNpcDamage(stream.ToArray());
        }
    }

    internal static bool BlockClientWeaponDrop(BodyScript body)
    {
        return IsClientProxy(body);
    }

    private void ApplyClientDamage(ushort peerId, byte[] packet)
    {
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(packet)))
            {
                var id = reader.ReadString();
                var amount = Mathf.Clamp(reader.ReadSingle(), 0f, 1000f);
                var critical = reader.ReadBoolean();
                BodyScript body;
                if (amount <= 0f || !hostNpcs.TryGetValue(id, out body) || body == null || !body.isAlive) return;
                body.health -= amount;
                var source = NetworkAvatarReplication.RemoteBodyForPeer(peerId);
                if (source != null) NetworkAvatarReplication.RecordDamageSource(body, source);
                body.Damaged(critical);
            }
        }
        catch (EndOfStreamException) { }
    }

    private void ApplyClientGrab(ushort peerId, byte[] packet)
    {
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(packet)))
            {
                var id = reader.ReadString();
                var rigId = reader.ReadUInt64();
                var point = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                var localPoint = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) ||
                    float.IsInfinity(point.y) || float.IsNaN(localPoint.x) || float.IsNaN(localPoint.y) ||
                    float.IsInfinity(localPoint.x) || float.IsInfinity(localPoint.y)) return;
                BodyScript body;
                var remotePlayer = NetworkAvatarReplication.RemoteBodyForPeer(peerId);
                if (!hostNpcs.TryGetValue(id, out body) || body == null || body.isAlive ||
                    remotePlayer == null || !remotePlayer.isAlive) return;
                Rigidbody2D target = null;
                foreach (var candidate in GetRigBodies(body))
                    if (RigId(NpcRoot(body).transform, candidate) == rigId) { target = candidate; break; }
                if (target == null || !target.simulated || target.bodyType != RigidbodyType2D.Dynamic) return;
                var force = point - target.position;
                if (force.magnitude > 5f) force = force.normalized * 5f;
                target.AddForceAtPosition(force * 100f, target.transform.TransformPoint(localPoint));
                target.angularVelocity *= 0.96f;
            }
        }
        catch (EndOfStreamException) { }
    }

    private static void ApplyWeapon(NpcProxy proxy, NpcState state)
    {
        var body = proxy.Body;
        var inventoryKey = string.Join("|", state.Weapons);
        if (proxy.AppliedInventory != inventoryKey)
        {
            var weapons = GetList(body, "weapons");
            while (weapons.Count < state.Weapons.Length) weapons.Add(null);
            while (weapons.Count > state.Weapons.Length) weapons.RemoveAt(weapons.Count - 1);
            var resolved = true;
            for (var index = 0; index < state.Weapons.Length; index++)
            {
                var preset = FindWeaponPreset(state.Weapons[index]);
                if (preset != null || string.IsNullOrEmpty(state.Weapons[index])) weapons[index] = preset;
                else resolved = false;
            }
            if (resolved) proxy.AppliedInventory = inventoryKey;
            proxy.AppliedWeapon = -2;
        }

        var currentWeapons = GetList(body, "weapons");
        if (state.WeaponSlot >= 0 && state.WeaponSlot < currentWeapons.Count &&
            currentWeapons[state.WeaponSlot] != null &&
            proxy.AppliedWeapon != state.WeaponSlot)
        {
            body.ChangeWeapon(state.WeaponSlot);
            proxy.AppliedWeapon = state.WeaponSlot;
            FreezeProxy(proxy);
        }
        else if (state.WeaponSlot < 0 && proxy.AppliedWeapon != -1)
        {
            body.ChangeToUnarmed();
            proxy.AppliedWeapon = -1;
            FreezeProxy(proxy);
        }
        if (body.weapon != null) body.weapon.ammo = state.WeaponAmmo;
    }

    private static WeaponPreset FindWeaponPreset(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (preset != null && preset.name == name) return preset;
        return null;
    }

    private static void SetTarget(NpcProxy proxy, Rigidbody2D body, Pose pose)
    {
        if (body == null) return;
        proxy.BodyTargets[body] = pose;
        var from = new Pose { Position = body.position, Rotation = body.rotation };
        if (!proxy.ReceivedFirstState)
        {
            body.position = pose.Position;
            body.rotation = pose.Rotation;
            from = pose;
        }
        proxy.BodyInterpolations[body] = new PoseInterpolation
        {
            From = from,
            StartedAt = Time.unscaledTime
        };
    }

    private static void SetTransformTargets(NpcProxy proxy, Transform[] transforms, TransformPose[] states)
    {
        for (var index = 0; index < states.Length && index < transforms.Length; index++)
            SetTransformTarget(proxy, transforms[index], states[index]);
    }

    private static void SetTransformTarget(NpcProxy proxy, Transform transform, TransformPose state)
    {
        if (transform == null) return;
        proxy.TransformTargets[transform] = state;
        var from = new TransformPose
        {
            Position = transform.position,
            Rotation = transform.eulerAngles.z
        };
        if (!proxy.ReceivedFirstState)
        {
            transform.position = state.Position;
            transform.rotation = Quaternion.Euler(0f, 0f, state.Rotation);
            from = state;
        }
        proxy.TransformInterpolations[transform] = new TransformInterpolation
        {
            From = from,
            StartedAt = Time.unscaledTime
        };
    }

    private void InterpolateClientNpcs()
    {
        foreach (var proxy in clientProxies)
        {
            if (proxy == null || proxy.Root == null || !proxy.Root.activeInHierarchy) continue;
            if (proxy.LocalPhysics)
            {
                if (Time.unscaledTime <= proxy.LocalPhysicsUntil) continue;
                proxy.LocalPhysics = false;
                FreezeProxy(proxy);
            }
            foreach (var pair in proxy.BodyTargets)
            {
                var body = pair.Key;
                if (body == null) continue;
                PoseInterpolation interpolation;
                if (!proxy.BodyInterpolations.TryGetValue(body, out interpolation)) continue;
                var amount = Mathf.Clamp01((Time.unscaledTime - interpolation.StartedAt) /
                    SnapshotInterval);
                body.position = Vector2.Lerp(interpolation.From.Position, pair.Value.Position, amount);
                body.rotation = Mathf.LerpAngle(interpolation.From.Rotation, pair.Value.Rotation, amount);
            }
            foreach (var pair in proxy.TransformTargets)
            {
                var transform = pair.Key;
                if (transform == null) continue;
                TransformInterpolation interpolation;
                if (!proxy.TransformInterpolations.TryGetValue(transform, out interpolation)) continue;
                var amount = Mathf.Clamp01((Time.unscaledTime - interpolation.StartedAt) /
                    SnapshotInterval);
                transform.position = Vector3.Lerp(interpolation.From.Position, pair.Value.Position, amount);
                transform.rotation = Quaternion.Lerp(Quaternion.Euler(0f, 0f, interpolation.From.Rotation),
                    Quaternion.Euler(0f, 0f, pair.Value.Rotation), amount);
            }
        }
    }

    private static void CacheFireVisuals(NpcProxy proxy)
    {
        var limbs = GetList(proxy.Body, "limbs");
        for (var index = 0; index < limbs.Count; index++)
        {
            var limb = limbs[index] as LimbScript;
            if (limb == null) continue;
            foreach (var fire in limb.GetComponentsInChildren<FireScript>(true))
            {
                if (fire.GetComponentInParent<LimbScript>() != limb) continue;
                proxy.FireVisuals[index] = fire.gameObject;
                proxy.OriginalFireActive[fire.gameObject] = fire.gameObject.activeSelf;
                break;
            }
        }
    }

    private static bool IsBurning(LimbScript limb)
    {
        foreach (var fire in limb.GetComponentsInChildren<FireScript>(true))
            if (fire != null && fire.gameObject.activeInHierarchy && fire.GetComponentInParent<LimbScript>() == limb)
                return true;
        return false;
    }

    private static void SetRemoteFire(NpcProxy proxy, int limbIndex, LimbScript limb, bool burning)
    {
        GameObject visual;
        proxy.FireVisuals.TryGetValue(limbIndex, out visual);
        if (!burning)
        {
            if (visual != null) visual.SetActive(false);
            return;
        }
        if (visual == null)
        {
            var prefab = Resources.Load<GameObject>("Spawnables/FireParticle");
            if (prefab == null) return;
            visual = Instantiate(prefab, limb.transform.position, Quaternion.identity);
            visual.name = "MP NPC Fire";
            visual.transform.SetParent(limb.transform, true);
            proxy.FireVisuals[limbIndex] = visual;
            proxy.OwnedFireVisuals.Add(visual);
            foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            foreach (var behaviour in visual.GetComponentsInChildren<Behaviour>(true))
                if (behaviour.GetType().Name == "AudioSource") behaviour.enabled = false;
        }
        visual.SetActive(true);
    }

    private static void CacheDismembermentVisuals(NpcProxy proxy)
    {
        foreach (var manager in proxy.Body.GetComponentsInChildren<DismemberManager>(true))
        {
            if (manager.dismemberJoint != null)
                foreach (var joint in manager.dismemberJoint)
                    if (joint != null && !proxy.OriginalJointStates.ContainsKey(joint))
                        proxy.OriginalJointStates.Add(joint, joint.enabled);
            if (manager.dismemberRender == null) continue;
            foreach (var renderer in manager.dismemberRender)
                if (renderer != null && !proxy.OriginalDismemberSprites.ContainsKey(renderer))
                    proxy.OriginalDismemberSprites.Add(renderer, renderer.sprite);
        }
    }

    private static void ApplyDismembermentVisuals(NpcProxy proxy)
    {
        foreach (var pair in proxy.OriginalDismemberSprites)
            if (pair.Key != null) pair.Key.sprite = pair.Value;
        foreach (var pair in proxy.OriginalJointStates)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var manager in proxy.Body.GetComponentsInChildren<DismemberManager>(true))
        {
            var triggered = false;
            if (manager.dismemberLimbs != null)
                foreach (var limb in manager.dismemberLimbs)
                    if (limb != null && limb.dismembered) { triggered = true; break; }
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

    private void ResetReplication(bool restoreClient)
    {
        if (restoreClient)
            foreach (var proxy in clientProxies) RestoreProxy(proxy);
        hostIds.Clear();
        hostNpcs.Clear();
        hostLayouts.Clear();
        clientNpcs.Clear();
        clientBodies.Clear();
        clientProxies.Clear();
        locallyPossessedNpcIds.Clear();
        remotelyPossessedNpcIds.Clear();
        possessionRenderGuardUntil = -1f;
        prefabRetryAt.Clear();
        receivedSequence = -1;
        sendSequence = 0;
        nextSnapshot = 0f;
        nextDiscovery = 0f;
    }

    private void WriteDiagnostics()
    {
        if (Time.unscaledTime < nextDiagnostics || string.IsNullOrEmpty(diagnosticPath)) return;
        nextDiagnostics = Time.unscaledTime + 1f;
        var line = DateTime.Now.ToString("HH:mm:ss.fff") +
            " role=" + (MultiplayerSession.IsHost ? "host" : "client") +
            " connected=" + MultiplayerSession.IsConnected +
            " hostNpcs=" + hostNpcs.Count +
            " clientNpcs=" + clientProxies.Count +
            " states=" + lastStateCount +
            " packetBytes=" + lastPacketBytes +
            " rebound=" + reboundExisting +
            " created=" + createdFromNetwork +
            " missingPrefabs=" + missingPrefabs +
            " applyFailures=" + applyFailures +
            " rigMisses=" + rigMisses +
            " lastError=" + lastApplyError + Environment.NewLine;
        try { File.AppendAllText(diagnosticPath, line); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void RestoreProxy(NpcProxy proxy)
    {
        if (proxy == null || proxy.Root == null) return;
        if (proxy.NetworkCreated)
        {
            Destroy(proxy.Root);
            return;
        }
        proxy.Root.SetActive(true);
        foreach (var pair in proxy.Behaviours)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var pair in proxy.OtherBehaviours)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var pair in proxy.RigidbodySettings)
        {
            if (pair.Key == null) continue;
            pair.Key.bodyType = pair.Value.BodyType;
            pair.Key.simulated = pair.Value.Simulated;
        }
        foreach (var pair in proxy.OriginalJointStates)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var pair in proxy.OriginalFireActive)
            if (pair.Key != null) pair.Key.SetActive(pair.Value);
        foreach (var visual in proxy.OwnedFireVisuals)
            if (visual != null) Destroy(visual);
        if (proxy.Marker != null) Destroy(proxy.Marker);
        proxy.Root.SetActive(proxy.OriginalRootActive);
    }

    private static void WritePose(BinaryWriter writer, Rigidbody2D body)
    {
        if (body == null) { writer.Write(0f); writer.Write(0f); writer.Write(0f); return; }
        writer.Write(body.position.x);
        writer.Write(body.position.y);
        writer.Write(body.rotation);
    }

    private static Rigidbody2D[] GetRigBodies(BodyScript body)
    {
        var result = new List<Rigidbody2D>();
        foreach (var rigBody in NpcRoot(body).GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (rigBody == null || rigBody.GetComponentInParent<WeaponScript>() != null ||
                rigBody.GetComponentInParent<DroppedWeapon>() != null) continue;
            result.Add(rigBody);
        }
        return result.ToArray();
    }

    private HostNpcLayout HostLayout(BodyScript body)
    {
        HostNpcLayout layout;
        if (hostLayouts.TryGetValue(body, out layout)) return layout;
        var rigBodies = GetRigBodies(body);
        var rigIds = new ulong[rigBodies.Length];
        for (var index = 0; index < rigBodies.Length; index++)
            rigIds[index] = RigId(NpcRoot(body).transform, rigBodies[index]);
        layout = new HostNpcLayout
        {
            DestroyOnDeath = GetList(body, "destroyOnDeath"),
            RigBodies = rigBodies,
            RigIds = rigIds,
            Limbs = GetList(body, "limbs"),
            TailBases = GetList(body, "tailBases"),
            Tails = GetTransforms(body, "tails"),
            GunTransform = GetTransform(body, "gunTransform"),
            GunAnimationTransform = GetTransform(body, "gunAnimTransform")
        };
        hostLayouts[body] = layout;
        return layout;
    }

    private static void CacheRigBodies(NpcProxy proxy)
    {
        proxy.RigBodies.Clear();
        foreach (var rigBody in GetRigBodies(proxy.Body))
            proxy.RigBodies[RigId(NpcRoot(proxy.Body).transform, rigBody)] = rigBody;
    }

    private static ulong RigId(Transform root, Rigidbody2D body)
    {
        var hierarchy = new List<Transform>();
        for (var current = body.transform; current != null && current != root; current = current.parent)
            hierarchy.Add(current);
        var key = new StringBuilder();
        for (var index = hierarchy.Count - 1; index >= 0; index--)
        {
            var current = hierarchy[index];
            key.Append('/').Append(current.name).Append('#').Append(current.GetSiblingIndex());
        }
        var components = body.GetComponents<Rigidbody2D>();
        for (var index = 0; index < components.Length; index++)
            if (components[index] == body) { key.Append(":rb#").Append(index); break; }

        unchecked
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var character in key.ToString())
            {
                hash ^= (byte)character;
                hash *= prime;
                hash ^= (byte)(character >> 8);
                hash *= prime;
            }
            return hash;
        }
    }

    private static Pose ReadPose(BinaryReader reader)
    {
        return new Pose
        {
            Position = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
            Rotation = reader.ReadSingle()
        };
    }

    private static void WriteTransforms(BinaryWriter writer, Transform[] transforms)
    {
        writer.Write((ushort)transforms.Length);
        foreach (var transform in transforms) WriteTransform(writer, transform);
    }

    private static TransformPose[] ReadTransforms(BinaryReader reader)
    {
        var count = reader.ReadUInt16();
        var states = new TransformPose[count];
        for (var index = 0; index < count; index++) states[index] = ReadTransform(reader);
        return states;
    }

    private static void WriteTransform(BinaryWriter writer, Transform transform)
    {
        if (transform == null) { writer.Write(0f); writer.Write(0f); writer.Write(0f); return; }
        writer.Write(transform.position.x);
        writer.Write(transform.position.y);
        writer.Write(transform.eulerAngles.z);
    }

    private static TransformPose ReadTransform(BinaryReader reader)
    {
        return new TransformPose
        {
            Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), 0f),
            Rotation = reader.ReadSingle()
        };
    }

    private static void WriteLine(BinaryWriter writer, LineRenderer line)
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
            var position = line.GetPosition(index);
            writer.Write(position.x);
            writer.Write(position.y);
            writer.Write(position.z);
        }
    }

    private static LineState ReadLine(BinaryReader reader)
    {
        var line = new LineState { Visible = reader.ReadBoolean() };
        if (!line.Visible) return line;
        var count = reader.ReadByte();
        line.UseWorldSpace = reader.ReadBoolean();
        line.StartColor = ReadColor(reader);
        line.EndColor = ReadColor(reader);
        line.StartWidth = reader.ReadSingle();
        line.EndWidth = reader.ReadSingle();
        line.Points = new Vector3[count];
        for (var index = 0; index < count; index++)
            line.Points[index] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return line;
    }

    private static void ApplyLine(LineState state, LineRenderer line, GameObject container)
    {
        if (!state.Visible)
        {
            if (line != null) line.enabled = false;
            if (container != null) container.SetActive(false);
            return;
        }
        if (line == null) return;
        if (container != null) container.SetActive(true);
        line.gameObject.SetActive(true);
        line.enabled = true;
        line.useWorldSpace = state.UseWorldSpace;
        line.startColor = state.StartColor;
        line.endColor = state.EndColor;
        line.startWidth = state.StartWidth;
        line.endWidth = state.EndWidth;
        line.positionCount = state.Points.Length;
        line.SetPositions(state.Points);
    }

    private static void WriteColor(BinaryWriter writer, Color color)
    {
        writer.Write(color.r); writer.Write(color.g); writer.Write(color.b); writer.Write(color.a);
    }

    private static Color ReadColor(BinaryReader reader)
    {
        return new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static IList GetList(BodyScript body, string name)
    {
        var field = typeof(BodyScript).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null) return new ArrayList();
        return field.GetValue(body) as IList ?? new ArrayList();
    }

    private static Transform[] GetTransforms(BodyScript body, string name)
    {
        var field = typeof(BodyScript).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field == null ? new Transform[0] : field.GetValue(body) as Transform[] ?? new Transform[0];
    }

    private static Transform GetTransform(BodyScript body, string name)
    {
        var field = typeof(BodyScript).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field == null ? null : (Transform)field.GetValue(body);
    }

    private static T GetFieldValue<T>(object instance, string name) where T : class
    {
        if (instance == null) return null;
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field == null ? null : field.GetValue(instance) as T;
    }

    private static string StableId(BodyScript body)
    {
        var path = new StringBuilder(body.gameObject.scene.name);
        var hierarchy = new List<Transform>();
        for (var current = body.transform; current != null; current = current.parent) hierarchy.Add(current);
        for (var index = hierarchy.Count - 1; index >= 0; index--)
        {
            var current = hierarchy[index];
            path.Append('/').Append(current.name).Append('#').Append(SameNameSiblingIndex(current));
        }
        var components = body.GetComponents<BodyScript>();
        for (var index = 0; index < components.Length; index++)
            if (components[index] == body) { path.Append(":body#").Append(index); break; }
        return path.ToString();
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

    private static string CleanCloneName(string name)
    {
        const string suffix = "(Clone)";
        if (name != null && name.EndsWith(suffix, StringComparison.Ordinal))
            return name.Substring(0, name.Length - suffix.Length).Trim();
        return name == null ? "" : name.Trim();
    }

    private sealed class HostNpcLayout
    {
        public IList DestroyOnDeath = new ArrayList();
        public Rigidbody2D[] RigBodies = new Rigidbody2D[0];
        public ulong[] RigIds = new ulong[0];
        public IList Limbs = new ArrayList();
        public IList TailBases = new ArrayList();
        public Transform[] Tails = new Transform[0];
        public Transform GunTransform;
        public Transform GunAnimationTransform;
    }

    private sealed class NpcProxy
    {
        public string NetworkId = "";
        public string RootName = "";
        public string SpeciesName = "";
        public BodyScript Body;
        public GameObject Root;
        public bool NetworkCreated;
        public bool OriginalRootActive;
        public bool NetworkVisible;
        public bool ReceivedFirstState;
        public float LastHostHealth;
        public bool LastHostAlive = true;
        public bool LocalPhysics;
        public float LocalPhysicsUntil;
        public int AppliedWeapon = -2;
        public string AppliedInventory = "\0";
        public NpcNetworkReplica Marker;
        public readonly Dictionary<MonoBehaviour, bool> Behaviours = new Dictionary<MonoBehaviour, bool>();
        public readonly Dictionary<Behaviour, bool> OtherBehaviours = new Dictionary<Behaviour, bool>();
        public readonly Dictionary<Rigidbody2D, RigidbodySettings> RigidbodySettings = new Dictionary<Rigidbody2D, RigidbodySettings>();
        public readonly Dictionary<Rigidbody2D, Pose> BodyTargets = new Dictionary<Rigidbody2D, Pose>();
        public readonly Dictionary<Rigidbody2D, PoseInterpolation> BodyInterpolations =
            new Dictionary<Rigidbody2D, PoseInterpolation>();
        public readonly Dictionary<ulong, Rigidbody2D> RigBodies = new Dictionary<ulong, Rigidbody2D>();
        public readonly Dictionary<Transform, TransformPose> TransformTargets = new Dictionary<Transform, TransformPose>();
        public readonly Dictionary<Transform, TransformInterpolation> TransformInterpolations =
            new Dictionary<Transform, TransformInterpolation>();
        public readonly Dictionary<int, GameObject> FireVisuals = new Dictionary<int, GameObject>();
        public readonly Dictionary<GameObject, bool> OriginalFireActive = new Dictionary<GameObject, bool>();
        public readonly HashSet<GameObject> OwnedFireVisuals = new HashSet<GameObject>();
        public readonly Dictionary<SpriteRenderer, Sprite> OriginalDismemberSprites = new Dictionary<SpriteRenderer, Sprite>();
        public readonly Dictionary<Joint2D, bool> OriginalJointStates = new Dictionary<Joint2D, bool>();
    }

    private sealed class NpcState
    {
        public string Id = "";
        public string RootName = "";
        public string CharacterName = "";
        public string SpeciesName = "";
        public bool Active;
        public bool IsRight;
        public int CurrentState;
        public int ControlState;
        public float Health;
        public float Stamina;
        public bool IsAlive;
        public bool Grounded;
        public bool IsInWater;
        public bool NoLegs;
        public bool DeHeaded;
        public float BurnIntensity;
        public bool[] DeathObjects = new bool[0];
        public Pose Body;
        public RigPose[] RigBodies = new RigPose[0];
        public LimbState[] Limbs = new LimbState[0];
        public Pose[] TailBases = new Pose[0];
        public TransformPose[] Tails = new TransformPose[0];
        public TransformPose Gun;
        public TransformPose GunAnimation;
        public TransformPose Weapon;
        public int WeaponSlot;
        public int WeaponAmmo;
        public string[] Weapons = new string[0];
        public LineState Laser;
    }

    private struct LimbState
    {
        public Pose Pose;
        public bool Dismembered;
        public bool Burning;
    }

    private struct RigPose
    {
        public ulong Id;
        public Pose Pose;
    }

    private struct Pose
    {
        public Vector2 Position;
        public float Rotation;
    }

    private struct PoseInterpolation
    {
        public Pose From;
        public float StartedAt;
    }

    private struct TransformInterpolation
    {
        public TransformPose From;
        public float StartedAt;
    }

    private struct TransformPose
    {
        public Vector3 Position;
        public float Rotation;

        public static TransformPose From(Pose pose)
        {
            return new TransformPose { Position = pose.Position, Rotation = pose.Rotation };
        }
    }

    private struct RigidbodySettings
    {
        public RigidbodyType2D BodyType;
        public bool Simulated;
    }

    private struct LineState
    {
        public bool Visible;
        public bool UseWorldSpace;
        public Color StartColor;
        public Color EndColor;
        public float StartWidth;
        public float EndWidth;
        public Vector3[] Points;
    }
}

internal sealed class NpcNetworkReplica : MonoBehaviour { }
