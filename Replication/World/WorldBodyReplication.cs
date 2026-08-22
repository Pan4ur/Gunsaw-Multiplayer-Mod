using UnityEngine;

public class WorldBodyReplication
{
    internal readonly Dictionary<string, Rigidbody2D> bodies = new();
    internal readonly Dictionary<Rigidbody2D, string> ids = new();
    internal readonly Dictionary<string, ulong> wireIds = new();
    internal readonly Dictionary<ulong, string> idsByWire = new();
    internal readonly HashSet<Rigidbody2D> interactivePropBodies = new();
    internal readonly HashSet<Rigidbody2D> frozenFarClientProps = new();
    private readonly Dictionary<Rigidbody2D, Vector2> frozenFarPropPositions = new();
    private readonly Dictionary<Rigidbody2D, Vector2> lodPositions = new();
    internal readonly Dictionary<Rigidbody2D, WorldReplication.State> received = new();
    internal readonly Dictionary<Rigidbody2D, List<VehiclePathState>> vehiclePaths = new();
    internal readonly Dictionary<string, WorldReplication.ClientBodyState> pushes = new();
    internal readonly Dictionary<Rigidbody2D, float> locallyControlledUntil = new();
    internal readonly Dictionary<string, WorldReplication.PropAuthority> propAuthorities = new();
    internal readonly Dictionary<Rigidbody2D, LocalSettings> localSettings = new();
    private readonly Dictionary<Rigidbody2D, BodyClassification> classifications = new();
    private readonly Dictionary<Rigidbody2D, NearLocalCache> nearLocal = new();
    internal readonly List<Rigidbody2D> staleVehiclePaths = new();
    internal readonly ContactPoint2D[] contactBuffer = new ContactPoint2D[32];
    internal Rigidbody2D[] localContactBodies = new Rigidbody2D[0];

    internal const float ContactStateInterval = 0.1f;

    
    internal void RefreshWorldBodies()
    {
        foreach (var body in WorldReplication.FindObjectsOfType<Rigidbody2D>())
            RegisterWorldBody(body);
    }

    internal void RegisterRuntimeWorldBodies(GameObject runtimeObject)
    {
        if (!MultiplayerSession.IsHost || runtimeObject == null) return;
        foreach (var body in runtimeObject.GetComponentsInChildren<Rigidbody2D>(true))
            RegisterWorldBody(body);
    }

    internal void RegisterWorldBody(Rigidbody2D body)
    {
        if (!IsWorldBody(body))
        {
            RemoveWorldBody(body);
            return;
        }

        var id = WorldReplication.Instance.Id(body);
        bodies[id] = body;
        lodPositions[body] = body.position;
        classifications[body] = new BodyClassification
        {
            Mechanism = IsMechanismBody(body),
            InteractiveProp = WorldReplication.IsInteractivePropBodyUncached(body),
            ClientPhysicsJoint = IsClientPhysicsJointBody(body)
        };
        LoadDistanceSystem.RegisterWorldBody(body);
        WorldReplication.Instance.WireId(id);
        if (!WorldReplication.Instance.droppedWeapons.ContainsKey(body))
            WorldReplication.Instance.droppedWeapons[body] = body.GetComponentInParent<DroppedWeapon>();
        if (!WorldReplication.Instance.bodyLayouts.ContainsKey(body)) WorldReplication.Instance.bodyLayouts[body] = CreateBodyLayout(body);
        var interactiveProp = classifications[body].InteractiveProp;
        if (interactiveProp) interactivePropBodies.Add(body);
        else interactivePropBodies.Remove(body);
        if (MultiplayerSession.IsHost && interactiveProp)
            NetworkAvatarReplication.IgnoreRemotePlayerPropCollisions(body);
        if (!MultiplayerSession.IsHost) MakeClientControlled(body);
    }

    internal void RemoveWorldBody(Rigidbody2D body)
    {
        if (body == null) return;
        LoadDistanceSystem.UnregisterWorldBody(body);
        string id;
        if (ids.TryGetValue(body, out id))
        {
            bodies.Remove(id);
            ids.Remove(body);
            propAuthorities.Remove(id);
        }

        WorldReplication.Instance.droppedWeapons.Remove(body);
        WorldReplication.Instance.bodyLayouts.Remove(body);
        interactivePropBodies.Remove(body);
        frozenFarClientProps.Remove(body);
        frozenFarPropPositions.Remove(body);
        lodPositions.Remove(body);
        received.Remove(body);
        locallyControlledUntil.Remove(body);
        WorldReplication.Instance.nextContactStateAt.Remove(body);
        localSettings.Remove(body);
        classifications.Remove(body);
        nearLocal.Remove(body);
        WorldReplication.Instance.initializedBodies.Remove(body);
    }

    internal static bool IsWorldBody(Rigidbody2D body)
    {
        if (body == null || !body.gameObject.scene.isLoaded) return false;
        if (body.GetComponentInParent<BodyScript>() != null ||
            body.GetComponentInParent<PlayerScript>() != null ||
            body.GetComponentInParent<NetworkReplica>() != null ||
            NpcReplication.IsNpcRigBody(body)) return false;


        var localPlayer = PlayerScript.player;
        if (localPlayer != null && localPlayer.bodyScript != null &&
            body.transform.root == localPlayer.bodyScript.transform.root) return false;

        if (WorldReplication.IsInteractivePropBodyUncached(body)) return true;
        if (IsDroneBody(body)) return true;
        return !WorldReplication.IsGameplayOwned(body) && IsMechanismBody(body);
    }

    internal bool IsInteractivePropBody(Rigidbody2D body)
    {
        if (body == null) return false;
        if (interactivePropBodies.Contains(body)) return true;
        BodyClassification classification;
        if (classifications.TryGetValue(body, out classification)) return classification.InteractiveProp;
        return !ids.ContainsKey(body) && WorldReplication.IsInteractivePropBodyUncached(body);
    }
    
    internal static bool IsMechanismBody(Rigidbody2D body)
    {
        return body != null && (body.GetComponentInParent<DoorScript>() != null ||
                                body.GetComponentInParent<MovingBelt>() != null ||
                                body.GetComponentInParent<RbMoveToObj>() != null ||
                                body.GetComponentInParent<SawScript>() != null ||
                                body.GetComponentInParent<CustJoint>() != null ||
                                body.GetComponentInParent<VehiclePart>() != null ||
                                IsSafetyRailingBody(body) ||
                                IsChainlinkFenceBody(body));
    }
    
    internal static bool IsDroneBody(Rigidbody2D body)
    {
        return body != null && body.GetComponentInParent<DroneScript>() != null;
    }
    
    internal WorldReplication.BodyLayout BodyLayoutFor(Rigidbody2D body)
    {
        WorldReplication.BodyLayout layout;
        if (WorldReplication.Instance.bodyLayouts.TryGetValue(body, out layout)) return layout;
        layout = CreateBodyLayout(body);
        WorldReplication.Instance.bodyLayouts[body] = layout;
        return layout;
    }

    internal static WorldReplication.BodyLayout CreateBodyLayout(Rigidbody2D body)
    {
        var crate = body.GetComponentInParent<CrateScript>();
        var vehiclePart = body.GetComponent<VehiclePart>();
        return new WorldReplication.BodyLayout
        {
            Crate = crate,
            CratePrefabName = crate == null ? "" : WorldReplication.CleanCloneName(crate.transform.root.name),
            SafetyRailing = IsSafetyRailingBody(body) || IsChainlinkFenceBody(body),
            Joints = body.GetComponents<Joint2D>(),
            VehiclePart = vehiclePart,
            Vehicle = vehiclePart == null ? null : vehiclePart.vehicle ?? vehiclePart.GetComponentInParent<VehicleBase>(),
            VehicleJoint = body.GetComponent<Joint2D>()
        };
    }
    
    internal void MakeClientControlled(Rigidbody2D body)
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
        if (IsSafetyRailingBody(body))
            foreach (var joint in body.GetComponents<Joint2D>())
            {
                joint.breakForce = Mathf.Infinity;
                joint.breakTorque = Mathf.Infinity;
            }
        var classification = ClassificationFor(body);
        if (classification.Mechanism && !classification.InteractiveProp && !classification.ClientPhysicsJoint && body.simulated)
            body.bodyType = RigidbodyType2D.Kinematic;
    }
    
    internal void CaptureLocalContacts()
    {
        var player = PlayerScript.player;
        if (player == null || player.bodyScript == null) return;
        var localBody = player.bodyScript;
        var root = localBody.transform.root;
        if (root != WorldReplication.Instance.localContactRoot)
        {
            WorldReplication.Instance.localContactRoot = root;
            localContactBodies = root.GetComponentsInChildren<Rigidbody2D>();
            WorldReplication.Instance.nextContactStateAt.Clear();
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
    
    internal void QueueContactBodyState(Rigidbody2D body, float now)
    {
        float nextAt;
        if (WorldReplication.Instance.nextContactStateAt.TryGetValue(body, out nextAt) && now < nextAt) return;
        WorldReplication.Instance.nextContactStateAt[body] = now + ContactStateInterval;
        WorldReplication.Instance.QueueBodyState(body);
    }
    
    internal void MaintainMovingLocalAuthorities()
    {
        if (locallyControlledUntil.Count == 0) return;
        var now = Time.unscaledTime;
        var renew = new List<Rigidbody2D>();
        foreach (var pair in locallyControlledUntil)
        {
            var body = pair.Key;
            if (body == null || pair.Value >= now ||
                !(IsInteractivePropBody(body) || WorldReplication.Instance.droneBodies.Contains(body) ||
                  IsClientAuthorityJointBody(body))) continue;
            if (body.velocity.sqrMagnitude > 0.0004f || Mathf.Abs(body.angularVelocity) > 1f)
                renew.Add(body);
        }
        foreach (var body in renew)
            locallyControlledUntil[body] = now + WorldReplication.ClientAuthorityGrace;
        WorldReplication.Instance.clientFastSerializeState = WorldReplication.ClientAuthorityGrace;
    }
    
    internal static WorldReplication.ClientBodyState CaptureBodyState(Rigidbody2D body)
    {
        return new WorldReplication.ClientBodyState
        {
            position = body.position,
            rotation = body.rotation,
            velocity = body.velocity,
            angularVelocity = body.angularVelocity
        };
    }
    
    internal void ApplyAuthoritativeState(Rigidbody2D body, WorldReplication.State state)
    {
        lodPositions[body] = state.position;
        if (state.safetyRailing && !state.safetyRailingAttached) DetachSafetyRailing(body);
        
        ApplyVehicleState(body, state);
        
        if (!MultiplayerSession.IsHost && !state.vehiclePart && frozenFarClientProps.Contains(body))
        {
            frozenFarPropPositions[body] = state.position;
            body.simulated = false;
            return;
        }

        var classification = ClassificationFor(body);
        var mechanism = classification.Mechanism && !classification.InteractiveProp && !classification.ClientPhysicsJoint;
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

        if (mechanism)
        {
            // TODO This is more of a temporary fix
            // it would be better to simply signal to clients when the door is about to open or close,
            // but I'm not sure if that would break some of the custom level features

            if (!state.vehiclePart)
            {
                body.position = state.position;
                body.rotation = state.rotation;
                if (!WorldReplication.Instance.initializedBodies.Contains(body) || (state.position - body.position).sqrMagnitude > 256f)
                {
                    WorldReplication.Instance.initializedBodies.Add(body);
                }
                body.velocity = state.velocity;
                body.angularVelocity = state.angularVelocity;
                return;
            }
            if (!WorldReplication.Instance.initializedBodies.Contains(body) ||
                (state.position - body.position).sqrMagnitude > 256f)
            {
                WorldReplication.Instance.initializedBodies.Add(body);
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

        if (state.bodyType != RigidbodyType2D.Dynamic || !WorldReplication.Instance.initializedBodies.Contains(body))
        {
            body.position = state.position;
            body.rotation = state.rotation;
            body.velocity = state.velocity;
            body.angularVelocity = state.angularVelocity;
                WorldReplication.Instance.initializedBodies.Add(body);
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
    
    internal static void ApplyVehicleState(Rigidbody2D body, WorldReplication.State state)
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
    
    private void DetachSafetyRailing(Rigidbody2D body)
    {
        if (!IsSafetyRailingBody(body) && !IsChainlinkFenceBody(body)) return;
        foreach (var joint in body.GetComponents<Joint2D>())
            if (joint != null) WorldReplication.Destroy(joint);
    }
    
    internal static bool IsSafetyRailingBody(Rigidbody2D body)
    {
        if (body == null) return false;
        for (var current = body.transform; current != null; current = current.parent)
            if (current.name.StartsWith("SafetyRailing", StringComparison.Ordinal)) return true;
        return false;
    }

    internal static bool IsChainlinkFenceBody(Rigidbody2D body)
    {
        if (body == null || body.GetComponent<SoundOnJointBreak>() == null) return false;
        for (var current = body.transform; current != null; current = current.parent)
            if (current.name.IndexOf("chainlink", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    internal static bool IsDetachedChainlinkFenceBody(Rigidbody2D body)
    {
        if (!IsChainlinkFenceBody(body)) return false;
        foreach (var joint in body.GetComponents<Joint2D>())
            if (joint != null && joint.enabled) return false;
        return true;
    }

    internal static bool IsClientAuthorityJointBody(Rigidbody2D body)
    {
        return IsSafetyRailingBody(body) || IsChainlinkFenceBody(body);
    }

    private static bool IsClientPhysicsJointBody(Rigidbody2D body)
    {
        return IsSafetyRailingBody(body) || IsChainlinkFenceBody(body);
    }

    private BodyClassification ClassificationFor(Rigidbody2D body)
    {
        BodyClassification classification;
        if (classifications.TryGetValue(body, out classification)) return classification;
        classification = new BodyClassification
        {
            Mechanism = IsMechanismBody(body),
            InteractiveProp = WorldReplication.IsInteractivePropBodyUncached(body),
            ClientPhysicsJoint = IsClientPhysicsJointBody(body)
        };
        classifications[body] = classification;
        return classification;
    }

    private bool IsNearLocalPlayer(Rigidbody2D body)
    {
        NearLocalCache cached;
        var now = Time.unscaledTime;
        if (nearLocal.TryGetValue(body, out cached) && now < cached.NextSampleAt) return cached.Near;
        cached.Near = LoadDistanceSystem.IsWorldNearLocalPlayer(body);
        cached.NextSampleAt = now + 0.1f;
        nearLocal[body] = cached;
        return cached.Near;
    }

    private struct BodyClassification
    {
        public bool Mechanism;
        public bool InteractiveProp;
        public bool ClientPhysicsJoint;
    }

    private struct NearLocalCache
    {
        public bool Near;
        public float NextSampleAt;
    }

    internal void TickVehiclePaths()
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
    
    internal void FreezeFarClientProps()
    {
        if (MultiplayerSession.IsHost) return;
        var player = PlayerScript.player;
        var localBody = player == null ? null : player.bodyScript;
        if (localBody == null) return;
        var localPosition = localBody.rb == null ? (Vector2)localBody.transform.position : localBody.rb.position;
        foreach (var body in interactivePropBodies)
        {
            if (body == null) continue;
            Vector2 position;
            float controlledUntil;
            if (locallyControlledUntil.TryGetValue(body, out controlledUntil) && Time.unscaledTime < controlledUntil)
            {
                position = body.position;
                lodPositions[body] = position;
            }
            else if (!lodPositions.TryGetValue(body, out position))
            {
                position = body.position;
                lodPositions[body] = position;
            }
            if ((position - localPosition).sqrMagnitude < LoadDistanceSystem.WorldDistanceSqr)
            {
                frozenFarClientProps.Remove(body);
                frozenFarPropPositions.Remove(body);
                continue;
            }
            frozenFarClientProps.Add(body);
            frozenFarPropPositions[body] = position;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = false;
        }
    }
    
    internal class VehiclePathState
    {
        public Vector2 position;
        public float rotation;
        public Vector2 velocity;
        public float angularVelocity;
        public float arrivedAt;
    }
    
    internal struct LocalSettings
    {
        public RigidbodyType2D bodyType;
        public bool simulated;
        public CrateScript crate;
        public bool crateEnabled;
        public DroppedWeapon droppedWeapon;
        public bool droppedWeaponEnabled;
    }
}
