using HarmonyLib;
using UnityEngine;

/*
    А, с нами молодая Astra, Astra 6.0
    Она умеет юзать тулы, ебашит в консоль
    Эта модель любит лимиты кушать под 0
    Astra в codex — это боль, а-а-а, а-а
*/
internal static class PlayerCarrySystem
{
    private static ushort carrierId, targetId;
    private static bool carrying;
    private static bool pendingStart;
    private static float pendingStartUntil;
    private static bool localTargetActive;
    private static bool localCarrierArmsFrozen, localCarrierAnimatorWasEnabled, localCarrierWasFullControl;
    private static int localCarrierWeaponSlot = -1;
    private static bool restoreCarrierWeaponPending;
    private static readonly Dictionary<BodyScript, float> pickupCrouchUntil = new();
    private static BodyScript collisionCarrier, collisionTarget;
    private static readonly Dictionary<BodyScript, List<CarryBodyPart>> targetPoses = new();
    private static readonly Dictionary<BodyScript, List<bool>> targetLimbAnimation = new();
    private static readonly Dictionary<BodyScript, bool> targetOriginalDirections = new();
    private static readonly Dictionary<Rigidbody2D, RigidbodyInterpolation2D> targetInterpolation = new();
    private static readonly Dictionary<Rigidbody2D, CarryPhysicsState> targetPhysics = new();
    private static readonly Dictionary<BodyScript, float> remoteArmsRotation = new();
    private static readonly Dictionary<BodyScript, float> targetArmsBaseRotation = new();
    private static readonly Dictionary<BodyScript, Vector2> targetArmsOffset = new();
    private static readonly Dictionary<BodyScript, float> targetArmsBodyRotation = new();
    private static readonly Dictionary<BodyScript, Vector3> targetArmsOriginalLocalPosition = new();
    private static readonly Dictionary<BodyScript, Quaternion> targetArmsOriginalLocalRotation = new();
    private static readonly Dictionary<BodyScript, Vector3> targetArmsOriginalLocalScale = new();
    private static readonly Dictionary<Collider2D, bool> remoteTargetColliders = new();
    private static readonly Dictionary<Transform, CarryRenderPose> renderPoses = new();
    private static readonly NetworkAvatarTransformOrder renderOrder = new();
    private static readonly Dictionary<Transform, Pose> physicsPoses = new();
    private static readonly NetworkAvatarTransformOrder physicsOrder = new();
    private static readonly Dictionary<SpriteRenderer, (int Layer, int Order)> carrierArmSorting = new();
    private static Vector2 releaseVelocity;
    
    internal static string Prompt { get; private set; } = "";
    internal static bool IsLocalCarrier => carrying && carrierId == MultiplayerSession.LocalPeerId;
    internal static bool IsCarriedPeer(ushort peerId) => carrying && targetId == peerId;
    internal static bool IsLocalTarget => IsCarriedPeer(MultiplayerSession.LocalPeerId);
    internal static bool IsCarriedTarget(BodyScript body) => carrying && body != null && body == BodyForPeer(targetId);
    internal static bool MustLockRemoteCarryPose(BodyScript body) =>
        carrying && body != null && body == BodyForPeer(targetId) &&
        NetworkAvatarManager.IsRemoteAvatarBody(body);


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

        var carryTarget = FindCarryTarget(local);
        
        Prompt = !carrying && carryTarget != 0 ? "Press C to carry player" : "";
        
        if (!carrying && Input.GetKeyDown(KeyCode.C) && carryTarget != 0)
            RequestStart(carryTarget);
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

    internal static void PreBodyFixedUpdate(BodyScript body)
    {
        if (!carrying || body == null || body != BodyForPeer(targetId)) return;
        
        body.shockTime = Mathf.Max(body.shockTime, 0.5f);

        if (targetPoses.ContainsKey(body) && body.controlState == BodyScript.RagdollState.FullControl)
            body.EnterHalfControl();
    }

    internal static void FixedTick()
    {
        RestoreRenderPose();
        if (!carrying) return;
        var target = BodyForPeer(targetId);
        if (target != null) ApplyTargetPhysics(target, target == PlayerScript.player?.bodyScript);
    }

    internal static void LateTick()
    {
        RestoreRenderPose();
        if (!carrying) return;
        var target = BodyForPeer(targetId);
        var carrier = BodyForPeer(carrierId);
        if (target == null || carrier == null || target.mainTorso == null ||
            !targetPoses.TryGetValue(target, out var parts)) return;
        var torso = carrier.mainTorso != null ? carrier.mainTorso : carrier.rb;
        if (torso == null) return;

        var remote = NetworkAvatarManager.IsRemoteAvatarBody(target);
        var anchor = (Vector2)torso.transform.position + new Vector2(0f, 0.05f);
        var delta = anchor - (Vector2)target.mainTorso.transform.position;
        var poseRotation = Quaternion.Euler(0f, 0f, 80f);
        var head = target.headTransform == null ? null : target.headTransform.GetComponent<Rigidbody2D>();
        foreach (var part in parts)
        {
            if (part.Body == null) continue;
            var transform = part.Body.transform;
            if (!remote && target.Arms != null && transform.IsChildOf(target.Arms)) continue;
            var pose = new CarryRenderPose(transform);
            pose.Position += (Vector3)delta;
            if (!part.IsArm && !IsFreeLowerLeg(part.LimbIndex) && (!part.IsTail || part.IsTailBase))
            {
                var position = anchor + (Vector2)(poseRotation * part.Offset);
                pose.Position = new Vector3(position.x, position.y, pose.Position.z);
                pose.Rotation = Quaternion.Euler(0f, 0f, CarryPartRotation(part, 80f, head));
            }
            renderPoses[transform] = pose;
        }
        if (target.Arms != null)
        {
            var pose = new CarryRenderPose(target.Arms);
            pose.Position += (Vector3)delta;
            renderPoses[target.Arms] = pose;
        }
        foreach (var transform in renderOrder.Update(renderPoses))
        {
            var pose = renderPoses[transform];
            transform.SetPositionAndRotation(pose.Position, pose.Rotation);
        }
        if (remote) ApplyRemoteArmPose(target, anchor, poseRotation, false);
    }

    internal static void RestoreRenderPose()
    {
        foreach (var pair in renderPoses)
            if (pair.Key != null)
            {
                pair.Key.localPosition = pair.Value.LocalPosition;
                pair.Key.localRotation = pair.Value.LocalRotation;
            }
        renderPoses.Clear();
    }

    private static void Receive(ushort sender, PlayerCarryPacket packet)
    {
        if (MultiplayerSession.IsHost)
        {
            var stop = packet;
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
                if (!carrying)
                {
                    if (sender != 0 && (sender == packet.CarrierId || sender == packet.TargetId))
                        MultiplayerSession.Send(new PlayerCarryPacket(false, 0, 0), sender, true);
                    return;
                }
                
                if ((sender != carrierId && sender != targetId) || packet.CarrierId != carrierId || packet.TargetId != targetId) 
                    return;

                if (carrierId == MultiplayerSession.LocalPeerId)
                    stop = StopPacketWithVelocity();
                else if (sender != carrierId || !packet.HasReleaseVelocity)
                {
                    MultiplayerSession.Send(new PlayerCarryPacket(false, carrierId, targetId), carrierId, true);
                    return;
                }
                releaseVelocity = new Vector2(stop.ReleaseVelocityX, stop.ReleaseVelocityY);
                carrying = false;
                carrierId = targetId = 0;
                ClearTargetPoses();
            }
            MultiplayerSession.Send(new PlayerCarryPacket(carrying, carrierId, targetId, stop.ReleaseVelocityX, stop.ReleaseVelocityY, !carrying && stop.HasReleaseVelocity), 0, true);
        }
        else if (sender == 1)
        {
            if (!packet.Carrying && !packet.HasReleaseVelocity && IsLocalCarrier && packet.CarrierId == carrierId && packet.TargetId == targetId)
            {
                MultiplayerSession.Send(StopPacketWithVelocity(), 1, true);
                return;
            }
            pendingStart = false;
            if (carrying && (!packet.Carrying || packet.CarrierId != carrierId || packet.TargetId != targetId))
            {
                releaseVelocity = new Vector2(packet.ReleaseVelocityX, packet.ReleaseVelocityY);
                ClearTargetPoses();
            }
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
        var packet = IsLocalCarrier ? StopPacketWithVelocity() : new PlayerCarryPacket(false, carrierId, targetId);
        if (MultiplayerSession.IsHost) Receive(MultiplayerSession.LocalPeerId, packet);
        else MultiplayerSession.Send(packet, 1, true);
    }

    private static PlayerCarryPacket StopPacketWithVelocity()
    {
        var velocity = (BodyForPeer(carrierId)?.rb?.velocity ?? Vector2.zero) * 1.5f;
        return new PlayerCarryPacket(false, carrierId, targetId, velocity.x, velocity.y, true);
    }

    private static BodyScript BodyForPeer(ushort peerId)
    {
        return peerId == MultiplayerSession.LocalPeerId ? PlayerScript.player?.bodyScript : NetworkAvatarManager.RemoteBodyForPeer(peerId);
    }

    private static ushort FindCarryTarget(BodyScript local)
    {
        var camera = Camera.main;
        if (camera == null) return 0;
        var point = (Vector2)camera.ScreenToWorldPoint(Input.mousePosition);
        foreach (var remote in NetworkAvatarManager.replicas)
        {
            if (remote.Value == null)
                continue;
            
            var body = remote.Value.remoteBody;
            if (body == null || !body.isAlive || Vector2.Distance(local.transform.position, body.transform.position) > 2.4f) continue;
            if (PointerOverBody(body, point)) return remote.Key;
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

    private static void ApplyTargetPhysics(BodyScript target, bool localOwner)
    {
        var carrier = BodyForPeer(carrierId);
        if (carrier == null || !carrier.isAlive)
        {
            if (localOwner) RequestStop();
            return;
        }

        if (!targetPoses.TryGetValue(target, out var parts))
        {
            releaseVelocity = Vector2.zero;
            SetCarrierArmSorting(carrier, target);
            targetOriginalDirections[target] = target.isRight;
            if (target.Arms != null)
            {
                targetArmsOriginalLocalPosition[target] = target.Arms.localPosition;
                targetArmsOriginalLocalRotation[target] = target.Arms.localRotation;
                targetArmsOriginalLocalScale[target] = target.Arms.localScale;
            }
            if (!target.isRight) ForceSwitchDirectionPreservePose(target);
            if (localOwner)
            {
                target.EnterHalfControl();
                localTargetActive = true;
            }
            parts = CaptureTargetPose(target);
            targetPoses[target] = parts;
            targetArmsBaseRotation[target] = target.Arms == null ? target.mainTorso.rotation : target.Arms.rotation.eulerAngles.z;
            targetArmsOffset[target] = target.Arms == null ? Vector2.zero : Quaternion.Euler(0f, 0f, -target.mainTorso.rotation) * ((Vector2)target.Arms.position - target.mainTorso.position);
            targetArmsBodyRotation[target] = target.Arms == null ? 0f : Mathf.DeltaAngle(target.mainTorso.rotation, target.Arms.rotation.eulerAngles.z);
            var animation = new List<bool>(target.limbs.Count);
            foreach (var limb in target.limbs)
            {
                animation.Add(limb.animated);
                limb.animated = limb.limbType == 1;
            }
            targetLimbAnimation[target] = animation;
            if (NetworkAvatarManager.IsRemoteAvatarBody(target))
            {
                DisableRemoteTargetColliders(target);
                EnableRemoteCarryPhysics(parts);
            }
        }

        var carrierTorso = carrier.mainTorso != null ? carrier.mainTorso : carrier.rb;
        if (carrierTorso == null) return;
        var anchor = (Vector2) carrierTorso.transform.position + new Vector2(0f, 0.05f);
        var rotation = 80f;
        var poseRotation = Quaternion.Euler(0f, 0f, rotation);
        var head = target.headTransform == null ? null : target.headTransform.GetComponent<Rigidbody2D>();
        var remote = NetworkAvatarManager.IsRemoteAvatarBody(target);
        foreach (var part in parts)
        {
            if (part.Body == null) continue;
            if (IsFreeLowerLeg(part.LimbIndex) || (part.IsTail && !part.IsTailBase)) continue;
            if (part.IsArm) continue;

            if (!targetInterpolation.ContainsKey(part.Body))
            {
                targetInterpolation[part.Body] = part.Body.interpolation;
                part.Body.interpolation = RigidbodyInterpolation2D.None;
            }

            var position = anchor + (Vector2)(poseRotation * part.Offset);

            part.Body.position = position;
            part.Body.rotation = CarryPartRotation(part, rotation, head);

            part.Body.velocity = Vector2.zero;
            part.Body.angularVelocity = 0f;
        }
        SyncTargetTransforms(parts);
        if (remote)
            ApplyRemoteArmPose(target, anchor, poseRotation, true);
    }

    private static void SyncTargetTransforms(List<CarryBodyPart> parts)
    {
        foreach (var part in parts)
        {
            if (part.Body == null || part.IsArm) continue;
            var position = part.Body.position;
            var transform = part.Body.transform;
            physicsPoses[transform] = new Pose(new Vector3(position.x, position.y, transform.position.z), Quaternion.Euler(0f, 0f, part.Body.rotation));
        }
        
        foreach (var transform in physicsOrder.Update(physicsPoses))
        {
            var pose = physicsPoses[transform];
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }
        physicsPoses.Clear();
    }

    private static void ApplyRemoteArmPose(
        BodyScript target,
        Vector2 anchor,
        Quaternion poseRotation,
        bool physicsTick)
    {
        if (target.Arms == null)
            return;

        var offset =
            targetArmsOffset.TryGetValue(target, out var armsOffset)
                ? armsOffset
                : Vector2.zero;

        target.Arms.position =
anchor + (Vector2)(poseRotation * offset);

        float armsWorldRotation;

        if (remoteArmsRotation.TryGetValue(target, out var networkArmsRotation))
        {
            armsWorldRotation = networkArmsRotation;
        }
        else if (targetArmsBaseRotation.TryGetValue(target, out var baseArmsRotation))
        {
            armsWorldRotation = baseArmsRotation;
        }
        else
        {
            armsWorldRotation = target.Arms.eulerAngles.z;
        }

        target.Arms.rotation =
            Quaternion.Euler(0f, 0f, armsWorldRotation);

        foreach (var limb in target.limbs)
        {
            if (limb == null ||
                limb.limbType != 1 ||
                limb.rb == null ||
                limb.transformToFollow == null)
                continue;

            var follow = limb.transformToFollow;

            if (follow != target.Arms &&
                !follow.IsChildOf(target.Arms))
                continue;

            if (physicsTick)
            {
                limb.rb.position = follow.position;
                limb.rb.rotation = follow.eulerAngles.z;
            }
            else
            {
                limb.transform.SetPositionAndRotation(follow.position, follow.rotation);
            }

            if (physicsTick)
            {
                limb.rb.velocity = Vector2.zero;
                limb.rb.angularVelocity = 0f;
            }
        }
    }

    private static void SetCarrierArmSorting(BodyScript carrier, BodyScript target)
    {
        var backmost = target.GetComponentsInChildren<SpriteRenderer>(true)
            .Where(renderer => renderer.enabled && renderer.sprite != null)
            .OrderBy(renderer => SortingLayer.GetLayerValueFromID(renderer.sortingLayerID))
            .ThenBy(renderer => renderer.sortingOrder).FirstOrDefault();
        if (backmost == null) return;
        var sprites = new HashSet<SpriteRenderer>();
        if (carrier.Arms != null)
            sprites.UnionWith(carrier.Arms.GetComponentsInChildren<SpriteRenderer>(true));
        foreach (var limb in carrier.limbs)
            if (limb != null && limb.limbType == 1)
                sprites.UnionWith(limb.GetComponentsInChildren<SpriteRenderer>(true));
        if (sprites.Count == 0) return;
        var offset = backmost.sortingOrder - 1 - sprites.Max(renderer => renderer.sortingOrder);
        foreach (var renderer in sprites)
        {
            carrierArmSorting[renderer] = (renderer.sortingLayerID, renderer.sortingOrder);
            renderer.sortingLayerID = backmost.sortingLayerID;
            renderer.sortingOrder += offset;
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
        if (local != null)
        {
            local.lastMoveDir = releaseVelocity;
            if (local.isAlive) local.EnterHalfControl();
            local.shockTime = Mathf.Max(local.shockTime, 0.5f);
            local.isCrouching = false;
            if (local.rb != null)
                local.rb.velocity = releaseVelocity;
            if (local.mainTorso != null)
                local.mainTorso.velocity = releaseVelocity;
        }
        localTargetActive = false;
    }

    private static void ResetTargetRoot(BodyScript body, List<CarryBodyPart> parts)
    {
        if (body.rb == null) return;
        foreach (var part in parts)
            if (part.Body != null && part.Body != body.rb)
            {
                var transform = part.Body.transform;
                physicsPoses[transform] = new Pose(transform.position, transform.rotation);
            }
        if (body.Arms != null)
            physicsPoses[body.Arms] = new Pose(body.Arms.position, body.Arms.rotation);
        body.rb.rotation = 0f;
        body.rb.angularVelocity = 0f;
        body.rb.transform.rotation = Quaternion.identity;
        foreach (var transform in physicsOrder.Update(physicsPoses))
        {
            var pose = physicsPoses[transform];
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }
        physicsPoses.Clear();
        physicsOrder.Clear();
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

    private static void ForceSwitchDirectionPreservePose(BodyScript body)
    {
        if (body == null) return;
        
        var rigidbodies = body.GetComponentsInChildren<Rigidbody2D>(true);
        var positions = new Vector2[rigidbodies.Length];
        var rotations = new float[rigidbodies.Length];
        for (var i = 0; i < rigidbodies.Length; i++)
        {
            var rb = rigidbodies[i];
            if (rb == null) continue;
            positions[i] = rb.position;
            rotations[i] = rb.rotation;
        }

        var armsPosition = body.Arms == null ? Vector3.zero : body.Arms.position;
        var armsRotation = body.Arms == null ? Quaternion.identity : body.Arms.rotation;

        if (body.weapon != null) body.weapon.animFollowMult = 1f;
        if (body.limbs != null && body.limbs.Count > 0 && body.limbs[0] != null)
            body.limbs[0].bonusRot = -body.limbs[0].bonusRot;

        body.isRight = !body.isRight;

        if (body.limbs != null)
            foreach (var limb in body.limbs)
            {
                if (limb == null) continue;
                var hinge = limb.GetComponent<HingeJoint2D>();
                if (hinge == null || !hinge.useLimits) continue;
                var limits = hinge.limits;
                var oldMin = limits.min;
                limits.min = -limits.max;
                limits.max = -oldMin;
                hinge.limits = limits;
            }

        var scale = body.transform.localScale;
        scale.x = -scale.x;
        body.transform.localScale = scale;
        
        for (var i = 0; i < rigidbodies.Length; i++)
        {
            var rb = rigidbodies[i];
            if (rb == null) continue;
            rb.position = positions[i];
            rb.rotation = rotations[i];
        }

        if (body.Arms != null)
        {
            body.Arms.position = armsPosition;
            body.Arms.rotation = armsRotation;
        }
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
                    IsArm = (limbIndex >= 0 && target.limbs[limbIndex].limbType == 1) || (target.Arms != null && body.transform.IsChildOf(target.Arms)),
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
        RestoreRenderPose();
        foreach (var pair in carrierArmSorting)
            if (pair.Key != null)
            {
                pair.Key.sortingLayerID = pair.Value.Layer;
                pair.Key.sortingOrder = pair.Value.Order;
            }
        carrierArmSorting.Clear();
        renderOrder.Clear();
        physicsPoses.Clear();
        physicsOrder.Clear();
        foreach (var pair in targetLimbAnimation)
        {
            var body = pair.Key;
            if (body == null) continue;
            var states = pair.Value;
            for (var index = 0; index < body.limbs.Count && index < states.Count; index++)
                if (body.limbs[index] != null) body.limbs[index].animated = states[index];
        }
        targetLimbAnimation.Clear();
        foreach (var pair in targetOriginalDirections)
            if (pair.Key != null && pair.Key.isRight != pair.Value)
                ForceSwitchDirectionPreservePose(pair.Key);
        targetOriginalDirections.Clear();
        
        foreach (var pair in targetArmsOriginalLocalPosition)
        {
            var body = pair.Key;
            if (body == null || body.Arms == null) continue;

            body.Arms.localPosition = pair.Value;
            if (targetArmsOriginalLocalRotation.TryGetValue(body, out var localRotation))
                body.Arms.localRotation = localRotation;
            if (targetArmsOriginalLocalScale.TryGetValue(body, out var localScale))
                body.Arms.localScale = localScale;
            
            if (body.limbs == null) continue;
            foreach (var limb in body.limbs)
            {
                if (limb == null || limb.limbType != 1 || limb.rb == null ||
                    limb.transformToFollow == null || limb.dismembered) continue;

                var follow = limb.transformToFollow;
                var localTarget = (Vector2)follow.localPosition;
                if (limb.transform.parent == body.Arms)
                    localTarget -= (Vector2)body.armsOffset;
                if (limb.reverseXPosWhenFlipped && !body.isRight)
                    localTarget = -localTarget;

                limb.transform.localPosition = localTarget;
                limb.transform.localEulerAngles = new Vector3(0f, 0f, follow.localEulerAngles.z);
                limb.rb.velocity = Vector2.zero;
                limb.rb.angularVelocity = 0f;
            }
        }
        targetArmsOriginalLocalPosition.Clear();
        targetArmsOriginalLocalRotation.Clear();
        targetArmsOriginalLocalScale.Clear();

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
        foreach (var pair in targetPoses)
            if (pair.Key == PlayerScript.player?.bodyScript)
            {
                ResetTargetRoot(pair.Key, pair.Value);
                foreach (var part in pair.Value)
                    if (part.Body != null) part.Body.velocity = releaseVelocity;
            }
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

    private struct CarryRenderPose
    {
        internal Vector3 LocalPosition;
        internal Quaternion LocalRotation;
        internal Vector3 Position;
        internal Quaternion Rotation;

        internal CarryRenderPose(Transform transform)
        {
            LocalPosition = transform.localPosition;
            LocalRotation = transform.localRotation;
            Position = transform.position;
            Rotation = transform.rotation;
        }
    }

}

[HarmonyPatch(typeof(BodyScript), "ChangeWeapon")]
internal static class PlayerCarryWeaponPatch
{
    private static bool Prefix(BodyScript __instance) =>
        !PlayerCarrySystem.IsLocalCarrier || __instance != PlayerScript.player?.bodyScript;
}

[HarmonyPatch(typeof(BodyScript), "SwitchDir")]
internal static class PlayerCarryDirectionPatch
{
    private static bool Prefix(BodyScript __instance) => !PlayerCarrySystem.IsCarriedTarget(__instance);
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
    private static void Prefix(BodyScript __instance) => PlayerCarrySystem.PreBodyFixedUpdate(__instance);
}
