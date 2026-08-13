using HarmonyLib;
using UnityEngine;

internal static class PlayerCarrySystem
{
    private static ushort carrierId, targetId;
    private static bool carrying;
    private static bool pendingStart;
    private static float pendingStartUntil;
    private static bool localTargetActive, localTargetWasFullControl;
    private static bool localCarrierArmsFrozen, localCarrierAnimatorWasEnabled, localCarrierWasFullControl;
    private static int localCarrierWeaponSlot = -1;
    private static bool restoreCarrierWeaponPending;
    private static readonly Dictionary<BodyScript, float> pickupCrouchUntil = new();
    private static BodyScript collisionCarrier, collisionTarget;
    private static bool allowCarryDirectionChange;
    private static readonly Dictionary<BodyScript, List<CarryBodyPart>> targetPoses = new();
    private static readonly Dictionary<BodyScript, List<bool>> targetLimbAnimation = new();
    private static readonly Dictionary<BodyScript, Vector3> targetScales = new();
    private static readonly Dictionary<Rigidbody2D, RigidbodyInterpolation2D> targetInterpolation = new();
    private static readonly Dictionary<Rigidbody2D, CarryPhysicsState> targetPhysics = new();
    private static readonly Dictionary<BodyScript, float> remoteArmsRotation = new();
    private static readonly Dictionary<BodyScript, float> targetArmsBaseRotation = new();
    private static readonly Dictionary<BodyScript, Vector2> targetArmsOffset = new();
    private static readonly Dictionary<BodyScript, float> targetArmsBodyRotation = new();
    private static readonly Dictionary<Collider2D, bool> remoteTargetColliders = new();
    internal static string Prompt { get; private set; } = "";
    internal static bool IsLocalCarrier => carrying && carrierId == MultiplayerSession.LocalPeerId;
    internal static bool IsCarriedTarget(BodyScript body) => carrying && body != null && body == BodyForPeer(targetId);
    internal static bool AllowDirectionChange => allowCarryDirectionChange;
    internal static bool MustLockRemoteCarryPose(BodyScript body) =>
        IsLocalCarrier && body != null && body == BodyForPeer(targetId);


    internal static void Tick()
    {
        if (!MultiplayerSession.IsActive)
        {
            carrying = false;
            pendingStart = false;
            carrierId = targetId = 0;
            ClearTargetPoses();
            ReleaseLocalTarget();
            RestoreCarrierPose();
            SetCarryCollision(false);
            Prompt = "";
            return;
        }

        ushort sender;
        PlayerCarryPacket packet;
        while (MultiplayerSession.TryTakePlayerCarry(out sender, out packet)) Receive(sender, packet);

        if (pendingStart && Time.unscaledTime >= pendingStartUntil)
        {
            pendingStart = false;
            carrying = false;
            carrierId = targetId = 0;
            ClearTargetPoses();
        }

        var local = PlayerScript.player?.bodyScript;
        if (local == null) return;
        ApplyCarryCollision(local);
        if (restoreCarrierWeaponPending && !IsLocalCarrier)
            RestoreCarrierWeapon(local);
        if (carrying && (!local.isAlive || (carrierId == MultiplayerSession.LocalPeerId && !local.isAlive))) RequestStop();

        if (IsLocalCarrier)
        {
            if (!local.unarmed)
            {
                if (localCarrierWeaponSlot < 0) localCarrierWeaponSlot = local.currentWeapon;
                local.ChangeToUnarmed();
            }
            ApplyCarrierPose(local, true);
            Prompt = "Press C to stop carrying player";
            if (Input.GetKeyDown(KeyCode.C)) RequestStop();
            return;
        }

        if (carrying && targetId == MultiplayerSession.LocalPeerId)
        {
            Prompt = "Press SPACE to jump off";
            if (Input.GetKeyDown(KeyCode.Space)) RequestStop();
            return;
        }

        if (localTargetActive) ReleaseLocalTarget();
        RestoreCarrierPose();
        Prompt = !carrying && FindCarryTarget(local) != 0 ? "Press C to carry player" : "";
        if (!carrying && Input.GetKeyDown(KeyCode.C))
        {
            var peer = FindCarryTarget(local);
            if (peer != 0) RequestStart(peer);
        }
    }

    internal static void PostBodyUpdate(BodyScript body)
    {
        if (!carrying || body == null) return;
        if (body == BodyForPeer(carrierId)) ApplyCarrierPose(body, body == PlayerScript.player?.bodyScript);
    }

    internal static void PreBodyUpdate(BodyScript body)
    {
        if (body == null) return;
        if (pickupCrouchUntil.TryGetValue(body, out var until))
        {
            if (Time.unscaledTime < until)
                body.isCrouching = true;
            else
                pickupCrouchUntil.Remove(body);
        }
    }

    internal static void ApplyPickupCrouch()
    {
        var body = PlayerScript.player?.bodyScript;
        if (body != null && pickupCrouchUntil.TryGetValue(body, out var until) && Time.unscaledTime < until)
            body.isCrouching = true;
    }

    internal static void PostBodyFixedUpdate(BodyScript body)
    {
        if (carrying && body != null && body == BodyForPeer(targetId))
            ApplyTargetPose(body, body == PlayerScript.player?.bodyScript, true);
    }

    internal static void FixedTick()
    {
        if (!carrying) return;
        var target = BodyForPeer(targetId);
        if (target != null) ApplyTargetPose(target, target == PlayerScript.player?.bodyScript, true);
    }

    internal static void LateTick()
    {
    }

    private static void Receive(ushort sender, PlayerCarryPacket packet)
    {
        if (MultiplayerSession.IsHost)
        {
            if (packet.Carrying)
            {
                if (sender != packet.CarrierId || packet.CarrierId == 0 || packet.TargetId == 0 || packet.CarrierId == packet.TargetId || carrying) return;
                carrying = true;
                carrierId = packet.CarrierId;
                targetId = packet.TargetId;
                StartPickupCrouch(BodyForPeer(carrierId));
            }
            else
            {
                if (!carrying || (sender != carrierId && sender != targetId)) return;
                carrying = false;
                carrierId = targetId = 0;
                ClearTargetPoses();
            }
            MultiplayerSession.Send(new PlayerCarryPacket(carrying, carrierId, targetId), 0, true);
        }
        else if (sender == 1)
        {
            pendingStart = false;
            if (carrying && (!packet.Carrying || packet.CarrierId != carrierId || packet.TargetId != targetId))
                ClearTargetPoses();
            carrying = packet.Carrying;
            carrierId = packet.CarrierId;
            targetId = packet.TargetId;
            if (carrying) StartPickupCrouch(BodyForPeer(carrierId));
            if (!carrying) ClearTargetPoses();
        }
    }

    private static void RequestStart(ushort peerId)
    {
        StartPickupCrouch(PlayerScript.player?.bodyScript);
        if (MultiplayerSession.IsHost) Receive(MultiplayerSession.LocalPeerId, new PlayerCarryPacket(true, MultiplayerSession.LocalPeerId, peerId));
        else
        {
            carrying = true;
            carrierId = MultiplayerSession.LocalPeerId;
            targetId = peerId;
            pendingStart = true;
            pendingStartUntil = Time.unscaledTime + 2.5f;
            MultiplayerSession.Send(new PlayerCarryPacket(true, MultiplayerSession.LocalPeerId, peerId), 1, true);
        }
    }

    private static void StartPickupCrouch(BodyScript body)
    {
        if (body != null) pickupCrouchUntil[body] = Time.unscaledTime + 0.25f;
    }

    private static void RequestStop()
    {
        if (!carrying) return;
        pendingStart = false;
        if (MultiplayerSession.IsHost) Receive(MultiplayerSession.LocalPeerId, new PlayerCarryPacket(false, carrierId, targetId));
        else MultiplayerSession.Send(new PlayerCarryPacket(false, carrierId, targetId), 1, true);
    }

    private static BodyScript BodyForPeer(ushort peerId)
    {
        return peerId == MultiplayerSession.LocalPeerId ? PlayerScript.player?.bodyScript : NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
    }

    private static ushort FindCarryTarget(BodyScript local)
    {
        var camera = Camera.main;
        if (camera == null) return 0;
        var point = (Vector2)camera.ScreenToWorldPoint(Input.mousePosition);
        foreach (var remote in NetworkAvatarRegistry.RemotePlayers())
        {
            var body = remote.Body;
            if (body == null || !body.isAlive || Vector2.Distance(local.transform.position, body.transform.position) > 2.4f) continue;
            if (PointerOverBody(body, point)) return remote.PeerId;
        }
        return 0;
    }

    private static bool PointerOverBody(BodyScript body, Vector2 point)
    {
        foreach (var collider in body.GetComponentsInChildren<Collider2D>(true))
            if (collider != null && collider.enabled && collider.OverlapPoint(point)) return true;
        foreach (var rigidbody in body.GetComponentsInChildren<Rigidbody2D>(true))
            if (rigidbody != null && Vector2.Distance(rigidbody.position, point) <= 0.38f) return true;
        return false;
    }

    private static void ApplyTargetPose(BodyScript target, bool localOwner, bool physicsTick = false)
    {
        var carrier = BodyForPeer(carrierId);
        if (carrier == null || !carrier.isAlive)
        {
            if (localOwner) RequestStop();
            return;
        }

        if (!target.isRight)
        {
            allowCarryDirectionChange = true;
            target.SwitchDir(true);
            allowCarryDirectionChange = false;
        }

        if (!targetPoses.TryGetValue(target, out var parts))
        {
            if (localOwner)
            {
                localTargetWasFullControl = target.controlState == BodyScript.RagdollState.FullControl;
                target.EnterHalfControl();
                localTargetActive = true;
            }
            if (target.controlState == BodyScript.RagdollState.FullControl) target.EnterHalfControl();
            targetScales[target] = target.transform.localScale;
            var scale = target.transform.localScale;
            scale.x = Mathf.Abs(scale.x);
            target.transform.localScale = scale;
            target.isRight = true;
            parts = CaptureTargetPose(target);
            targetPoses[target] = parts;
            targetArmsBaseRotation[target] = target.Arms == null ? target.mainTorso.rotation : target.Arms.rotation.eulerAngles.z;
            targetArmsOffset[target] = target.Arms == null ? Vector2.zero :
                Quaternion.Euler(0f, 0f, -target.mainTorso.rotation) *
                ((Vector2)target.Arms.position - target.mainTorso.position);
            targetArmsBodyRotation[target] = target.Arms == null ? 0f :
                Mathf.DeltaAngle(target.mainTorso.rotation, target.Arms.rotation.eulerAngles.z);
            var animation = new List<bool>(target.limbs.Count);
            foreach (var limb in target.limbs)
            {
                animation.Add(limb.animated);
                limb.animated = limb.limbType == 1;
            }
            targetLimbAnimation[target] = animation;
            if (NetworkAvatarRegistry.IsRemoteAvatarBody(target))
            {
                DisableRemoteTargetColliders(target);
                EnableRemoteCarryPhysics(parts);
            }
        }

        var carrierTorso = carrier.mainTorso != null ? carrier.mainTorso : carrier.rb;
        if (carrierTorso == null) return;
        var anchor = carrierTorso.position + new Vector2(0f, 0.05f);
        var rotation = target.isRight ? 80f : 100f;
        var poseRotation = Quaternion.Euler(0f, 0f, rotation);
        var armDelta = 0f;
        if (targetArmsBaseRotation.TryGetValue(target, out var armsBaseRotation) &&
            remoteArmsRotation.TryGetValue(target, out var currentArmsRotation))
            armDelta = Mathf.DeltaAngle(armsBaseRotation, currentArmsRotation);
        var head = target.headTransform == null ? null : target.headTransform.GetComponent<Rigidbody2D>();
        foreach (var part in parts)
        {
            if (part.Body == null) continue;
            if (IsFreeLowerLeg(part.LimbIndex) || (part.IsTail && !part.IsTailBase)) continue;
            if (part.IsArm && NetworkAvatarRegistry.IsRemoteAvatarBody(target)) continue;
            if (!targetInterpolation.ContainsKey(part.Body))
            {
                targetInterpolation[part.Body] = part.Body.interpolation;
                part.Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            }
            var offset = part.IsArm
                ? (Vector2)(Quaternion.Euler(0f, 0f, armDelta) * part.Offset)
                : part.Offset;
            var position = anchor + (Vector2)(poseRotation * offset);
            if (physicsTick) part.Body.MovePosition(position);
            else part.Body.position = position;
            if (!part.IsArm)
            {
                var partRotation = CarryPartRotation(part, rotation, head);
                if (physicsTick) part.Body.MoveRotation(partRotation);
                else part.Body.rotation = partRotation;
            }
            else
            {
                var partRotation = rotation + part.Rotation + armDelta;
                if (physicsTick) part.Body.MoveRotation(partRotation);
                else part.Body.rotation = partRotation;
            }
            part.Body.angularVelocity = 0f;
        }
        if (NetworkAvatarRegistry.IsRemoteAvatarBody(target))
            ApplyRemoteArmPose(target, anchor, poseRotation, rotation, armDelta, physicsTick);
    }

    private static void ApplyRemoteArmPose(BodyScript target, Vector2 anchor, Quaternion poseRotation,
        float bodyRotation, float armDelta, bool physicsTick)
    {
        if (target.Arms == null) return;
        var offset = targetArmsOffset.TryGetValue(target, out var armsOffset) ? armsOffset : Vector2.zero;
        var relativeRotation = targetArmsBodyRotation.TryGetValue(target, out var armsBodyRotation)
            ? armsBodyRotation : 0f;
        target.Arms.position = anchor + (Vector2)(poseRotation * offset);
        target.Arms.rotation = Quaternion.Euler(0f, 0f, bodyRotation + relativeRotation + armDelta - 80f);
        foreach (var limb in target.limbs)
        {
            if (limb == null || limb.limbType != 1 || limb.rb == null || limb.transformToFollow == null) continue;
            var follow = limb.transformToFollow;
            if (follow != target.Arms && !follow.IsChildOf(target.Arms)) continue;
            if (physicsTick)
            {
                limb.rb.MovePosition(follow.position);
                limb.rb.MoveRotation(follow.eulerAngles.z);
            }
            else
            {
                limb.rb.position = follow.position;
                limb.rb.rotation = follow.eulerAngles.z;
            }
            limb.rb.velocity = Vector2.zero;
            limb.rb.angularVelocity = 0f;
        }
    }

    private static void ApplyCarrierPose(BodyScript body, bool localOwner)
    {
        if (body.Arms == null) return;
        if (localOwner && !localCarrierArmsFrozen)
        {
            localCarrierArmsFrozen = true;
            localCarrierWasFullControl = body.controlState == BodyScript.RagdollState.FullControl;
            localCarrierAnimatorWasEnabled = body.ArmsAnimator != null && body.ArmsAnimator.enabled;
            if (body.ArmsAnimator != null) body.ArmsAnimator.enabled = false;
        }
        var torso = body.mainTorso != null ? body.mainTorso : body.rb;
        body.Arms.rotation = Quaternion.Euler(0f, 0f,
            (torso == null ? 0f : torso.rotation) + (body.isRight ? -28f : 28f));
    }

    private static void RestoreCarrierPose()
    {
        if (!localCarrierArmsFrozen && localCarrierWeaponSlot < 0) return;
        var local = PlayerScript.player?.bodyScript;
        if (localCarrierArmsFrozen && local?.ArmsAnimator != null) local.ArmsAnimator.enabled = localCarrierAnimatorWasEnabled;
        if (localCarrierArmsFrozen && local != null && localCarrierWasFullControl && local.isAlive)
        {
            local.EnterFullControl();
            local.isCrouching = false;
            local.crouchAmount = 0f;
        }
        localCarrierArmsFrozen = false;
        localCarrierWasFullControl = false;
        if (localCarrierWeaponSlot >= 0) restoreCarrierWeaponPending = true;
    }

    private static void RestoreCarrierWeapon(BodyScript body)
    {
        restoreCarrierWeaponPending = false;
        if (body.isAlive && localCarrierWeaponSlot >= 0 && body.weapons != null &&
            localCarrierWeaponSlot < body.weapons.Count && body.weapons[localCarrierWeaponSlot] != null)
            body.ChangeWeapon(localCarrierWeaponSlot);
        localCarrierWeaponSlot = -1;
    }

    private static void ReleaseLocalTarget()
    {
        if (!localTargetActive) return;
        var local = PlayerScript.player?.bodyScript;
        if (local != null && localTargetWasFullControl)
        {
            local.EnterFullControl();
            local.isCrouching = false;
            if (local.coll != null) local.coll.enabled = true;
            if (local.rb != null)
            {
                local.rb.rotation = 0f;
                local.rb.velocity = Vector2.zero;
                local.rb.angularVelocity = 0f;
            }
            if (local.mainTorso != null)
            {
                local.mainTorso.rotation = 0f;
                local.mainTorso.velocity = Vector2.zero;
                local.mainTorso.angularVelocity = 0f;
            }
        }
        localTargetActive = false;
        localTargetWasFullControl = false;
    }

    private static void ApplyCarryCollision(BodyScript local)
    {
        if (!carrying)
        {
            SetCarryCollision(false);
            return;
        }
        var carrier = BodyForPeer(carrierId);
        var target = BodyForPeer(targetId);
        if (carrier == null || target == null) return;
        if (collisionCarrier == carrier && collisionTarget == target) return;
        SetCarryCollision(false);
        collisionCarrier = carrier;
        collisionTarget = target;
        SetCarryCollision(true);
    }

    private static void SetCarryCollision(bool ignored)
    {
        if (collisionCarrier == null || collisionTarget == null) return;
        foreach (var left in collisionCarrier.GetComponentsInChildren<Collider2D>(true))
            foreach (var right in collisionTarget.GetComponentsInChildren<Collider2D>(true))
                if (left != null && right != null) Physics2D.IgnoreCollision(left, right, ignored || !MultiplayerSession.PlayerCollisions);
        if (!ignored) collisionCarrier = collisionTarget = null;
    }

    private static List<CarryBodyPart> CaptureTargetPose(BodyScript target)
    {
        var parts = new List<CarryBodyPart>();
        var anchor = target.mainTorso;
        if (anchor == null) return parts;
        var inverse = Quaternion.Euler(0f, 0f, -anchor.rotation);
        var bodies = new HashSet<Rigidbody2D>();
        var tailBodies = new HashSet<Rigidbody2D>();
        var tailBaseBodies = new HashSet<Rigidbody2D>();
        foreach (var body in target.GetComponentsInChildren<Rigidbody2D>(true)) bodies.Add(body);
        if (target.tails != null)
            foreach (var tail in target.tails)
                if (tail != null)
                {
                    if (tail.childCount > 0)
                    {
                        var tailBase = tail.GetChild(0).GetComponent<Rigidbody2D>();
                        if (tailBase != null) tailBaseBodies.Add(tailBase);
                    }
                    foreach (var body in tail.GetComponentsInChildren<Rigidbody2D>(true))
                    {
                        bodies.Add(body);
                        tailBodies.Add(body);
                    }
                }
        foreach (var body in bodies)
            if (body != null)
            {
                var limbIndex = -1;
                for (var index = 0; index < target.limbs.Count; index++)
                    if (target.limbs[index] != null && target.limbs[index].rb == body)
                    {
                        limbIndex = index;
                        break;
                    }
                parts.Add(new CarryBodyPart
                {
                    Body = body,
                    Offset = inverse * (body.position - anchor.position),
                    Rotation = Mathf.DeltaAngle(anchor.rotation, body.rotation),
                    LimbIndex = limbIndex,
                    IsArm = limbIndex >= 0 && target.limbs[limbIndex].limbType == 1,
                    IsTail = tailBodies.Contains(body),
                    IsTailBase = tailBaseBodies.Contains(body)
                });
            }
        return parts;
    }

    private static float CarryPartRotation(CarryBodyPart part, float bodyRotation, Rigidbody2D head)
    {
        if (part.Body == head) return bodyRotation + part.Rotation - 47f;
        switch (part.LimbIndex)
        {
            case 9: return bodyRotation + 10f;
            case 10: return bodyRotation - 20f;
            case 11: return bodyRotation - 18f;
            case 12: return bodyRotation + 12f;
            case 13: return bodyRotation - 20f;
            case 14: return bodyRotation - 20f;
            default: return bodyRotation + part.Rotation;
        }
    }

    private static bool IsFreeLowerLeg(int limbIndex)
    {
        return limbIndex == 10 || limbIndex == 11 || limbIndex == 13 || limbIndex == 14;
    }

    internal static bool SetRemoteArmRotation(BodyScript body, Rigidbody2D part, Quaternion rotation)
    {
        if (!MustLockRemoteCarryPose(body) || part == null) return false;
        foreach (var limb in body.limbs)
            if (limb != null && limb.rb == part && limb.limbType == 1)
            {
                return true;
            }
        return false;
    }

    internal static bool SetRemoteArmsRotation(BodyScript body, Quaternion rotation)
    {
        if (!MustLockRemoteCarryPose(body)) return false;
        remoteArmsRotation[body] = rotation.eulerAngles.z;
        return true;
    }

    private static void ClearTargetPoses()
    {
        foreach (var pair in targetLimbAnimation)
        {
            var body = pair.Key;
            if (body == null) continue;
            var states = pair.Value;
            for (var index = 0; index < body.limbs.Count && index < states.Count; index++)
                if (body.limbs[index] != null) body.limbs[index].animated = states[index];
        }
        targetLimbAnimation.Clear();
        foreach (var pair in targetScales)
            if (pair.Key != null) pair.Key.transform.localScale = pair.Value;
        targetScales.Clear();
        foreach (var pair in targetInterpolation)
            if (pair.Key != null) pair.Key.interpolation = pair.Value;
        targetInterpolation.Clear();
        remoteArmsRotation.Clear();
        targetArmsBaseRotation.Clear();
        targetArmsOffset.Clear();
        targetArmsBodyRotation.Clear();
        foreach (var pair in targetPhysics)
            if (pair.Key != null)
            {
                pair.Key.velocity = Vector2.zero;
                pair.Key.angularVelocity = 0f;
                pair.Key.simulated = pair.Value.Simulated;
                pair.Key.bodyType = pair.Value.BodyType;
            }
        targetPhysics.Clear();
        foreach (var pair in remoteTargetColliders)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        remoteTargetColliders.Clear();
        targetPoses.Clear();
    }

    private static void DisableRemoteTargetColliders(BodyScript target)
    {
        foreach (var collider in target.GetComponentsInChildren<Collider2D>(true))
            if (collider != null && !remoteTargetColliders.ContainsKey(collider))
            {
                remoteTargetColliders[collider] = collider.enabled;
                collider.enabled = false;
            }
    }

    private static void EnableRemoteCarryPhysics(List<CarryBodyPart> parts)
    {
        foreach (var part in parts)
        {
            if (part.Body == null || (!IsFreeLowerLeg(part.LimbIndex) &&
                (!part.IsTail || part.IsTailBase))) continue;
            if (!targetPhysics.ContainsKey(part.Body))
                targetPhysics[part.Body] = new CarryPhysicsState
                {
                    Simulated = part.Body.simulated,
                    BodyType = part.Body.bodyType
                };
            part.Body.simulated = true;
            part.Body.bodyType = RigidbodyType2D.Dynamic;
        }
    }


    private struct CarryBodyPart
    {
        internal Rigidbody2D Body;
        internal Vector2 Offset;
        internal float Rotation;
        internal int LimbIndex;
        internal bool IsArm;
        internal bool IsTail;
        internal bool IsTailBase;
    }

    private struct CarryPhysicsState
    {
        internal bool Simulated;
        internal RigidbodyType2D BodyType;
    }

}

[HarmonyPatch(typeof(BodyScript), "ChangeWeapon")]
internal static class PlayerCarryWeaponPatch
{
    private static bool Prefix() => !PlayerCarrySystem.IsLocalCarrier;
}

[HarmonyPatch(typeof(BodyScript), "SwitchDir")]
internal static class PlayerCarryDirectionPatch
{
    private static bool Prefix(BodyScript __instance) => !PlayerCarrySystem.IsCarriedTarget(__instance) || PlayerCarrySystem.AllowDirectionChange;
}

[HarmonyPatch(typeof(BodyScript), "Update")]
internal static class PlayerCarryBodyPosePatch
{
    private static void Prefix(BodyScript __instance) => PlayerCarrySystem.PreBodyUpdate(__instance);

    private static void Postfix(BodyScript __instance) => PlayerCarrySystem.PostBodyUpdate(__instance);
}

[HarmonyPatch(typeof(PlayerScript), "Update")]
internal static class PlayerCarryPickupCrouchPatch
{
    private static void Postfix() => PlayerCarrySystem.ApplyPickupCrouch();
}

[HarmonyPatch(typeof(BodyScript), "FixedUpdate")]
internal static class PlayerCarryBodyPhysicsPatch
{
    private static void Postfix(BodyScript __instance) => PlayerCarrySystem.PostBodyFixedUpdate(__instance);
}
