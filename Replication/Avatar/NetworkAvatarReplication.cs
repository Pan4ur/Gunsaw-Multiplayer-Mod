using System.Collections;
using UnityEngine;
using static NetworkAvatarManager;
using static NetworkAvatarUtilities;

internal class NetworkAvatarReplication : MonoBehaviour
{
    private const bool BufferedRemoteInterpolation = true;
    internal BodyScript remoteBody;
    internal Vector2 lastAuthoritativePosition;
    internal bool hasAuthoritativePosition;
    private GameObject remoteAvatar;
    private Transform remoteAvatarParent;
    private string remotePrefabPath = "";
    internal string remoteName = "Player";
    private readonly Queue<RemoteProjectileVisual> remoteProjectiles = new();
    private readonly Dictionary<Rigidbody2D, TargetState> targets = new();
    private readonly Dictionary<Transform, WorldTargetState> worldTargets = new();
    private readonly Dictionary<Transform, WorldTargetState> localTargets = new();
    internal bool receivedFirstSnapshot;
    private int appliedWeapon = -1;
    private ulong appliedWeaponSprite;
    private string appliedInventory = "";
    private VisualLayout remoteVisualLayout;
    private LineRenderer remoteLevitLine;
    private LineRenderer remoteCrystalTongueLine;
    private readonly RemoteLineInterpolation remoteLevitInterpolation = new();
    private readonly RemoteLineInterpolation remoteCrystalTongueInterpolation = new();
    private GameObject remoteScarf;
    private GameObject remoteScarfHold;
    private readonly Dictionary<int, GameObject> remoteFires = new();
    internal readonly Dictionary<Collider2D, bool> remoteColliderTriggers = new();
    private BodyScript collisionRuleLocalBody;
    private bool collisionRuleApplied;
    private bool collisionRulePlayerCollisions;
    private readonly Dictionary<SpriteRenderer, Sprite> originalDismemberSprites = new();
    private readonly HashSet<int> displayedDismembermentEffects = new();
    private bool dismembermentVisualsInitialized;
    private readonly List<Transform> staleWorldTargets = [];
    private readonly List<KeyValuePair<Transform, WorldTargetState>> orderedWorldTargets = [];
    private Rigidbody2D[] remoteRigidbodies = new Rigidbody2D[0];
    private readonly List<Rigidbody2D> remoteTailBases = [];
    private Transform[] remoteTails = new Transform[0];
    private readonly List<SpriteRenderer[]> remoteTailSprites = [];
    private readonly List<SpriteRenderer[]> remoteTailRootSprites = [];
    internal bool remotePhysicsModeKnown;
    private float remoteVehicleHeadRotation;
    private float vehicleHeadFromRotation;
    private float vehicleHeadStartedAt;
    private bool hasRemoteVehicleHeadRotation;
    private float vehicleArmsTargetRotation;
    private Vector2 vehicleArmsLocalPosition;
    private float vehicleArmsLocalRotation;
    private Vector2 vehicleArmsFromLocalPosition;
    private float vehicleArmsFromLocalRotation;
    private float vehicleArmsStartedAt;
    private bool hasVehicleArmsTarget;
    private readonly List<VehicleTailTarget> vehicleTailTargets = [];
    private readonly List<VehicleTailTransformTarget> vehicleTailTransformTargets = [];
    internal ushort remotePeerId;
    internal float lastRemoteHealth;
    internal bool lastRemoteAlive = true;
    private AudioSource remoteTelekinesisSound;
    private int appliedDismembermentHash = int.MinValue;
    private float pendingRemoteDamage;
    internal bool remoteCanBeGrabbed;
    private bool remoteVehicleReflected;
    private bool hasRemoteVehicleReflection;
    private Vector3 remoteScaleBeforeVehicle;
    private bool hasRemoteScaleBeforeVehicle;

    protected virtual void Update()
    {
        TickRemote();
    }

    private void TickRemote()
    {
        if (!MultiplayerSession.HasPeer(remotePeerId))
        {
            if (remoteAvatar != null) DestroyRemote();
            return;
        }
        if (remoteAvatar == null) return;
        UpdateRemoteLineInterpolation(remoteLevitLine, remoteLevitInterpolation);
        UpdateRemoteLineInterpolation(remoteCrystalTongueLine, remoteCrystalTongueInterpolation);
        if (PlayerCarrySystem.MustLockRemoteCarryPose(remoteBody))
        {
            targets.Clear();
            worldTargets.Clear();
            localTargets.Clear();
            return;
        }
        UpdateRemotePhysicsMode();
        VoyagerBody.UpdatePvpVoyagerVisuals(remoteBody, Time.deltaTime);
        foreach (var pair in targets)
        {
            var body = pair.Key;
            var target = pair.Value;
            if (body == null) continue;
            var alpha = Mathf.Clamp01((Time.unscaledTime - target.startedAt) /
                Mathf.Max(0.001f, target.duration));
            body.transform.position = Vector3.Lerp(target.fromPosition, target.position, alpha);
            body.transform.rotation = Quaternion.Lerp(target.fromRotation, target.rotation, alpha);
        }

        staleWorldTargets.Clear();
        orderedWorldTargets.Clear();
        foreach (var pair in worldTargets) orderedWorldTargets.Add(pair);
        orderedWorldTargets.Sort((left, right) => TransformDepth(left.Key).CompareTo(TransformDepth(right.Key)));
        foreach (var pair in orderedWorldTargets)
        {
            var transform = pair.Key;
            if (transform == null) { staleWorldTargets.Add(transform); continue; }
            var progress = Mathf.Clamp01((Time.unscaledTime - pair.Value.startedAt) / pair.Value.duration);
            transform.position = Vector3.Lerp(pair.Value.fromPosition, pair.Value.position, progress);
            transform.rotation = Quaternion.Slerp(pair.Value.fromRotation, pair.Value.rotation, progress);
        }
        foreach (var transform in staleWorldTargets) worldTargets.Remove(transform);

        staleWorldTargets.Clear();
        foreach (var pair in localTargets)
        {
            var transform = pair.Key;
            if (transform == null) { staleWorldTargets.Add(transform); continue; }
            var progress = Mathf.Clamp01((Time.unscaledTime - pair.Value.startedAt) / pair.Value.duration);
            transform.localPosition = Vector3.Lerp(pair.Value.fromPosition, pair.Value.position, progress);
            transform.localRotation = Quaternion.Slerp(pair.Value.fromRotation, pair.Value.rotation, progress);
        }
        foreach (var transform in staleWorldTargets) localTargets.Remove(transform);
        MaintainRemoteVehicleAttachment();
    }

    protected virtual void LateUpdate()
    {
        if (!MultiplayerSession.IsConnected || remoteAvatar == null)
            return;

        if (remoteBody != null && remoteBody.inVehicle)
        {
            MaintainRemoteVehiclePose();
            ApplyVehicleArmsTarget();
            SnapRemoteVehicleArmLimbs();

            if (hasRemoteVehicleReflection)
                ApplyVehicleReflection();

            if (hasRemoteVehicleHeadRotation)
                ApplyVehicleHeadRotation();

            ApplyVehicleTailTargets();
        }
    }

    protected virtual void OnDestroy()
    {
        NetworkAvatarReplication current;
        if (replicas.TryGetValue(remotePeerId, out current) && current == this)
            replicas.Remove(remotePeerId);
    }

    internal void CreateRemote(string identity, BodyScript localBody)
    {
        var split = identity.IndexOf('\n');
        if (split < 1) return;
        var name = identity.Substring(0, split);
        var prefabPath = identity.Substring(split + 1);
        var sanitizedName = NetworkAvatarUtilities.SanitizePlayerName(name);
        if (remoteBody != null && remotePrefabPath == prefabPath)
        {
            remoteName = sanitizedName;
            return;
        }
        var prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            GunsawMultiplayerPlugin.LogInfo("Remote character prefab not found: " + prefabPath);
            return;
        }

        DestroyRemote();
        remoteName = sanitizedName;
        GameObject avatar = null;
        remoteAvatarCreationDepth++;
        try
        {
            avatar = Instantiate(prefab, localBody.transform.position + new Vector3(2f, 0f, 0f), Quaternion.identity);
            foreach (var remotePlayer in avatar.GetComponentsInChildren<PlayerScript>(true))
                DestroyImmediate(remotePlayer);
            RestoreLocalPlayerSingleton();
            avatar.AddComponent<NetworkReplica>();
        }
        finally
        {
            remoteAvatarCreationDepth--;
        }
        remoteAvatar = avatar;
        remoteAvatarParent = avatar.transform.parent;
        remotePrefabPath = prefabPath;
        remoteBody = avatar.GetComponentInChildren<BodyScript>();
        if (remoteBody == null) { Destroy(avatar); return; }
        InitializeSeasonalHats(avatar);
        RemoveReplicaScarfArtifacts(avatar);
        remoteBody.WakeUp();
        remoteBody.isPlayer = true;
        BlackoutRule.RegisterBody(remoteBody);
        remoteBody.dropWeapon = false;
        remoteBody.team = RemoteTeam(localBody);
        foreach (var chatter in avatar.GetComponentsInChildren<Chatter>(true)) DestroyImmediate(chatter);
        foreach (var ai in avatar.GetComponentsInChildren<AIScript>(true)) DestroyImmediate(ai);
        foreach (var collider in avatar.GetComponentsInChildren<Collider2D>(true))
            remoteColliderTriggers[collider] = collider.isTrigger;
        remoteRigidbodies = avatar.GetComponentsInChildren<Rigidbody2D>(true);
        remotePhysicsModeKnown = false;

        remoteTailBases.Clear();
        remoteTailBases.AddRange(GetNetworkTailBodies(remoteBody));

        remoteTails = remoteBody.tails ?? [];
        remoteTailSprites.Clear();
        foreach (var tailBase in remoteTailBases)
            remoteTailSprites.Add(tailBase == null ? new SpriteRenderer[0] : tailBase.GetComponentsInChildren<SpriteRenderer>(true));
        remoteTailRootSprites.Clear();
        foreach (var tail in remoteTails)
            remoteTailRootSprites.Add(tail == null ? new SpriteRenderer[0] : tail.GetComponentsInChildren<SpriteRenderer>(true));
        foreach (var behaviour in avatar.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
        foreach (var animator in avatar.GetComponentsInChildren<Animator>()) animator.enabled = false;
        var remoteCrystalTongue = avatar.GetComponentInChildren<CrystalTongue>(true);
        remoteCrystalTongueLine = remoteCrystalTongue == null ? null : remoteCrystalTongue.line;
        if (remoteCrystalTongueLine != null) remoteCrystalTongueLine.enabled = false;
        UpdateRemotePhysicsMode();
        CacheDismembermentVisuals();
        CreateRemoteLevitLine(avatar.transform);
        GunsawMultiplayerPlugin.LogInfo("Spawned remote avatar for " + name + ".");
    }

    internal void DestroyRemote()
    {
        if (remoteAvatar != null)
        {
            foreach (var remotePlayer in remoteAvatar.GetComponentsInChildren<PlayerScript>(true))
                DestroyImmediate(remotePlayer);
            Destroy(remoteAvatar);
            RestoreLocalPlayerSingleton();
        }
        remoteAvatar = null;
        remoteAvatarParent = null;
        remoteBody = null;
        remotePrefabPath = "";
        remoteName = "Player";
        remoteLevitLine = null;
        remoteCrystalTongueLine = null;
        remoteScarf = null;
        remoteScarfHold = null;
        remoteFires.Clear();
        remoteRigidbodies = new Rigidbody2D[0];
        remotePhysicsModeKnown = false;
        remoteTailBases.Clear();
        remoteTails = new Transform[0];
        remoteTailSprites.Clear();
        remoteTailRootSprites.Clear();
        remoteColliderTriggers.Clear();
        collisionRuleLocalBody = null;
        collisionRuleApplied = false;
        originalDismemberSprites.Clear();
        displayedDismembermentEffects.Clear();
        dismembermentVisualsInitialized = false;
        targets.Clear();
        worldTargets.Clear();
        localTargets.Clear();
        receivedFirstSnapshot = false;
        appliedWeapon = -1;
        appliedWeaponSprite = 0UL;
        appliedInventory = "";
        appliedDismembermentHash = int.MinValue;
        pendingRemoteDamage = 0f;
        remoteCanBeGrabbed = false;
        hasRemoteScaleBeforeVehicle = false;
    }

    private void CreateRemoteLevitLine(Transform avatar)
    {
        var player = PlayerScript.player;
        var source = player == null ? null : player.levitLine;
        if (source == null) return;
        var beam = new GameObject("MP Levit Beam");
        beam.transform.SetParent(avatar.root, true);
        remoteLevitLine = beam.AddComponent<LineRenderer>();
        remoteLevitLine.sharedMaterial = source.sharedMaterial;
        remoteLevitLine.widthMultiplier = source.widthMultiplier;
        remoteLevitLine.startWidth = source.startWidth;
        remoteLevitLine.endWidth = source.endWidth;
        remoteLevitLine.startColor = source.startColor;
        remoteLevitLine.endColor = source.endColor;
        remoteLevitLine.useWorldSpace = source.useWorldSpace;
        remoteLevitLine.textureMode = source.textureMode;
        remoteLevitLine.alignment = source.alignment;
        remoteLevitLine.numCapVertices = source.numCapVertices;
        remoteLevitLine.numCornerVertices = source.numCornerVertices;
        remoteLevitLine.sortingLayerID = source.sortingLayerID;
        remoteLevitLine.sortingOrder = source.sortingOrder;
        beam.SetActive(false);
    }

    internal void Apply(PlayerSnapshotPacket snapshot)
    {
        if (remoteBody == null || remoteBody.rb == null) return;
        var performanceStarted = MultiplayerPerformance.Start();
        try
        {
            var packetWriter = new PacketWriter(512);
            snapshot.Write(ref packetWriter);
            using (var reader = new BinaryReader(new MemoryStream(packetWriter.ToArray(), false)))
            {
                reader.ReadInt32();
                var flags = reader.ReadByte();
                var remoteInVehicle = (flags & 1) != 0;
                var remoteVehicleId = remoteInVehicle ? reader.ReadUInt64() : 0UL;
                var remoteVehicleDriver = (flags & 2) != 0;
                var remoteState = (BodyScript.EntityState)reader.ReadByte();
                if (remoteState < BodyScript.EntityState.Idle || remoteState > BodyScript.EntityState.MoveLeft)
                    remoteState = BodyScript.EntityState.Idle;
                var remoteVehicleAttached = SynchronizeRemoteVehicle(remoteInVehicle, remoteVehicleId, remoteVehicleDriver, remoteState);
                var remoteVehicleStreamed = remoteInVehicle && remoteBody.inVehicle &&
                    remoteBody.curVehicle != null && remoteBody.curVehicle.mainPart != null &&
                    remoteBody.curVehicle.mainPart.rb != null;

                var isRight = (flags & 4) != 0;
                var reflected = (flags & 8) != 0;

                remoteAvatar.SetActive((flags & 16) != 0);
                var remoteHeadRotation = ReadQuantizedRotation(reader);
                var hadVehicleHeadRotation = hasRemoteVehicleHeadRotation;

                hasRemoteVehicleHeadRotation = remoteVehicleStreamed;
                hasVehicleArmsTarget = remoteVehicleStreamed;
                hasRemoteVehicleReflection = remoteVehicleStreamed;
                remoteVehicleReflected = reflected;

                if (remoteVehicleStreamed)
                {
                    if (hadVehicleHeadRotation)
                    {
                        var progress = Mathf.Clamp01(
                            (Time.unscaledTime -
                             vehicleHeadStartedAt) /
                            0.10f);

                        vehicleHeadFromRotation =
                            Mathf.LerpAngle(
                                vehicleHeadFromRotation,
                                remoteVehicleHeadRotation,
                                progress);
                    }
                    else
                    {
                        vehicleHeadFromRotation = remoteHeadRotation;
                    }

                    vehicleHeadStartedAt = Time.unscaledTime;
                }

                remoteVehicleHeadRotation = remoteHeadRotation;

                if (!remoteVehicleStreamed &&
                    remoteBody.isRight != isRight)
                {
                    remoteBody.SwitchDir(true);
                }

                var limbs = remoteBody.limbs ?? [];
                var sourceVehicleRoot = Vector2.zero;
                var sourceVehicleRotation = 0f;
                if (remoteInVehicle)
                {
                    ReadVehicleRoot(reader, out sourceVehicleRoot, out sourceVehicleRotation);
                    lastAuthoritativePosition = remoteBody.rb.position;
                }
                else
                {
                    lastAuthoritativePosition = SetTarget(reader, remoteBody.rb);
                }
                hasAuthoritativePosition = true;
                var limbCount = reader.ReadUInt16();
                for (var index = 0; index < limbCount; index++)
                {
                    if (index >= limbs.Count)
                    {
                        SkipLimb(reader);
                        continue;
                    }
                    var limb = limbs[index];
                    if (remoteInVehicle) SkipLimb(reader);
                    else SetQuantizedLimbTarget(reader, limb.rb);
                }
                var tailCount = reader.ReadUInt16();
                if (!remoteVehicleStreamed)
                {
                    vehicleTailTargets.Clear();
                    vehicleTailTransformTargets.Clear();
                }

                for (var index = 0; index < tailCount; index++)
                    ReadTailTarget(reader,
                        index < remoteTailBases.Count ? remoteTailBases[index] : null,
                        index < remoteTailSprites.Count ? remoteTailSprites[index] : null,
                        remoteVehicleStreamed);
                var tailRootCount = reader.ReadUInt16();
                for (var index = 0; index < tailRootCount; index++)
                    ReadTailTarget(reader,
                        index < remoteTails.Length ? remoteTails[index] : null,
                        index < remoteTailRootSprites.Count ? remoteTailRootSprites[index] : null,
                        remoteVehicleStreamed);

                if (remoteVehicleStreamed)
                {
                    ReadVehicleArmsTarget(reader, sourceVehicleRoot, sourceVehicleRotation);
                    ReadLocalRotationImmediately(reader, remoteBody.gunTransform);
                    ReadLocalRotationImmediately(reader, remoteBody.gunAnimTransform);
                }
                else
                {
                    ReadWorldTransform(reader, remoteBody.Arms);
                    ReadLocalTransform(reader, remoteBody.gunTransform);
                    ReadLocalTransform(reader, remoteBody.gunAnimTransform);
                }

                if (remoteVehicleStreamed) ApplyVehicleHeadRotation();
                receivedFirstSnapshot = true;
            }
        }
        catch (EndOfStreamException) { }
        finally { MultiplayerPerformance.AddAvatarApply(performanceStarted); }
    }

    internal void Apply(PlayerStatePacket packet)
    {
        if (remoteBody == null) return;
        try
        {
            var packetWriter = new PacketWriter(256);
            packet.Write(ref packetWriter);
            using (var reader = new BinaryReader(new MemoryStream(packetWriter.ToArray(), false)))
            {
                var remoteHealth = reader.ReadSingle();
                if (MultiplayerSession.IsHost)
                {
                    if (remoteHealth < lastRemoteHealth)
                        pendingRemoteDamage = Mathf.Max(0f, pendingRemoteDamage - (lastRemoteHealth - remoteHealth));
                    else if (remoteHealth > lastRemoteHealth) pendingRemoteDamage = 0f;
                }
                var wasRemoteAlive = lastRemoteAlive;
                remoteBody.health = remoteHealth;
                remoteBody.isAlive = reader.ReadBoolean();
                remoteBody.stamina = reader.ReadSingle();
                remoteBody.controlState = (BodyScript.RagdollState)reader.ReadByte();
                remoteCanBeGrabbed = reader.ReadBoolean();
                lastRemoteHealth = remoteBody.health;
                lastRemoteAlive = remoteBody.isAlive;
                if (MultiplayerSession.IsHost && wasRemoteAlive && !lastRemoteAlive) remoteBody.DropAllWeapons();
                if (lastRemoteAlive && !wasRemoteAlive)
                {
                    if (MultiplayerSession.IsHost) ScoreboardSystem.NoteHostPlayerRespawn(remotePeerId);
                    ClearReplicaBloodEffects(remoteBody);
                    pendingRemoteDamage = 0f;
                }
                remoteBody.burnIntensity = reader.ReadSingle();
                remoteBody.noLegs = reader.ReadBoolean();
                remoteBody.deHeaded = reader.ReadBoolean();
                if (remoteBody.limbMat != null) remoteBody.limbMat.SetFloat("BurnIntensity", remoteBody.burnIntensity);
                var weaponSlot = reader.ReadInt32();
                var weaponAmmo = reader.ReadInt32();
                var inventoryCount = reader.ReadUInt16();
                var inventoryChanged = reader.ReadBoolean();
                var inventorySprites = new ulong[inventoryCount];
                if (inventoryChanged)
                    for (var index = 0; index < inventoryCount; index++) inventorySprites[index] = reader.ReadUInt64();
                else
                    for (var index = 0; index < inventoryCount && index < remoteBody.weapons.Count; index++)
                        inventorySprites[index] = NetworkWireId.FromString(remoteBody.weapons[index] == null ? "" : NetworkAvatarUtilities.SpriteId(remoteBody.weapons[index].sprite));
                var weaponSprite = weaponSlot >= 0 && weaponSlot < inventorySprites.Length ? inventorySprites[weaponSlot] : 0UL;
                var inventoryKey = weaponSlot + "|" + string.Join("|", inventorySprites);
                if (inventoryKey != appliedInventory)
                {
                    while (remoteBody.weapons.Count < inventorySprites.Length) remoteBody.weapons.Add(null);
                    while (remoteBody.weaponAmmos.Count < inventorySprites.Length) remoteBody.weaponAmmos.Add(0);
                    for (var index = 0; index < inventorySprites.Length; index++) remoteBody.weapons[index] = WeaponPresetProvider.FindWeaponPresetBySpriteHash(inventorySprites[index]);
                }
                if (weaponSlot >= 0 && weaponSlot < remoteBody.weaponAmmos.Count) remoteBody.weaponAmmos[weaponSlot] = weaponAmmo;
                if (weaponSlot < 0)
                {
                    if (!remoteBody.unarmed) remoteBody.ChangeToUnarmed();
                    appliedWeapon = -1; appliedWeaponSprite = 0UL; appliedInventory = inventoryKey;
                }
                else if (weaponSlot != appliedWeapon || inventoryKey != appliedInventory)
                {
                    remoteBody.ChangeWeapon(weaponSlot); appliedWeapon = weaponSlot;
                }
                if (weaponSlot >= 0 && remoteBody.weapon != null)
                {
                    remoteBody.weapon.ammo = weaponAmmo;
                    if (weaponSprite != appliedWeaponSprite || inventoryKey != appliedInventory)
                    {
                        ApplyWeaponVisual(remoteBody, weaponSprite, weaponSlot, inventorySprites);
                        appliedWeaponSprite = weaponSprite; appliedInventory = inventoryKey;
                    }
                }
                ReadLineState(reader, remoteBody.wepLaserLine, remoteBody.wepLaser);
                ReadScarfState(reader);
                if (reader.ReadBoolean()) ApplyVisualState(ReadVisualState(reader), remoteBody.transform);
                if (reader.BaseStream.Position < reader.BaseStream.Length) reader.ReadByte();
                if (reader.BaseStream.Position + sizeof(float) <= reader.BaseStream.Length)
                    remoteBody.susnessMult = Mathf.Clamp(reader.ReadSingle(), 0.25f, 1f);
                if (reader.BaseStream.Position + sizeof(float) <= reader.BaseStream.Length)
                    ApplyRemoteCharacterScale(reader.ReadSingle());
                if (reader.BaseStream.Position + sizeof(ushort) <= reader.BaseStream.Length)
                {
                    var limbs = remoteBody.limbs ?? [];
                    var count = reader.ReadUInt16();
                    var dismembermentHash = 17;
                    for (var index = 0; index < count; index++)
                    {
                        var dismembered = reader.ReadBoolean();
                        var burning = reader.ReadBoolean();
                        if (index >= limbs.Count) continue;
                        var limb = limbs[index];
                        limb.dismembered = dismembered;
                        dismembermentHash = unchecked(dismembermentHash * 31 + (dismembered ? 1 : 0));
                        SetRemoteFire(index, limb, burning);
                    }
                    if (dismembermentHash != appliedDismembermentHash)
                    {
                        appliedDismembermentHash = dismembermentHash;
                        ApplyDismembermentVisuals();
                    }
                }
            }
        }
        catch (EndOfStreamException) { }
    }

    internal void Apply(PlayerSpecialLinesPacket packet)
    {
        SetRemoteLineTarget(remoteLevitLine, remoteLevitLine == null ? null : remoteLevitLine.gameObject,
            packet.Levitator, remoteLevitInterpolation);
        SetRemoteLineTarget(remoteCrystalTongueLine, null, packet.CrystalTongue, remoteCrystalTongueInterpolation);
    }

    private void ApplyRemoteCharacterScale(float characterScale)
    {
        if (remoteBody == null || float.IsNaN(characterScale) || float.IsInfinity(characterScale)) return;
        AvatarScaleHandler.TrySet(remoteBody, characterScale);
    }

    private bool SynchronizeRemoteVehicle(bool inVehicle, ulong vehicleId, bool driver, BodyScript.EntityState state)
    {
        if (remoteBody == null) return false;
        remoteBody.CurrentState = state;
        if (!inVehicle || vehicleId == 0UL)
        {
            if (remoteBody.inVehicle && remoteBody.curVehicle != null)
            {
                var previousVehicle = remoteBody.curVehicle;
                remoteBody.ExitVehicle();
                SetRemoteVehicleCollisions(previousVehicle, false);
                DetachRemoteAvatar();
            }
            return false;
        }
        var world = GunsawMultiplayerPlugin.World;
        var vehicle = world == null ? null : world.FindVehicle(vehicleId);
        if (vehicle == null || (driver && vehicle.occupant != null && vehicle.occupant != remoteBody)) return false;
        if (!remoteBody.inVehicle || remoteBody.curVehicle != vehicle)
        {
            if (remoteBody.inVehicle && remoteBody.curVehicle != null)
            {
                var previousVehicle = remoteBody.curVehicle;
                remoteBody.ExitVehicle();
                SetRemoteVehicleCollisions(previousVehicle, false);
                DetachRemoteAvatar();
            }
            AttachRemoteToVehicle(vehicle, driver);
        }
        if (!remoteBody.inVehicle || remoteBody.curVehicle != vehicle) return false;
        return true;
    }

    private void AttachRemoteToVehicle(VehicleBase vehicle, bool driver)
    {
        if (vehicle == null || remoteBody == null || vehicle.mainPart == null ||
            vehicle.mainPart.rb == null || vehicle.seatPos == null) return;

        remoteScaleBeforeVehicle = remoteBody.transform.localScale;
        hasRemoteScaleBeforeVehicle = true;

        if (driver)
        {
            vehicle.occupant = remoteBody;
            vehicle.occupJoint = null;
            KartPassengers.RegisterDriver(vehicle, remoteBody);
        }
        else KartPassengers.Attach(vehicle, remoteBody, false);

        remoteBody.inVehicle = true;
        remoteBody.curVehicle = vehicle;
        remoteBody.rb.freezeRotation = true;
        SetRemoteVehicleRigPhysics(false);
        remoteBody.enabled = false;

        foreach (var limb in remoteAvatar.GetComponentsInChildren<LimbScript>(true)) limb.enabled = false;

        if (remoteBody.BodyAnimator != null)
        {
            remoteBody.BodyAnimator.enabled = true;
            remoteBody.BodyAnimator.SetBool("inVehicle", true);
            remoteBody.BodyAnimator.Play("PlayerSit");
        }

        if (remoteAvatar != null)
        {
            var offset = (Vector3)KartPassengers.SeatPosition(vehicle, remoteBody) - remoteBody.transform.position;
            remoteAvatar.transform.SetParent(vehicle.mainPart.transform, true);
            remoteAvatar.transform.position += offset;
        }

        remoteBody.BodyAnimator.Update(0f);
        SnapRemoteVehicleLimbs();
        targets.Clear();
        worldTargets.Clear();
        localTargets.Clear();
        CaptureRemoteVehicleRigPose(vehicle);
        SetRemoteVehicleCollisions(vehicle, true);
    }

    private void MaintainRemoteVehiclePose()
    {
        if (remoteBody == null || !remoteBody.inVehicle || remoteBody.BodyAnimator == null) return;
        var animator = remoteBody.BodyAnimator;
        animator.enabled = true;
        animator.SetBool("inVehicle", true);
        animator.Play("PlayerSit", 0, 0f);
        animator.Update(0f);
        remoteBody.standAnimForce = 1f;
    }

    private void SnapRemoteVehicleLimbs()
    {
        if (remoteBody == null) return;
        foreach (var limb in remoteBody.limbs)
        {
            if (limb == null || limb.dismembered || limb.rb == null || limb.transformToFollow == null) continue;
            var position = limb.transformToFollow.localPosition;
            if (limb.reverseXPosWhenFlipped && !remoteBody.isRight) position = -position;
            limb.transform.localPosition = position;
            limb.transform.localRotation = Quaternion.Euler(0f, 0f, limb.transformToFollow.localEulerAngles.z);
            limb.rb.position = limb.transform.position;
            limb.rb.rotation = limb.transform.eulerAngles.z;
            limb.rb.velocity = Vector2.zero;
            limb.rb.angularVelocity = 0f;
        }
    }

    private void SetRemoteVehicleRigPhysics(bool simulated)
    {
        foreach (var body in remoteRigidbodies)
        {
            if (body == null) continue;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = simulated;
        }
    }

    private void CaptureRemoteVehicleRigPose(VehicleBase vehicle)
    {
        hasVehicleArmsTarget = remoteBody != null && remoteBody.Arms != null && vehicle != null &&
            vehicle.mainPart != null && vehicle.mainPart.rb != null;

        vehicleTailTargets.Clear();

        if (!hasVehicleArmsTarget) return;
        vehicleArmsLocalPosition = vehicle.mainPart.transform.InverseTransformPoint(remoteBody.Arms.position);
        vehicleArmsLocalRotation = Mathf.DeltaAngle(vehicle.mainPart.rb.rotation, remoteBody.Arms.eulerAngles.z);
        vehicleArmsFromLocalPosition = vehicleArmsLocalPosition;
        vehicleArmsFromLocalRotation = vehicleArmsLocalRotation;
        vehicleArmsStartedAt = Time.unscaledTime;
        foreach (var tailBody in remoteTailBases)
        {
            if (tailBody == null)
                continue;

            var localRotation = Mathf.DeltaAngle(
                vehicle.mainPart.rb.rotation,
                tailBody.rotation);

            vehicleTailTargets.Add(new VehicleTailTarget
            {
                Body = tailBody,

                LocalRotation = localRotation,
                FromLocalRotation = localRotation,
                StartedAt = Time.unscaledTime
            });
        }

        for (var index = 0;
             index < vehicleTailTransformTargets.Count;
             index++)
        {
            var state =
                vehicleTailTransformTargets[index];

            if (state.Transform == null)
                continue;

            var localRotation = Mathf.DeltaAngle(
                vehicle.mainPart.rb.rotation,
                state.Transform.eulerAngles.z);

            state.LocalRotation = localRotation;
            state.FromLocalRotation = localRotation;
            state.StartedAt = Time.unscaledTime;

            vehicleTailTransformTargets[index] = state;
        }
    }

    private void DetachRemoteAvatar()
    {
        if (remoteBody != null)
        {
            remoteBody.inVehicle = false;
            remoteBody.curVehicle = null;
            remoteBody.standAnimForce = 1f;
            if (remoteBody.BodyAnimator != null)
            {
                remoteBody.BodyAnimator.enabled = true;
                remoteBody.BodyAnimator.SetBool("inVehicle", false);
                remoteBody.BodyAnimator.Rebind();
                remoteBody.BodyAnimator.Update(0f);
            }

            if (hasRemoteScaleBeforeVehicle)
            {
                remoteBody.transform.localScale = remoteScaleBeforeVehicle;
                hasRemoteScaleBeforeVehicle = false;
            }


            SnapRemoteVehicleLimbs();
            remoteBody.enabled = false;
        }
        if (remoteAvatar != null)
            foreach (var limb in remoteAvatar.GetComponentsInChildren<LimbScript>(true)) limb.enabled = false;
        if (remoteBody != null && remoteBody.BodyAnimator != null)
            remoteBody.BodyAnimator.enabled = false;
        if (remoteAvatar != null) remoteAvatar.transform.SetParent(remoteAvatarParent, true);
        remotePhysicsModeKnown = false;
        UpdateRemotePhysicsMode();
        targets.Clear();
        worldTargets.Clear();
        localTargets.Clear();
        vehicleTailTargets.Clear();
        vehicleTailTransformTargets.Clear();
        hasRemoteVehicleHeadRotation = false;
        hasVehicleArmsTarget = false;
    }

    private void MaintainRemoteVehicleAttachment()
    {
        if (remoteBody == null || !remoteBody.inVehicle || remoteBody.curVehicle == null ||
            remoteBody.curVehicle.seatPos == null || remoteBody.curVehicle.mainPart == null ||
            remoteBody.curVehicle.mainPart.rb == null || remoteBody.rb == null) return;
        if (remoteAvatar == null || remoteAvatar.transform.parent == remoteBody.curVehicle.mainPart.transform)
            return;
        var offset = (Vector3)KartPassengers.SeatPosition(remoteBody.curVehicle, remoteBody) - remoteBody.transform.position;
        remoteAvatar.transform.SetParent(remoteBody.curVehicle.mainPart.transform, true);
        remoteAvatar.transform.position += offset;
    }

    private void ApplyVehicleHeadRotation()
    {
        if (remoteBody == null || remoteBody.headTransform == null || remoteBody.curVehicle == null ||
            remoteBody.curVehicle.mainPart == null || remoteBody.curVehicle.mainPart.rb == null) return;
        var progress = Mathf.Clamp01((Time.unscaledTime - vehicleHeadStartedAt) / 0.10f);
        var relativeRotation = Mathf.LerpAngle(vehicleHeadFromRotation, remoteVehicleHeadRotation, progress);
        remoteBody.headTransform.rotation = Quaternion.Euler(0f, 0f,
            remoteBody.curVehicle.mainPart.rb.rotation + relativeRotation);
    }

    private void ReadVehicleArmsTarget(BinaryReader reader, Vector2 sourceRoot, float sourceRootRotation)
    {
        var position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        var rotation = ReadQuantizedRotation(reader);
        if (remoteBody == null || remoteBody.curVehicle == null || remoteBody.curVehicle.mainPart == null ||
            remoteBody.curVehicle.mainPart.rb == null) return;
        var vehicle = remoteBody.curVehicle;
        var angle = vehicle.mainPart.rb.rotation - sourceRootRotation;
        var target = KartPassengers.SeatPosition(vehicle, remoteBody) +
            (Vector2)(Quaternion.Euler(0f, 0f, angle) * (position - sourceRoot));
        var localPosition = vehicle.mainPart.transform.InverseTransformPoint(target);
        var localRotation = Mathf.DeltaAngle(vehicle.mainPart.rb.rotation,
            vehicle.mainPart.rb.rotation + Mathf.DeltaAngle(sourceRootRotation, rotation));
        var progress = Mathf.Clamp01((Time.unscaledTime - vehicleArmsStartedAt) / 0.02f);
        vehicleArmsFromLocalPosition = Vector2.Lerp(vehicleArmsFromLocalPosition, vehicleArmsLocalPosition, progress);
        vehicleArmsFromLocalRotation = Mathf.LerpAngle(vehicleArmsFromLocalRotation, vehicleArmsLocalRotation, progress);
        vehicleArmsLocalPosition = localPosition;
        vehicleArmsLocalRotation = localRotation;
        vehicleArmsStartedAt = Time.unscaledTime;
        hasVehicleArmsTarget = true;
    }

    private void ApplyVehicleArmsTarget()
    {
        if (!hasVehicleArmsTarget ||
            remoteBody == null ||
            !remoteBody.inVehicle ||
            remoteBody.Arms == null ||
            remoteBody.curVehicle == null ||
            remoteBody.curVehicle.mainPart == null ||
            remoteBody.curVehicle.mainPart.rb == null)
            return;

        var progress = Mathf.Clamp01(
            (Time.unscaledTime - vehicleArmsStartedAt) / 0.02f);

        var localRotation = Mathf.LerpAngle(
            vehicleArmsFromLocalRotation,
            vehicleArmsLocalRotation,
            progress);

        var vehicle = remoteBody.curVehicle;

        vehicleArmsTargetRotation =
            vehicle.mainPart.rb.rotation + localRotation;

        remoteBody.Arms.rotation = Quaternion.Euler(
            0f,
            0f,
            vehicleArmsTargetRotation);
    }

    private void ApplyVehicleTailTargets()
    {
        if (remoteBody == null ||
            !remoteBody.inVehicle ||
            remoteBody.curVehicle == null ||
            remoteBody.curVehicle.mainPart == null ||
            remoteBody.curVehicle.mainPart.rb == null)
            return;

        var vehicle =
            remoteBody.curVehicle;

        var vehicleRotation =
            vehicle.mainPart.rb.rotation;

        var seatPosition = KartPassengers.SeatPosition(vehicle, remoteBody)
                           -
                           (Vector2)vehicle.mainPart.transform.right * 0.15f; // оффсет так называемой попы (мне кажется я делаю что-то не так)

        foreach (var target in vehicleTailTransformTargets)
        {
            if (target.Transform == null)
                continue;

            var position = target.Transform.position;

            position.x = seatPosition.x;
            position.y = seatPosition.y;

            target.Transform.position = position;
            var progress = Mathf.Clamp01((Time.unscaledTime - target.StartedAt) / SnapshotInterval);
            var localRotation = Mathf.LerpAngle(target.FromLocalRotation, target.LocalRotation, progress);
            target.Transform.rotation = Quaternion.Euler( 0f, 0f, vehicleRotation + localRotation);
        }

        foreach (var target in vehicleTailTargets)
        {
            if (target.Body == null)
                continue;

            var progress = Mathf.Clamp01((Time.unscaledTime - target.StartedAt) / SnapshotInterval);
            var localRotation = Mathf.LerpAngle(target.FromLocalRotation, target.LocalRotation, progress);
            var worldRotation = vehicleRotation + localRotation;

            target.Body.transform.rotation = Quaternion.Euler(0f, 0f, worldRotation);
            target.Body.rotation = worldRotation;
            target.Body.velocity = Vector2.zero;
            target.Body.angularVelocity = 0f;
        }
    }

    private void SetRemoteVehicleCollisions(VehicleBase vehicle, bool ignored)
    {
        if (vehicle == null) return;
        var vehicleColliders = vehicle.GetComponentsInChildren<Collider2D>(true);
        foreach (var remoteCollider in remoteColliderTriggers.Keys)
        {
            if (remoteCollider == null) continue;
            foreach (var vehicleCollider in vehicleColliders)
                if (vehicleCollider != null)
                    Physics2D.IgnoreCollision(remoteCollider, vehicleCollider, ignored);
        }
    }

    internal void SpawnRemoteDeathDropIfNeeded(float amount)
    {
        pendingRemoteDamage += amount;
    }

    internal void UpdateRemotePhysicsMode()
    {
        if (remoteAvatar == null) return;
        var player = PlayerScript.player;
        if (remoteBody != null && player != null && player.bodyScript != null)
            remoteBody.team = RemoteTeam(player.bodyScript);

        ApplyPlayerCollisionRule(player == null ? null : player.bodyScript);

        if (remotePhysicsModeKnown) return;
        remotePhysicsModeKnown = true;

        foreach (var body in remoteRigidbodies)
        {
            if (body == null) continue;
            body.simulated = true;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        foreach (var pair in remoteColliderTriggers)
            if (pair.Key != null) pair.Key.isTrigger = pair.Value;

        if (MultiplayerSession.IsHost)
            foreach (var prop in FindObjectsOfType<Rigidbody2D>())
                if (prop != null && (prop.GetComponentInParent<CrateScript>() != null || prop.GetComponentInParent<DroppedWeapon>() != null))
                    IgnoreRemotePlayerPropCollisions(prop);
    }

    internal bool TryRemotePart(Rigidbody2D rigidbody, out byte kind, out short index)
    {
        kind = 0;
        index = 0;
        if (remoteBody == null) return false;
        if (rigidbody == remoteBody.rb) return true;
        var limbs = remoteBody.limbs ?? [];
        for (var position = 0; position < limbs.Count; position++)
            if (limbs[position].rb == rigidbody)
            {
                kind = 1;
                index = (short)position;
                return true;
            }
        var tails = GetNetworkTailBodies(remoteBody);
        for (var position = 0; position < tails.Count; position++)
            if (tails[position] == rigidbody)
            {
                kind = 2;
                index = (short)position;
                return true;
            }
        return false;
    }

    internal void PlayRemoteVelvetWeb(VelvetWebPacket packet)
    {
        if (remoteBody == null) return;
        var origin = new Vector2(packet.PositionX, packet.PositionY);
        var direction = new Vector2(packet.DirectionX, packet.DirectionY);
        if (!NetworkAvatarUtilities.IsFinite(origin.x) || !NetworkAvatarUtilities.IsFinite(origin.y) || !NetworkAvatarUtilities.IsFinite(direction.x) ||
            !NetworkAvatarUtilities.IsFinite(direction.y) || direction.sqrMagnitude < 0.01f) return;
        var velvet = remoteBody.GetComponent<VelvetScript>();
        var prefab = velvet == null ? null : velvet.spawnPrefab;
        if (prefab == null) return;
        var visual = Instantiate(prefab, origin, Quaternion.identity);
        var web = visual.GetComponent<WebScript>();
        if (web == null)
        {
            Destroy(visual);
            return;
        }
        var speed = web.speed;
        var groundSprite = web.groundSprite;
        var bodySprite = web.bodySprite;
        visual.name = "MP Velvet Web";
        visual.transform.right = direction.normalized;
        foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        foreach (var collider in visual.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
        foreach (var rigidbody in visual.GetComponentsInChildren<Rigidbody2D>(true)) rigidbody.simulated = false;
        foreach (var source in visual.GetComponentsInChildren<AudioSource>(true)) source.enabled = false;
        var splash = Resources.Load<GameObject>("Spawnables/WebSplashSpit");
        if (splash != null)
        {
            var effect = Instantiate(splash, origin, Quaternion.identity);
            effect.transform.right = direction.normalized;
            Destroy(effect, 10f);
        }
        var sound = Resources.Load<AudioClip>("Sounds/spit");
        if (sound != null) Sound.Play(sound, origin);
        StartCoroutine(MoveRemoteVelvetWeb(visual, speed, groundSprite, bodySprite, remoteBody));
    }

    private IEnumerator MoveRemoteVelvetWeb(GameObject visual, float speed, Sprite groundSprite, Sprite bodySprite, BodyScript ignoredBody)
    {
        var downwardVelocity = 0f;
        var remaining = 5f;
        while (visual != null && remaining > 0f)
        {
            Transform visualTransform = visual.transform;
            float deltaTime = Time.deltaTime;
            visualTransform.position += visualTransform.right * (speed * deltaTime);
            downwardVelocity += Physics2D.gravity.y * (0.1f * deltaTime);
            visualTransform.position += Vector3.up * (downwardVelocity * deltaTime);

            var velocity = ((Vector2)visual.transform.right * speed + Vector2.up * downwardVelocity).normalized;
            visual.transform.right = velocity;
            visual.transform.eulerAngles -= new Vector3(0f, 0f, Time.deltaTime * 10f);
            RaycastHit2D hit = default(RaycastHit2D);
            foreach (var candidate in Physics2D.RaycastAll(visual.transform.position, visual.transform.right, 1f))
            {
                if (candidate.collider == null || candidate.collider.isTrigger) continue;
                var body = candidate.collider.GetComponentInParent<BodyScript>();
                var limb = candidate.collider.GetComponentInParent<LimbScript>();
                var hitBody = body ?? (limb == null ? null : limb.body);
                if (hitBody != null && (hitBody == ignoredBody || hitBody.team == ignoredBody.team)) continue;
                if (candidate.collider.gameObject.layer != LayerMask.NameToLayer("Ground") &&
                    body == null && limb == null) continue;
                hit = candidate;
                break;
            }
            if (hit.collider != null)
            {
                CreateRemoteVelvetWebImpact(visual, hit, groundSprite, bodySprite);
                yield break;
            }
            remaining -= Time.deltaTime;
            yield return null;
        }
        if (visual != null) Destroy(visual);
    }

    private void CreateRemoteVelvetWebImpact(GameObject visual, RaycastHit2D hit,
        Sprite groundSprite, Sprite bodySprite)
    {
        var splash = Resources.Load<GameObject>("Spawnables/WebSplashHit");
        if (splash != null) Destroy(Instantiate(splash, hit.point, Quaternion.identity), 10f);
        var renderer = visual.GetComponent<SpriteRenderer>();
        var limb = hit.collider.GetComponentInParent<LimbScript>();
        var onGround = hit.collider.gameObject.layer == LayerMask.NameToLayer("Ground");
        if (onGround)
        {
            visual.transform.position = hit.point;
            visual.transform.up = hit.normal;
            visual.transform.SetParent(hit.collider.transform, true);
            if (renderer != null)
            {
                renderer.sprite = groundSprite;
                renderer.flipX = UnityEngine.Random.Range(0f, 1f) > 0.5f;
            }
        }
        else if (limb != null)
        {
            visual.transform.SetParent(limb.transform, false);
            visual.transform.localPosition = new Vector2(UnityEngine.Random.Range(-0.1f, 0.1f),
                UnityEngine.Random.Range(-0.1f, 0.1f));
            visual.transform.localScale = new Vector2(UnityEngine.Random.Range(0.7f, 1.3f),
                UnityEngine.Random.Range(0.7f, 1.3f));
            visual.transform.eulerAngles = new Vector3(0f, 0f, UnityEngine.Random.Range(0f, 360f));
            if (renderer != null)
            {
                var limbRenderer = limb.GetComponent<SpriteRenderer>();
                renderer.sprite = bodySprite;
                renderer.flipX = UnityEngine.Random.Range(0f, 1f) > 0.5f;
                renderer.flipY = UnityEngine.Random.Range(0f, 1f) > 0.5f;
                if (limbRenderer != null)
                {
                    renderer.sortingLayerID = limbRenderer.sortingLayerID;
                    renderer.sortingOrder = limbRenderer.sortingOrder + 1;
                }
            }
        }
        else
        {
            Destroy(visual);
            return;
        }
        var sound = Resources.Load<AudioClip>("Sounds/webSplat");
        if (sound != null) Sound.Play(sound, visual.transform.position);
        Destroy(visual, 20f);
    }

    internal void PlayRemotePlayerSound(PlayerSoundPacket packet)
    {
        if ((packet.SoundId & 0xf0000000u) == 0x80000000u)
        {
            var clip = ResourceManager.main == null ? null : ResourceManager.main.FootSound((SType)(packet.SoundId & 0xff));
            if (clip != null)
                Sound.Play(clip, new Vector2(packet.PositionX, packet.PositionY), false, true, null, packet.Volume / 64f, packet.Pitch / 64f);
            return;
        }

        if ((packet.SoundId & 0xff000000u) == 0x10000000u || (packet.SoundId & 0xff000000u) == 0x11000000u)
        {
            var sounds = (packet.SoundId & 0xff000000u) == 0x11000000u ? remoteBody?.deathNoises : remoteBody?.painNoises;
            var index = (int)(packet.SoundId & 0x00ffffffu);
            var clip = sounds != null && index < sounds.Count ? sounds[index] : null;
            if (clip != null)
                Sound.Play(clip, new Vector2(packet.PositionX, packet.PositionY), false, false, null, packet.Volume / 64f, packet.Pitch / 64f);
            return;
        }

        if ((packet.SoundId & 0xffff0000u) == 0x20000000u)
        {
            BuildAnimatedSoundCatalog();
            var index = (ushort)packet.SoundId;
            if (index < animatedSoundNames.Count)
            {
                var clip = Resources.Load<AudioClip>("Sounds/" + animatedSoundNames[index]);
                if (clip != null)
                    Sound.Play(clip, new Vector2(packet.PositionX, packet.PositionY), false, false, null, packet.Volume / 64f, packet.Pitch / 64f);
            }
            return;
        }

        if ((packet.SoundId & 0xff000000u) == 0x50000000u)
        {
            if (remoteTelekinesisSound == null)
            {
                var root = remoteAvatar == null ? gameObject : remoteAvatar;
                remoteTelekinesisSound = root.GetComponent<AudioSource>() ?? root.AddComponent<AudioSource>();
                remoteTelekinesisSound.clip = Resources.Load<AudioClip>("Sounds/Energy");
                remoteTelekinesisSound.loop = true;
                remoteTelekinesisSound.spatialBlend = 1f;
            }
            remoteTelekinesisSound.transform.position = new Vector2(packet.PositionX, packet.PositionY);
            remoteTelekinesisSound.volume = packet.Volume / 64f;
            remoteTelekinesisSound.pitch = packet.Pitch / 64f;
            if (packet.Volume > 0 && !remoteTelekinesisSound.isPlaying) remoteTelekinesisSound.Play();
            else if (packet.Volume == 0 && remoteTelekinesisSound.isPlaying) remoteTelekinesisSound.Stop();
            return;
        }

        if ((packet.SoundId & 0xff000000u) == 0x40000000u)
        {
            var name = packet.SoundId == 0x40000001u ? "Kick" : packet.SoundId == 0x40000002u ? "Hit" : packet.SoundId == 0x40000003u ? "boneCrunch" : "wepPickup";
            var clip = Resources.Load<AudioClip>("Sounds/" + name);
            if (clip != null)
                Sound.Play(clip, new Vector2(packet.PositionX, packet.PositionY), false, false, null, packet.Volume / 64f, packet.Pitch / 64f);
            return;
        }

        LoadPlayerSoundClips();
        AudioClip sound;
        if (playerSoundClips.TryGetValue(packet.SoundId, out sound) && sound != null)
            Sound.Play(sound, new Vector2(packet.PositionX, packet.PositionY), false, false, null, packet.Volume / 64f, packet.Pitch / 64f);
    }

    internal void PlayRemoteShot(ShotVisualPacket packet)
    {
        var origin = new Vector2(packet.OriginX, packet.OriginY);
        var direction = new Vector2(packet.DirectionX, packet.DirectionY);
        var up = new Vector2(packet.UpX, packet.UpY);
        var sprite = packet.WeaponSprite;
        var npcShot = packet.IsNpcShot;
        var spreadSeed = packet.SpreadSeed;
        var exactDirections = packet.ExactDirections;

        if (!NetworkAvatarUtilities.IsFinite(origin.x) || !NetworkAvatarUtilities.IsFinite(origin.y) || !NetworkAvatarUtilities.IsFinite(direction.x) ||
            !NetworkAvatarUtilities.IsFinite(direction.y) || !NetworkAvatarUtilities.IsFinite(up.x) || !NetworkAvatarUtilities.IsFinite(up.y) ||
            direction.sqrMagnitude < 0.01f || up.sqrMagnitude < 0.01f) return;
        direction.Normalize();
        up.Normalize();
        var preset = WeaponPresetProvider.FindWeaponPreset(sprite);
        if (preset == null && remoteBody != null && remoteBody.weapon != null)
            preset = remoteBody.weapon.stats;
        if (preset == null) return;

        if (preset.fireSound != null)
            Sound.Play(preset.fireSound, origin, false, false, null, 1f, 1f);

        if (preset.muzzleFlash != null)
        {
            var flash = Instantiate(preset.muzzleFlash, origin, Quaternion.identity);
            Destroy(flash, 0.4f);
        }

        if (preset.shootType != 1 && preset.range == 0f && remoteBody != null &&
            CustomWeaponsCompatibility.TryPlayRemoteMelee(preset, remoteBody.weapon, remoteBody)) return;

        if (preset.shootType == 1)
        {
            if (CustomWeaponsCompatibility.TryGetProjectileVisual(preset, out _))
            {
                var projectileCount = Mathf.Clamp(preset.bulletAmount, 1, 64);
                for (var index = 0; index < projectileCount; index++)
                {
                    var exactDirection = index < exactDirections.Length
                        ? new Vector2(exactDirections[index].X, exactDirections[index].Y)
                        : Vector2.zero;
                    var projectileDirection = exactDirection.sqrMagnitude > 0.01f
                        ? exactDirection.normalized
                        : (direction + up * (preset.bulletSpread * NetworkAvatarUtilities.SpreadValue(spreadSeed, index))).normalized;
                    PlayRemoteProjectile(preset, origin, projectileDirection, !npcShot);
                }
                return;
            }
            PlayRemoteProjectile(preset, origin, direction, !npcShot);
            return;
        }

        var count = Mathf.Clamp(preset.bulletAmount, 1, 12);
        for (var index = 0; index < count; index++)
        {
            Vector2 exactDirection = Vector2.zero;

            if (index < exactDirections.Length)
            {
                ShotVisualDirection dir = exactDirections[index];
                exactDirection = new Vector2(dir.X, dir.Y);
            }

            Vector2 shotDirection = exactDirection.sqrMagnitude > 0.01f
                ? exactDirection.normalized
                : (direction + up * (preset.bulletSpread * NetworkAvatarUtilities.SpreadValue(spreadSeed, index))).normalized;

            CreateRemoteTracer(preset, origin, FindRemoteShotEnd(origin, shotDirection, !npcShot));
            CreateRemoteBulletImpact(preset, origin, shotDirection, !npcShot);
        }
    }

    private void PlayRemoteCasing()
    {
        if (remoteBody != null && remoteBody.weapon != null && remoteBody.weapon.casing != null)
            remoteBody.weapon.DoCasing();
    }

    internal void PlayRemoteReloadEffect(ReloadEffectPacket packet)
    {
        if (packet.Mag)
            CreateRemoteDroppedMagazine();
        else
            PlayRemoteCasing();
    }

    private void CreateRemoteDroppedMagazine()
    {
        var body = remoteBody;
        var weapon = body == null ? null : body.weapon;
        var stats = weapon == null ? null : weapon.stats;
        var sprite = stats == null ? null : stats.magSprite;
        if (weapon == null || sprite == null) return;

        var prefab = Resources.Load<GameObject>("Spawnables/DropMag");
        if (prefab == null) return;
        var magazine = Instantiate(prefab, weapon.transform.TransformPoint(stats.magPosition), weapon.transform.rotation);
        if (magazine == null) return;

        if (!body.isRight) magazine.transform.localScale = new Vector2(-1f, 1f);
        var rigidbody = magazine.GetComponent<Rigidbody2D>();
        if (rigidbody != null)
        {
            rigidbody.velocity = body.lastMoveDir;
            rigidbody.angularVelocity = UnityEngine.Random.Range(-25f, 25f);
        }
        var renderer = magazine.GetComponent<SpriteRenderer>();
        if (renderer != null) renderer.sprite = sprite;
        var collider = magazine.GetComponent<BoxCollider2D>();
        if (collider != null && sprite.texture != null && sprite.pixelsPerUnit > 0f)
            collider.size = new Vector2(sprite.texture.width / sprite.pixelsPerUnit,
                sprite.texture.height / sprite.pixelsPerUnit);
        Destroy(magazine, 30f);
    }

    private void PlayRemoteProjectile(WeaponPreset preset, Vector2 origin, Vector2 direction, bool ignoreRemoteAvatar)
    {
        if (CustomWeaponsCompatibility.TryGetProjectileVisual(preset, out var customProjectile))
        {
            var customVisual = new GameObject("MP Custom Projectile Visual");
            customVisual.transform.position = origin;
            customVisual.transform.right = direction;
            customVisual.AddComponent<SpriteRenderer>().sprite = customProjectile.Sprite;
            remoteProjectiles.Enqueue(new RemoteProjectileVisual { Visual = customVisual, ExpiresAt = Time.unscaledTime + customProjectile.Lifetime });
            StartCoroutine(MoveRemoteCustomProjectile(customVisual, direction, customProjectile.Speed, customProjectile.Lifetime, customProjectile.Ricochets, ignoreRemoteAvatar));
            return;
        }

        GameObject visual = null;
        GameObject impactEffect = null;
        AudioClip explosionSound = null;
        var speed = 22f;
        var speedIncrease = 0f;
        var grenadeGravityScale = 1f;
        var ballisticGrenade = false;
        if (preset.tracerLine != null)
        {
            visual = Instantiate(preset.tracerLine, origin, Quaternion.identity);
            visual.name = "MP Projectile Visual";
            visual.transform.right = direction;
            BlackoutRule.MakeAlwaysBright(visual);
            var rocket = visual.GetComponentInChildren<RocketProjectile>(true);
            if (rocket != null && rocket.moveSpeed > 0f) speed = rocket.moveSpeed;
            if (rocket != null) speedIncrease = rocket.moveSpeedSpeedUp;
            if (rocket != null)
            {
                impactEffect = rocket.objOnDestroy;
                explosionSound = rocket.sound;
            }
            var grenade = visual.GetComponentInChildren<GrenadeScript>(true);
            if (grenade != null && grenade.startSpeed > 0f) speed = grenade.startSpeed;
            if (grenade != null)
            {
                impactEffect = grenade.objOnDestroy;
                explosionSound = grenade.explosionSound;
                var grenadeBody = grenade.GetComponent<Rigidbody2D>();
                if (grenadeBody != null) grenadeGravityScale = grenadeBody.gravityScale;
                ballisticGrenade = true;
            }
            foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var collider in visual.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            foreach (var rigidbody in visual.GetComponentsInChildren<Rigidbody2D>(true)) rigidbody.simulated = false;
            foreach (var source in visual.GetComponentsInChildren<AudioSource>(true)) source.enabled = false;
        }
        if (visual == null)
        {
            visual = new GameObject("MP Projectile Visual");
            visual.transform.position = origin;
            visual.transform.right = direction;
            var line = AddFallbackTracer(visual);
            line.SetPosition(0, origin - direction * 0.7f);
            line.SetPosition(1, origin);
        }
        remoteProjectiles.Enqueue(new RemoteProjectileVisual
        {
            Visual = visual,
            ImpactEffect = impactEffect,
            ExplosionSound = explosionSound,
            ExpiresAt = Time.unscaledTime + 5f
        });
        if (ballisticGrenade)
            StartCoroutine(MoveRemoteGrenade(visual, direction * speed, grenadeGravityScale,
                ignoreRemoteAvatar));
        else
            StartCoroutine(MoveRemoteProjectile(visual, direction,
                FindRemoteShotEnd(origin, direction, ignoreRemoteAvatar), speed, speedIncrease));
    }

    private IEnumerator MoveRemoteProjectile(GameObject visual, Vector2 direction, Vector2 end,
        float speed, float speedIncrease)
    {
        var maximumLifetime = 2f;
        while (visual != null && maximumLifetime > 0f)
        {
            var current = (Vector2)visual.transform.position;
            var step = speed * Time.deltaTime;
            if (Vector2.Distance(current, end) <= step)
            {
                visual.transform.position = end;
                break;
            }
            visual.transform.position = current + direction * step;
            speed += speedIncrease * Time.deltaTime;
            maximumLifetime -= Time.deltaTime;
            yield return null;
        }
        if (visual != null) Destroy(visual);
    }

    private IEnumerator MoveRemoteGrenade(GameObject visual, Vector2 velocity, float gravityScale,
        bool ignoreRemoteAvatar)
    {
        var maximumLifetime = 5f;
        while (visual != null && maximumLifetime > 0f)
        {
            var current = (Vector2)visual.transform.position;
            velocity += Physics2D.gravity * gravityScale * Time.deltaTime;
            var next = current + velocity * Time.deltaTime;
            var hit = FindRemoteProjectileCollision(current, next, ignoreRemoteAvatar);
            visual.transform.position = hit.HasValue ? hit.Value : next;
            if (velocity.sqrMagnitude > 0.01f) visual.transform.right = velocity;
            if (hit.HasValue)
            {
                velocity = Vector2.zero;
            }
            maximumLifetime -= Time.deltaTime;
            yield return null;
        }
        if (visual != null) Destroy(visual);
    }

    private IEnumerator MoveRemoteCustomProjectile(GameObject visual, Vector2 direction, float speed,
        float lifetime, int ricochets, bool ignoreRemoteAvatar)
    {
        while (visual != null && lifetime > 0f)
        {
            var position = (Vector2)visual.transform.position;
            var distance = speed * Time.deltaTime;
            RaycastHit2D collision = default;
            foreach (var hit in Physics2D.RaycastAll(position, direction, distance))
            {
                var collider = hit.collider;
                if (collider == null || collider.isTrigger || (ignoreRemoteAvatar && remoteAvatar != null && collider.transform.IsChildOf(remoteAvatar.transform))) continue;
                collision = hit;
                break;
            }
            if (collision.collider == null) visual.transform.position = position + direction * distance;
            else
            {
                visual.transform.position = collision.point;
                var layer = collision.collider.gameObject.layer;
                if (ricochets <= 0 || (layer != LayerMask.NameToLayer("Ground") && layer != LayerMask.NameToLayer("Default"))) break;
                ricochets--;
                direction = Vector2.Reflect(direction, collision.normal).normalized;
                visual.transform.position = collision.point + direction * 0.01f;
                visual.transform.right = direction;
            }
            lifetime -= Time.deltaTime;
            yield return null;
        }
        if (visual != null) Destroy(visual);
    }

    internal void PlayRemoteProjectileImpact(ProjectileImpactPacket packet)
    {
        var position = new Vector2(packet.PositionX, packet.PositionY);
        var sprite = packet.WeaponSpriteId;
        if (!NetworkAvatarUtilities.IsFinite(position.x) || !NetworkAvatarUtilities.IsFinite(position.y)) return;

        RemoteProjectileVisual projectile = null;
        while (remoteProjectiles.Count > 0 && remoteProjectiles.Peek().ExpiresAt < Time.unscaledTime)
            remoteProjectiles.Dequeue();
        if (remoteProjectiles.Count > 0) projectile = remoteProjectiles.Dequeue();
        if (projectile == null)
        {
            var preset = WeaponPresetProvider.FindWeaponPreset(sprite);
            projectile = CreateRemoteProjectileVisualData(preset == null ? null : preset.tracerLine);
        }
        if (projectile == null) return;
        if (projectile.Visual != null) Destroy(projectile.Visual);
        CreateRemoteProjectileImpact(position, projectile.ImpactEffect, projectile.ExplosionSound, packet);
    }

    private RemoteProjectileVisual CreateRemoteProjectileVisualData(GameObject projectile)
    {
        if (projectile == null) return null;
        var result = new RemoteProjectileVisual { ExpiresAt = Time.unscaledTime + 5f };
        var rocket = projectile.GetComponentInChildren<RocketProjectile>(true);
        if (rocket != null)
        {
            result.ImpactEffect = rocket.objOnDestroy;
            result.ExplosionSound = rocket.sound;
        }
        var grenade = projectile.GetComponentInChildren<GrenadeScript>(true);
        if (grenade != null)
        {
            result.ImpactEffect = grenade.objOnDestroy;
            result.ExplosionSound = grenade.explosionSound;
        }
        return rocket == null && grenade == null ? null : result;
    }

    private void CreateRemoteProjectileImpact(Vector2 position, GameObject impactEffect, AudioClip explosionSound,
        ProjectileImpactPacket packet)
    {
        if (impactEffect != null)
        {
            var effect = Instantiate(impactEffect, position, Quaternion.identity);
            BlackoutRule.MakeAlwaysBright(effect);
            foreach (var projectile in effect.GetComponentsInChildren<RocketProjectile>(true)) projectile.enabled = false;
            foreach (var grenade in effect.GetComponentsInChildren<GrenadeScript>(true)) grenade.enabled = false;
            foreach (var collider in effect.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            foreach (var rigidbody in effect.GetComponentsInChildren<Rigidbody2D>(true)) rigidbody.simulated = false;
            Destroy(effect, 60f);
        }
        if (explosionSound != null) Sound.Play(explosionSound, position);
        CreateRemoteExplosionCracks(packet);
    }

    private void CreateRemoteTracer(WeaponPreset preset, Vector2 origin, Vector2 end)
    {
        GameObject visual = null;
        LineRenderer line = null;
        if (preset.tracerLine != null)
        {
            visual = Instantiate(preset.tracerLine, Vector3.zero, Quaternion.identity);
            line = visual.GetComponent<LineRenderer>();
            if (line == null) line = visual.GetComponentInChildren<LineRenderer>(true);
        }
        if (line == null)
        {
            if (visual != null) Destroy(visual);
            visual = new GameObject("MP Shot Tracer");
            line = AddFallbackTracer(visual);
        }
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.SetPosition(0, origin);
        line.SetPosition(1, end);
        Destroy(visual, 0.08f);
    }

    private Vector2 FindRemoteShotEnd(Vector2 origin, Vector2 direction, bool ignoreRemoteAvatar)
    {
        var end = origin + direction * 100f;
        var closest = 100f;
        foreach (var hit in Physics2D.RaycastAll(origin, direction, 100f))
        {
            var collider = hit.collider;
            if (collider == null || collider.isTrigger || (ignoreRemoteAvatar && remoteAvatar != null &&
                collider.transform.IsChildOf(remoteAvatar.transform))) continue;
            if (hit.distance < closest)
            {
                closest = hit.distance;
                end = hit.point;
            }
        }
        return end;
    }

    private Vector2? FindRemoteProjectileCollision(Vector2 origin, Vector2 end, bool ignoreRemoteAvatar)
    {
        var delta = end - origin;
        var distance = delta.magnitude;
        if (distance < 0.0001f) return null;
        foreach (var hit in Physics2D.RaycastAll(origin, delta / distance, distance))
        {
            var collider = hit.collider;
            if (collider == null || collider.isTrigger || (ignoreRemoteAvatar && remoteAvatar != null &&
                collider.transform.IsChildOf(remoteAvatar.transform))) continue;
            return hit.point;
        }
        return null;
    }

    private void CreateRemoteBulletImpact(WeaponPreset preset, Vector2 origin, Vector2 direction,
        bool ignoreRemoteAvatar)
    {
        if (preset == null || direction.sqrMagnitude < 0.01f) return;
        var penetration = preset.penetration;
        foreach (var hit in Physics2D.RaycastAll(origin, direction.normalized, preset.range,
            LayerMask.GetMask("Entity", "EntityStand", "Ground", "Default", "Water")))
        {
            var collider = hit.collider;
            if (collider == null || collider.isTrigger || (ignoreRemoteAvatar && remoteAvatar != null &&
                collider.transform.IsChildOf(remoteAvatar.transform))) continue;
            if (collider.transform.CompareTag("Water")) continue;
            if (collider.GetComponent<BodyScript>() != null)
            {
                penetration -= 2;
                if (penetration < 0) return;
                continue;
            }
            if (collider.GetComponent<LimbScript>() != null)
            {
                penetration--;
                if (penetration < 0) return;
                continue;
            }

            if (collider.transform.gameObject.CompareTag("Lamp")) return;

            if (preset.HitSounds != null && preset.HitSounds.Count > 0)
                Sound.Play(preset.HitSounds[UnityEngine.Random.Range(0, preset.HitSounds.Count)], hit.point);
            SpriteRenderer surface;
            if (collider.TryGetComponent<SpriteRenderer>(out surface))
            {
                var hole = new GameObject("MP BulletHole", typeof(SpriteRenderer));
                var renderer = hole.GetComponent<SpriteRenderer>();
                renderer.sprite = Resources.Load<Sprite>("Bhole/" + UnityEngine.Random.Range(1, 7));
                renderer.sortingOrder = surface.sortingOrder + 1;
                renderer.sortingLayerName = surface.sortingLayerName;
                renderer.material = Resources.Load<Material>("BaseSpriteMaterial");
                hole.transform.position = hit.point + hit.normal * UnityEngine.Random.Range(-0.03f, -0.25f);
                hole.transform.rotation = Quaternion.FromToRotation(Vector3.right, hit.normal);
                hole.transform.SetParent(collider.transform);
            }
            if (preset.hitSpark != null)
                Destroy(Instantiate(preset.hitSpark, hit.point, Quaternion.identity), 5f);
            return;
        }
    }

    private Vector2 SetTarget(BinaryReader reader, Rigidbody2D body)
    {
        var position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        var rotation = Quaternion.Euler(0f, 0f, ReadQuantizedRotation(reader));
        if (body == null) return position;
        SetTailTarget(body, position, rotation);
        return position;
    }

    private Vector2 SetQuantizedLimbTarget(BinaryReader reader, Rigidbody2D body)
    {
        var position = lastAuthoritativePosition + new Vector2(ReadQuantizedTailOffset(reader),
            ReadQuantizedTailOffset(reader));
        var rotation = Quaternion.Euler(0f, 0f, ReadQuantizedRotation(reader));
        if (body != null) SetTailTarget(body, position, rotation);
        return position;
    }

    private void SetTailTarget(Rigidbody2D body, Vector2 position, Quaternion rotation)
    {
        if (PlayerCarrySystem.SetRemoteArmRotation(remoteBody, body, rotation)) return;
        if (PlayerCarrySystem.MustLockRemoteCarryPose(remoteBody)) return;
        var now = Time.unscaledTime;
        TargetState previous;
        var hasPrevious = targets.TryGetValue(body, out previous);
        if (hasPrevious)
        {
            var previousAngle = previous.rotation.eulerAngles.z;
            var angle = rotation.eulerAngles.z;
            rotation = Quaternion.Euler(0f, 0f,
                previousAngle + Mathf.DeltaAngle(previousAngle, angle));
        }
        if (!receivedFirstSnapshot || !hasPrevious)
        {
            body.transform.position = position;
            body.transform.rotation = rotation;
            targets[body] = new TargetState
            {
                fromPosition = position,
                fromRotation = rotation,
                position = position,
                rotation = rotation,
                startedAt = now,
                receivedAt = now,
                duration = SnapshotInterval
            };
            return;
        }

        var arrivalInterval = Mathf.Clamp(now - previous.receivedAt, SnapshotInterval, 0.30f);
        targets[body] = new TargetState
        {
            fromPosition = previous.position,
            fromRotation = previous.rotation,
            position = position,
            rotation = rotation,
            startedAt = now,
            receivedAt = now,
            duration = arrivalInterval
        };
    }

    private void ReadTailTarget(
        BinaryReader reader,
        Rigidbody2D body,
        SpriteRenderer[] sprites,
        bool inVehicle)
    {
        var delta = new Vector2(
            reader.ReadSingle(),
            reader.ReadSingle());

        var deltaAngle = reader.ReadSingle();

        ApplyTailSpriteFlip(sprites, reader.ReadBoolean());
        ApplyTailSpriteColor(sprites, reader);

        if (body == null)
            return;

        if (inVehicle)
        {
            SetVehicleTailRotationTarget(body, deltaAngle);
            return;
        }

        SetTailTarget(body, lastAuthoritativePosition + delta, Quaternion.Euler(0f, 0f, remoteBody.rb.rotation + deltaAngle));
    }

    private void SetVehicleTailRotationTarget(
        Rigidbody2D body,
        float localRotation)
    {
        var index = vehicleTailTargets.FindIndex(
            target => target.Body == body);

        if (index < 0)
            return;

        var state = vehicleTailTargets[index];
        var progress = Mathf.Clamp01((Time.unscaledTime - state.StartedAt) / SnapshotInterval);

        state.FromLocalRotation = Mathf.LerpAngle(state.FromLocalRotation, state.LocalRotation, progress);
        state.LocalRotation = localRotation;
        state.StartedAt = Time.unscaledTime;

        vehicleTailTargets[index] = state;
    }

    private void SetVehicleTailTransformTarget(Transform transform, float localRotation)
    {
        if (transform == null)
            return;

        var now = Time.unscaledTime;
        var index = vehicleTailTransformTargets.FindIndex(target => target.Transform == transform);

        if (index < 0)
        {
            vehicleTailTransformTargets.Add(
                new VehicleTailTransformTarget
                {
                    Transform = transform,
                    LocalRotation = localRotation,
                    FromLocalRotation = localRotation,
                    StartedAt = now
                });

            return;
        }

        var state = vehicleTailTransformTargets[index];
        var progress = Mathf.Clamp01((now - state.StartedAt) / SnapshotInterval);

        state.FromLocalRotation = Mathf.LerpAngle(state.FromLocalRotation, state.LocalRotation, progress);
        state.LocalRotation = localRotation;
        state.StartedAt = now;

        vehicleTailTransformTargets[index] = state;
    }

    private void ReadTailTarget(
        BinaryReader reader,
        Transform transform,
        SpriteRenderer[] sprites,
        bool inVehicle)
    {
        var delta = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        var deltaAngle = reader.ReadSingle();

        ApplyTailSpriteFlip(sprites, reader.ReadBoolean());
        ApplyTailSpriteColor(sprites, reader);

        if (transform == null)
            return;

        if (inVehicle)
        {
            SetVehicleTailTransformTarget(transform, deltaAngle);
            return;
        }

        SetWorldTarget(transform, new TargetState
        {
            position = new Vector3(lastAuthoritativePosition.x + delta.x, lastAuthoritativePosition.y + delta.y, transform.position.z),
            rotation = Quaternion.Euler(0f, 0f, remoteBody.rb.rotation + deltaAngle)
        });
    }

    private void ReadWorldTransform(BinaryReader reader, Transform transform)
    {
        SetWorldTarget(transform, ReadWorldTarget(reader, transform));
    }

    private void ReadLocalTransform(BinaryReader reader, Transform transform)
    {
        if (transform == null)
        {
            SkipBody(reader);
            return;
        }
        var target = new TargetState
        {
            position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), transform.localPosition.z),
            rotation = Quaternion.Euler(0f, 0f, ReadQuantizedRotation(reader))
        };
        SetLocalTarget(transform, target);
    }

    private TargetState ReadWorldTarget(BinaryReader reader, Transform transform)
    {
        var z = transform == null ? 0f : transform.position.z;
        return new TargetState
        {
            position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), z),
            rotation = Quaternion.Euler(0f, 0f, ReadQuantizedRotation(reader))
        };
    }

    private void SetWorldTarget(Transform transform, TargetState target)
    {
        if (PlayerCarrySystem.MustLockRemoteCarryPose(remoteBody))
        {
            if (transform == remoteBody.Arms) PlayerCarrySystem.SetRemoteArmsRotation(remoteBody, target.rotation);
            return;
        }
        if (transform == null) return;
        WorldTargetState previous;
        var firstTarget = !worldTargets.TryGetValue(transform, out previous);
        target.position.z = transform.position.z;
        if (!firstTarget)
        {
            var previousAngle = previous.rotation.eulerAngles.z;
            var angle = target.rotation.eulerAngles.z;
            target.rotation = Quaternion.Euler(0f, 0f,
                previousAngle + Mathf.DeltaAngle(previousAngle, angle));
        }
        var now = Time.unscaledTime;
        if (!receivedFirstSnapshot || firstTarget)
        {
            transform.position = target.position;
            transform.rotation = target.rotation;
            worldTargets[transform] = new WorldTargetState
            {
                fromPosition = target.position,
                fromRotation = target.rotation,
                position = target.position,
                rotation = target.rotation,
                startedAt = now,
                receivedAt = now,
                duration = SnapshotInterval
            };
            return;
        }

        var arrivalInterval = Mathf.Clamp(now - previous.receivedAt,
            SnapshotInterval, 0.30f);
        worldTargets[transform] = new WorldTargetState
        {
            fromPosition = BufferedRemoteInterpolation ? previous.position : transform.position,
            fromRotation = BufferedRemoteInterpolation ? previous.rotation : transform.rotation,
            position = target.position,
            rotation = target.rotation,
            startedAt = now,
            receivedAt = now,
            duration = arrivalInterval
        };
    }

    private void SetLocalTarget(Transform transform, TargetState target)
    {
        if (PlayerCarrySystem.MustLockRemoteCarryPose(remoteBody)) return;
        WorldTargetState previous;
        var firstTarget = !localTargets.TryGetValue(transform, out previous);
        var now = Time.unscaledTime;
        if (!receivedFirstSnapshot || firstTarget)
        {
            transform.localPosition = target.position;
            transform.localRotation = target.rotation;
            localTargets[transform] = new WorldTargetState
            {
                fromPosition = target.position,
                fromRotation = target.rotation,
                position = target.position,
                rotation = target.rotation,
                startedAt = now,
                receivedAt = now,
                duration = SnapshotInterval
            };
            return;
        }

        var arrivalInterval = Mathf.Clamp(now - previous.receivedAt,
            SnapshotInterval, 0.30f);
        localTargets[transform] = new WorldTargetState
        {
            fromPosition = BufferedRemoteInterpolation ? previous.position : transform.localPosition,
            fromRotation = BufferedRemoteInterpolation ? previous.rotation : transform.localRotation,
            position = target.position,
            rotation = target.rotation,
            startedAt = now,
            receivedAt = now,
            duration = arrivalInterval
        };
    }

    private void ApplyPlayerCollisionRule(BodyScript localBody)
    {
        var collisionsEnabled = MultiplayerSession.PlayerCollisions;
        if (localBody == null || (collisionRuleApplied && collisionRuleLocalBody == localBody &&
            collisionRulePlayerCollisions == collisionsEnabled)) return;

        var localColliders = localBody.GetComponentsInChildren<Collider2D>(true);
        foreach (var remoteCollider in remoteColliderTriggers.Keys)
        {
            if (remoteCollider == null) continue;
            foreach (var localCollider in localColliders)
                if (localCollider != null)
                    Physics2D.IgnoreCollision(remoteCollider, localCollider, !collisionsEnabled);
        }

        collisionRuleLocalBody = localBody;
        collisionRulePlayerCollisions = collisionsEnabled;
        collisionRuleApplied = true;
    }

    private void ApplyVisualState(PlayerVisualState state, Transform root)
    {
        remoteVisualLayout = GetVisualLayout(remoteVisualLayout, root);
        foreach (var rendererState in state.Renderers)
        {
            SpriteRenderer renderer;
            if (!remoteVisualLayout.RenderersByPath.TryGetValue(rendererState.Path, out renderer) || renderer == null)
                continue;
            renderer.enabled = rendererState.Visible;
            renderer.color = rendererState.Color;
            renderer.flipX = rendererState.FlipX;
            renderer.flipY = rendererState.FlipY;
        }
        foreach (var lightState in state.Lights)
        {
            Component light;
            if (!remoteVisualLayout.LightsByPath.TryGetValue(lightState.Path, out light) || light == null) continue;
            var behaviour = light as Behaviour;
            if (behaviour != null) behaviour.enabled = lightState.Visible;
            var light2D = light as UnityEngine.Experimental.Rendering.Universal.Light2D;
            if (light2D != null && light2D.GetComponentInParent<Headlamp>() == null)
            {
                light2D.intensity = lightState.Intensity;
                light2D.color = lightState.Color;
            }
        }
        var expressions = root.GetComponentsInChildren<FacialExpression>(true);
        for (var index = 0; index < expressions.Length && index < state.FacialExpressions.Length; index++)
        {
            var sprite = FacialExpressionSprite(expressions[index], state.FacialExpressions[index]);
            if (sprite != null && expressions[index].head != null) expressions[index].head.sprite = sprite;
        }
        HideChildrenOfDisabledHeadAccessories(root);
    }

    private void ReadScarfState(BinaryReader reader)
    {
        var visible = reader.ReadBoolean();
        if (!visible)
        {
            if (remoteScarf != null) Destroy(remoteScarf);
            if (remoteScarfHold != null) Destroy(remoteScarfHold);
            remoteScarf = null;
            remoteScarfHold = null;
            return;
        }

        var startColor = ReadColor(reader);
        var endColor = ReadColor(reader);
        if (remoteScarf == null || remoteScarfHold == null) CreateRemoteScarf();
        if (remoteScarf == null) return;
        var scarf = remoteScarf.GetComponent<ScarfPhysics>();
        var line = scarf == null ? remoteScarf.GetComponent<LineRenderer>() : scarf.pointRenderer;
        if (line == null) return;
        line.startColor = startColor;
        line.endColor = endColor;
        if (remoteScarfHold != null)
        {
            var holdRenderer = remoteScarfHold.GetComponent<SpriteRenderer>();
            if (holdRenderer != null) holdRenderer.color = startColor;
        }
    }

    private void CreateRemoteScarf()
    {
        if (remoteBody == null) return;
        var limbs = remoteBody.limbs ?? [];
        if (limbs.Count < 2) return;
        var parent = limbs[1].transform;
        if (remoteScarf == null)
        {
            var prefab = Resources.Load<GameObject>("Scarf");
            if (prefab == null) return;
            remoteScarf = Instantiate(prefab, parent);
            remoteScarf.name = "MP Remote Scarf";
            remoteScarf.transform.localRotation = Quaternion.identity;
            remoteScarf.transform.localPosition = new Vector3(-0.067f, 0.052f, 0f);
            var scarf = remoteScarf.GetComponent<ScarfPhysics>();
            if (scarf != null)
            {
                scarf.refbody = remoteBody;
                scarf.enabled = true;
            }
        }
        CreateRemoteScarfHold(parent);
    }

    private void CreateRemoteScarfHold(Transform parent)
    {
        if (parent == null || remoteScarfHold != null) return;
        var sprite = Resources.Load<Sprite>("scarfImage");
        if (sprite == null) return;
        remoteScarfHold = new GameObject("MP Remote Scarf Hold", typeof(SpriteRenderer));
        remoteScarfHold.transform.SetParent(parent, false);
        remoteScarfHold.transform.localPosition = Vector3.zero;
        remoteScarfHold.transform.localRotation = Quaternion.identity;
        remoteScarfHold.transform.localScale = Vector3.one;
        var renderer = remoteScarfHold.GetComponent<SpriteRenderer>();
        var parentRenderer = parent.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = parentRenderer == null ? null : parentRenderer.sharedMaterial;
        renderer.color = parentRenderer == null ? Color.white : parentRenderer.color;
        renderer.sortingLayerID = parentRenderer == null ? 0 : parentRenderer.sortingLayerID;
        renderer.sortingOrder = parentRenderer == null ? 1 : parentRenderer.sortingOrder + 1;
    }

    private void SetRemoteFire(int limbIndex, LimbScript limb, bool burning)
    {
        GameObject visual;
        remoteFires.TryGetValue(limbIndex, out visual);
        if (!burning)
        {
            if (visual != null) Destroy(visual);
            remoteFires.Remove(limbIndex);
            return;
        }
        if (visual != null) return;

        var prefab = Resources.Load<GameObject>("Spawnables/FireParticle");
        if (prefab == null) return;
        visual = Instantiate(prefab, limb.transform.position, Quaternion.identity);
        visual.name = "MP Remote Fire";
        visual.transform.SetParent(limb.transform, true);
        BlackoutRule.MakeAlwaysBright(visual);
        foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
        foreach (var behaviour in visual.GetComponentsInChildren<Behaviour>(true))
            if (behaviour.GetType().Name == "AudioSource") behaviour.enabled = false;
        remoteFires[limbIndex] = visual;
    }

    private void ApplyDismembermentVisuals()
    {
        if (remoteBody == null) return;
        foreach (var pair in originalDismemberSprites)
            if (pair.Key != null) pair.Key.sprite = pair.Value;
        foreach (var manager in remoteBody.GetComponentsInChildren<DismemberManager>(true))
        {
            var triggered = false;
            if (manager.dismemberLimbs != null)
            {
                foreach (var limb in manager.dismemberLimbs)
                {
                    if (limb == null || !limb.dismembered) continue;
                    triggered = true;
                    break;
                }
            }
            var managerId = manager.GetInstanceID();
            if (!triggered)
            {
                displayedDismembermentEffects.Remove(managerId);
                continue;
            }
            var showEffect = dismembermentVisualsInitialized && displayedDismembermentEffects.Add(managerId);
            if (manager.dismemberJoint != null)
                foreach (var joint in manager.dismemberJoint)
                    if (joint != null) joint.enabled = false;
            if (manager.dismemberRender != null && manager.dismemberSprites != null)
            {
                var count = Mathf.Min(manager.dismemberRender.Length, manager.dismemberSprites.Length);
                for (var index = 0; index < count; index++)
                    if (manager.dismemberRender[index] != null)
                        manager.dismemberRender[index].sprite = manager.dismemberSprites[index];
            }
            if (showEffect) SpawnRemoteDismembermentEffects(manager);
        }
        dismembermentVisualsInitialized = true;
    }

    private void SpawnRemoteDismembermentEffects(DismemberManager manager)
    {
        if (GunsawMultiplayerPlugin.IsHeadlessMode || remoteBody == null || remoteBody.isRobot) return;
        var position = manager.transform.position;
        Sound.Play(Resources.Load<AudioClip>("Sounds/dismember" + UnityEngine.Random.Range(1, 4)), position, false, false);
        Sound.Play(Resources.Load<AudioClip>("Sounds/bloodDrip"), position, false, false, remoteBody.transform);
        var blood = Resources.Load<GameObject>("Spawnables/BloodSplashGoreBleed");
        if (blood != null)
        {
            var effect = Instantiate(blood, position, Quaternion.identity, remoteBody.transform);
            BlackoutRule.ApplyToObject(effect);
            Destroy(effect, 10f);
        }
        if (manager.lethal)
        {
            var gib = Resources.Load<GameObject>(manager.doDeHead ? "Spawnables/BrainDestroyGib" : "Spawnables/GutGib");
            if (gib != null)
            {
                var effect = Instantiate(gib, position, Quaternion.identity);
                BlackoutRule.ApplyToObject(effect);
            }
        }
        var gore = Resources.Load<GameObject>("Spawnables/GoreChunk");
        if (gore == null) return;
        var chunk = Instantiate(gore, position, Quaternion.identity);
        BlackoutRule.ApplyToObject(chunk);
        var particles = chunk.GetComponent<ParticleSystem>();
        if (particles != null)
        {
            var main = particles.main;
            main.startColor = remoteBody.bloodColor;
        }
        Destroy(chunk, 120f);
    }

    private void CacheDismembermentVisuals()
    {
        originalDismemberSprites.Clear();
        if (remoteBody == null) return;
        foreach (var manager in remoteBody.GetComponentsInChildren<DismemberManager>(true))
        {
            if (manager.dismemberRender == null) continue;
            foreach (var renderer in manager.dismemberRender)
                if (renderer != null && !originalDismemberSprites.ContainsKey(renderer))
                    originalDismemberSprites.Add(renderer, renderer.sprite);
        }
    }

    private void SnapRemoteVehicleArmLimbs()
    {
        if (remoteBody == null || remoteBody.Arms == null ||
            remoteBody.limbs == null)
            return;

        foreach (var limb in remoteBody.limbs)
        {
            if (limb == null ||
                limb.dismembered ||
                limb.rb == null ||
                limb.transformToFollow == null)
                continue;

            var follow = limb.transformToFollow;

            if (follow != remoteBody.Arms &&
                !follow.IsChildOf(remoteBody.Arms))
                continue;

            limb.transform.position = follow.position;
            limb.transform.rotation = follow.rotation;

            limb.rb.position = follow.position;
            limb.rb.rotation = follow.eulerAngles.z;
            limb.rb.velocity = Vector2.zero;
            limb.rb.angularVelocity = 0f;
        }
    }

    private void ApplyVehicleReflection()
    {
        if (remoteBody == null)
            return;

        var scale = remoteBody.transform.localScale;
        var magnitude = Mathf.Abs(scale.x);

        scale.x =
            (magnitude < 0.0001f ? 1f : magnitude) *
            (remoteVehicleReflected ? -1f : 1f);

        remoteBody.transform.localScale = scale;
    }

    private struct VehicleTailTarget
    {
        public Rigidbody2D Body;
        public float LocalRotation;
        public float FromLocalRotation;
        public float StartedAt;
    }

    private struct TargetState
    {
        public Vector3 fromPosition;
        public Quaternion fromRotation;
        public Vector3 position;
        public Quaternion rotation;
        public float startedAt;
        public float receivedAt;
        public float duration;
    }

    private struct WorldTargetState
    {
        public Vector3 fromPosition;
        public Quaternion fromRotation;
        public Vector3 position;
        public Quaternion rotation;
        public float startedAt;
        public float receivedAt;
        public float duration;
    }

    private sealed class RemoteProjectileVisual
    {
        public GameObject Visual;
        public GameObject ImpactEffect;
        public AudioClip ExplosionSound;
        public float ExpiresAt;
    }

    private struct VehicleTailTransformTarget
    {
        public Transform Transform;
        public float LocalRotation;
        public float FromLocalRotation;
        public float StartedAt;
    }
}