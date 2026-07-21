using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class NpcReplication : MonoBehaviour
{
    private const byte ProtocolVersion = 10;
    private const byte CompressedSnapshotMarker = 0xFF;
    private const int CompressionThresholdBytes = 256;
    private const int MaxDecompressedSnapshotBytes = 1024 * 1024;
    private const float RotationToWire = ushort.MaxValue / 360f;
    private const float RotationFromWire = 360f / ushort.MaxValue;

    private const float SnapshotInterval = 1f / 50f;
    private const float DiscoveryInterval = 1f;
    private const float FullSnapshotInterval = 1f;
    private const bool DiagnosticsEnabled = false;
    private static readonly MethodInfo LimbRenderCallback = typeof(LimbScript).GetMethod(
        "OnWillRenderObject", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Dictionary<string, FieldInfo> bodyFieldCache =
        new Dictionary<string, FieldInfo>();
    private readonly Dictionary<BodyScript, string> hostIds = new Dictionary<BodyScript, string>();
    private readonly Dictionary<string, BodyScript> hostNpcs = new Dictionary<string, BodyScript>();
    private readonly Dictionary<string, ulong> wireIds = new Dictionary<string, ulong>();
    private readonly Dictionary<ulong, string> idsByWire = new Dictionary<ulong, string>();
    private readonly Dictionary<BodyScript, HostNpcLayout> hostLayouts =
        new Dictionary<BodyScript, HostNpcLayout>();
    private readonly Dictionary<string, byte[]> lastSentStates = new Dictionary<string, byte[]>();
    private readonly Dictionary<string, MemoryStream> stateScratch = new Dictionary<string, MemoryStream>();
    private readonly Dictionary<string, float> lastChangedNpcAt = new Dictionary<string, float>();
    private readonly Dictionary<string, NpcProxy> clientNpcs = new Dictionary<string, NpcProxy>();
    private readonly Dictionary<string, NpcIdentity> clientIdentities = new Dictionary<string, NpcIdentity>();
    private readonly Dictionary<BodyScript, NpcProxy> clientBodies = new Dictionary<BodyScript, NpcProxy>();
    private readonly HashSet<NpcProxy> clientProxies = new HashSet<NpcProxy>();
    private readonly HashSet<string> locallyPossessedNpcIds = new HashSet<string>();
    private readonly HashSet<string> remotelyPossessedNpcIds = new HashSet<string>();
    private float possessionRenderGuardUntil;
    private readonly Dictionary<string, float> prefabRetryAt = new Dictionary<string, float>();
    private float nextSnapshot;
    private float nextDiscovery;
    private float nextFullSnapshot;
    private int sendSequence;
    private int receivedSequence = -1;
    private bool wasConnected;
    private bool wasHost;
    private string activeScene = "";
    private string diagnosticPath = "";
    private float nextDiagnostics;
    private int lastPacketBytes;
    private int lastStateCount;
    private int culledNpcCount;
    private float nextActivitySample;
    private int sentPacketsWindow;
    private int sentStatesWindow;
    private int receivedPacketsWindow;
    private int receivedStatesWindow;
    private int sentPacketsPerSecond;
    private int sentStatesPerSecond;
    private int receivedPacketsPerSecond;
    private int receivedStatesPerSecond;
    private int coreBytesWindow;
    private int rigBytesWindow;
    private int limbBytesWindow;
    private int tailBytesWindow;
    private int weaponBytesWindow;
    private int effectsBytesWindow;
    private int coreBytesPerSecond;
    private int rigBytesPerSecond;
    private int limbBytesPerSecond;
    private int tailBytesPerSecond;
    private int weaponBytesPerSecond;
    private int effectsBytesPerSecond;
    private int createdFromNetwork;
    private int missingPrefabs;
    private int reboundExisting;
    private int applyFailures;
    private int rigMisses;
    private string lastApplyError = "none";
    internal static NpcReplication Instance;
    internal static bool ApplyingAuthoritativeDeath;

    internal static bool IsEvaluatingAuthoritativePose { get; private set; }

    internal int TotalNpcCount
    {
        get { return MultiplayerSession.IsHost ? hostNpcs.Count : clientProxies.Count; }
    }

    internal int LastSnapshotNpcCount { get { return lastStateCount; } }
    internal int CulledNpcCount { get { return culledNpcCount; } }
    internal int SentPacketsPerSecond { get { return sentPacketsPerSecond; } }
    internal int SentStatesPerSecond { get { return sentStatesPerSecond; } }
    internal int ReceivedPacketsPerSecond { get { return receivedPacketsPerSecond; } }
    internal int ReceivedStatesPerSecond { get { return receivedStatesPerSecond; } }
    internal int CoreBytesPerSecond { get { return coreBytesPerSecond; } }
    internal int RigBytesPerSecond { get { return rigBytesPerSecond; } }
    internal int LimbBytesPerSecond { get { return limbBytesPerSecond; } }
    internal int TailBytesPerSecond { get { return tailBytesPerSecond; } }
    internal int WeaponBytesPerSecond { get { return weaponBytesPerSecond; } }
    internal int EffectsBytesPerSecond { get { return effectsBytesPerSecond; } }

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
        var performanceStarted = MultiplayerPerformance.Start();
        try
        {
        SampleActivity();
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
                AnimateHostNpcs();
                var snapshot = SerializeSnapshot();
                if (snapshot != null) MultiplayerSession.SendNpcSnapshot(snapshot);
            }
            return;
        }

        if (refreshDiscovery) RefreshClientNpcs(player.bodyScript);
        byte[] packet;
        byte[] latestPacket = null;
        while (MultiplayerSession.TryTakeNpcSnapshot(out packet)) latestPacket = packet;
        if (latestPacket != null) ApplySnapshot(latestPacket);
        }
        finally
        {
            MultiplayerPerformance.AddNpc(performanceStarted);
        }
    }

    private void LateUpdate()
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return;
        InterpolateClientNpcs();
        foreach (var proxy in clientProxies)
            if (proxy != null && proxy.Root != null && !proxy.NetworkVisible && proxy.Root.activeSelf)
                proxy.Root.SetActive(false);
    }

    internal void ApplyDistanceCulling()
    {
        if (!MultiplayerSession.IsHost) return;
        culledNpcCount = 0;
        foreach (var body in hostNpcs.Values)
        {
            MultiplayerLoadDistance.ApplyNpc(body);
            if (MultiplayerLoadDistance.IsNpcSimulationCulled(body)) culledNpcCount++;
        }
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
        if (packet.Length != sizeof(ulong)) return;
        var id = ResolveWireId(BitConverter.ToUInt64(packet, 0));
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

    private void AnimateHostNpcs()
    {
        if (LimbRenderCallback == null) return;
        foreach (var body in hostNpcs.Values)
        {
            if (body == null || !body.gameObject.activeInHierarchy || !MultiplayerLoadDistance.IsNpcNearAnyPlayer(body)) continue;
            var layout = HostLayout(body);
            IsEvaluatingAuthoritativePose = true;
            try
            {
                for (var index = 0; index < layout.Limbs.Count; index++)
                {
                    var limb = layout.Limbs[index] as LimbScript;
                    if (limb == null || !limb.gameObject.activeInHierarchy) continue;
                    var callback = index < layout.LimbRenderCallbacks.Length
                        ? layout.LimbRenderCallbacks[index] : null;
                    if (callback == null) continue;
                    try { callback(); }
                    catch (Exception) { }
                }
            }
            finally { IsEvaluatingAuthoritativePose = false; }
        }
    }

    internal static bool IsHostNpc(BodyScript body)
    {
        var instance = Instance;
        return body != null && MultiplayerSession.IsHost && instance != null &&
            instance.hostIds.ContainsKey(body);
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
        var fullSnapshot = Time.unscaledTime >= nextFullSnapshot;
        if (fullSnapshot) nextFullSnapshot = Time.unscaledTime + FullSnapshotInterval;
        var changed = new List<NpcSerializedState>();
        var liveIds = new HashSet<string>();
        foreach (var pair in hostNpcs)
        {
            if (pair.Value == null) continue;
            liveIds.Add(pair.Key);
            MemoryStream scratch;
            if (!stateScratch.TryGetValue(pair.Key, out scratch))
            {
                scratch = new MemoryStream(4096);
                stateScratch[pair.Key] = scratch;
            }
            scratch.SetLength(0);
            var stateBreakdown = new NpcWireBreakdown();
            using (var writer = new BinaryWriter(scratch, Encoding.UTF8, true))
                WriteState(writer, pair.Key, pair.Value, false, ref stateBreakdown);
            byte[] previous;
            var stateChanged = !lastSentStates.TryGetValue(pair.Key, out previous) || !StreamEquals(scratch, previous);
            if (fullSnapshot || stateChanged)
            {
                NpcSerializedState state;
                if (fullSnapshot)
                {
                    var fullBreakdown = new NpcWireBreakdown();
                    using (var fullState = new MemoryStream())
                    using (var writer = new BinaryWriter(fullState))
                    {
                        WriteState(writer, pair.Key, pair.Value, true, ref fullBreakdown);
                        state = new NpcSerializedState { Data = fullState.ToArray(), Breakdown = fullBreakdown };
                    }
                }
                else state = new NpcSerializedState { Data = scratch.ToArray(), Breakdown = stateBreakdown };
                changed.Add(state);
                if (stateChanged) lastChangedNpcAt[pair.Key] = Time.unscaledTime;
                if (stateChanged) lastSentStates[pair.Key] = fullSnapshot ? scratch.ToArray() : state.Data;
            }
        }
        if (fullSnapshot)
        {
            var stale = new List<string>();
            foreach (var id in lastSentStates.Keys) if (!liveIds.Contains(id)) stale.Add(id);
            foreach (var id in stale) lastSentStates.Remove(id);
            foreach (var id in stale) stateScratch.Remove(id);
        }
        if (!fullSnapshot && changed.Count == 0) return null;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(ProtocolVersion);
            writer.Write(++sendSequence);
            writer.Write(fullSnapshot);
            writer.Write((ushort)changed.Count);
            foreach (var state in changed)
            {
                writer.Write(state.Data);
                AddWireBreakdown(state.Breakdown);
            }
            var packet = CompressSnapshot(stream.ToArray());
            lastPacketBytes = packet.Length;
            lastStateCount = changed.Count;
            sentPacketsWindow++;
            sentStatesWindow += changed.Count;
            return packet;
        }
    }

    private static bool StreamEquals(MemoryStream stream, byte[] previous)
    {
        if (previous == null || stream.Length != previous.Length) return false;
        var buffer = stream.GetBuffer();
        for (var index = 0; index < previous.Length; index++) if (buffer[index] != previous[index]) return false;
        return true;
    }

    private static byte[] CompressSnapshot(byte[] packet)
    {
        if (packet == null || packet.Length < CompressionThresholdBytes) return packet;
        using (var stream = new MemoryStream(packet.Length))
        {
            stream.WriteByte(CompressedSnapshotMarker);
            using (var compressor = new DeflateStream(stream, CompressionLevel.Fastest, true))
                compressor.Write(packet, 0, packet.Length);
            var compressed = stream.ToArray();
            return compressed.Length < packet.Length ? compressed : packet;
        }
    }

    private static bool TryDecompressSnapshot(byte[] packet, out byte[] result)
    {
        result = packet;
        if (packet == null || packet.Length == 0) return false;
        if (packet[0] != CompressedSnapshotMarker) return true;
        try
        {
            using (var input = new MemoryStream(packet, 1, packet.Length - 1, false))
            using (var decompressor = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[4096];
                int read;
                while ((read = decompressor.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaxDecompressedSnapshotBytes) return false;
                    output.Write(buffer, 0, read);
                }
                result = output.ToArray();
                return result.Length > 0;
            }
        }
        catch (InvalidDataException) { return false; }
        catch (IOException) { return false; }
    }

    private void WriteState(BinaryWriter writer, string id, BodyScript body, bool includeIdentity,
        ref NpcWireBreakdown breakdown)
    {
            var layout = HostLayout(body);
            var sectionStarted = writer.BaseStream.Position;
            writer.Write(WireId(id));
                writer.Write(includeIdentity);
            if (includeIdentity)
            {
                writer.Write(CleanCloneName(layout.Root == null ? "" : layout.Root.name));
                writer.Write(body.characterName ?? "");
                writer.Write(body.speciesName ?? "");
            }
                breakdown.Core += (int)(writer.BaseStream.Position - sectionStarted);
                sectionStarted = writer.BaseStream.Position;
                writer.Write(layout.Root != null && layout.Root.activeSelf);
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
                WriteTransform(writer, body.Arms);
                breakdown.Core += (int)(writer.BaseStream.Position - sectionStarted);

                sectionStarted = writer.BaseStream.Position;
                var rigBodies = layout.RigBodies;
                writer.Write((ushort)rigBodies.Length);
                for (var rigIndex = 0; rigIndex < rigBodies.Length; rigIndex++)
                {
                    var rigBody = rigBodies[rigIndex];
                    writer.Write(layout.RigIds[rigIndex]);
                    WritePose(writer, rigBody);
                }
                breakdown.Rig += (int)(writer.BaseStream.Position - sectionStarted);

                sectionStarted = writer.BaseStream.Position;
                var limbs = layout.Limbs;
                writer.Write((ushort)limbs.Count);
                for (var limbIndex = 0; limbIndex < limbs.Count; limbIndex++)
                {
                    var limb = limbs[limbIndex] as LimbScript;
                    WritePose(writer, limb.rb);
                    WriteTransform(writer, limb == null ? null : limb.transform);
                    var fire = limbIndex < layout.LimbFires.Length ? layout.LimbFires[limbIndex] : null;
                    writer.Write((byte)((limb.dismembered ? 1 : 0) | (IsBurning(fire) ? 2 : 0)));
                }
                breakdown.Limbs += (int)(writer.BaseStream.Position - sectionStarted);

                sectionStarted = writer.BaseStream.Position;
                var tailBases = layout.TailBases;
                writer.Write((ushort)tailBases.Count);
                foreach (Rigidbody2D tailBase in tailBases) WritePose(writer, tailBase);
                WriteTransforms(writer, layout.Tails);
                breakdown.Tails += (int)(writer.BaseStream.Position - sectionStarted);

                sectionStarted = writer.BaseStream.Position;
                WriteTransform(writer, layout.GunTransform);
                WriteTransform(writer, layout.GunAnimationTransform);
                WriteTransform(writer, body.weapon == null ? null : body.weapon.transform);

                writer.Write(body.currentWeapon);
                writer.Write(body.weapon == null ? 0 : body.weapon.ammo);
                var weapons = layout.Weapons;
                writer.Write((ushort)weapons.Count);
                foreach (WeaponPreset preset in weapons)
                    writer.Write(NetworkWireId.FromString(preset == null ? "" : preset.name));
                breakdown.Weapons += (int)(writer.BaseStream.Position - sectionStarted);

                sectionStarted = writer.BaseStream.Position;
                WriteLine(writer, layout.WeaponLaserLine);
                WriteVisuals(writer, layout);
                breakdown.Effects += (int)(writer.BaseStream.Position - sectionStarted);
    }

    private void AddWireBreakdown(NpcWireBreakdown breakdown)
    {
        coreBytesWindow += breakdown.Core;
        rigBytesWindow += breakdown.Rig;
        limbBytesWindow += breakdown.Limbs;
        tailBytesWindow += breakdown.Tails;
        weaponBytesWindow += breakdown.Weapons;
        effectsBytesWindow += breakdown.Effects;
    }

    internal void DrawReplicationDebugOverlay(Camera camera, GUIStyle style, GUIStyle shadowStyle)
    {
        foreach (var pair in hostNpcs)
        {
            var body = pair.Value;
            if (body == null) continue;
            float changedAt;
            MultiplayerHud.DrawReplicationMarker(camera, body.transform.position,
                lastChangedNpcAt.TryGetValue(pair.Key, out changedAt) &&
                Time.unscaledTime - changedAt <= 1f, style, shadowStyle);
        }
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left == right) return true;
        if (left == null || right == null || left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
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
        if (wire == 0UL) return "";
        string id;
        if (idsByWire.TryGetValue(wire, out id)) return id;
        foreach (var known in hostNpcs.Keys)
            if (NetworkWireId.FromString(known) == wire)
            {
                wireIds[known] = wire;
                idsByWire[wire] = known;
                return known;
            }

        foreach (var known in clientNpcs.Keys)
            if (NetworkWireId.FromString(known) == wire)
            {
                wireIds[known] = wire;
                idsByWire[wire] = known;
                return known;
            }
        id = "net/" + wire.ToString("X16");
        wireIds[id] = wire;
        idsByWire[wire] = id;
        return id;
    }

    private void ApplySnapshot(byte[] packet)
    {
        try
        {
            byte[] decoded;
            if (!TryDecompressSnapshot(packet, out decoded)) return;
            using (var reader = new BinaryReader(new MemoryStream(decoded)))
            {
                if (reader.ReadByte() != ProtocolVersion) return;
                var sequence = reader.ReadInt32();
                if (sequence <= receivedSequence) return;
                var fullSnapshot = reader.ReadBoolean();
                var count = reader.ReadUInt16();
                var states = new List<NpcState>(count);
                for (var index = 0; index < count; index++) states.Add(ReadState(reader));
                receivedSequence = sequence;
                lastPacketBytes = packet.Length;
                lastStateCount = count;
                receivedPacketsWindow++;
                receivedStatesWindow += count;
                ApplyStates(states, fullSnapshot);
            }
        }
        catch (EndOfStreamException) { }
    }

    private NpcState ReadState(BinaryReader reader)
    {
        var id = ResolveWireId(reader.ReadUInt64());
        NpcIdentity identity;
        if (reader.ReadBoolean())
        {
            identity = new NpcIdentity
            {
                RootName = reader.ReadString(), CharacterName = reader.ReadString(), SpeciesName = reader.ReadString()
            };
            clientIdentities[id] = identity;
        }
        else clientIdentities.TryGetValue(id, out identity);
        var state = new NpcState
        {
            Id = id,
            RootName = identity == null ? "" : identity.RootName,
            CharacterName = identity == null ? "" : identity.CharacterName,
            SpeciesName = identity == null ? "" : identity.SpeciesName,
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
        state.Arms = ReadTransform(reader);
        var rigBodyCount = reader.ReadUInt16();
        state.RigBodies = new RigPose[rigBodyCount];
        for (var index = 0; index < rigBodyCount; index++)
            state.RigBodies[index] = new RigPose { Id = reader.ReadUInt64(), Pose = ReadPose(reader) };
        var limbCount = reader.ReadUInt16();
        state.Limbs = new LimbState[limbCount];
        for (var index = 0; index < limbCount; index++)
        {
            var pose = ReadPose(reader);
            var visual = ReadTransform(reader);
            var flags = reader.ReadByte();
            state.Limbs[index] = new LimbState
            {
                Pose = pose, Visual = visual, Dismembered = (flags & 1) != 0, Burning = (flags & 2) != 0
            };
        }
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
        state.Weapons = new ulong[weaponCount];
        for (var index = 0; index < weaponCount; index++) state.Weapons[index] = reader.ReadUInt64();
        state.Laser = ReadLine(reader);
        state.Visuals = ReadVisuals(reader);
        return state;
    }

    private void ApplyStates(List<NpcState> states, bool fullSnapshot)
    {
        if (fullSnapshot)
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

        if (fullSnapshot)
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
        proxy.SpriteRenderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        proxy.Particles = root.GetComponentsInChildren<ParticleSystem>(true);
        proxy.Lights = GetLights(root);
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
        SetTransformTarget(proxy, body.Arms, state.Arms);
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
            if (limb.rb != null)
                SetTransformTarget(proxy, limb.rb.transform, TransformPose.From(state.Limbs[index].Pose));

            SetTransformTarget(proxy, limb.transform, state.Limbs[index].Visual);
            limb.dismembered = state.Limbs[index].Dismembered;
            SetRemoteFire(proxy, index, limb, state.Limbs[index].Burning);
        }

        var tailBases = GetList(body, "tailBases");
        for (var index = 0; index < state.TailBases.Length && index < tailBases.Count; index++)
        {
            var tailBase = tailBases[index] as Rigidbody2D;
            if (tailBase != null)
            {
                SetTarget(proxy, tailBase, state.TailBases[index]);
                SetTransformTarget(proxy, tailBase.transform, TransformPose.From(state.TailBases[index]));
            }
        }
        SetTransformTargets(proxy, GetTransforms(body, "tails"), state.Tails);
        SetTransformTarget(proxy, GetTransform(body, "gunTransform"), state.Gun);
        SetTransformTarget(proxy, GetTransform(body, "gunAnimTransform"), state.GunAnimation);
        ApplyWeapon(proxy, state);
        SetTransformTarget(proxy, body.weapon == null ? null : body.weapon.transform, state.Weapon);
        ApplyLine(state.Laser, GetFieldValue<LineRenderer>(body, "wepLaserLine"),
            GetFieldValue<GameObject>(body, "wepLaser"));
        ApplyVisuals(proxy, state.Visuals);
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

        if (!MultiplayerSession.IsHost) MultiplayerSession.SendNpcPossession(current.WireId(id));
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
        if (body == null) return;
        if (!Instance.clientBodies.TryGetValue(body, out proxy) || proxy == null) return;
        if (!proxy.ReceivedFirstState || proxy.LastHostAlive) return;
        ulong rigId = 0;
        var found = false;
        foreach (var pair in proxy.RigBodies)
            if (pair.Value == rigidbody) { rigId = pair.Key; found = true; break; }

        if (!found) rigId = RigId(NpcRoot(proxy.Body).transform, rigidbody);

        EnableLocalCorpsePhysics(proxy);
        proxy.LocalPhysicsUntil = Time.unscaledTime + 0.15f;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Instance.WireId(proxy.NetworkId));
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
            writer.Write(WireId(id));
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
                var id = ResolveWireId(reader.ReadUInt64());
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
                var id = ResolveWireId(reader.ReadUInt64());
                var rigId = reader.ReadUInt64();
                var point = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                var localPoint = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) ||
                    float.IsInfinity(point.y) || float.IsNaN(localPoint.x) || float.IsNaN(localPoint.y) ||
                    float.IsInfinity(localPoint.x) || float.IsInfinity(localPoint.y)) return;
                BodyScript body;
                var remotePlayer = NetworkAvatarReplication.RemoteBodyForPeer(peerId);
                if (!hostNpcs.TryGetValue(id, out body) || body == null || body.isAlive ||
                    remotePlayer == null || !remotePlayer.isAlive)
                    return;
                Rigidbody2D target = null;
                foreach (var candidate in NpcRoot(body).GetComponentsInChildren<Rigidbody2D>(true))
                    if (RigId(NpcRoot(body).transform, candidate) == rigId) { target = candidate; break; }
                if (target == null)
                    return;
                target.simulated = true;
                target.bodyType = RigidbodyType2D.Dynamic;
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
                if (preset != null || state.Weapons[index] == 0UL) weapons[index] = preset;
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

    private static WeaponPreset FindWeaponPreset(ulong weaponId)
    {
        if (weaponId == 0UL) return null;
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (preset != null && NetworkWireId.FromString(preset.name) == weaponId) return preset;
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

    private static bool IsBurning(FireScript fire)
    {
        return fire != null && fire.gameObject.activeInHierarchy;
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
        nextFullSnapshot = 0f;
        nextDiscovery = 0f;
        lastSentStates.Clear();
        stateScratch.Clear();
        lastChangedNpcAt.Clear();
        nextActivitySample = 0f;
        sentPacketsWindow = sentStatesWindow = receivedPacketsWindow = receivedStatesWindow = 0;
        sentPacketsPerSecond = sentStatesPerSecond = receivedPacketsPerSecond = receivedStatesPerSecond = 0;
        coreBytesWindow = rigBytesWindow = limbBytesWindow = tailBytesWindow = weaponBytesWindow = effectsBytesWindow = 0;
        coreBytesPerSecond = rigBytesPerSecond = limbBytesPerSecond = tailBytesPerSecond = weaponBytesPerSecond = effectsBytesPerSecond = 0;
    }

    private void SampleActivity()
    {
        if (Time.unscaledTime < nextActivitySample) return;
        nextActivitySample = Time.unscaledTime + 1f;
        sentPacketsPerSecond = sentPacketsWindow;
        sentStatesPerSecond = sentStatesWindow;
        receivedPacketsPerSecond = receivedPacketsWindow;
        receivedStatesPerSecond = receivedStatesWindow;
        coreBytesPerSecond = coreBytesWindow;
        rigBytesPerSecond = rigBytesWindow;
        limbBytesPerSecond = limbBytesWindow;
        tailBytesPerSecond = tailBytesWindow;
        weaponBytesPerSecond = weaponBytesWindow;
        effectsBytesPerSecond = effectsBytesWindow;
        sentPacketsWindow = sentStatesWindow = receivedPacketsWindow = receivedStatesWindow = 0;
        coreBytesWindow = rigBytesWindow = limbBytesWindow = tailBytesWindow = weaponBytesWindow = effectsBytesWindow = 0;
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
        if (body == null) { writer.Write(0f); writer.Write(0f); WriteRotation(writer, 0f); return; }
        writer.Write(body.position.x);
        writer.Write(body.position.y);
        WriteRotation(writer, body.rotation);
    }

    private static Rigidbody2D[] GetRigBodies(BodyScript body)
    {
        var result = new List<Rigidbody2D>();
        var dedicatedBodies = new HashSet<Rigidbody2D>();
        if (body.rb != null) dedicatedBodies.Add(body.rb);
        foreach (LimbScript limb in GetList(body, "limbs"))
            if (limb != null && limb.rb != null) dedicatedBodies.Add(limb.rb);
        foreach (Rigidbody2D tailBase in GetList(body, "tailBases"))
            if (tailBase != null) dedicatedBodies.Add(tailBase);
        foreach (var rigBody in NpcRoot(body).GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (rigBody == null || rigBody.GetComponentInParent<WeaponScript>() != null ||
                rigBody.GetComponentInParent<DroppedWeapon>() != null || dedicatedBodies.Contains(rigBody)) continue;
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
        var limbs = GetList(body, "limbs");
        var limbRenderCallbacks = new Action[limbs.Count];
        var limbFires = new FireScript[limbs.Count];
        for (var index = 0; index < limbs.Count; index++)
        {
            var limb = limbs[index] as LimbScript;
            if (limb == null || LimbRenderCallback == null) continue;
            try { limbRenderCallbacks[index] = (Action)LimbRenderCallback.CreateDelegate(typeof(Action), limb); }
            catch (ArgumentException) { }
            limbFires[index] = FindLimbFire(limb);
        }
        layout = new HostNpcLayout
        {
            Root = NpcRoot(body),
            DestroyOnDeath = GetList(body, "destroyOnDeath"),
            RigBodies = rigBodies,
            RigIds = rigIds,
            Limbs = limbs,
            LimbRenderCallbacks = limbRenderCallbacks,
            LimbFires = limbFires,
            TailBases = GetList(body, "tailBases"),
            Tails = GetTransforms(body, "tails"),
            GunTransform = GetTransform(body, "gunTransform"),
            GunAnimationTransform = GetTransform(body, "gunAnimTransform"),
            Weapons = GetList(body, "weapons"),
            WeaponLaserLine = GetFieldValue<LineRenderer>(body, "wepLaserLine"),
            SpriteRenderers = NpcRoot(body).GetComponentsInChildren<SpriteRenderer>(true),
            Particles = NpcRoot(body).GetComponentsInChildren<ParticleSystem>(true),
            Lights = GetLights(NpcRoot(body))
        };
        hostLayouts[body] = layout;
        return layout;
    }

    private static FireScript FindLimbFire(LimbScript limb)
    {
        if (limb == null) return null;
        foreach (var fire in limb.GetComponentsInChildren<FireScript>(true))
            if (fire != null && fire.GetComponentInParent<LimbScript>() == limb) return fire;
        return null;
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
            Rotation = ReadRotation(reader)
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
        if (transform == null) { writer.Write(0f); writer.Write(0f); WriteRotation(writer, 0f); return; }
        writer.Write(transform.position.x);
        writer.Write(transform.position.y);
        WriteRotation(writer, transform.eulerAngles.z);
    }

    private static TransformPose ReadTransform(BinaryReader reader)
    {
        return new TransformPose
        {
            Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), 0f),
            Rotation = ReadRotation(reader)
        };
    }

    private static void WriteRotation(BinaryWriter writer, float rotation)
    {
        writer.Write((ushort)Mathf.RoundToInt(Mathf.Repeat(rotation, 360f) * RotationToWire));
    }

    private static float ReadRotation(BinaryReader reader)
    {
        return reader.ReadUInt16() * RotationFromWire;
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

    private static void WriteVisuals(BinaryWriter writer, HostNpcLayout layout)
    {
        writer.Write((ushort)layout.SpriteRenderers.Length);
        foreach (var renderer in layout.SpriteRenderers)
        {
            var flags = renderer != null && renderer.gameObject.activeSelf ? 1 : 0;
            if (renderer != null && renderer.enabled) flags |= 2;
            writer.Write((byte)flags);
            WriteColor32(writer, renderer == null ? Color.clear : renderer.color);
        }
        writer.Write((ushort)layout.Particles.Length);
        foreach (var particle in layout.Particles)
        {
            var flags = particle != null && particle.gameObject.activeSelf ? 1 : 0;
            if (particle != null && particle.isPlaying) flags |= 2;
            writer.Write((byte)flags);
        }
        writer.Write((ushort)layout.Lights.Length);
        foreach (var light in layout.Lights)
        {
            var flags = light != null && light.gameObject.activeSelf ? 1 : 0;
            if (light != null && light.enabled) flags |= 2;
            writer.Write((byte)flags);
            writer.Write(GetFloatMember(light, "intensity", 0f));
            WriteColor32(writer, GetColorMember(light, "color", Color.white));
        }
    }

    private static NpcVisualState ReadVisuals(BinaryReader reader)
    {
        var state = new NpcVisualState();
        var rendererCount = reader.ReadUInt16();
        state.Renderers = new RendererVisualState[rendererCount];
        for (var index = 0; index < rendererCount; index++)
        {
            var flags = reader.ReadByte();
            state.Renderers[index] = new RendererVisualState
            {
                Active = (flags & 1) != 0,
                Enabled = (flags & 2) != 0,
                Color = ReadColor32(reader)
            };
        }
        var particleCount = reader.ReadUInt16();
        state.Particles = new ParticleVisualState[particleCount];
        for (var index = 0; index < particleCount; index++)
        {
            var flags = reader.ReadByte();
            state.Particles[index] = new ParticleVisualState
            {
                Active = (flags & 1) != 0,
                Playing = (flags & 2) != 0
            };
        }
        var lightCount = reader.ReadUInt16();
        state.Lights = new LightVisualState[lightCount];
        for (var index = 0; index < lightCount; index++)
        {
            var flags = reader.ReadByte();
            state.Lights[index] = new LightVisualState
            {
                Active = (flags & 1) != 0,
                Enabled = (flags & 2) != 0,
                Intensity = reader.ReadSingle(),
                Color = ReadColor32(reader)
            };
        }
        return state;
    }

    private static void ApplyVisuals(NpcProxy proxy, NpcVisualState state)
    {
        if (proxy == null) return;
        for (var index = 0; index < state.Renderers.Length && index < proxy.SpriteRenderers.Length; index++)
        {
            var renderer = proxy.SpriteRenderers[index];
            if (renderer == null) continue;

            if (!IsCoreNpcRenderer(renderer))
            {
                renderer.gameObject.SetActive(state.Renderers[index].Active);
                renderer.enabled = state.Renderers[index].Enabled;
            }
            renderer.color = state.Renderers[index].Color;
        }
        for (var index = 0; index < state.Particles.Length && index < proxy.Particles.Length; index++)
        {
            var particle = proxy.Particles[index];
            if (particle == null) continue;
            particle.gameObject.SetActive(state.Particles[index].Active);
            if (!state.Particles[index].Active) continue;
            if (state.Particles[index].Playing)
            {
                if (!particle.isPlaying) particle.Play(true);
            }
            else if (particle.isPlaying)
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        for (var index = 0; index < state.Lights.Length && index < proxy.Lights.Length; index++)
        {
            var light = proxy.Lights[index];
            if (light == null) continue;
            light.gameObject.SetActive(state.Lights[index].Active);
            light.enabled = state.Lights[index].Enabled;
            SetFloatMember(light, "intensity", state.Lights[index].Intensity);
            SetColorMember(light, "color", state.Lights[index].Color);
        }
    }

    private static Behaviour[] GetLights(GameObject root)
    {
        if (root == null) return new Behaviour[0];
        var lights = new List<Behaviour>();
        foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
            if (behaviour != null && behaviour.GetType().Name == "Light2D") lights.Add(behaviour);
        return lights.ToArray();
    }

    private static bool IsCoreNpcRenderer(SpriteRenderer renderer)
    {
        return renderer.GetComponentInParent<LimbScript>() != null ||
               renderer.GetComponentInParent<WeaponScript>() != null;
    }

    private static float GetFloatMember(object instance, string name, float fallback)
    {
        if (instance == null) return fallback;
        var type = instance.GetType();
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.PropertyType == typeof(float)) return (float)property.GetValue(instance, null);
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null && field.FieldType == typeof(float) ? (float)field.GetValue(instance) : fallback;
    }

    private static Color GetColorMember(object instance, string name, Color fallback)
    {
        if (instance == null) return fallback;
        var type = instance.GetType();
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.PropertyType == typeof(Color)) return (Color)property.GetValue(instance, null);
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null && field.FieldType == typeof(Color) ? (Color)field.GetValue(instance) : fallback;
    }

    private static void SetFloatMember(object instance, string name, float value)
    {
        if (instance == null) return;
        var type = instance.GetType();
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.PropertyType == typeof(float) && property.CanWrite)
        {
            property.SetValue(instance, value, null);
            return;
        }
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(float)) field.SetValue(instance, value);
    }

    private static void SetColorMember(object instance, string name, Color value)
    {
        if (instance == null) return;
        var type = instance.GetType();
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.PropertyType == typeof(Color) && property.CanWrite)
        {
            property.SetValue(instance, value, null);
            return;
        }
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(Color)) field.SetValue(instance, value);
    }

    private static void WriteColor32(BinaryWriter writer, Color color)
    {
        var value = (Color32)color;
        writer.Write(value.r); writer.Write(value.g); writer.Write(value.b); writer.Write(value.a);
    }

    private static Color ReadColor32(BinaryReader reader)
    {
        return new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
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
        var field = GetBodyField(name);
        if (field == null) return new ArrayList();
        return field.GetValue(body) as IList ?? new ArrayList();
    }

    private static Transform[] GetTransforms(BodyScript body, string name)
    {
        var field = GetBodyField(name);
        return field == null ? new Transform[0] : field.GetValue(body) as Transform[] ?? new Transform[0];
    }

    private static Transform GetTransform(BodyScript body, string name)
    {
        var field = GetBodyField(name);
        return field == null ? null : (Transform)field.GetValue(body);
    }

    private static T GetFieldValue<T>(object instance, string name) where T : class
    {
        if (instance == null) return null;
        var field = instance is BodyScript
            ? GetBodyField(name)
            : instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field == null ? null : field.GetValue(instance) as T;
    }

    private static FieldInfo GetBodyField(string name)
    {
        FieldInfo field;
        if (bodyFieldCache.TryGetValue(name, out field)) return field;
        field = typeof(BodyScript).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        bodyFieldCache[name] = field;
        return field;
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
        public GameObject Root;
        public IList DestroyOnDeath = new ArrayList();
        public Rigidbody2D[] RigBodies = new Rigidbody2D[0];
        public ulong[] RigIds = new ulong[0];
        public IList Limbs = new ArrayList();
        public Action[] LimbRenderCallbacks = new Action[0];
        public FireScript[] LimbFires = new FireScript[0];
        public IList TailBases = new ArrayList();
        public Transform[] Tails = new Transform[0];
        public Transform GunTransform;
        public Transform GunAnimationTransform;
        public IList Weapons = new ArrayList();
        public LineRenderer WeaponLaserLine;
        public SpriteRenderer[] SpriteRenderers = new SpriteRenderer[0];
        public ParticleSystem[] Particles = new ParticleSystem[0];
        public Behaviour[] Lights = new Behaviour[0];
    }

    private struct NpcSerializedState
    {
        public byte[] Data;
        public NpcWireBreakdown Breakdown;
    }

    private struct NpcWireBreakdown
    {
        public int Core;
        public int Rig;
        public int Limbs;
        public int Tails;
        public int Weapons;
        public int Effects;
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
        public SpriteRenderer[] SpriteRenderers = new SpriteRenderer[0];
        public ParticleSystem[] Particles = new ParticleSystem[0];
        public Behaviour[] Lights = new Behaviour[0];
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
        public TransformPose Arms;
        public RigPose[] RigBodies = new RigPose[0];
        public LimbState[] Limbs = new LimbState[0];
        public Pose[] TailBases = new Pose[0];
        public TransformPose[] Tails = new TransformPose[0];
        public TransformPose Gun;
        public TransformPose GunAnimation;
        public TransformPose Weapon;
        public int WeaponSlot;
        public int WeaponAmmo;
        public ulong[] Weapons = new ulong[0];
        public LineState Laser;
        public NpcVisualState Visuals = new NpcVisualState();
    }

    private sealed class NpcIdentity
    {
        public string RootName = "";
        public string CharacterName = "";
        public string SpeciesName = "";
    }

    private struct LimbState
    {
        public Pose Pose;
        public TransformPose Visual;
        public bool Dismembered;
        public bool Burning;
    }

    private struct RigPose
    {
        public ulong Id;
        public Pose Pose;
    }

    private sealed class NpcVisualState
    {
        public RendererVisualState[] Renderers = new RendererVisualState[0];
        public ParticleVisualState[] Particles = new ParticleVisualState[0];
        public LightVisualState[] Lights = new LightVisualState[0];
    }

    private struct RendererVisualState
    {
        public bool Active;
        public bool Enabled;
        public Color Color;
    }

    private struct ParticleVisualState
    {
        public bool Active;
        public bool Playing;
    }

    private struct LightVisualState
    {
        public bool Active;
        public bool Enabled;
        public float Intensity;
        public Color Color;
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
