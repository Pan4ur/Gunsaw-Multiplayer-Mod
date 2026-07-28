using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

internal static class KartPassengers
{
    private static readonly HashSet<BodyScript> passengers = new HashSet<BodyScript>();
    private static readonly Dictionary<BodyScript, Dictionary<Collider2D, bool>> colliderStates = new Dictionary<BodyScript, Dictionary<Collider2D, bool>>();

    internal static bool IsPassenger(BodyScript body) => body != null && passengers.Contains(body);

    internal static Vector2 SeatPosition(VehicleBase vehicle, BodyScript body)
    {
        if (vehicle == null || vehicle.seatPos == null) return Vector2.zero;
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
        DisableCollisions(body);
        RefreshDriver(vehicle);
    }

    internal static void RegisterDriver(VehicleBase vehicle, BodyScript body)
    {
        if (vehicle == null || body == null || vehicle.occupant != body) return;
        passengers.Remove(body);
        RefreshDriver(vehicle);
    }

    internal static void Exit(BodyScript body)
    {
        if (body == null) return;
        var vehicle = body.curVehicle;
        passengers.Remove(body);
        RestoreCollisions(body);
        if (vehicle != null) RefreshDriver(vehicle);
    }

    internal static bool ExitPassenger(BodyScript body)
    {
        if (!IsPassenger(body) || body.curVehicle == null) return false;
        var vehicle = body.curVehicle;
        var joint = body.GetComponent<FixedJoint2D>();
        if (joint != null) UnityEngine.Object.Destroy(joint);
        body.inVehicle = false;
        body.transform.position += vehicle.mainPart.transform.up * vehicle.upPush * 2f;
        body.curVehicle = null;
        body.rb.freezeRotation = true;
        body.rb.transform.rotation = Quaternion.identity;
        Exit(body);
        return true;
    }

    private static void RefreshDriver(VehicleBase vehicle)
    {
        if (vehicle == null || vehicle.occupant == null) return;
        var driver = vehicle.occupant;
        if (vehicle.occupJoint != null) vehicle.occupJoint.connectedAnchor = vehicle.mainPart.transform.InverseTransformPoint(SeatPosition(vehicle, driver));
        driver.transform.position = SeatPosition(vehicle, driver);
    }

    private static void DisableCollisions(BodyScript body)
    {
        var states = new Dictionary<Collider2D, bool>();
        foreach (var collider in body.GetComponentsInChildren<Collider2D>(true))
        {
            states[collider] = collider.enabled;
            collider.enabled = false;
        }
        colliderStates[body] = states;
    }

    private static void RestoreCollisions(BodyScript body)
    {
        if (!colliderStates.TryGetValue(body, out var states)) return;
        foreach (var pair in states)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        colliderStates.Remove(body);
    }
}

