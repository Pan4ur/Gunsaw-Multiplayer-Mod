using UnityEngine;

internal static class KartPassengers
{
    private static readonly HashSet<BodyScript> passengers = new HashSet<BodyScript>();
    private static readonly HashSet<BodyScript> reportedRagdolls = new();
    private static readonly Dictionary<BodyScript, Dictionary<Collider2D, bool>> colliderStates = new Dictionary<BodyScript, Dictionary<Collider2D, bool>>();
    private static readonly Dictionary<BodyScript, Dictionary<Rigidbody2D, bool>> physicsStates = new();

    private static readonly Dictionary<BodyScript, VehicleBase> collisionVehicles = new();
    private static readonly Dictionary<BodyScript, List<(Collider2D Left, Collider2D Right, bool Ignored)>> vehicleCollisionStates = new();

    private static bool IsPassenger(BodyScript body) => body != null && passengers.Contains(body);

    internal static bool IsProtectedPassenger(BodyScript body) => IsPassenger(body) && body.inVehicle && body.curVehicle != null;

    internal static Vector2 SeatPosition(VehicleBase vehicle, BodyScript body)
    {
        if (vehicle == null || vehicle.seatPos == null) return Vector2.zero;
        
     //   var offset = IsPassenger(body) ? new Vector2(-1f, 2f) : HasPassengers(vehicle) ? new Vector2(1f, 2f) : Vector2.zero;
     
        var offset = IsPassenger(body) ? new Vector2(-0.105f, -0.08f) : HasPassengers(vehicle) ? new Vector2(0.105f, 0.08f) : Vector2.zero;
        return vehicle.seatPos.position + vehicle.mainPart.transform.TransformVector(offset);
    }

    internal static bool HasPassengers(VehicleBase vehicle)
    {
        foreach (var passenger in passengers)
            if (passenger != null && passenger.curVehicle == vehicle) return true;
        return false;
    }

    internal static bool TryEnter(VehicleBase vehicle, BodyScript body)
    {
        if (vehicle == null || body == null || vehicle.occupant == null || vehicle.occupant == body || !body.IsConsc() || (!body.isPlayer && HasPassengers(vehicle)) || vehicle.mainPart == null || vehicle.mainPart.rb == null || vehicle.seatPos == null || vehicle.mainPart.rb.velocity.magnitude >= 8f || Vector2.Distance(vehicle.mainPart.transform.position, body.transform.position) >= 2.2f || body.isClimbing) return false;
        Attach(vehicle, body, true);
        return true;
    }

    internal static void Attach(VehicleBase vehicle, BodyScript body, bool createJoint)
    {
        if (vehicle == null || body == null || vehicle.mainPart == null || vehicle.mainPart.rb == null) return;
        passengers.Add(body);
        body.transform.position = SeatPosition(vehicle, body);
        body.rb.freezeRotation = false;
        body.transform.rotation = vehicle.mainPart.transform.rotation;
        body.rb.rotation = vehicle.mainPart.rb.rotation;
        if (createJoint)
        {
            var joint = body.GetComponent<FixedJoint2D>();
            if (joint == null) joint = body.gameObject.AddComponent<FixedJoint2D>();
            joint.connectedBody = vehicle.mainPart.rb;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector2.zero;
            joint.connectedAnchor = vehicle.mainPart.transform.InverseTransformPoint(SeatPosition(vehicle, body));
            joint.frequency = 10f;
            joint.dampingRatio = 0.5f;
        }
        if (!body.isRight) body.SwitchDir(true);
        body.inVehicle = true;
        body.curVehicle = vehicle;
        if (body.BodyAnimator != null)
        {
            body.BodyAnimator.SetBool("inVehicle", true);
            body.BodyAnimator.Play("PlayerSit");
        }
        UpdateVehicleCollisions(body, vehicle);
        MaintainPassenger(body);
        RefreshDriver(vehicle);
    }

    internal static void RegisterDriver(VehicleBase vehicle, BodyScript body)
    {
        if (vehicle == null || body == null || vehicle.occupant != body) return;
        passengers.Remove(body);
        UpdateVehicleCollisions(body, vehicle);
        RefreshDriver(vehicle);
    }

    internal static void Exit(BodyScript body, VehicleBase vehicle)
    {
        if (body == null) return;
        passengers.Remove(body);
        reportedRagdolls.Remove(body);
        if (body.BodyAnimator != null)
        {
            body.BodyAnimator.SetBool("inVehicle", false);
            body.BodyAnimator.Rebind();
            body.BodyAnimator.Update(0f);
        }
        body.transform.rotation = Quaternion.identity;
        var velocity = vehicle.mainPart.rb.velocity;
        foreach (var rigidbody in body.GetComponentsInChildren<Rigidbody2D>(true))
        {
            rigidbody.position = rigidbody.transform.position;
            rigidbody.rotation = rigidbody.transform.eulerAngles.z;
            rigidbody.velocity = velocity;
            rigidbody.angularVelocity = 0f;
        }
        body.lastMoveDir = velocity;
        body.framesInGround = 0;
        body.grounded = false;
        if (body == PlayerScript.player?.bodyScript)
            LocalPlayerReplication.Instance?.RestoreLocalVehiclePhysics();
        RestorePhysics(body);
        RestoreCollisions(body);
        UpdateVehicleCollisions(body, null);
        RefreshDriver(vehicle);
    }

    internal static bool ExitPassenger(BodyScript body)
    {
        if (!IsPassenger(body) || body.curVehicle == null) return false;
        var joint = body.GetComponent<FixedJoint2D>();
        if (joint != null)
        {
            joint.enabled = false;
            UnityEngine.Object.Destroy(joint);
        }
        body.inVehicle = false;
        body.transform.position += body.curVehicle.mainPart.transform.up * body.curVehicle.upPush * 2f;
        body.curVehicle = null;
        body.rb.freezeRotation = true;
        body.standAnimForce = 1f;
        return true;
    }

    private static void RefreshDriver(VehicleBase vehicle)
    {
        if (vehicle == null || vehicle.occupant == null) return;
        var driver = vehicle.occupant;
        if (vehicle.occupJoint != null) vehicle.occupJoint.connectedAnchor = vehicle.mainPart.transform.InverseTransformPoint(SeatPosition(vehicle, driver));
        driver.transform.position = SeatPosition(vehicle, driver);
    }

    internal static void UpdateVehicleCollisions(BodyScript body, VehicleBase vehicle)
    {
        if (body == null) return;
        if (collisionVehicles.TryGetValue(body, out var previous) && previous == vehicle)
        {
            if (vehicleCollisionStates.TryGetValue(body, out var current))
                foreach (var pair in current)
                    if (pair.Left != null && pair.Right != null && pair.Left.enabled && pair.Right.enabled)
                        Physics2D.IgnoreCollision(pair.Left, pair.Right, true);
            return;
        }
        if (vehicleCollisionStates.TryGetValue(body, out var states))
        {
            foreach (var pair in states)
                if (pair.Left != null && pair.Right != null)
                    Physics2D.IgnoreCollision(pair.Left, pair.Right, pair.Ignored);
            vehicleCollisionStates.Remove(body);
            collisionVehicles.Remove(body);
        }
        if (vehicle == null) return;
        states = new List<(Collider2D Left, Collider2D Right, bool Ignored)>();
        foreach (var part in vehicle.GetComponentsInChildren<VehiclePart>(true))
            foreach (var left in part.GetComponents<Collider2D>())
                foreach (var right in body.GetComponentsInChildren<Collider2D>(true))
                {
                    var ignored = right == body.coll && vehicle.occupant == body
                        ? false : Physics2D.GetIgnoreCollision(left, right);
                    states.Add((left, right, ignored));
                    Physics2D.IgnoreCollision(left, right, true);
                }
        collisionVehicles[body] = vehicle;
        vehicleCollisionStates[body] = states;
    }

    internal static void ReportPassengerRagdoll(BodyScript body)
    {
        if (reportedRagdolls.Add(body))
            GunsawMultiplayerPlugin.LogInfo("Blocked seated passenger ragdoll, health=" + body.health +
                ", stamina=" + body.stamina + ". Caller: " + Environment.StackTrace);
    }

    internal static void MaintainPassenger(BodyScript body)
    {
        if (!IsProtectedPassenger(body)) return;
        body.framesInGround = 0;
        body.grounded = false;
        if (!colliderStates.TryGetValue(body, out var colliders))
        {
            colliders = new Dictionary<Collider2D, bool>();
            foreach (var collider in body.GetComponentsInChildren<Collider2D>(true))
                colliders[collider] = collider.enabled;
            colliderStates[body] = colliders;
        }
        foreach (var collider in colliders.Keys)
            if (collider != null) collider.enabled = false;
        if (!physicsStates.TryGetValue(body, out var rigidbodies))
        {
            rigidbodies = new Dictionary<Rigidbody2D, bool>();
            foreach (var rigidbody in body.GetComponentsInChildren<Rigidbody2D>(true))
                if (rigidbody != body.rb) rigidbodies[rigidbody] = rigidbody.simulated;
            physicsStates[body] = rigidbodies;
        }
        foreach (var rigidbody in rigidbodies.Keys)
            if (rigidbody != null)
            {
                rigidbody.velocity = Vector2.zero;
                rigidbody.angularVelocity = 0f;
                rigidbody.simulated = false;
            }
    }

    private static void RestorePhysics(BodyScript body)
    {
        if (!physicsStates.TryGetValue(body, out var states)) return;
        foreach (var pair in states)
            if (pair.Key != null)
            {
                if (body.rb != null)
                {
                    pair.Key.velocity = body.rb.velocity;
                    pair.Key.angularVelocity = body.rb.angularVelocity;
                }
                pair.Key.simulated = pair.Value;
            }
        physicsStates.Remove(body);
    }

    private static void RestoreCollisions(BodyScript body)
    {
        if (!colliderStates.TryGetValue(body, out var states)) return;
        foreach (var pair in states)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        colliderStates.Remove(body);
    }
}

