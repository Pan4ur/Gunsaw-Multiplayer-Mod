using UnityEngine;

internal sealed class WorldDoorReplication
{
    private readonly Dictionary<string, DoorScript> replicatedDoors = new();
    private readonly Dictionary<DoorScript, string> replicatedDoorIds = new();
    private readonly Dictionary<string, bool> hostDoorTargets = new();
    private readonly Dictionary<string, bool> hostDoorMoving = new();
    private readonly Dictionary<string, uint> hostDoorRevisions = new();
    private readonly Dictionary<string, uint> clientDoorRevisions = new();
    private readonly Dictionary<string, bool> clientDoorTargets = new();
    private readonly Dictionary<string, bool> clientDoorMoving = new();
    private readonly Dictionary<string, Vector2> clientDoorTargetPositions = new();
    private float nextDoorSnapshotRequest;
    private readonly ContactPoint2D[] playerContacts = new ContactPoint2D[16];
    private BodyScript referencePlayer;
    private Transform playerReference;
    private float referenceContactAt;

    internal void Update()
    {
        if (MultiplayerSession.IsHost) BroadcastChangedDoorStates();
        ProcessDoorStatePackets();
        if (!MultiplayerSession.IsHost && Time.unscaledTime >= nextDoorSnapshotRequest && !MultiplayerSession.HasPendingReliablePacket(MultiplayerSession.HostPeerId, PacketType.DoorState))
        {
            var sent = MultiplayerSession.Send(DoorStatePacket.RequestSnapshot(MultiplayerSession.SnapshotEpoch), MultiplayerSession.HostPeerId);
            nextDoorSnapshotRequest = Time.unscaledTime + (sent ? 5f : 1f);
        }
    }

    internal void Refresh()
    {
        foreach (var door in WorldReplication.FindObjectsOfType<DoorScript>())
        {
            if (door == null || WorldReplication.IsGameplayOwned(door)) continue;
            if (!replicatedDoorIds.TryGetValue(door, out var id))
            {
                id = WorldReplication.Instance.ComponentId(door);
                replicatedDoorIds[door] = id;
                replicatedDoors[id] = door;
                WorldReplication.Instance.WireId(id);
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
        while (MultiplayerSession.TryTakeDoorState(out var peerId, out var packet))
        {
            if (!MultiplayerSession.IsSnapshotEpochCurrent(packet.SceneEpoch))
                continue;
           
            if (MultiplayerSession.IsHost)
            {
                if (packet.OpCode == DoorStateOpCode.RequestSnapshot)
                    SendDoorStateSnapshot(peerId);
                
                continue;
            }
            
            if (packet.OpCode != DoorStateOpCode.States)
                continue;
            
            foreach (var state in packet.States)
                ApplyDoorState(state, packet.IncludesPositions);
        }
    }

    private void BroadcastChangedDoorStates()
    {
        var changed = new List<DoorStateEntry>();
        foreach (var pair in replicatedDoors)
        {
            var door = pair.Value;
            if (door == null) continue;
            var moving = IsDoorMoving(door);
            hostDoorMoving.TryGetValue(pair.Key, out var wasMoving);
            
            if (hostDoorTargets.TryGetValue(pair.Key, out var previous) && previous == door.followingFirstPoint && wasMoving == moving)
                continue;
            
            hostDoorTargets[pair.Key] = door.followingFirstPoint;
            hostDoorMoving[pair.Key] = moving;
            hostDoorRevisions.TryGetValue(pair.Key, out var revision);
            revision++;
            hostDoorRevisions[pair.Key] = revision;
            var target = DoorTarget(door);
            changed.Add(new DoorStateEntry(WorldReplication.Instance.WireId(pair.Key), revision, door.followingFirstPoint, moving, target.x, target.y));
        }
        
        if (changed.Count > 0)
            MultiplayerSession.Send(DoorStatePacket.StatesUpdate(MultiplayerSession.SnapshotEpoch, false, [.. changed]));
    }

    private void SendDoorStateSnapshot(ushort peerId)
    {
        var states = new List<DoorStateEntry>(replicatedDoors.Count);
        foreach (var pair in replicatedDoors)
        {
            var door = pair.Value;
            if (door == null)
                continue;
            
            hostDoorRevisions.TryGetValue(pair.Key, out var revision);
            revision++;
            hostDoorRevisions[pair.Key] = revision;
            var position = door.transform.position;
            var target = DoorTarget(door);
            states.Add(new DoorStateEntry(WorldReplication.Instance.WireId(pair.Key), revision, door.followingFirstPoint, IsDoorMoving(door), target.x, target.y, position.x, position.y, door.transform.eulerAngles.z));
        }
        MultiplayerSession.Send(DoorStatePacket.StatesUpdate(MultiplayerSession.SnapshotEpoch, true, [.. states]), peerId);
    }

    private void ApplyDoorState(DoorStateEntry state, bool includesPosition)
    {
        var id = WorldReplication.Instance.ResolveWireId(state.Id);
        var hadState = clientDoorRevisions.TryGetValue(id, out var previousRevision);
        if (hadState && state.Revision != previousRevision && unchecked(state.Revision - previousRevision) >= 0x80000000u)
            return;
        
        if (!replicatedDoors.TryGetValue(id, out var door) || door == null)
            return;
        
        clientDoorRevisions[id] = state.Revision;
        clientDoorTargets[id] = state.FollowingFirstPoint;
        clientDoorMoving.TryGetValue(id, out var wasMoving);
        clientDoorMoving[id] = state.IsMoving;
        clientDoorTargetPositions[id] = new Vector2(state.TargetX, state.TargetY);
        door.followingFirstPoint = state.FollowingFirstPoint;
        var source = door.GetComponent<AudioSource>();
        
        if (includesPosition && source != null)
        {
            if (state.IsMoving)
            {
                if (!source.isPlaying && source.clip != null)
                    source.Play();
            }
            else 
                source.Stop();
        }
        
        if (!includesPosition && wasMoving != state.IsMoving)
        {
            if (state.IsMoving)
                door.StartMoving();
            else
            {
                source.Let(it => it.Stop());
                door.endSound.Let(it => Sound.Play(it, door.transform.position, false, false, door.transform));
            }
        }
        
        if (!includesPosition || (hadState && state.IsMoving))
            return;
        
        var body = door.GetComponent<Rigidbody2D>();
        if (body == null)
            return;
        
        body.position = new Vector2(state.PositionX, state.PositionY);
        body.rotation = state.Rotation;
        body.velocity = Vector2.zero;
        body.angularVelocity = 0f;
    }

    internal void FixedTick()
    {
        if (MultiplayerSession.IsHost) return;
        foreach (var pair in clientDoorTargets)
        {
            if (!replicatedDoors.TryGetValue(pair.Key, out var door) || door == null)
                continue;
            
            var body = door.GetComponent<Rigidbody2D>();
            if (!clientDoorTargetPositions.TryGetValue(pair.Key, out var target) || body == null || door.speed <= 0f)
                continue;
            
            var next = Vector2.MoveTowards(body.position, target, door.speed * Time.fixedDeltaTime);
            body.MovePosition(next);
         
            if ((next - target).sqrMagnitude <= 0.0001f)
            {
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                door.GetComponent<AudioSource>().Let(it => it.Stop());
            }
        }
    }

    private static bool IsDoorMoving(DoorScript door)
    {
        if (door == null)
            return false;
        
        var target = DoorTarget(door);
        return Vector2.Distance(door.transform.position, target) >= door.speed * 0.05f;
    }

    private static Vector2 DoorTarget(DoorScript door)
    {
        var target = door != null && door.followingFirstPoint ? door.point1 : door == null ? null : door.point2;
        return target == null ? Vector2.zero : target.position;
    }

    internal void TryGetPlayerSupport(BodyScript body, out ulong id, out Vector2 offset, out bool grounded)
    {
        id = 0;
        offset = Vector2.zero;
        grounded = false;
        
        if (referencePlayer != body)
        {
            referencePlayer = body;
            playerReference = null;
        }
        
        if (body == null || body.inVehicle)
        {
            playerReference = null;
            return;
        }
        
        var support = PlayerSupport(body, body.coll, ref grounded);
        if (support == null && (body.coll == null || !body.coll.enabled) && body.limbs != null)
        {
            foreach (var limb in body.limbs)
            {
                if (limb == null || limb.dismembered) continue;
                support = PlayerSupport(body, limb.GetComponent<Collider2D>(), ref grounded);
                if (support != null) break;
            }
        }

        if (grounded)
        {
            playerReference = support;
            referenceContactAt = Time.unscaledTime;
        }
        
        if (playerReference == null || Time.unscaledTime - referenceContactAt > 0.5f)
            return;
        
        var door = playerReference.GetComponent<DoorScript>();
        if (door != null && replicatedDoorIds.TryGetValue(door, out var doorId))
            id = WorldReplication.Instance.WireId(doorId);
        else
        {
            var part = playerReference.GetComponent<VehiclePart>();
            var vehicle = part == null ? null : part.vehicle ?? part.GetComponentInParent<VehicleBase>();
            if (vehicle == null) return;
            id = WorldReplication.Instance.VehicleWireId(vehicle);
        }
        offset = (Vector2)body.transform.position - (Vector2)playerReference.position;
    }

    private Transform PlayerSupport(BodyScript body, Collider2D collider, ref bool grounded)
    {
        if (collider == null || !collider.enabled) return null;
        var count = collider.GetContacts(playerContacts);
        for (var index = 0; index < count; index++)
        {
            var contact = playerContacts[index];
            var other = contact.collider == collider ? contact.otherCollider : contact.collider;
            if (other == null || other.transform.root == body.transform.root || Mathf.Abs(contact.normal.y) < 0.5f ||
                contact.point.y > collider.bounds.center.y) continue;
            grounded = true;
            var support = other.attachedRigidbody;
            var door = support == null ? null : support.GetComponentInParent<DoorScript>();
            if (door != null && replicatedDoorIds.ContainsKey(door)) return door.transform;
            var part = support == null ? null : support.GetComponent<VehiclePart>();
            var vehicle = part == null ? null : part.vehicle ?? part.GetComponentInParent<VehicleBase>();
            if (vehicle != null && vehicle.mainPart != null && !WorldBodyReplication.IsDetachedVehiclePart(support))
                return vehicle.mainPart.transform;
        }
        return null;
    }

    internal Transform SupportTransform(ulong id)
    {
        var key = WorldReplication.Instance.ResolveWireId(id);
        if (replicatedDoors.TryGetValue(key, out var door) && door != null) return door.transform;
        return WorldReplication.Instance.bodies.bodies.TryGetValue(key, out var body) && body != null &&
            body.GetComponent<VehiclePart>() != null ? body.transform : null;
    }

    internal void Clear()
    {
        replicatedDoors.Clear();
        replicatedDoorIds.Clear();
        hostDoorTargets.Clear();
        hostDoorMoving.Clear();
        hostDoorRevisions.Clear();
        clientDoorRevisions.Clear();
        clientDoorTargets.Clear();
        clientDoorMoving.Clear();
        clientDoorTargetPositions.Clear();
        nextDoorSnapshotRequest = 0f;
        playerReference = null;
        referencePlayer = null;
    }
}