using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using static NetworkAvatarManager;
using static NetworkAvatarUtilities;

internal sealed class LocalPlayerReplication : NetworkAvatarReplication
{
    internal static string selectedCharacterPrefab = "";
    internal static string pendingRespawnCharacterPrefab = "";
    internal static float localRespawnProtectionUntil = -1f;
    private const float RespawnProtectionSeconds = 3f;
    internal static PlayerScript localPlayerInstance;
    internal static Transform localGlobalBody;
    private const float StateInterval = 1f / 10f;
    private const float VisualStateInterval = 1f / 2f;
    private const float FullVisualStateInterval = 5f;
    private float nextState;
    internal PlayerStatePacket? pendingStatePacket;
    internal PlayerSpecialLinesPacket? pendingSpecialLinesPacket;
    private bool specialLinesWereVisible;
    private PlayerVisualState lastSerializedVisualState;
    private float nextVisualState;
    private float nextFullVisualSnapshot;
    private float nextAvatarTrafficSample;
    private int avatarCoreBytesWindow;
    private int avatarLimbBytesWindow;
    private int avatarRigBytesWindow;
    private int avatarWeaponBytesWindow;
    private int avatarEffectsBytesWindow;
    private int avatarVisualBytesWindow;
    private int avatarCoreBytesPerSecond;
    private int avatarLimbBytesPerSecond;
    private int avatarRigBytesPerSecond;
    private int avatarWeaponBytesPerSecond;
    private int avatarEffectsBytesPerSecond;
    private int avatarVisualBytesPerSecond;
    private string lastSerializedInventory = "";
    protected float nextFullInventory;
    internal float forceFullInventoryUntil;
    private VisualLayout localVisualLayout;

    internal static int AvatarCoreBytesPerSecond => Instance?.avatarCoreBytesPerSecond ?? 0;
    internal static int AvatarLimbBytesPerSecond => Instance?.avatarLimbBytesPerSecond ?? 0;
    internal static int AvatarRigBytesPerSecond => Instance?.avatarRigBytesPerSecond ?? 0;
    internal static int AvatarWeaponBytesPerSecond => Instance?.avatarWeaponBytesPerSecond ?? 0;
    internal static int AvatarEffectsBytesPerSecond => Instance?.avatarEffectsBytesPerSecond ?? 0;
    internal static int AvatarVisualBytesPerSecond => Instance?.avatarVisualBytesPerSecond ?? 0;

    internal static LocalPlayerReplication Instance { get; private set; }
    internal string localName;
    private BodyScript identityBody;
    private string identityCharacterName = "";
    private string identitySpeciesName = "";
    private string identityRootName = "";
    private string identityFallback = "";
    private string resolvedIdentityPrefab = "";
    private string? cachedCharacterPrefabPreference;
    internal float nextSnapshot;
    internal ushort outgoingGrabPeerId;
    private GrabCommand incomingGrab;
    private float incomingGrabUntil;
    private BodyScript localVehicleBody;
    private VehicleBase localVehicle;
    private bool localVehicleLocked;
    private bool localVehicleWasSimulated;
    private BodyScript startingLoadoutAppliedBody;
    private BodyScript startingAmmoAppliedBody;
    private BodyScript pendingRespawnLoadoutBody;
    private BodyScript pendingRespawnLoadoutSource;
    private int localSpawnScene = int.MinValue;
    private Vector3 localSpawnPosition;
    private Vector3 localDeathPosition;
    private bool localWasAlive = true;
    private int remainingLives;
    private int livesRule = -1;
    public float respawnAt = -1f;
    internal bool localRespawnPending;
    private int localRespawnGeneration;
    public ushort spectatorPeerId;
    internal bool spectating;
    internal static bool IsSpectating => Instance != null && Instance.spectating && !Instance.CanRespawn;

    internal PlayerSnapshotPacket Serialize(int sequence, BodyScript body)
    {
        var performanceStarted = MultiplayerPerformance.Start();
        var includeState = Time.unscaledTime >= nextState;
        if (includeState) nextState = Time.unscaledTime + StateInterval;
        pendingStatePacket = null;
        pendingSpecialLinesPacket = null;
        var visualState = default(PlayerVisualState);
        var includeVisualState = false;
        if (includeState)
        {
            localVisualLayout = GetVisualLayout(localVisualLayout, body.transform);
            visualState = SerializeVisualState(localVisualLayout);
            var visualChanged = !PlayerVisualState.Equals(lastSerializedVisualState, visualState);
            includeVisualState = Time.unscaledTime >= nextFullVisualSnapshot ||
                visualChanged && Time.unscaledTime >= nextVisualState;
            if (includeVisualState)
            {
                lastSerializedVisualState = visualState;
                nextVisualState = Time.unscaledTime + VisualStateInterval;
                nextFullVisualSnapshot = Time.unscaledTime + FullVisualStateInterval;
            }
        }
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            var breakdown = new AvatarWireBreakdown();
            var sectionStarted = writer.BaseStream.Position;
            var vehicleId = body.inVehicle && body.curVehicle != null && GunsawMultiplayerPlugin.World != null
                ? GunsawMultiplayerPlugin.World.VehicleWireId(body.curVehicle) : 0UL;
            var inVehicle = vehicleId != 0UL;
            var isVehicleDriver = inVehicle && body.curVehicle.occupant == body;
            var isReflected = body.transform.localScale.x < 0f;
            var isActive = body.transform.root.gameObject.activeInHierarchy;
            writer.Write(inVehicle);
            writer.Write(vehicleId);
            writer.Write(isVehicleDriver);
            writer.Write((byte)body.CurrentState);
            writer.Write(body.isRight);
            writer.Write(isReflected);
            writer.Write(isActive);
            var headReferenceRotation = vehicleId != 0UL && body.curVehicle.mainPart != null &&
                body.curVehicle.mainPart.rb != null ? body.curVehicle.mainPart.rb.rotation : body.rb.rotation;
            var headRotation = body.headTransform == null ? 0f :
                Mathf.DeltaAngle(headReferenceRotation, body.headTransform.eulerAngles.z);
            writer.Write(headRotation);
            WriteBody(writer, body.rb);
            breakdown.Core += (int)(writer.BaseStream.Position - sectionStarted);

            sectionStarted = writer.BaseStream.Position;
            var limbs = body.limbs ?? [];
            var limbStates = new PlayerSnapshotLimbState[limbs.Count];
            writer.Write((ushort)limbs.Count);
            var limbIndex = 0;
            foreach (LimbScript limb in limbs)
            {
                var limbBody = limb.rb == null ? new PlayerSnapshotBodyState(0f, 0f, 0f) :
                    new PlayerSnapshotBodyState(limb.rb.position.x, limb.rb.position.y, limb.rb.rotation);
                var dismembered = limb.dismembered;
                var burning = IsBurning(limb);
                writer.Write(limbBody.X);
                writer.Write(limbBody.Y);
                writer.Write(limbBody.Rotation);
                writer.Write(dismembered);
                writer.Write(burning);
                limbStates[limbIndex++] = new PlayerSnapshotLimbState(limbBody, dismembered, burning);
            }
            breakdown.Limbs += (int)(writer.BaseStream.Position - sectionStarted);

            sectionStarted = writer.BaseStream.Position;
            var tailBases = GetNetworkTailBodies(body);
            var tailBaseStates = new PlayerSnapshotTailState[tailBases.Count];
            writer.Write((ushort)tailBases.Count);
            var tailBaseIndex = 0;
            foreach (Rigidbody2D tailBase in tailBases)
            {
                var tailBaseTransform = tailBase == null ? null : tailBase.transform;
                var tailBaseRotation = tailBase != null ? tailBase.rotation : 0f;
                WriteTailTransform(writer, body.rb, tailBaseTransform, tailBaseRotation);
                tailBaseStates[tailBaseIndex++] = CreateTailBaseState(body.rb, tailBaseTransform, tailBaseRotation);
            }
            var tails = body.tails;
            var tailStates = new PlayerSnapshotTailState[tails.Length];
            writer.Write((ushort)tails.Length);
            for (var tailIndex = 0; tailIndex < tails.Length; tailIndex++)
            {
                var tail = tails[tailIndex];
                var tailRotation = tail == null ? 0f : tail.eulerAngles.z;
                WriteTailTransform(writer, body.rb, tail, tailRotation);
                tailStates[tailIndex] = CreateTailBaseState(body.rb, tail, tailRotation);
            }

            var arms = body.Arms;
            var gunTransform = body.gunTransform;
            var gunAnimationTransform = body.gunAnimTransform;
            WriteWorldTransform(writer, arms);
            WriteLocalTransform(writer, gunTransform);
            WriteLocalTransform(writer, gunAnimationTransform);
            var armsTransform = arms == null ? new PlayerSnapshotTransform(0f, 0f, 0f) : new PlayerSnapshotTransform(arms.position.x, arms.position.y, arms.eulerAngles.z);
            var gunTransformState = gunTransform == null ? new PlayerSnapshotTransform(0f, 0f, 0f) : new PlayerSnapshotTransform(gunTransform.localPosition.x, gunTransform.localPosition.y, gunTransform.localEulerAngles.z);
            var gunAnimationTransformState = gunAnimationTransform == null ? new PlayerSnapshotTransform(0f, 0f, 0f) : new PlayerSnapshotTransform(gunAnimationTransform.localPosition.x, gunAnimationTransform.localPosition.y, gunAnimationTransform.localEulerAngles.z);
            breakdown.Rig += (int)(writer.BaseStream.Position - sectionStarted);
            sectionStarted = writer.BaseStream.Position;
            var health = body.health;
            var isAlive = body.isAlive;
            var deathCause = DeathCauseFor(body);
            var stamina = body.stamina;
            var controlState = (byte)body.controlState;
            var canBeGrabbed = CanGrabOnlyState(body);
            var burnIntensity = body.burnIntensity;
            var hasNoLegs = body.noLegs;
            var isDecapitated = body.deHeaded;
            writer.Write(health);
            writer.Write(isAlive);
            writer.Write(stamina);
            writer.Write(controlState);
            writer.Write(canBeGrabbed);
            writer.Write(burnIntensity);
            writer.Write(hasNoLegs);
            writer.Write(isDecapitated);
            var weaponSlot = body.unarmed ? -1 : body.currentWeapon;
            var weaponAmmo = body.weapon == null ? 0 : body.weapon.ammo;
            var weapons = body.weapons ?? [];
            writer.Write(weaponSlot);
            writer.Write(weaponAmmo);
            writer.Write((ushort)weapons.Count);
            var inventoryIds = new ulong[weapons.Count];
            for (var index = 0; index < weapons.Count; index++)
            {
                var preset = weapons[index];
                inventoryIds[index] = NetworkWireId.FromString(preset == null ? "" : NetworkAvatarUtilities.SpriteId(preset.sprite));
            }
            var inventoryKey = string.Join("|", inventoryIds);
            var inventoryChanged = inventoryKey != lastSerializedInventory || Time.unscaledTime >= nextFullInventory || Time.unscaledTime < forceFullInventoryUntil;
            writer.Write(inventoryChanged);
            if (inventoryChanged)
            {
                foreach (var inventoryId in inventoryIds) writer.Write(inventoryId);
                lastSerializedInventory = inventoryKey;
                nextFullInventory = Time.unscaledTime + 1f;
            }
            breakdown.Weapons += (int)(writer.BaseStream.Position - sectionStarted);

            sectionStarted = writer.BaseStream.Position;
            var weaponLaserState = CreateWeaponLaserState(body.wepLaserLine);
            WriteLineState(writer, weaponLaserState);
            var player = PlayerScript.player;
            var levitatorLaserState = CreateWeaponLaserState(player == null ? null : player.levitLine);
            var crystalTongue = body.GetComponent<CrystalTongue>();
            var crystalTongueState = CreateWeaponLaserState(crystalTongue == null ? null : crystalTongue.line);
            var scarfState = CreateScarfState(body);
            WriteScarfState(writer, scarfState);
            breakdown.Effects += (int)(writer.BaseStream.Position - sectionStarted);

            sectionStarted = writer.BaseStream.Position;
            writer.Write(includeVisualState);
            if (includeVisualState) WriteVisualState(writer, visualState);
            writer.Write((byte)deathCause);
            writer.Write(body.susnessMult);
            breakdown.Visual += (int)(writer.BaseStream.Position - sectionStarted);
            AddAvatarWireBreakdown(breakdown);
            MultiplayerPerformance.AddAvatarSerialize(performanceStarted);
            var coreBody = body.rb == null ? new PlayerSnapshotBodyState(0f, 0f, 0f) :
                new PlayerSnapshotBodyState(body.rb.position.x, body.rb.position.y, body.rb.rotation);
            var packetVisualState = includeVisualState ? CreatePacketVisualState(visualState) :
                (PlayerSnapshotVisualState?)null;
            if (includeState)
            {
                pendingStatePacket = new PlayerStatePacket(health, isAlive, stamina, controlState, canBeGrabbed,
                    burnIntensity, hasNoLegs, isDecapitated, weaponSlot, weaponAmmo, inventoryIds, inventoryChanged,
                    weaponLaserState, scarfState, includeVisualState, packetVisualState, deathCause,
                    body.susnessMult, body.characterScale, limbStates, tailBaseStates, tailStates);
            }
            var specialLinesVisible = levitatorLaserState.Visible || crystalTongueState.Visible;
            if (specialLinesVisible || specialLinesWereVisible)
                pendingSpecialLinesPacket = new PlayerSpecialLinesPacket(levitatorLaserState, crystalTongueState);
            specialLinesWereVisible = specialLinesVisible;
            return new PlayerSnapshotPacket(sequence, inVehicle, vehicleId, isVehicleDriver,
                (byte)body.CurrentState, body.isRight, isReflected, isActive, headRotation, coreBody,
                armsTransform, gunTransformState, gunAnimationTransformState, limbStates, tailBaseStates, tailStates);
        }
    }

    private void AddAvatarWireBreakdown(AvatarWireBreakdown breakdown)
    {
        avatarCoreBytesWindow += breakdown.Core;
        avatarLimbBytesWindow += breakdown.Limbs;
        avatarRigBytesWindow += breakdown.Rig;
        avatarWeaponBytesWindow += breakdown.Weapons;
        avatarEffectsBytesWindow += breakdown.Effects;
        avatarVisualBytesWindow += breakdown.Visual;
        if (Time.unscaledTime < nextAvatarTrafficSample) return;
        nextAvatarTrafficSample = Time.unscaledTime + 1f;
        avatarCoreBytesPerSecond = avatarCoreBytesWindow;
        avatarLimbBytesPerSecond = avatarLimbBytesWindow;
        avatarRigBytesPerSecond = avatarRigBytesWindow;
        avatarWeaponBytesPerSecond = avatarWeaponBytesWindow;
        avatarEffectsBytesPerSecond = avatarEffectsBytesWindow;
        avatarVisualBytesPerSecond = avatarVisualBytesWindow;
        avatarCoreBytesWindow = avatarLimbBytesWindow = avatarRigBytesWindow = avatarWeaponBytesWindow =
            avatarEffectsBytesWindow = avatarVisualBytesWindow = 0;
    }

    private static PlayerVisualState SerializeVisualState(VisualLayout layout)
    {
        var renderers = layout == null || layout.Renderers == null
            ? new SpriteRenderer[0]
            : layout.Renderers;
        var rendererStates = new RendererVisualState[renderers.Length];
        for (var index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            var path = layout.RendererPaths != null && index < layout.RendererPaths.Length
                ? layout.RendererPaths[index] ?? ""
                : "";
            rendererStates[index] = renderer == null
                ? new RendererVisualState(path, false, Color.white, false, false)
                : new RendererVisualState(path, renderer.enabled && renderer.gameObject.activeInHierarchy,
                    renderer.color, renderer.flipX, renderer.flipY);
        }

        var lights = layout == null || layout.Lights == null ? new Component[0] : layout.Lights;
        var lightStates = new LightVisualState[lights.Length];
        for (var index = 0; index < lights.Length; index++)
        {
            var light = lights[index];
            var path = layout.LightPaths != null && index < layout.LightPaths.Length
                ? layout.LightPaths[index] ?? ""
                : "";
            if (light == null)
            {
                lightStates[index] = new LightVisualState(path, false, 0f, Color.white);
                continue;
            }
            var behaviour = light as Behaviour;
            var light2D = light as UnityEngine.Experimental.Rendering.Universal.Light2D;
            lightStates[index] = new LightVisualState(path,
                behaviour == null || behaviour.enabled && light.gameObject.activeInHierarchy,
                light2D == null ? 0f : light2D.intensity, light2D == null ? Color.white : light2D.color);
        }
        var expressions = layout == null || layout.Root == null
            ? Array.Empty<FacialExpression>() : layout.Root.GetComponentsInChildren<FacialExpression>(true);
        var expressionStates = new byte[expressions.Length];
        for (var index = 0; index < expressions.Length; index++)
            expressionStates[index] = FacialExpressionState(expressions[index]);
        return new PlayerVisualState(rendererStates, lightStates, expressionStates);
    }

    private static PlayerSnapshotVisualState CreatePacketVisualState(PlayerVisualState state)
    {
        var sourceRenderers = state == null || state.Renderers == null
            ? new RendererVisualState[0]
            : state.Renderers;
        var renderers = new PlayerSnapshotRendererState[sourceRenderers.Length];
        for (var index = 0; index < sourceRenderers.Length; index++)
        {
            var renderer = sourceRenderers[index];
            renderers[index] = new PlayerSnapshotRendererState(renderer.Path, renderer.Visible,
                new PlayerSnapshotColor(renderer.Color.r, renderer.Color.g, renderer.Color.b, renderer.Color.a),
                renderer.FlipX, renderer.FlipY);
        }
        var sourceLights = state == null || state.Lights == null ? new LightVisualState[0] : state.Lights;
        var lights = new PlayerSnapshotLightState[sourceLights.Length];
        for (var index = 0; index < sourceLights.Length; index++)
        {
            var light = sourceLights[index];
            lights[index] = new PlayerSnapshotLightState(light.Path, light.Visible, light.Intensity,
                new PlayerSnapshotColor(light.Color.r, light.Color.g, light.Color.b, light.Color.a));
        }
        return new PlayerSnapshotVisualState(renderers, lights, state == null ? Array.Empty<byte>() : state.FacialExpressions);
    }

    protected static string ResolveCharacterPrefab(BodyScript body)
    {
        var fallback = string.IsNullOrEmpty(selectedCharacterPrefab)
            ? PlayerPrefs.GetString("charPrefab")
            : selectedCharacterPrefab;
        var bestPath = "";
        var bestScore = -1;
        var currentRootName = NetworkAvatarUtilities.CleanCloneName(body.transform.root.name);

        var paths = new List<string>(knownCharacterPrefabs);
        if (!string.IsNullOrEmpty(fallback) && !paths.Contains(fallback)) paths.Add(fallback);
        foreach (var path in paths)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) continue;
            var prefabBody = prefab.GetComponentInChildren<BodyScript>(true);
            if (prefabBody == null) continue;
            var score = path == fallback ? 1 : 0;
            if (prefabBody.characterName == body.characterName) score += 100;
            if (prefabBody.speciesName == body.speciesName) score += 10;
            if (NetworkAvatarUtilities.CleanCloneName(prefab.name) == currentRootName) score += 200;
            if (score <= bestScore) continue;
            bestScore = score;
            bestPath = path;
        }
        return string.IsNullOrEmpty(bestPath) ? fallback : bestPath;
    }

    internal string ResolveLocalCharacterPrefab(BodyScript body)
    {
        if (body == null) return "";
        if (IsProtogenBody(body)) return ProtogenPrefabPath;
        var fallback = string.IsNullOrEmpty(selectedCharacterPrefab)
            ? (cachedCharacterPrefabPreference ??= PlayerPrefs.GetString("charPrefab"))
            : selectedCharacterPrefab;
        var characterName = body.characterName ?? "";
        var speciesName = body.speciesName ?? "";
        var rootName = NetworkAvatarUtilities.CleanCloneName(body.transform.root.name);
        if (identityBody == body && identityCharacterName == characterName &&
            identitySpeciesName == speciesName && identityRootName == rootName &&
            identityFallback == fallback)
            return resolvedIdentityPrefab;
        identityBody = body;
        identityCharacterName = characterName;
        identitySpeciesName = speciesName;
        identityRootName = rootName;
        identityFallback = fallback;
        resolvedIdentityPrefab = ResolveCharacterPrefab(body);
        return resolvedIdentityPrefab;
    }

    private static bool IsProtogenBody(BodyScript body)
    {
        var root = body == null || body.transform.root == null ? "" :
            NetworkAvatarUtilities.CleanCloneName(body.transform.root.name);
        return string.Equals(root, "RobotEnemy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(body.characterName, "G4", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool BlockLocalRespawnDeath(BodyScript body)
    {
        if (body == null || Time.unscaledTime >= localRespawnProtectionUntil) return false;
        var player = PlayerScript.player;
        if (player == null || player.bodyScript != body) return false;

        ReviveRespawnBody(body);
        return true;
    }

    internal static void ConsumeLocalDeathWeapon(BodyScript body, bool allWeapons)
    {
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || player == null || body == null || body.isAlive ||
            body != player.bodyScript) return;
        ClearDroppedWeapon(body, allWeapons);
    }

    internal static bool HandleClientRestart()
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        KillLocalPlayer(PlayerDeathCause.SelfKill);
        return false;
    }

    internal static bool KillLocalPlayer(PlayerDeathCause cause)
    {
        var player = PlayerScript.player;
        var body = player?.bodyScript;
        if (body == null || !body.isAlive) return false;
        RecordEnvironmentalDeathCause(body, cause);
        body.Death();
        return true;
    }

    internal static bool PrepareLocalPlayerUpdate(PlayerScript player)
    {
        if (player == null || player != PlayerScript.player || player.bodyScript == null)
            return false;

        if (Instance != null && Instance.localRespawnPending) return false;
        var body = player.bodyScript;
        if (!body.gameObject.activeInHierarchy || body.limbs == null || body.limbs.Count < 15 ||
            body.limbs[0] == null || body.limbs[11] == null || body.limbs[14] == null ||
            GameManager.main == null || ResourceManager.main == null || ScreenFXManager.main == null)
            return false;
        if (MultiplayerSession.IsConnected) body.dropWeapon = false;

        var buttons = player.buttons;
        if (buttons != null)
            foreach (var button in buttons)
                if (button == null)
                {
                    player.buttons = null;
                    break;
                }
        return true;
    }

    internal static BodyScript SpectatorTargetBody()
    {
        if (Instance == null || !Instance.spectating || Instance.CanRespawn || Instance.spectatorPeerId == 0)
            return null;
        NetworkAvatarReplication replica;
        return NetworkAvatarManager.replicas.TryGetValue(Instance.spectatorPeerId, out replica) && replica != null ? replica.remoteBody : null;
    }

    internal static void ResetExhaustedLives()
    {
        exhaustedLivesLobbies.Remove(MultiplayerSession.LobbyId);
    }

    internal static bool TrySetPendingRespawnCharacter(string character, out string characterName)
    {
        if (!TryResolveCharacterPrefab(character, out var prefabPath, out characterName)) return false;
        pendingRespawnCharacterPrefab = prefabPath;
        return true;
    }

    internal void ReceivePlayerGrab(ushort senderId, PlayerGrabPacket packet)
    {
        if (!MultiplayerSession.CanGrabPlayers || !packet.IsGrabbing)
        {
            incomingGrabUntil = 0f;
            return;
        }
        var command = new GrabCommand
        {
            Kind = packet.PartKind,
            Index = packet.PartIndex,
            Point = new Vector2(packet.PointX, packet.PointY),
            LocalPoint = new Vector2(packet.LocalPointX, packet.LocalPointY)
        };
        if (!NetworkAvatarUtilities.IsFinite(command.Point.x) || !NetworkAvatarUtilities.IsFinite(command.Point.y) ||
            !NetworkAvatarUtilities.IsFinite(command.LocalPoint.x) || !NetworkAvatarUtilities.IsFinite(command.LocalPoint.y)) return;
        incomingGrab = command;
        incomingGrabUntil = Time.unscaledTime + 0.15f;
        RecordGrabSource(PlayerScript.player?.bodyScript, NetworkAvatarManager.GetOrCreateReplica(senderId)?.remoteBody);
    }

    private void ApplyIncomingGrab()
    {
        if (Time.unscaledTime > incomingGrabUntil || !MultiplayerSession.CanGrabPlayers) return;
        var player = PlayerScript.player;
        var body = player?.bodyScript;
        if (body == null || !CanGrabBody(body)) { incomingGrabUntil = 0f; return; }
        var rigidbody = ResolveLocalPart(body, incomingGrab.Kind, incomingGrab.Index);
        if (rigidbody == null || !rigidbody.simulated) return;
        var force = incomingGrab.Point - rigidbody.position;
        if (force.magnitude > 5f) force = force.normalized * 5f;
        rigidbody.AddForceAtPosition(force * 100f, rigidbody.transform.TransformPoint(incomingGrab.LocalPoint));
        rigidbody.angularVelocity *= 0.96f;
        if (force.magnitude > 2f && body.controlState == 0) body.EnterHalfControl();
    }

    private void UpdateLocalVehicleLock()
    {
        var player = PlayerScript.player;
        var body = player?.bodyScript;
        var vehicle = body != null && body.inVehicle ? body.curVehicle : null;

        var valid =
            MultiplayerSession.IsConnected &&
            !MultiplayerSession.IsHost &&
            body != null &&
            body.rb != null &&
            vehicle != null &&
            vehicle.mainPart != null &&
            vehicle.mainPart.rb != null;

        if (!valid)
        {
            RestoreLocalVehiclePhysics();
            return;
        }

        if (localVehicleLocked && (localVehicleBody != body || localVehicle != vehicle))
        {
            RestoreLocalVehiclePhysics();
        }

        if (!localVehicleLocked)
        {
            localVehicleBody = body;
            localVehicle = vehicle;
            localVehicleWasSimulated = body.rb.simulated;
            localVehicleLocked = true;

            body.rb.velocity = Vector2.zero;
            body.rb.angularVelocity = 0f;
            body.rb.simulated = false;
        }

        var seat = KartPassengers.SeatPosition(vehicle, body);
        var angle = vehicle.mainPart.rb.rotation;

        var position = body.transform.position;
        position.x = seat.x;
        position.y = seat.y;

        body.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));

        body.rb.position = seat;
        body.rb.rotation = angle;

        var vehicleRb = vehicle.mainPart.rb;
        var vehicleVelocity = vehicleRb.velocity;

        body.rb.velocity = vehicleVelocity;
        body.rb.angularVelocity = vehicleRb.angularVelocity;
        body.lastMoveDir = vehicleVelocity;
    }

    private void RestoreLocalVehiclePhysics()
    {
        if (!localVehicleLocked)
            return;

        if (localVehicleBody != null && localVehicleBody.rb != null)
        {
            var rb = localVehicleBody.rb;

            rb.position = localVehicleBody.transform.position;
            rb.rotation = localVehicleBody.transform.eulerAngles.z;

            if (localVehicle != null &&
                localVehicle.mainPart != null &&
                localVehicle.mainPart.rb != null)
            {
                rb.velocity = localVehicle.mainPart.rb.velocity;
                rb.angularVelocity = localVehicle.mainPart.rb.angularVelocity;
            }

            rb.simulated = localVehicleWasSimulated;
        }

        localVehicleBody = null;
        localVehicle = null;
        localVehicleLocked = false;
    }

    internal bool TryGetLocalSpawnPosition(out Vector3 position)
    {
        var body = PlayerScript.player?.bodyScript;
        if (body == null)
        {
            position = default(Vector3);
            return false;
        }
        var scene = SceneManager.GetActiveScene();
        if (localSpawnScene != scene.handle)
        {
            localSpawnScene = scene.handle;
            localSpawnPosition = body.transform.position;
        }
        position = localSpawnPosition;
        return true;
    }

    internal void UpdateLocalRespawn(PlayerScript player)
    {
        if (localRespawnPending) return;
        var body = player?.bodyScript;
        if (body == null) return;
        var scene = SceneManager.GetActiveScene();
        if (livesRule != MultiplayerSession.NumberOfLives)
        {
            livesRule = MultiplayerSession.NumberOfLives;
            remainingLives = exhaustedLivesLobbies.Contains(MultiplayerSession.LobbyId) ? 0 : livesRule;
        }
        if (scene.handle != localSpawnScene)
        {
            localSpawnScene = scene.handle;
            localSpawnPosition = body.transform.position;
            localDeathPosition = localSpawnPosition;
            localWasAlive = body.isAlive;
            respawnAt = -1f;
            remainingLives = exhaustedLivesLobbies.Contains(MultiplayerSession.LobbyId) ? 0 : MultiplayerSession.NumberOfLives;
        }

        if (body.isAlive)
        {
            localWasAlive = true;
            if (MultiplayerSession.NumberOfLives > 0 && remainingLives == 0)
            {
                localWasAlive = false;
                body.Death();
                return;
            }
            respawnAt = -1f;
            return;
        }

        if (localWasAlive)
        {
            localWasAlive = false;
            localDeathPosition = body.transform.position;
            if (MultiplayerSession.NumberOfLives > 0) remainingLives = Mathf.Max(0, remainingLives - 1);
            if (remainingLives == 0) exhaustedLivesLobbies.Add(MultiplayerSession.LobbyId);
            respawnAt = CanRespawn
                ? Time.unscaledTime + MultiplayerSession.RespawnTimeSeconds
                : -1f;
        }
        TeleportRespawnBodyToNpc(body);
        if (CanRespawn)
        {
            if (respawnAt >= 0f && Time.unscaledTime >= respawnAt)
                RespawnLocalPlayer(player, body);
        }
        else
            GameManager.main.swapAmount = 0;
    }

    private void TeleportRespawnBodyToNpc(BodyScript body)
    {
        if (MultiplayerSession.RespawnAtStart || MultiplayerSession.PvpEnabled || !CanRespawn ||
            respawnAt < 0f || body == null || body.isAlive || !Input.GetMouseButtonDown(0)) return;
        var camera = Camera.main;
        if (camera == null) return;
        var point = (Vector2) camera.ScreenToWorldPoint(Input.mousePosition);
        foreach (var collider in Physics2D.OverlapPointAll(point))
        {
            var npc = collider == null ? null : collider.GetComponentInParent<BodyScript>();
            if (npc == null || npc.isPlayer || !npc.gameObject.activeInHierarchy) continue;
            Vector3 position;
            if (!TryFindRespawnPositionNearBody(npc, body, out position)) return;
            var offset = position - body.transform.position;
            body.transform.root.position += offset;
            foreach (var rigidbody in body.GetComponentsInChildren<Rigidbody2D>(true))
            {
                if (rigidbody == null) continue;
                rigidbody.velocity = Vector2.zero;
                rigidbody.angularVelocity = 0f;
            }
            localDeathPosition = position;
            return;
        }
    }

    public bool CanRespawn => MultiplayerSession.AllowRespawn &&
        (MultiplayerSession.NumberOfLives == 0 || remainingLives > 0);

    internal void UpdateSpectator(PlayerScript player)
    {
        var body = player?.bodyScript;
        if (body == null) return;
        if (CanRespawn || body.isAlive)
        {
            if (spectating)
            {
                if (CameraFollow.cam != null) CameraFollow.cam.target = body.transform;
                RestoreSpectatorVisuals(player);
            }
            spectating = false;
            spectatorPeerId = 0;
            return;
        }

        var candidates = new List<NetworkAvatarReplication>();
        foreach (var pair in NetworkAvatarManager.replicas)
        {
            var replica = pair.Value;
            if (replica != null && replica.remoteBody != null && replica.remoteBody.isAlive)
                candidates.Add(replica);
        }
        candidates.Sort((left, right) => left.remotePeerId.CompareTo(right.remotePeerId));

        if (candidates.Count == 0)
        {
            spectating = true;
            spectatorPeerId = 0;
            if (CameraFollow.cam != null) CameraFollow.cam.target = body.transform;
            SuppressSpectatorDeathEffects(player);
            return;
        }

        var requestedChange = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) ||
            Input.GetKeyDown(KeyCode.Q) || Input.mouseScrollDelta.y < 0f;
        var requestedNext = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) ||
            Input.GetKeyDown(KeyCode.E) || Input.mouseScrollDelta.y > 0f;
        var selectedIndex = -1;
        for (var index = 0; index < candidates.Count; index++)
            if (candidates[index].remotePeerId == spectatorPeerId)
            {
                selectedIndex = index;
                break;
            }
        if (selectedIndex < 0) selectedIndex = 0;
        if (requestedChange) selectedIndex = (selectedIndex + candidates.Count - 1) % candidates.Count;
        if (requestedNext) selectedIndex = (selectedIndex + 1) % candidates.Count;

        var target = candidates[selectedIndex];
        spectating = true;
        spectatorPeerId = target.remotePeerId;
        SuppressSpectatorDeathEffects(player);
        if (CameraFollow.cam != null && CameraFollow.cam.target != target.remoteBody.transform)
            CameraFollow.cam.target = target.remoteBody.transform;
    }

    internal static void SuppressSpectatorDeathEffects(PlayerScript player)
    {
        if (Instance == null || !Instance.spectating || Instance.CanRespawn ||
            player == null || player.bodyScript == null || player.bodyScript.isAlive) return;
        if (player.deathNoise != null) player.deathNoise.color = Color.clear;
        if (player.deathText != null) player.deathText.SetActive(false);
        if (player.crosshair != null) player.crosshair.gameObject.SetActive(false);
        if (player.crossDot != null) player.crossDot.gameObject.SetActive(false);
        if (player.crossLine != null) player.crossLine.gameObject.SetActive(false);
        var screen = ScreenFXManager.main;
        if (screen == null) return;
        screen.targetVign = 0f;
        if (screen.vign != null) screen.vign.intensity.value = 0f;
        screen.targetBlur = 4f;
        screen.doFogBlur = true;
        foreach (var source in player.GetComponents<AudioSource>())
            if (source != null && source.clip != null && source.clip.name == "Underwater")
                source.volume = 0f;
    }

    private static void RestoreSpectatorVisuals(PlayerScript player)
    {
        var screen = ScreenFXManager.main;
        if (screen != null) screen.targetVign = 0.45f;
        if (player == null) return;
        if (player.crosshair != null) player.crosshair.gameObject.SetActive(true);
        if (player.crossDot != null) player.crossDot.gameObject.SetActive(true);
        if (player.crossLine != null) player.crossLine.gameObject.SetActive(true);
    }

    private void RespawnLocalPlayer(PlayerScript player, BodyScript oldBody)
    {
        if (localRespawnPending) return;
        localRespawnPending = true;
        var generation = ++localRespawnGeneration;
        respawnAt = -1f;
        localRespawnProtectionUntil = Time.unscaledTime + RespawnProtectionSeconds;
        var prefabPath = string.IsNullOrEmpty(pendingRespawnCharacterPrefab)
            ? ResolveCharacterPrefab(oldBody) : pendingRespawnCharacterPrefab;
        var prefab = string.IsNullOrEmpty(prefabPath) ? null : Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            localRespawnPending = false;
            GunsawMultiplayerPlugin.LogInfo("Could not respawn player: character prefab is missing.");
            return;
        }
        if (!string.IsNullOrEmpty(pendingRespawnCharacterPrefab))
        {
            selectedCharacterPrefab = pendingRespawnCharacterPrefab;
            PlayerPrefs.SetString("charPrefab", selectedCharacterPrefab);
            pendingRespawnCharacterPrefab = "";
        }
        EnsureRespawnWeaponSlots(oldBody);

        var position = ResolveRespawnPosition(oldBody);
        pendingRespawnLoadoutSource = prefab.GetComponentInChildren<BodyScript>(true);
        var avatar = Instantiate(prefab, position, Quaternion.identity);

        foreach (var prefabPlayer in avatar.GetComponentsInChildren<PlayerScript>(true))
            DestroyImmediate(prefabPlayer);
        var newBody = avatar.GetComponentInChildren<BodyScript>();
        if (newBody == null)
        {
            Destroy(avatar);
            localRespawnPending = false;
            GunsawMultiplayerPlugin.LogInfo("Could not respawn player: character body is invalid.");
            return;
        }

        ReviveRespawnBody(newBody);
        newBody.isPlayer = true;
        newBody.team = "goodguys";
        newBody.crateDamage = true;
        newBody.healthRegen = newBody.regenOnSwap;
        LobbyRegenRule.Apply(newBody, MultiplayerSession.RegenFactor);
        LobbyHealthRule.RestoreFull(newBody);
        newBody.isWalking = false;
        newBody.EnterFullControl();
        foreach (var chatter in avatar.GetComponentsInChildren<Chatter>(true)) DestroyImmediate(chatter);
        foreach (var ai in avatar.GetComponentsInChildren<AIScript>(true)) DestroyImmediate(ai);
        if (newBody.limbs == null || newBody.limbs.Count == 0)
        {
            Destroy(avatar);
            localRespawnPending = false;
            GunsawMultiplayerPlugin.LogInfo("Could not respawn player: character has no initialized limbs.");
            return;
        }

        EnsureRespawnWeaponSlots(newBody);
        RestoreRespawnScarf(newBody);
        var levitator = newBody.gameObject.AddComponent<LevitatorScript>();
        levitator.levitMask = LayerMask.GetMask("Ground");
        levitator.grabMask = LayerMask.GetMask("Default", "Ground", "Entity", "EntityStand", "DropWeapon");
        levitator.rb = newBody.rb;
        levitator.refBody = newBody;
        var weaponBack = newBody.GetComponent<WeaponBackShow>();
        if (weaponBack != null) weaponBack.active = true;
        player.bodyScript = newBody;
        player.levit = levitator;
        player.enabled = true;
        localPlayerInstance = player;
        localGlobalBody = newBody.transform;
        RestoreLocalPlayerSingleton();
        localWasAlive = true;
        StartCoroutine(FinalizeLocalRespawn(newBody, oldBody, generation));

        if (ScreenFXManager.main != null) ScreenFXManager.main.Teleported();

        Sound.Play(EmbeddedAudioLoader.RespawnSound, position, true, false);
    }

    private IEnumerator FinalizeLocalRespawn(BodyScript newBody, BodyScript oldBody, int generation)
    {
        yield return null;
        try
        {
            if (generation != localRespawnGeneration)
            {
                var current = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
                if (newBody != null && newBody != current) Destroy(newBody.transform.root.gameObject);
                yield break;
            }
            if (newBody == null || newBody.limbs == null ||
                newBody.limbs.Count == 0) yield break;
            EnsureRespawnWeaponSlots(oldBody);
            EnsureRespawnWeaponSlots(newBody);

            if (localPlayerInstance == null) yield break;
            localPlayerInstance.bodyScript = newBody;
            localGlobalBody = newBody.transform;
            RestoreLocalPlayerSingleton();
            EnsurePlayerAmmoDisplaySlots(localPlayerInstance);

            ReviveRespawnBody(newBody);
            LobbyRegenRule.Apply(newBody, MultiplayerSession.RegenFactor);
            LobbyHealthRule.RestoreFull(newBody);
            newBody.isPlayer = true;
            newBody.team = "goodguys";

            if (newBody.isInWater)
                newBody.EnterHalfControl();
            else
                newBody.EnterFullControl();

            if (oldBody != null)
            {
                oldBody.OnWeaponChanged.RemoveListener(localPlayerInstance.BodyWeaponChanged);
                oldBody.OnDeath.RemoveListener(localPlayerInstance.OnDied);
                oldBody.OnAmmoChanged.RemoveListener(localPlayerInstance.BodyAmmoChanged);
            }
            newBody.OnWeaponChanged.AddListener(localPlayerInstance.BodyWeaponChanged);
            newBody.OnDeath.AddListener(localPlayerInstance.OnDied);
            newBody.OnAmmoChanged.AddListener(localPlayerInstance.BodyAmmoChanged);
            localPlayerInstance.BodyWeaponChanged();
            localPlayerInstance.BodyAmmoChanged();
            localPlayerInstance.curHealthShow = newBody.health;
            localPlayerInstance.curStaminaShow = newBody.stamina;
            RebindLimbDamageIndicators(localPlayerInstance, newBody, oldBody);
            localPlayerInstance.UnDie();
            pendingRespawnLoadoutBody = newBody;
            if (CameraFollow.cam != null) CameraFollow.cam.target = newBody.transform;
            localRespawnProtectionUntil = Time.unscaledTime + RespawnProtectionSeconds;
            if (localPlayerInstance.bloodBars != null)
                localPlayerInstance.bloodBars.body = newBody;

        GunsawMultiplayerPlugin.LogInfo("Local player respawned at " +
                (MultiplayerSession.RespawnAtStart ? "level start." : "death position."));
        }
        finally
        {
            if (oldBody != null && oldBody.transform != null && newBody != null &&
                oldBody.transform.root != newBody.transform.root)
                Destroy(oldBody.transform.root.gameObject);
            localRespawnPending = false;
        }
    }

    private static void RebindLimbDamageIndicators(PlayerScript player, BodyScript newBody, BodyScript oldBody)
    {
        if (player == null || newBody == null || player.dismemberMananagers == null) return;
        var managers = player.dismemberMananagers;
        var fallback = newBody.GetComponentsInChildren<DismemberManager>(true);
        var fallbackIndex = 0;
        for (var slot = 0; slot < managers.Length; slot++)
        {
            DismemberManager rebound = null;
            var previous = managers[slot];
            if (previous != null && oldBody != null && oldBody.limbs != null && newBody.limbs != null)
            {
                var limbIndex = oldBody.limbs.IndexOf(previous.GetComponent<LimbScript>());
                if (limbIndex >= 0 && limbIndex < newBody.limbs.Count && newBody.limbs[limbIndex] != null)
                    rebound = newBody.limbs[limbIndex].GetComponent<DismemberManager>();
            }
            if (rebound == null && fallbackIndex < fallback.Length) rebound = fallback[fallbackIndex++];
            managers[slot] = rebound;
        }
    }

    private static void RestoreRespawnScarf(BodyScript body)
    {
        if (body == null || body.limbs == null || body.limbs.Count < 2 || body.limbs[1] == null) return;
        RemoveReplicaScarfArtifacts(body.transform.root.gameObject);
        PlayerScript.AddScarfToCreature(body);
        var neck = body.limbs[1];
        var neckRenderer = neck.GetComponent<SpriteRenderer>();
        var sprite = Resources.Load<Sprite>("scarfImage");
        if (neckRenderer == null || sprite == null) return;
        var hold = new GameObject("ScarfHold", typeof(SpriteRenderer));
        var renderer = hold.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.material = body.limbMat;
        renderer.color = Color.HSVToRGB(PlayerPrefs.GetFloat("scStHue"),
            PlayerPrefs.GetFloat("scStSat"), PlayerPrefs.GetFloat("scStVal"));
        renderer.sortingOrder = neckRenderer.sortingOrder + 1;
        renderer.sortingLayerName = neckRenderer.sortingLayerName;
        hold.transform.SetParent(neck.transform);
        hold.transform.localPosition = Vector3.zero;
        hold.transform.localRotation = Quaternion.identity;
        hold.transform.localScale = Vector3.one;
    }

    private Vector3 ResolveRespawnPosition(BodyScript oldBody)
    {
        Vector3 spawnPoint;
        if (MultiplayerSession.RespawnAtStart &&
            CustomLevelSpawnSelection.TryGetRandomSpawnPosition(out spawnPoint))
        {
            if (!IsRespawnPositionBlocked(spawnPoint, oldBody)) return spawnPoint;
            for (var index = 0; index < 8; index++)
            {
                var angle = index * Mathf.PI * 0.25f;
                var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 2.5f;
                if (!IsRespawnPositionBlocked(spawnPoint + offset, oldBody)) return spawnPoint + offset;
            }
        }
        var candidate = MultiplayerSession.RespawnAtStart
            ? localSpawnPosition
            : (oldBody == null ? localDeathPosition : oldBody.transform.position);
        if (!IsRespawnPositionBlocked(candidate, oldBody)) return candidate;
        if (!MultiplayerSession.RespawnAtStart &&
            TryFindRespawnPositionNearPlayer(oldBody, out candidate)) return candidate;
        if (!IsRespawnPositionBlocked(localSpawnPosition, oldBody)) return localSpawnPosition;
        for (var index = 0; index < 8; index++)
        {
            var angle = index * Mathf.PI * 0.25f;
            var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 2.5f;
            if (!IsRespawnPositionBlocked(localSpawnPosition + offset, oldBody))
                return localSpawnPosition + offset;
        }
        return localSpawnPosition;
    }

    private static bool TryFindRespawnPositionNearPlayer(BodyScript oldBody, out Vector3 position)
    {
        foreach (var player in FindObjectsOfType<BodyScript>())
        {
            if (player == null || player == oldBody || !player.isPlayer || !player.isAlive ||
                !player.gameObject.activeInHierarchy) continue;
            if (TryFindRespawnPositionNearBody(player, oldBody, out position)) return true;
        }
        position = default(Vector3);
        return false;
    }

    private static bool TryFindRespawnPositionNearBody(BodyScript target, BodyScript oldBody, out Vector3 position)
    {
        if (target != null)
            for (var index = 0; index < 8; index++)
            {
                var angle = index * Mathf.PI * 0.25f;
                var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 2.5f;
                var candidate = target.transform.position + offset;
                if (!IsRespawnPositionBlocked(candidate, oldBody))
                {
                    position = candidate;
                    return true;
                }
            }
        position = default(Vector3);
        return false;
    }

    private static bool IsRespawnPositionBlocked(Vector3 position, BodyScript oldBody)
    {
        // test anti nugget
        foreach (var collider in Physics2D.OverlapPointAll(position + Vector3.up * 0.01f))
        {
            if (collider == null || collider.isTrigger) continue;
            if (oldBody != null && collider.transform.IsChildOf(oldBody.transform.root)) continue;
            return true;
        }
        return false;
    }

    internal static void ReviveRespawnBody(BodyScript body)
    {
        if (body == null) return;
        LobbyHealthRule.RestoreFull(body);
        body.CurrentState = 0;
        body.controlState = BodyScript.RagdollState.FullControl;
        body.isAlive = true;
        body.WakeUp();
        body.health = body.maxHealth;
        body.isAlive = true;

        foreach (var limb in body.limbs)
        {
            if (limb == null || limb.passer == null || limb.passer.relevantDismember == null)
                continue;

            var manager = limb.passer.relevantDismember;
                                        // Expie
            manager.currentDamage = 0f; // 0 0
            manager.damageFall += 4f;   // 0 4
            manager.damageFall *= 1.8f; // 33 59.4
        }
    }

    internal static void EnsureRespawnWeaponSlots(BodyScript body)
    {
        if (body == null) return;

        if (body.weapons == null) body.weapons = new List<WeaponPreset>();
        while (body.weapons.Count < 3) body.weapons.Add(null);

        if (body.weaponAmmos == null) body.weaponAmmos = new List<int>();
        while (body.weaponAmmos.Count < body.weapons.Count) body.weaponAmmos.Add(0);

        if (body.currentWeapon < 0 || body.currentWeapon >= 3)
            body.currentWeapon = 0;
    }

    internal static void ApplyLobbyLoadout(BodyScript body, string rule)
    {
        if (body == null || string.Equals((rule ?? "").Trim(), "Default", StringComparison.OrdinalIgnoreCase)) return;
        EnsureRespawnWeaponSlots(body);
        var values = SplitLobbyWeaponRule(rule);
        var firstWeapon = -1;
        for (var slot = 0; slot < 3; slot++)
        {
            var value = slot < values.Count ? values[slot].Trim() : "None";
            if (string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
            {
                if (body.weapons[slot] != null && firstWeapon < 0) firstWeapon = slot;
                continue;
            }
            if (string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(value))
            {
                body.weapons[slot] = null;
                body.weaponAmmos[slot] = 0;
                continue;
            }
            var preset = FindLobbyWeaponPreset(value);
            body.weapons[slot] = preset;
            body.weaponAmmos[slot] = preset == null ? 0 : preset.magSize;
            if (preset != null && firstWeapon < 0) firstWeapon = slot;
        }
        if (firstWeapon >= 0)
        {
            body.currentWeapon = firstWeapon;
            body.ChangeWeapon(firstWeapon);
        }
        else body.ChangeToUnarmed();
    }

    internal void ApplyStartingLobbyLoadout(BodyScript body)
    {
        if (body == null || body == startingLoadoutAppliedBody || localRespawnPending) return;
        if (MultiplayerSession.GunGameEnabled)
        {
            startingLoadoutAppliedBody = body;
            return;
        }
        var rule = MultiplayerSession.StartingWeapon;
        if (string.Equals((rule ?? "").Trim(), "Default", StringComparison.OrdinalIgnoreCase)) return;
        ApplyLobbyLoadout(body, rule);
        startingLoadoutAppliedBody = body;
    }

    internal void ApplyPendingRespawnLobbyLoadout(BodyScript body)
    {
        if (body == null || body != pendingRespawnLoadoutBody || !body.isAlive) return;
        if (MultiplayerSession.GunGameEnabled)
        {
            pendingRespawnLoadoutBody = null;
            pendingRespawnLoadoutSource = null;
            startingLoadoutAppliedBody = body;
            startingAmmoAppliedBody = body;
            return;
        }
        var rule = MultiplayerSession.RespawnWeapon;
        if (string.Equals((rule ?? "").Trim(), "Default", StringComparison.OrdinalIgnoreCase))
            ApplyDefaultLobbyLoadout(body, pendingRespawnLoadoutSource);
        else ApplyLobbyLoadout(body, rule);
        LobbyAmmoRules.Apply(body, MultiplayerSession.RespawnAmmo);
        startingLoadoutAppliedBody = body;
        startingAmmoAppliedBody = body;
        pendingRespawnLoadoutBody = null;
        pendingRespawnLoadoutSource = null;
        forceFullInventoryUntil = Time.unscaledTime + 0.5f;
        nextFullInventory = 0f;
        nextSnapshot = 0f;
    }

    private static void ApplyDefaultLobbyLoadout(BodyScript body, BodyScript source)
    {
        if (body == null) return;
        EnsureRespawnWeaponSlots(body);
        var preset = body.desiredStartWep ?? (source == null ? null : source.desiredStartWep);
        if (preset != null && preset.slot >= 0 && preset.slot < 3)
        {
            for (var slot = 0; slot < 3; slot++)
            {
                body.weapons[slot] = null;
                body.weaponAmmos[slot] = 0;
            }
            body.weapons[preset.slot] = preset;
            body.weaponAmmos[preset.slot] = preset.magSize;
            body.currentWeapon = preset.slot;
            body.ChangeWeapon(preset.slot);
            return;
        }
        ApplyLobbyLoadout(body, "None;None;None");
    }

    internal void ApplyStartingLobbyAmmo(BodyScript body)
    {
        if (body == null || body == startingAmmoAppliedBody || !MultiplayerSession.LobbySettingsReceived) return;
        LobbyAmmoRules.Apply(body, MultiplayerSession.StartingAmmo);
        startingAmmoAppliedBody = body;
    }

    private static List<string> SplitLobbyWeaponRule(string rule)
    {
        var values = new List<string>();
        var start = 0;
        var depth = 0;
        var text = rule ?? string.Empty;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '[') depth++;
            else if (text[index] == ']' && depth > 0) depth--;
            else if (text[index] == ';' && depth == 0)
            {
                values.Add(text.Substring(start, index - start));
                start = index + 1;
            }
        }
        values.Add(text.Substring(start));
        return values;
    }

    private static WeaponPreset FindLobbyWeaponPreset(string value)
    {
        if (!TryParseRandomWeaponRule(value, out var include, out var names)) return FindWeaponPresetByName(value);
        var candidates = new List<WeaponPreset>();
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
        {
            if (preset == null || preset.sprite == null) continue;
            var listed = false;
            foreach (var name in names)
            {
                if (string.Equals(preset.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    listed = true;
                    break;
                }
            }
            if ((include && names.Count > 0 && !listed) || (!include && listed)) continue;
            candidates.Add(preset);
        }
        return candidates.Count == 0 ? null : candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    private static bool TryParseRandomWeaponRule(string value, out bool include, out List<string> names)
    {
        include = false;
        names = new List<string>();
        var text = (value ?? string.Empty).Trim();
        if (string.Equals(text, "Random", StringComparison.OrdinalIgnoreCase)) return true;
        if (!text.StartsWith("Random[", StringComparison.OrdinalIgnoreCase) || !text.EndsWith("]")) return false;
        var filter = text.Substring(7, text.Length - 8).Trim();
        if (filter.StartsWith("in=", StringComparison.OrdinalIgnoreCase)) include = true;
        else if (filter.StartsWith("ex=", StringComparison.OrdinalIgnoreCase)) include = false;
        else return false;
        foreach (var name in filter.Substring(3).Split(';'))
        {
            var trimmed = name.Trim();
            if (!string.IsNullOrEmpty(trimmed)) names.Add(trimmed);
        }
        return true;
    }

    private static WeaponPreset FindWeaponPresetByName(string name)
    {
        foreach (var preset in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (preset != null && preset.sprite != null && string.Equals(preset.name, name, StringComparison.OrdinalIgnoreCase))
                return preset;
        return null;
    }

    internal static void EnsurePlayerAmmoDisplaySlots(PlayerScript player)
    {
        if (player == null || player.bodyScript == null) return;
        var ammoTexts = player.ammoTexts;
        var required = ammoTexts == null ? 0 : ammoTexts.Count;
        if (required <= 0) return;
        if (player.bodyScript.ammoAmount == null)
            player.bodyScript.ammoAmount = new List<int>();
        while (player.bodyScript.ammoAmount.Count < required)
            player.bodyScript.ammoAmount.Add(0);
    }

    internal static bool PrepareWeaponReload(WeaponScript weapon)
    {
        if (weapon == null || weapon.stats == null || weapon.body == null) return false;

        var body = weapon.body;
        var ammoType = weapon.stats.ammoType;
        if (ammoType < 0) return false;
        if (body.ammoAmount == null) body.ammoAmount = new List<int>();
        while (body.ammoAmount.Count <= ammoType) body.ammoAmount.Add(0);

        var slot = body.currentWeapon;
        if (slot < 0) return false;
        if (body.weaponAmmos == null) body.weaponAmmos = new List<int>();
        while (body.weaponAmmos.Count <= slot) body.weaponAmmos.Add(0);

        if (!weapon.stats.hasSpecialReload) return true;
        if (weapon.stats.specialAnims == null) return false;
        var animationIndex = body.ammoAmount[ammoType] < weapon.stats.magSize &&
            body.ammoAmount[ammoType] < weapon.stats.magSize - weapon.ammo
            ? weapon.stats.magSize - body.ammoAmount[ammoType]
            : weapon.ammo;
        return animationIndex >= 0 && animationIndex < weapon.stats.specialAnims.Length;
    }

    internal static void EnsureLocalPlayerSingleton()
    {
        if (localPlayerInstance != null && localPlayerInstance.bodyScript != null &&
            localPlayerInstance.gameObject.activeInHierarchy)
        {
            localGlobalBody = localPlayerInstance.bodyScript.transform;
            RestoreLocalPlayerSingleton();
            return;
        }

        var current = PlayerScript.player;
        if (current != null && current.bodyScript != null)
        {
            localPlayerInstance = current;
            if (PlayerScript.globalBody == null)
                localGlobalBody = current.bodyScript.transform;
            RestoreLocalPlayerSingleton();
            return;
        }

        foreach (var candidate in FindObjectsOfType<PlayerScript>())
        {
            if (candidate == null || candidate.bodyScript == null ||
                !candidate.bodyScript.isPlayer) continue;
            localPlayerInstance = candidate;
            localGlobalBody = candidate.bodyScript.transform;
            RestoreLocalPlayerSingleton();
            return;
        }
    }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
    }

    internal void Configure(string name)
    {
        localName = string.IsNullOrEmpty(name) ? "Player" : name;
    }

    protected override void Update() { }

    protected override void LateUpdate() => UpdateLocalVehicleLock();

    private void FixedUpdate() => ApplyIncomingGrab();

    protected override void OnDestroy()
    {
        RestoreLocalVehiclePhysics();
        if (Instance == this)
            Instance = null;
    }

    private sealed class GrabCommand
    {
        internal byte Kind;
        internal short Index;
        internal Vector2 Point;
        internal Vector2 LocalPoint;
    }
}