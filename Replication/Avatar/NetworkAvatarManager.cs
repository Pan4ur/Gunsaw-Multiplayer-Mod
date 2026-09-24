using UnityEngine;
using static LocalPlayerReplication;
using static NetworkAvatarUtilities;

internal sealed class NetworkAvatarManager : MonoBehaviour
{
    internal static readonly Dictionary<ushort, NetworkAvatarReplication> replicas = new();

    internal static void UnregisterReplica(NetworkAvatarReplication replica)
    {
        if (ReferenceEquals(replica, null)) return;
        NetworkAvatarReplication current;
        if (replicas.TryGetValue(replica.remotePeerId, out current) && ReferenceEquals(current, replica))
            replicas.Remove(replica.remotePeerId);
    }

    internal static NetworkAvatarReplication GetOrCreateReplica(ushort peerId)
    {
        var local = LocalPlayerReplication.Instance;
        if (local == null || peerId == 0 || peerId == MultiplayerSession.LocalPeerId) return null;
        NetworkAvatarReplication replica;
        if (replicas.TryGetValue(peerId, out replica) && replica != null) return replica;
        replica = local.gameObject.AddComponent<NetworkAvatarReplication>();
        replica.remotePeerId = peerId;
        replicas[peerId] = replica;
        return replica;
    }

    internal static NetworkAvatarReplication ReplicaForBody(BodyScript body)
    {
        if (body == null) return null;
        foreach (var replica in replicas.Values)
            if (replica != null && replica.remoteBody == body) return replica;
        return null;
    }

    internal static BodyScript RemoteBodyForPeer(ushort peerId)
    {
        NetworkAvatarReplication replica;
        return replicas.TryGetValue(peerId, out replica) && replica != null ? replica.remoteBody : null;
    }

    internal static RemotePlayerInfo[] RemotePlayers()
    {
        var result = new List<RemotePlayerInfo>(replicas.Count);
        foreach (var pair in replicas)
            if (pair.Value != null)
                result.Add(new RemotePlayerInfo
                {
                    PeerId = pair.Key,
                    Name = pair.Value.remoteName,
                    Body = pair.Value.remoteBody,
                    AuthoritativePosition = pair.Value.lastAuthoritativePosition,
                    HasAuthoritativePosition = pair.Value.hasAuthoritativePosition,
                    PingMs = MultiplayerSession.PeerPing(pair.Key)
                });
        result.Sort((left, right) => left.PeerId.CompareTo(right.PeerId));
        return result.ToArray();
    }

    internal static string RemoteNameForBody(BodyScript body)
    {
        var replica = ReplicaForBody(body);
        return replica == null ? "Player" : replica.remoteName;
    }

    internal static bool IsRemoteAvatarBody(BodyScript body)
    {
        return ReplicaForBody(body) != null;
    }

    internal static bool IsRemoteReplicaBody(BodyScript body)
    {
        return body != null && (ReplicaForBody(body) != null || body.GetComponentInParent<NetworkReplica>() != null);
    }

    internal static void CleanupDisconnectedReplicas()
    {
        var stale = new List<ushort>();
        foreach (var pair in replicas)
            if (pair.Value == null || !MultiplayerSession.HasPeer(pair.Key)) stale.Add(pair.Key);
        foreach (var peerId in stale)
        {
            NetworkAvatarReplication replica;
            if (replicas.TryGetValue(peerId, out replica) && replica != null)
            {
                replica.DestroyRemote();
                UnityEngine.Object.Destroy(replica);
            }
            replicas.Remove(peerId);
        }
    }

    internal static void DestroyAllReplicas()
    {
        foreach (var replica in new List<NetworkAvatarReplication>(replicas.Values))
            if (replica != null)
            {
                replica.DestroyRemote();
                UnityEngine.Object.Destroy(replica);
            }
        replicas.Clear();
    }

    internal static void CreateRemoteExplosionCracks(ProjectileImpactPacket packet)
    {
        var position = new Vector2(packet.PositionX, packet.PositionY);
        if (!packet.HasExplosionTrace)
        {
            var trace = CaptureExplosionTrace(position);
            packet = new ProjectileImpactPacket(position.x, position.y, "", true, trace.HasBackgroundCrack,
                trace.BackgroundCrackRotation, trace.BackgroundCrackFlipX, trace.BackgroundCrackFlipY,
                trace.HasFloorCrack, trace.FloorCrackPosition.x, trace.FloorCrackPosition.y, trace.FloorCrackFlipX);
        }
        var backgroundCrack = Resources.Load<GameObject>("Spawnables/BackgroundCrack");
        if (packet.HasBackgroundCrack)
        {
            var crack = InstantiateExplosionCrack(backgroundCrack, position,
                Quaternion.Euler(0f, 0f, packet.BackgroundCrackRotation));
            SetCrackFlip(crack, packet.BackgroundCrackFlipX, packet.BackgroundCrackFlipY);
            if (crack != null) Destroy(crack, 120f);
        }

        var floorCrack = Resources.Load<GameObject>("Spawnables/FloorCrack");
        if (packet.HasFloorCrack)
        {
            var crack = InstantiateExplosionCrack(floorCrack,
                new Vector2(packet.FloorCrackX, packet.FloorCrackY), Quaternion.identity);
            SetCrackFlip(crack, packet.FloorCrackFlipX, false);
            if (crack != null) Destroy(crack, 120f);
        }
    }

    internal static ExplosionTrace CaptureExplosionTrace(Vector2 position)
    {
        var trace = new ExplosionTrace();
        foreach (var wall in GameManager.wallColls)
        {
            if (wall == null || !wall.OverlapPoint(position)) continue;
            trace.HasBackgroundCrack = true;
            trace.BackgroundCrackRotation = UnityEngine.Random.Range(0f, 360f);
            trace.BackgroundCrackFlipX = UnityEngine.Random.Range(0f, 1f) > 0.5f;
            trace.BackgroundCrackFlipY = UnityEngine.Random.Range(0f, 1f) > 0.5f;
            break;
        }
        foreach (var hit in Physics2D.RaycastAll(position, Vector2.down, 10f, LayerMask.GetMask("Ground")))
        {
            if (hit.rigidbody != null) continue;
            trace.HasFloorCrack = true;
            trace.FloorCrackPosition = hit.point;
            trace.FloorCrackFlipX = UnityEngine.Random.Range(0f, 1f) > 0.5f;
            break;
        }
        return trace;
    }

    private static GameObject InstantiateExplosionCrack(GameObject prefab, Vector2 position, Quaternion rotation)
    {
        if (prefab == null) return null;
        var crack = Instantiate(prefab, position, rotation);
        var sourceRenderer = prefab.GetComponent<SpriteRenderer>();
        var renderer = crack.GetComponent<SpriteRenderer>();
        if (sourceRenderer != null && renderer != null)
        {
            renderer.sortingLayerID = sourceRenderer.sortingLayerID;
            renderer.sortingOrder = sourceRenderer.sortingOrder;
        }
        return crack;
    }

    internal static HashSet<int> CaptureExplosionCracks()
    {
        var cracks = new HashSet<int>();
        foreach (var gameObject in FindObjectsOfType<GameObject>())
            if (IsExplosionCrack(gameObject)) cracks.Add(gameObject.GetInstanceID());
        return cracks;
    }

    internal static void DestroyNewExplosionCracks(HashSet<int> existingCracks)
    {
        foreach (var gameObject in FindObjectsOfType<GameObject>())
            if (IsExplosionCrack(gameObject) && !existingCracks.Contains(gameObject.GetInstanceID())) Destroy(gameObject);
    }

    internal static void ScheduleExplosionCrackCleanup()
    {
        foreach (var gameObject in FindObjectsOfType<GameObject>())
            if (IsExplosionCrack(gameObject)) Destroy(gameObject, 120f);
    }

    private static bool IsExplosionCrack(GameObject gameObject)
    {
        return gameObject != null && (gameObject.name.StartsWith("BackgroundCrack") ||
            gameObject.name.StartsWith("FloorCrack"));
    }

    private static void SetCrackFlip(GameObject crack, bool flipX, bool flipY)
    {
        if (crack == null) return;
        var renderer = crack.GetComponent<SpriteRenderer>();
        if (renderer == null) return;
        renderer.flipX = flipX;
        renderer.flipY = flipY;
    }

    internal readonly struct SuppressedTeleportBody
    {
        internal readonly BodyScript Body;
        internal readonly Vector3 Position;
        internal readonly bool IsPlayer;

        internal SuppressedTeleportBody(BodyScript body)
        {
            Body = body;
            Position = body.transform.position;
            IsPlayer = body.isPlayer;
        }
    }

    internal struct ExplosionTrace
    {
        internal bool HasBackgroundCrack;
        internal float BackgroundCrackRotation;
        internal bool BackgroundCrackFlipX;
        internal bool BackgroundCrackFlipY;
        internal bool HasFloorCrack;
        internal Vector2 FloorCrackPosition;
        internal bool FloorCrackFlipX;
    }

    internal static void RecordBodyColliderHit(BodyScript body, LimbScript limb)
    {
        if (!MultiplayerSession.IsConnected ||
            activeShotState == null ||
            activeShotState.Weapon == null ||
            body == null ||
            limb == null)
            return;

        var replica = NetworkAvatarManager.ReplicaForBody(body);

        if (replica == null || replica.remotePeerId == 0)
            return;

        if (!activeShotState.PendingBodyColliderHits.TryGetValue(replica.remotePeerId, out var queue))
        {
            queue = new Queue<LimbScript>();
            activeShotState.PendingBodyColliderHits[replica.remotePeerId] = queue;
        }

        queue.Enqueue(limb);
    }

    internal static bool TakeBodyColliderHit(ShotState state, ushort targetPeerId, LimbScript limb)
    {
        if (state == null || targetPeerId == 0 || limb == null)
            return false;

        if (!state.PendingBodyColliderHits.TryGetValue(targetPeerId, out var queue))
            return false;

        if (queue.Count == 0)
            return false;

        if (queue.Peek() != limb)
            return false;

        queue.Dequeue();

        if (queue.Count == 0)
            state.PendingBodyColliderHits.Remove(targetPeerId);

        return true;
    }

    internal static void AddForceAtPositionWithPropAuthority(Rigidbody2D rb, Vector2 force, Vector2 position, ForceMode2D mode)
    {
        rb.AddForceAtPosition(force, position, mode);
        TryTakePropAuthority(rb);
    }

    internal static void AddForceWithPropAuthority(Rigidbody2D rb, Vector2 force, ForceMode2D mode)
    {
        rb.AddForce(force, mode);
        TryTakePropAuthority(rb);
    }

    private static void TryTakePropAuthority(Rigidbody2D rb)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost || rb == null)
            return;

        var player = PlayerScript.player;
        if (player == null || currentShooter != player.bodyScript)
            return;

        GunsawMultiplayerPlugin.World?.QueueLevitated(rb);
    }

    internal static void EndWeaponShot(ShotState state)
    {
        if (state != null)
        {
            foreach (var collider in state.DisabledColliders)
                if (collider != null) collider.enabled = true;
            currentShooter = state.PreviousShooter;
            activeShotState = state.PreviousShotState;
        }
        else
        {
            currentShooter = null;
            activeShotState = null;
        }
    }

    internal sealed class ShotState
    {
        internal BodyScript PreviousShooter;
        internal ShotState PreviousShotState;
        internal WeaponScript Weapon;
        internal bool IsExplosion;
        internal int AmmoBefore;
        internal int SpreadSeed;
        internal int SpreadIndex;
        internal readonly List<Vector2> ShotDirections = new();
        internal readonly HashSet<string> DestroyedLampIds = new();
        internal readonly List<string> NewlyDestroyedLampIds = new();
        internal Vector2 Origin;
        internal Vector2 Direction;
        internal Vector2 Up;
        internal string WeaponSprite = "";
        internal readonly List<PlayerWound> Wounds = new();
        internal readonly List<Collider2D> DisabledColliders = new();
        internal readonly Dictionary<ushort, Queue<float>> PendingBaseDamage = new();
        internal readonly Dictionary<ushort, Queue<LimbScript>> PendingBodyColliderHits = new();
    }

    internal sealed class TargetScreenEffectState
    {
        internal bool Suppress;
        internal float ScreenShake;
    }

    internal sealed class PlayerWound
    {
        internal ushort TargetPeerId;
        internal short LimbIndex;
        internal Vector2 LocalPoint;
        internal Vector2 Direction;
        internal string WeaponSprite;
        internal string WoundSprite;
        internal bool HasSplash;
        internal bool Critical;
        internal float BaseDamage;
        internal bool BodyColliderHit;
    }

    internal static void ReplicateExplosion(GameObject explosionObject, Vector2 position,
        float range, float force, bool playExplosionSound)
    {
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || !NetworkAvatarUtilities.IsFinite(position.x) ||
            !NetworkAvatarUtilities.IsFinite(position.y) || !NetworkAvatarUtilities.IsFinite(range) || !NetworkAvatarUtilities.IsFinite(force) || range <= 0f || force <= 0f)
            return;
        GunsawMultiplayerPlugin.World?.BroadcastExplosion(explosionObject, position, range, force,
            replicatedExplosionImpulseExclusionPeerId, playExplosionSound);
    }

    internal static void ConfigureProjectileCollisions(Component projectile, BodyScript shooter)
    {
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || (MultiplayerSession.PvpEnabled && !TeamSystem.Enabled) ||
            LocalPlayerReplication.Instance == null || projectile == null || player == null ||
            shooter != player.bodyScript) return;
        foreach (var projectileCollider in projectile.GetComponentsInChildren<Collider2D>(true))
        foreach (var replica in NetworkAvatarManager.replicas.Values)
        {
        if (replica == null || (MultiplayerSession.PvpEnabled && !TeamSystem.Same(MultiplayerSession.LocalPeerId, replica.remotePeerId)))
            continue;
        foreach (var remoteCollider in replica.remoteColliderTriggers.Keys)
            if (projectileCollider != null && remoteCollider != null)
                Physics2D.IgnoreCollision(projectileCollider, remoteCollider, true);
        }
    }

    internal static BodyScript ProjectileOwner(GameObject projectile)
    {
        if (projectile == null) return null;
        var rocket = projectile.GetComponentInChildren<RocketProjectile>(true);
        if (rocket != null) return rocket.origBody;
        var grenade = projectile.GetComponentInChildren<GrenadeScript>(true);
        return grenade == null ? null : grenade.origBody;
    }

    internal static void CompleteWeaponShot(ShotState state, bool completed)
    {
        try
        {
            var player = PlayerScript.player;
            var shooter = state == null || state.Weapon == null ? null : state.Weapon.body;
            var localPlayerShot = player != null && shooter == player.bodyScript;
            var hostNpcShot = MultiplayerSession.IsHost && shooter != null && !shooter.isPlayer &&
                shooter.GetComponentInParent<NetworkReplica>() == null;
            if (!completed || state == null || state.Weapon == null || shooter == null ||
                (!localPlayerShot && !hostNpcShot) || (state.Weapon.ammo >= state.AmmoBefore &&
                !CustomWeaponsCompatibility.IsFiredCustomMelee(state.Weapon)) ||
                !MultiplayerSession.IsConnected) return;
            var targetPeers = new List<ushort>();
            foreach (var wound in state.Wounds)
                if (wound.TargetPeerId != 0 && !targetPeers.Contains(wound.TargetPeerId))
                    targetPeers.Add(wound.TargetPeerId);
            var directionCount = Math.Min(byte.MaxValue, state.ShotDirections.Count);
            var exactDirections = new ShotVisualDirection[directionCount];
            for (var index = 0; index < directionCount; index++)
            {
                var direction = state.ShotDirections[index];
                exactDirections[index] = new ShotVisualDirection(direction.x, direction.y);
            }
            MultiplayerSession.Send(new ShotVisualPacket(state.Origin.x, state.Origin.y, state.Direction.x,
                state.Direction.y, state.Up.x, state.Up.y, state.WeaponSprite, hostNpcShot,
                targetPeers.ToArray(), state.SpreadSeed, exactDirections,
                Array.Empty<string>()));
            foreach (var wound in state.Wounds)
                SendRemotePlayerWound(wound, wound.BaseDamage > 20f || wound.Critical);
        }
        finally { EndWeaponShot(state); }
    }

    internal static void RecordRemoteWound(WeaponScript weapon, LimbScript limb, Vector2 hitpoint,
        Vector2 direction, GameObject splash)
    {
        var replica = limb == null ? null : NetworkAvatarManager.ReplicaForBody(limb.body);
        if (!MultiplayerSession.IsConnected || activeShotState == null || replica == null ||
            weapon != activeShotState.Weapon) return;
        var limbs = replica.remoteBody.limbs ?? [];
        var limbIndex = limbs.IndexOf(limb);
        if (limbIndex < 0 || limbIndex > short.MaxValue) return;
        var woundRenderer = FindLatestWound(limb, hitpoint);
        activeShotState.Wounds.Add(new PlayerWound
        {
            TargetPeerId = replica.remotePeerId,
            LimbIndex = (short)limbIndex,
            LocalPoint = limb.transform.InverseTransformPoint(hitpoint),
            Direction = direction,
            WeaponSprite = NetworkAvatarUtilities.SpriteId(weapon == null || weapon.stats == null ? null : weapon.stats.sprite),
            WoundSprite = NetworkAvatarUtilities.SpriteId(woundRenderer == null ? null : woundRenderer.sprite),
            HasSplash = splash != null,
            Critical = limb.isCritical,
            BaseDamage = TakeBaseDamage(activeShotState, replica.remotePeerId),
            BodyColliderHit = TakeBodyColliderHit(activeShotState, replica.remotePeerId, limb)
        });
    }

    private static void SendRemotePlayerWound(
        PlayerWound wound,
        bool createScreenCrack)
    {
        var type = MultiplayerSession.IsHost
            ? PacketType.PlayerDamage
            : PacketType.PvpDamage;

        MultiplayerSession.Send(
            new PlayerWoundPacket(
                type,
                wound.LimbIndex,
                wound.LocalPoint.x,
                wound.LocalPoint.y,
                wound.Direction.x,
                wound.Direction.y,
                wound.WeaponSprite,
                wound.WoundSprite,
                wound.HasSplash,
                createScreenCrack,
                wound.BaseDamage,
                wound.BodyColliderHit
            ),
            wound.TargetPeerId
        );
    }

    private static uint PlayerSoundId(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in value) hash = (hash ^ char.ToLowerInvariant(character)) * 16777619u;
            return hash;
        }
    }

    internal static uint PlayerActionSoundId(string name)
    {
        if (name == "Kick") return 0x40000001u;
        if (name == "Hit") return 0x40000002u;
        if (name == "boneCrunch") return 0x40000003u;
        return PlayerSoundId(name) & 0x0fffffffu;
    }

    internal static void LoadPlayerSoundClips()
    {
        if (playerSoundClipsLoaded) return;
        playerSoundClipsLoaded = true;
        foreach (var clip in Resources.LoadAll<AudioClip>("Sounds"))
            if (clip != null) playerSoundClips[PlayerSoundId(clip.name) & 0x0fffffffu] = clip;
    }

    internal static readonly Dictionary<uint, AudioClip> playerSoundClips = new();
    private static bool playerSoundClipsLoaded;
    internal static ShotState BeginWeaponShot(WeaponScript weapon)
    {
        var state = new ShotState
        {
            PreviousShooter = currentShooter,
            PreviousShotState = activeShotState,
            Weapon = weapon,
            AmmoBefore = weapon == null ? 0 : weapon.ammo,
            SpreadSeed = Interlocked.Increment(ref nextShotSpreadSeed)
        };
        activeShotState = state;
        currentShooter = weapon == null ? null : weapon.body;
        if (weapon != null && weapon.stats != null)
        {
            state.Origin = weapon.transform.TransformPoint(weapon.stats.barrelPosition);
            var facing = weapon.body != null && !weapon.body.isRight ? -1f : 1f;
            state.Direction = (Vector2)(weapon.transform.right * facing);
            state.Up = weapon.transform.up;
            state.WeaponSprite = weapon.stats.name;
        }
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || (MultiplayerSession.PvpEnabled && !TeamSystem.Enabled) ||
            LocalPlayerReplication.Instance == null || player == null || currentShooter != player.bodyScript)
            return state;
        foreach (var replica in NetworkAvatarManager.replicas.Values)
            if (replica != null && replica.remoteBody != null && replica.remoteBody.isAlive)
            {
                if (MultiplayerSession.PvpEnabled && !TeamSystem.Same(MultiplayerSession.LocalPeerId, replica.remotePeerId))
                    continue;
                foreach (var collider in replica.remoteColliderTriggers.Keys)
                {
                    if (collider == null || !collider.enabled) continue;
                    collider.enabled = false;
                    state.DisabledColliders.Add(collider);
                }
            }
        return state;
    }

    internal static ShotState BeginMeleeAttack(BodyScript attacker)
    {
        var state = new ShotState { PreviousShooter = currentShooter };
        currentShooter = attacker;
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || (MultiplayerSession.PvpEnabled && !TeamSystem.Enabled) ||
            LocalPlayerReplication.Instance == null || player == null || attacker != player.bodyScript)
            return state;
        foreach (var replica in NetworkAvatarManager.replicas.Values)
            if (replica != null)
            {
                if (MultiplayerSession.PvpEnabled && !TeamSystem.Same(MultiplayerSession.LocalPeerId, replica.remotePeerId))
                    continue;
                foreach (var collider in replica.remoteColliderTriggers.Keys)
                {
                    if (collider == null || !collider.enabled) continue;
                    collider.enabled = false;
                    state.DisabledColliders.Add(collider);
                }
            }
        return state;
    }

    internal static void EndMeleeAttack(ShotState state)
    {
        EndWeaponShot(state);
    }

    internal static void BeginPlayerSound(BodyScript body, int footstepSurface = -1)
    {
        currentSoundBody = body;
        currentFootstepSurface = footstepSurface;
    }

    internal static void EndPlayerSound(BodyScript body)
    {
        if (currentSoundBody == body)
        {
            currentSoundBody = null;
            currentFootstepSurface = -1;
        }
    }

    internal static float PlayPlayerActionSound(AudioClip clip, Vector2 position, bool twoDimensional = false, bool pitchShift = false, Transform parent = null, float volume = 1f, float pitch = 1f)
    {
        var result = Sound.Play(clip, position, twoDimensional, pitchShift, parent, volume, pitch);
        var player = PlayerScript.player;
        if (clip != null && currentSoundBody != null && MultiplayerSession.IsConnected && player != null && currentSoundBody == player.bodyScript && !string.IsNullOrWhiteSpace(clip.name))
            MultiplayerSession.Send(new PlayerSoundPacket(currentFootstepSurface < 0 ? PlayerActionSoundId(clip.name) : 0x80000000u | (uint)currentFootstepSurface, position.x, position.y, (byte)Mathf.Clamp(Mathf.RoundToInt(volume * 64f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(pitch * 64f), 0, 255)));
        return result;
    }

    internal static void ReplicateWeaponPickup(BodyScript body)
    {
        if (!MultiplayerSession.IsConnected || body == null || PlayerScript.player == null || body != PlayerScript.player.bodyScript) return;
        MultiplayerSession.Send(new PlayerSoundPacket(0x40000004u, body.transform.position.x, body.transform.position.y, 64, 64));
    }

    internal static void ReplicatePain(BodyScript body)
    {
        if (!MultiplayerSession.IsConnected || body == null || PlayerScript.player == null || body != PlayerScript.player.bodyScript) return;
        if (body.painNoises == null || body.painNoises.Count == 0) return;
        var index = UnityEngine.Random.Range(0, body.painNoises.Count);
        if (body.painNoises[index] == null) return;
        var head = body.headTransform == null ? body.transform : body.headTransform;
        MultiplayerSession.Send(new PlayerSoundPacket(0x10000000u | (uint)index, head.position.x, head.position.y, 64, (byte)Mathf.Clamp(Mathf.RoundToInt(body.voicePitch * 64f), 0, 255)));
    }

    internal static void ReplicateScream(BodyScript body)
    {
        if (!MultiplayerSession.IsConnected || body == null || PlayerScript.player == null || body != PlayerScript.player.bodyScript) return;
        if (body.deathNoises == null || body.deathNoises.Count == 0) return;
        var index = UnityEngine.Random.Range(0, body.deathNoises.Count);
        if (body.deathNoises[index] == null) return;
        var head = body.headTransform == null ? body.transform : body.headTransform;
        MultiplayerSession.Send(new PlayerSoundPacket(0x11000000u | (uint)index, head.position.x, head.position.y, 64, (byte)Mathf.Clamp(Mathf.RoundToInt(body.voicePitch * 64f), 0, 255)));
    }

    internal static void ReplicateTelekinesis(LevitatorScript levitator)
    {
        var body = levitator == null ? null : levitator.refBody;
        if (!MultiplayerSession.IsConnected || body == null || PlayerScript.player == null || body != PlayerScript.player.bodyScript || Time.unscaledTime < nextTelekinesisSound) return;
        nextTelekinesisSound = Time.unscaledTime + 0.1f;
        var active = levitator.currentlyLevitating != null;
        MultiplayerSession.Send(new PlayerSoundPacket(0x50000000u, body.transform.position.x, body.transform.position.y, active ? (byte)38 : (byte)0, active ? (byte)64 : (byte)0));
    }
    internal static void ReplicateFootstep(BodyScript body, SType surface)
    {
        if (!MultiplayerSession.IsConnected || body == null || PlayerScript.player == null || body != PlayerScript.player.bodyScript || body.controlState != BodyScript.RagdollState.FullControl || body.inVehicle) return;
        MultiplayerSession.Send(new PlayerSoundPacket(0x80000000u | (uint)surface, body.transform.position.x, body.transform.position.y, 38, 64));
    }
    internal static void BuildAnimatedSoundCatalog()
    {
        if (animatedSoundCatalogBuilt) return;
        animatedSoundCatalogBuilt = true;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var clip in Resources.FindObjectsOfTypeAll<AnimationClip>())
        {
            if (clip == null) continue;
            foreach (var animationEvent in clip.events)
                if (animationEvent.functionName == "DoSound" && !string.IsNullOrWhiteSpace(animationEvent.stringParameter))
                    names.Add(animationEvent.stringParameter);
        }
        animatedSoundNames.AddRange(names);
        animatedSoundNames.Sort(StringComparer.Ordinal);
        for (ushort index = 0; index < animatedSoundNames.Count && index < ushort.MaxValue; index++)
            animatedSoundIds[animatedSoundNames[index]] = index;
    }

    internal static void ReplicateAnimatedSound(AnimatedBodyScript animatedBody, string soundName)
    {
        var body = animatedBody == null ? null : animatedBody.GetComponentInParent<BodyScript>();
        if (!MultiplayerSession.IsConnected || body == null || PlayerScript.player == null || body != PlayerScript.player.bodyScript) return;
        BuildAnimatedSoundCatalog();
        ushort soundId;
        if (!animatedSoundIds.TryGetValue(soundName ?? "", out soundId)) return;
        MultiplayerSession.Send(new PlayerSoundPacket(0x20000000u | soundId, body.transform.position.x, body.transform.position.y, 64, 64));
    }
    internal static void PrepareNpcTarget(AIScript ai)
    {
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || LocalPlayerReplication.Instance == null ||
            ai == null || ai.body == null || ai.followPlayer) return;
        var player = PlayerScript.player;
        var localBody = player == null ? null : player.bodyScript;
        if (localBody == null) return;
        var current = ai.targetBody;
        if (current != null && current != localBody && NetworkAvatarManager.ReplicaForBody(current) == null) return;

        BodyScript best = null;
        var bestDistance = float.MaxValue;
        SelectNpcPlayerTarget(ai.body, localBody, ref best, ref bestDistance);
        foreach (var replica in NetworkAvatarManager.replicas.Values)
        {
            var remote = replica == null ? null : replica.remoteBody;
            if (remote == null) continue;
            remote.isPlayer = true;
            remote.team = RemoteTeam(localBody);
            SelectNpcPlayerTarget(ai.body, remote, ref best, ref bestDistance);
        }
        if (best != null) ai.targetBody = best;
    }

    private static void SelectNpcPlayerTarget(BodyScript npc, BodyScript candidate,
        ref BodyScript best, ref float bestDistance)
    {
        if (candidate == null || !candidate.isAlive || !candidate.gameObject.activeInHierarchy ||
            candidate.team == npc.team) return;
        var distance = Vector2.Distance(npc.transform.position, candidate.transform.position);
        if (distance > 40f || distance >= bestDistance) return;
        var from = npc.headTransform == null ? (Vector2)npc.transform.position : (Vector2)npc.headTransform.position;
        var to = candidate.headTransform == null
            ? (Vector2)candidate.transform.position
            : (Vector2)candidate.headTransform.position;
        if (distance >= 3.5f && Physics2D.Linecast(from, to, LayerMask.GetMask("Ground"))) return;
        best = candidate;
        bestDistance = distance;
    }

    internal static string RemoteTeam(BodyScript localBody)
    {
        return MultiplayerSession.PvpEnabled ? PvpRemoteTeam : localBody.team;
    }

    internal static ShotState BeginProjectileExplosion(GameObject projectile)
    {
        var state = new ShotState
        {
            PreviousShooter = currentShooter,
            PreviousShotState = activeShotState,
            IsExplosion = true
        };
        activeShotState = state;
        currentShooter = ProjectileOwner(projectile) ?? replicatedExplosionShooter;
        return state;
    }

    internal static RocketProjectile BeginRocketUpdate(RocketProjectile projectile)
    {
        var previous = activeRocketProjectile;
        activeRocketProjectile = projectile;
        return previous;
    }

    internal static void EndRocketUpdate(RocketProjectile previous)
    {
        activeRocketProjectile = previous;
    }

    internal static GameObject ResolveExplosionProjectile(GameObject projectile)
    {
        return projectile == null && activeRocketProjectile != null
            ? activeRocketProjectile.gameObject : projectile;
    }

    internal static void ReplicateProjectileImpact(GameObject projectile, Vector2 position)
    {
        if (!MultiplayerSession.IsConnected || projectile == null || !NetworkAvatarUtilities.IsFinite(position.x) ||
            !NetworkAvatarUtilities.IsFinite(position.y)) return;
        var rocket = projectile.GetComponentInChildren<RocketProjectile>(true);
        var grenade = projectile.GetComponentInChildren<GrenadeScript>(true);
        if (rocket == null && grenade == null) return;
        var shooter = ProjectileOwner(projectile);
        var player = PlayerScript.player;
        var localPlayerShot = shooter != null && player != null && shooter == player.bodyScript;
        var hostNpcShot = MultiplayerSession.IsHost && shooter != null && !shooter.isPlayer &&
            shooter.GetComponentInParent<NetworkReplica>() == null;
        if (!localPlayerShot && !hostNpcShot) return;
        var weapon = shooter == null ? null : shooter.weapon;
        var trace = CaptureExplosionTrace(position);
        MultiplayerSession.Send(new ProjectileImpactPacket(position.x, position.y,
            NetworkAvatarUtilities.SpriteId(weapon == null || weapon.stats == null ? null : weapon.stats.sprite), true,
            trace.HasBackgroundCrack, trace.BackgroundCrackRotation, trace.BackgroundCrackFlipX,
            trace.BackgroundCrackFlipY, trace.HasFloorCrack, trace.FloorCrackPosition.x,
            trace.FloorCrackPosition.y, trace.FloorCrackFlipX));
    }

    internal static bool ShouldSuppressClientProjectileFires(GameObject projectile)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost || projectile == null ||
            (projectile.GetComponentInChildren<RocketProjectile>(true) == null &&
             projectile.GetComponentInChildren<GrenadeScript>(true) == null)) return false;
        var player = PlayerScript.player;
        return player != null && ProjectileOwner(projectile) == player.bodyScript;
    }

    internal static bool ApplyRemoteProjectileExplosion(ushort senderId, ProjectileImpactPacket packet)
    {
        if (senderId == 0 || !NetworkAvatarUtilities.IsFinite(packet.PositionX) || !NetworkAvatarUtilities.IsFinite(packet.PositionY)) return false;
        var replica = NetworkAvatarManager.GetOrCreateReplica(senderId);
        var shooter = replica == null ? null : replica.remoteBody;
        var preset = WeaponPresetProvider.FindWeaponPreset(packet.WeaponSpriteId);
        var projectile = preset == null ? null : preset.tracerLine;
        var rocket = projectile == null ? null : projectile.GetComponentInChildren<RocketProjectile>(true);
        var grenade = projectile == null ? null : projectile.GetComponentInChildren<GrenadeScript>(true);
        if (shooter == null || (rocket == null && grenade == null)) return false;
        var range = rocket == null ? grenade.range : rocket.range;
        var force = rocket == null ? grenade.force : rocket.force;
        var damage = rocket == null ? grenade.damage : rocket.damage;
        damage *= 2f; // Not vanilla but fun
        var fireAmount = rocket == null ? grenade.fireAmount : rocket.fireAmount;
        var sound = rocket == null ? grenade.explosionSound : rocket.sound;
        var impactEffect = rocket == null ? grenade.objOnDestroy : rocket.objOnDestroy;
        if (!NetworkAvatarUtilities.IsFinite(range) || !NetworkAvatarUtilities.IsFinite(force) || !NetworkAvatarUtilities.IsFinite(damage) || range <= 0f || force <= 0f ||
            damage < 0f) return false;

        var previousShooter = replicatedExplosionShooter;
        var previousExclusionPeerId = replicatedExplosionImpulseExclusionPeerId;
        replicatedExplosionShooter = shooter;
        replicatedExplosionImpulseExclusionPeerId = senderId;
        try
        {
            var position = new Vector2(packet.PositionX, packet.PositionY);
            var existingCracks = packet.HasExplosionTrace ? CaptureExplosionCracks() : null;
            ExplosionHandler.CreateExplosion(null, position, range, force, damage,
                Mathf.Clamp(fireAmount, 0, 64), sound);
            if (existingCracks != null)
            {
                DestroyNewExplosionCracks(existingCracks);
                CreateRemoteExplosionCracks(packet);
            }
            if (impactEffect != null)
            {
                var effect = Instantiate(impactEffect, position, Quaternion.identity);
                BlackoutRule.MakeAlwaysBright(effect);
                Destroy(effect, 60f);
            }
            return true;
        }
        finally
        {
            replicatedExplosionShooter = previousShooter;
            replicatedExplosionImpulseExclusionPeerId = previousExclusionPeerId;
        }
    }

    internal static void ReplicateVelvetWeb(VelvetScript velvet)
    {
        if (!MultiplayerSession.IsConnected || velvet == null) return;
        var body = velvet.GetComponent<BodyScript>();
        var player = PlayerScript.player;
        if (body == null || player == null || body != player.bodyScript || body.headTransform == null) return;
        WebScript web = null;
        var origin = (Vector2)body.headTransform.position - (Vector2)body.headTransform.up * 0.2f;
        foreach (var candidate in FindObjectsOfType<WebScript>())
        {
            if (candidate == null || localVelvetWebs.Contains(candidate) ||
                ((Vector2)candidate.transform.position - origin).sqrMagnitude > 1f) continue;
            web = candidate;
            break;
        }
        if (web == null) return;
        localVelvetWebs.Add(web);
        localVelvetWebs.RemoveWhere(candidate => candidate == null);
        var direction = (Vector2)web.transform.right;
        if (direction.sqrMagnitude < 0.01f) return;
        var normalizedDirection = direction.normalized;
        MultiplayerSession.Send(new VelvetWebPacket(web.transform.position.x, web.transform.position.y,
            normalizedDirection.x, normalizedDirection.y));
    }

    internal static void ReplicateTeleportZone(TeleportZone zone, int activationId)
    {
        if (!MultiplayerSession.IsHost || zone == null || zone.id != activationId || zone.teleportPoint == null)
            return;
        var collider = zone.GetComponent<BoxCollider2D>();
        if (collider == null) return;
        var teleported = new HashSet<BodyScript>();
        foreach (var candidate in Physics2D.OverlapBoxAll(zone.transform.position, collider.size,
            zone.transform.eulerAngles.z))
        {
            BodyScript body;
            if (!candidate.TryGetComponent(out body))
            {
                LimbScript limb;
                if (!candidate.TryGetComponent(out limb) || limb == null) continue;
                body = limb.body;
            }
            if (body == null || !teleported.Add(body)) continue;
        }
        foreach (var replica in NetworkAvatarManager.replicas.Values)
        {
            if (replica == null || replica.remoteBody == null || replica.remotePeerId == 0) continue;
            if (!teleported.Contains(replica.remoteBody) &&
                !IsInsideTeleportZone(replica.remoteBody, zone.transform, collider)) continue;
            if (MultiplayerSession.IsHost)
                MultiplayerSession.Send(new PlayerTeleportPacket(zone.teleportPoint.position.x,
                    zone.teleportPoint.position.y), replica.remotePeerId);
        }
    }

    internal static List<SuppressedTeleportBody> SuppressRemoteTeleportEffects(TeleportZone zone, int activationId)
    {
        var suppressed = new List<SuppressedTeleportBody>();
        if (!MultiplayerSession.IsHost || zone == null || zone.id != activationId) return suppressed;
        var collider = zone.GetComponent<BoxCollider2D>();
        if (collider == null) return suppressed;
        foreach (var replica in NetworkAvatarManager.replicas.Values)
        {
            var body = replica == null ? null : replica.remoteBody;
            if (body == null || !body.isPlayer || !IsInsideTeleportZone(body, zone.transform, collider)) continue;
            suppressed.Add(new SuppressedTeleportBody(body));
            body.isPlayer = false;
        }
        return suppressed;
    }

    internal static void RestoreRemoteTeleportEffects(List<SuppressedTeleportBody> suppressed)
    {
        if (suppressed == null) return;
        foreach (var state in suppressed)
        {
            if (state.Body == null) continue;
            state.Body.transform.position = state.Position;
            state.Body.isPlayer = state.IsPlayer;
        }
    }

    private static bool IsInsideTeleportZone(BodyScript body, Transform zone, BoxCollider2D collider)
    {
        if (IsInsideTeleportZone(body.transform.position, zone, collider)) return true;
        foreach (var limb in body.GetComponentsInChildren<LimbScript>(true))
            if (limb != null && IsInsideTeleportZone(limb.transform.position, zone, collider)) return true;
        return false;
    }

    private static bool IsInsideTeleportZone(Vector3 point, Transform zone, BoxCollider2D collider)
    {
        var localPoint = (Vector2)zone.InverseTransformPoint(point) - collider.offset;
        var halfSize = collider.size * 0.5f;
        return Mathf.Abs(localPoint.x) <= halfSize.x && Mathf.Abs(localPoint.y) <= halfSize.y;
    }

    internal static void ApplyRemoteTeleport(BodyScript body, PlayerTeleportPacket packet)
    {
        if (body == null || MultiplayerSession.IsHost) return;
        var position = new Vector2(packet.PositionX, packet.PositionY);
        if (!NetworkAvatarUtilities.IsFinite(position.x) || !NetworkAvatarUtilities.IsFinite(position.y)) return;
        body.transform.position = position;
        if (CameraFollow.cam != null) CameraFollow.cam.CenterToPlayer();
        if (ScreenFXManager.main != null) ScreenFXManager.main.Teleported();
        foreach (var unloader in FindObjectsOfType<ObjectUnloader>())
            if (unloader != null) unloader.CheckDistance();
        var sound = Resources.Load<AudioClip>("Sounds/Teleport");
        if (sound != null) Sound.Play(sound, position, false, false);
    }

    internal static void RouteVehicleImpact(BodyScript body, float impact, Vector2 position, bool ragdoll)
    {
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || body == null ||
            !NetworkAvatarUtilities.IsFinite(impact) || impact <= 6f) return;
        var replica = NetworkAvatarManager.ReplicaForBody(body);
        if (replica == null || replica.remotePeerId == 0 || !replica.receivedFirstSnapshot ||
            KartPassengers.IsProtectedPassenger(body)) return;
        MultiplayerSession.Send(new VehicleImpactPacket(impact, position.x, position.y, ragdoll),
            replica.remotePeerId);
    }

    internal static void ApplyVehicleImpact(BodyScript body, VehicleImpactPacket packet)
    {
        if (body == null || !body.isAlive || !NetworkAvatarUtilities.IsFinite(packet.Impact) || packet.Impact <= 6f ||
            Time.unscaledTime < localRespawnProtectionUntil || KartPassengers.IsProtectedPassenger(body)) return;
        var impact = Mathf.Min(packet.Impact, 1000f);
        body.shockTime += 3f;

        if (packet.Ragdoll && body.controlState == BodyScript.RagdollState.FullControl)
            body.EnterHalfControl();

        body.health -= impact * 2.5f;
        body.stamina -= impact * 3f;
        body.temporarySlowdown += impact * 0.1f;
        if (GameManager.main != null && NetworkAvatarUtilities.IsFinite(packet.PositionX) && NetworkAvatarUtilities.IsFinite(packet.PositionY))
            GameManager.main.DamageNumber(new Vector2(packet.PositionX, packet.PositionY), impact * 2.5f, body);
    }

    internal static void ApplyVehicleEject(BodyScript body)
    {
        if (body != null && !MultiplayerSession.IsHost && body.inVehicle)
        {
            Vector2 yVelocity = (Vector2)body.curVehicle.mainPart.transform.up * 10f;
            body.ExitVehicle();
            body.lastMoveDir += yVelocity;
            body.EnterHalfControl();
            body.Damaged();
        }

    }

    internal static void HandleTeleportRequest(ushort requesterId, TeleportRequestPacket request)
    {
        if (!MultiplayerSession.IsHost || MultiplayerSession.PvpEnabled || requesterId == 0) return;
        var target = request.TargetPeerId == MultiplayerSession.LocalPeerId
            ? (PlayerScript.player == null ? null : PlayerScript.player.bodyScript)
            : NetworkAvatarManager.RemoteBodyForPeer(request.TargetPeerId);
        if (target == null || !target.isAlive) return;
        var position = target.transform.position;
        if (!NetworkAvatarUtilities.IsFinite(position.x) || !NetworkAvatarUtilities.IsFinite(position.y)) return;
        MultiplayerSession.Send(new PlayerTeleportPacket(position.x, position.y), requesterId);
    }

    private const string PvpRemoteTeam = "gunsaw_mp_remote_player";
    private static BodyScript currentSoundBody;
    internal static readonly List<string> animatedSoundNames = new();
    private static readonly Dictionary<string, ushort> animatedSoundIds = new(StringComparer.Ordinal);
    private static bool animatedSoundCatalogBuilt;
    private static int currentFootstepSurface = -1;
    private static float nextTelekinesisSound;
    private static RocketProjectile activeRocketProjectile;
    private static BodyScript replicatedExplosionShooter;
    internal static ushort replicatedExplosionImpulseExclusionPeerId;
    private static int nextShotSpreadSeed;
    private static readonly HashSet<WebScript> localVelvetWebs = [];
    internal static void IgnoreRemotePlayerPropCollisions(Rigidbody2D prop, NetworkAvatarReplication? onlyReplica = null)
    {
        if (prop == null || !MultiplayerSession.IsHost) return;
        var propColliders = prop.GetComponentsInChildren<Collider2D>(true);
        if (propColliders == null || propColliders.Length == 0) return;
        foreach (var replica in NetworkAvatarManager.replicas.Values)
        {
            if (replica == null || !ReferenceEquals(onlyReplica, null) && !ReferenceEquals(replica, onlyReplica)) continue;
            foreach (var remoteCollider in replica.remoteColliderTriggers.Keys)
            {
                if (remoteCollider == null) continue;
                foreach (var propCollider in propColliders)
                    if (propCollider != null)
                        Physics2D.IgnoreCollision(remoteCollider, propCollider, true);
            }
        }
    }

    internal static void RefreshRemotePropCollisions(NetworkAvatarReplication? onlyReplica = null)
    {
        if (!MultiplayerSession.IsHost) return;
        foreach (var prop in FindObjectsOfType<Rigidbody2D>())
            if (prop != null && (prop.GetComponentInParent<CrateScript>() != null ||
                prop.GetComponentInParent<DroppedWeapon>() != null))
                IgnoreRemotePlayerPropCollisions(prop, onlyReplica);
    }

    internal static void TryGrabRemotePlayer(LevitatorScript levitator)
    {
        if (LocalPlayerReplication.Instance == null || levitator == null || levitator.currentlyLevitating != null ||
            !MultiplayerSession.IsConnected || !MultiplayerSession.CanGrabPlayers) return;
        var camera = Camera.main;
        if (camera == null || levitator.refBody == null) return;
        var mouse = (Vector2)camera.ScreenToWorldPoint(Input.mousePosition);
        var origin = (Vector2)levitator.refBody.transform.position;
        foreach (var hit in Physics2D.LinecastAll(origin, mouse))
        {
            var collider = hit.collider;
            if (collider == null || collider.GetComponentInParent<BodyScript>() == levitator.refBody ||
                collider.gameObject.layer == LayerMask.NameToLayer("Cosmetic")) continue;
            var marker = collider.GetComponentInParent<NetworkReplica>();
            if (marker == null)
            {
                if (!collider.isTrigger) break;
                continue;
            }
            var remote = NetworkAvatarManager.ReplicaForBody(collider.GetComponentInParent<BodyScript>());
            if (remote == null || !CanGrabBody(remote.remoteBody)) continue;
            var rigidbody = hit.rigidbody == null ? collider.attachedRigidbody : hit.rigidbody;
            if (rigidbody == null) return;
            levitator.currentlyLevitating = rigidbody;
            levitator.point = hit.point;
            levitator.localGrabPoint = rigidbody.transform.InverseTransformPoint(hit.point);
            return;
        }
    }

    internal static void ValidateRemoteGrab(LevitatorScript levitator)
    {
        if (levitator == null || levitator.currentlyLevitating == null ||
            levitator.currentlyLevitating.GetComponentInParent<NetworkReplica>() == null) return;
        var body = levitator.currentlyLevitating.GetComponentInParent<BodyScript>();
        if (!MultiplayerSession.CanGrabPlayers || !CanGrabBody(body)) levitator.UnGrab();
    }

    internal static void QueueRemoteGrab(LevitatorScript levitator)
    {
        if (LocalPlayerReplication.Instance == null || !MultiplayerSession.IsConnected || levitator == null) return;
        var target = levitator.currentlyLevitating;
        var targetBody = target == null ? null : target.GetComponentInParent<BodyScript>();
        var replica = NetworkAvatarManager.ReplicaForBody(targetBody);
        byte kind = 0;
        short index = 0;
        var localPoint = levitator.localGrabPoint;
        var hasTarget = false;
        if (target != null && replica != null && CanGrabBody(targetBody))
        {
            var limb = target.GetComponent<LimbScript>();
            if (limb != null && limb.limbType == 1 && targetBody.rb != null)
            {
                kind = 0;
                index = 0;
                localPoint = targetBody.rb.transform.InverseTransformPoint(levitator.point);
                hasTarget = true;
            }
            else hasTarget = replica.TryRemotePart(target, out kind, out index);
        }
        if (hasTarget)
        {
            if (LocalPlayerReplication.Instance.outgoingGrabPeerId != 0 && LocalPlayerReplication.Instance.outgoingGrabPeerId != replica.remotePeerId)
                MultiplayerSession.Send(new PlayerGrabPacket(false), LocalPlayerReplication.Instance.outgoingGrabPeerId);
            MultiplayerSession.Send(new PlayerGrabPacket(true, kind, index, levitator.point.x,
                levitator.point.y, localPoint.x, localPoint.y), replica.remotePeerId);
            LocalPlayerReplication.Instance.outgoingGrabPeerId = replica.remotePeerId;
            return;
        }
        if (LocalPlayerReplication.Instance.outgoingGrabPeerId == 0) return;
        MultiplayerSession.Send(new PlayerGrabPacket(false), LocalPlayerReplication.Instance.outgoingGrabPeerId);
        LocalPlayerReplication.Instance.outgoingGrabPeerId = 0;
    }

    internal static bool CanGrabBody(BodyScript body)
    {
        if (body == null || !MultiplayerSession.CanGrabPlayers) return false;
        if (!MultiplayerSession.GrabOnlyUnconscious) return true;
        var replica = NetworkAvatarManager.ReplicaForBody(body);
        if (replica != null) return replica.remoteCanBeGrabbed;
        return CanGrabOnlyState(body);
    }

    internal static bool CanGrabOnlyState(BodyScript body)
    {
        if (body == null) return false;
        if (!body.isAlive) return true;
        if (body.inVehicle) return false;
        return !body.IsConsc() || !body.CanMove() || body.health < body.dyingStateTreshold;
    }

    internal static bool HandleHostRemoteDamaged(BodyScript body, bool critical)
    {
        var replica = NetworkAvatarManager.ReplicaForBody(body);
        if (!MultiplayerSession.IsConnected || replica == null || !replica.receivedFirstSnapshot) return false;
        var amount = Mathf.Clamp(replica.lastRemoteHealth - body.health, 0f, 1000f);
        if (amount > 0.001f &&
            activeShotState != null &&
            activeShotState.Weapon != null &&
            activeShotState.Weapon.stats != null)
        {
            float baseDamage = amount;

            if (critical)
            {
                float critMultiplier = activeShotState.Weapon.stats.critDamage;

                float divisor = 1f + critMultiplier;

                if (Mathf.Abs(divisor) > 0.0001f)
                    baseDamage = amount / divisor;
            }

            baseDamage = Mathf.Clamp(baseDamage, 0f, 1000f);
            QueueBaseDamage(activeShotState, replica.remotePeerId, baseDamage);
        }

        body.health = replica.lastRemoteHealth;
        body.isAlive = replica.lastRemoteAlive;
        if (amount > 0.001f) RouteRemotePlayerDamage(replica, amount, critical);
        return true;
    }

    internal static bool HandleHostRemoteDeath(BodyScript body)
    {
        var replica = NetworkAvatarManager.ReplicaForBody(body);
        if (!MultiplayerSession.IsConnected || replica == null || !replica.receivedFirstSnapshot) return false;
        body.health = replica.lastRemoteHealth;
        body.isAlive = replica.lastRemoteAlive;
        RouteRemotePlayerDamage(replica, Mathf.Max(1f, replica.lastRemoteHealth + 1f), true);
        return true;
    }

    private static void QueueBaseDamage(
        ShotState state,
        ushort targetPeerId,
        float baseDamage)
    {
        if (state == null || targetPeerId == 0)
            return;

        if (!state.PendingBaseDamage.TryGetValue(targetPeerId, out var queue))
        {
            queue = new Queue<float>();
            state.PendingBaseDamage[targetPeerId] = queue;
        }

        queue.Enqueue(baseDamage);
    }

    internal static float TakeBaseDamage(
        ShotState state,
        ushort targetPeerId)
    {
        if (state == null || targetPeerId == 0)
            return 0f;

        if (!state.PendingBaseDamage.TryGetValue(targetPeerId, out var queue))
            return 0f;

        if (queue.Count == 0)
            return 0f;

        var damage = queue.Dequeue();

        if (queue.Count == 0)
            state.PendingBaseDamage.Remove(targetPeerId);

        return damage;
    }

    private static void RouteRemotePlayerDamage(NetworkAvatarReplication replica, float amount, bool critical)
    {
        if (ShouldCancelExplosionDamage()) return;
        if (replica != null && TeamSystem.Same(MultiplayerSession.LocalPeerId, replica.remotePeerId)) return;
        if (replica != null && KartPassengers.IsProtectedPassenger(replica.remoteBody))
        {
            return;
        }
        if (MultiplayerSession.PvpEnabled && currentShooter == PlayerScript.player?.bodyScript)
            ScoreboardSystem.RecordLocalPvpHit(amount, critical);
        if (MultiplayerSession.IsHost)
        {
            if (currentShooter == null || !currentShooter.isPlayer || MultiplayerSession.PvpEnabled)
                SendRemotePlayerDamage(replica.remotePeerId, amount, critical, currentShooter);
            
            return;
        }
        if (currentShooter == null) return;
        var localPlayer = PlayerScript.player;

        if (localPlayer == null || currentShooter != localPlayer.bodyScript) return;
        MultiplayerSession.Send(new PvpDamagePacket(amount, critical), replica.remotePeerId);
    }

    internal static bool ShouldCancelExplosionDamage() =>
        !MultiplayerSession.IsHost && activeShotState?.IsExplosion == true && PlayerScript.player != null && currentShooter == PlayerScript.player.bodyScript;

    private static void SendRemotePlayerDamage(ushort targetPeerId, float amount, bool critical, BodyScript source)
    {
        if (MultiplayerSession.IsHost)
            MultiplayerSession.Send(PlayerDamagePacket.Damage(amount, critical, source != null && source.isPlayer,
                DamageSourcePeerId(source), DamageSourceName(source), DamageWeapon(source)), targetPeerId);
    }

    private static ushort DamageSourcePeerId(BodyScript source)
    {
        if (source == null || !source.isPlayer) return 0;
        var player = PlayerScript.player;
        return source == player?.bodyScript ? MultiplayerSession.LocalPeerId :
            NetworkAvatarManager.ReplicaForBody(source)?.remotePeerId ?? 0;
    }

    internal static void RecordNetworkPlayerDamageSource(BodyScript victim, PlayerDamagePacket packet)
    {
        if (victim == null) return;
        var source = packet.SourcePeerId == MultiplayerSession.LocalPeerId ? PlayerScript.player?.bodyScript :
            NetworkAvatarManager.RemoteBodyForPeer(packet.SourcePeerId);
        if (source != null) SetDamageSource(victim, source, packet.SourceWeapon);
        else SetDamageSourceName(victim, packet.SourceName, packet.SourceWeapon);
        if (packet.SourcePeerId != 0) lastDamageSourcePeerIds[victim.GetInstanceID()] = packet.SourcePeerId;
    }

    private static string DamageSourceName(BodyScript source)
    {
        if (source == null) return "";
        if (source.isPlayer)
        {
            var player = PlayerScript.player;
            if (player != null && player.bodyScript == source) return MultiplayerSession.LocalPlayerName;
            return NetworkAvatarManager.RemoteNameForBody(source);
        }
        if (!string.IsNullOrWhiteSpace(source.characterName)) return source.characterName.Trim();
        return source.gameObject == null ? "Bot" : source.gameObject.name.Replace("(Clone)", "").Trim();
    }

    private static string DamageWeapon(BodyScript source)
    {
        var weapon = ActiveWeaponName(source);
        return string.IsNullOrEmpty(weapon) ? WeaponName(source == null ? null : source.weapon) : weapon;
    }

    internal static void ApplyPlayerDamage(BodyScript body, PlayerDamagePacket playerDamage)
    {
        if (body == null) return;
        if (KartPassengers.IsProtectedPassenger(body))
        {
            return;
        }

        var amount = Mathf.Clamp(playerDamage.Amount, 0f, 1000f);
        var critical = playerDamage.Critical;
        var effectType = playerDamage.Effect;

        if (CameraFollow.cam != null)
        {
            CameraFollow.cam.AddOffset(new Vector2(
                UnityEngine.Random.Range(-amount, amount) * 0.3f,
                UnityEngine.Random.Range(-amount, amount) * 0.3f
            ));

            CameraFollow.cam.AddRot(UnityEngine.Random.Range(-amount, amount) * 0.2f);
        }

        MusicManager.main.intensity += amount * 0.5f;
        if (Time.unscaledTime < localRespawnProtectionUntil) return;

        if (amount > 0f && body.isAlive)
        {
            var appliedAmount = Mathf.Min(amount, Mathf.Max(0f, body.health));
            body.health -= amount;
            if (body == PlayerScript.player?.bodyScript)
            {
                ScoreboardSystem.RecordLocalDamageReceived(appliedAmount);
                if (MissionManager.main != null)
                    MissionManager.main.damageReceived += appliedAmount;
            }
            applyingNetworkPlayerDamage = true;
            try
            {
                body.Damaged(critical);
                body.DoGrunt();
            }
            catch (Exception e)
            {
                GunsawMultiplayerPlugin.LogInfo(e.Message);
            }
            finally { applyingNetworkPlayerDamage = false; }
        }

        if (effectType == PlayerDamageEffect.Wound) ApplyNetworkWound(body, playerDamage);
    }

    private static void ApplyNetworkWound(BodyScript body, PlayerDamagePacket packet)
    {
        var localPlayer = PlayerScript.player;

        if (localPlayer == null || localPlayer.bodyScript == null || body != localPlayer.bodyScript)
            return;

        var limbIndex = packet.LimbIndex;
        var localPoint = new Vector2(packet.LocalPointX, packet.LocalPointY);
        var direction = new Vector2(packet.DirectionX, packet.DirectionY);
        var weaponSprite = packet.WeaponSprite;
        var woundSprite = packet.WoundSprite;
        var hasSplash = packet.HasSplash;
        var createScreenCrack = packet.CreateScreenCrack;
        var limbs = body.limbs ?? [];
        float baseDamage = Mathf.Clamp(packet.BaseDamage, 0f, 1000f);

        if (limbIndex >= 0 && limbIndex < limbs.Count)
        {
            var limb = limbs[limbIndex];
            var preset = WeaponPresetProvider.FindWeaponPreset(weaponSprite);

            if (limb != null && preset != null)
            {
                float staminaDamage = baseDamage * 1.38f;
                if (limb.isCritical)
                    staminaDamage += baseDamage * preset.critDamage;

                body.stamina -= staminaDamage;

                body.DoGrunt();

                if (limb.passer != null && limb.passer.relevantDismember != null)
                    limb.passer.relevantDismember.currentDamage += baseDamage;

                if (limb.limbType == 1) // arm (not ARM) (shake aim)
                {
                    if (packet.BodyColliderHit)
                    {
                        if (Mathf.Abs(body.currentRecoil) < 250f)
                        {
                            float recoilMult = 1f;
                            if (body.crouchAmount > 0.5f) recoilMult = 0.5f;
                            body.currentRecoil += localPlayer.aimPunchAmount * recoilMult;
                            localPlayer.aimPunchAmount *= -1f;
                        }
                    }
                    else
                    {
                        body.currentRecoil += localPlayer.aimPunchAmount;
                        localPlayer.aimPunchAmount *= -1f;
                    }
                }

                else if (limb.limbType == 2) // leg (reduce jump height)
                    body.temporarySlowdown += baseDamage * 0.065f;

                if (packet.BodyColliderHit && body.crouchAmount < 0.4f)
                {
                    body.crouchAmount += 0.15f;
                }

                GameObject sourceObject = new GameObject("MP Wound Source");
                sourceObject.SetActive(false);
                var sourceWeapon = sourceObject.AddComponent<WeaponScript>();
                sourceWeapon.stats = preset;
                sourceWeapon.body = body;
                var hitPoint = (Vector2)limb.transform.TransformPoint(localPoint);

                if (direction.sqrMagnitude > 0.001f) // hit velocity
                {
                    Vector2 hitDir = direction.normalized;
                    Rigidbody2D hitRb = packet.BodyColliderHit ? body.rb : limb.rb;

                    if (hitRb != null)
                        hitRb.AddForceAtPosition(hitDir * preset.knockback * 1.6f, hitPoint, ForceMode2D.Impulse);

                    if (packet.BodyColliderHit && body.controlState != BodyScript.RagdollState.FullControl && limb.rb != null)
                        limb.rb.AddForceAtPosition(hitDir * preset.knockback * 1.65f, hitPoint, ForceMode2D.Impulse);
                }

                sourceWeapon.DoWound(limb, hitPoint, direction, hasSplash ? preset.bloodSplash : null);
                if (!string.IsNullOrEmpty(woundSprite))
                {
                    var wound = FindLatestWound(limb, hitPoint);
                    var sprite = NetworkAvatarUtilities.FindSprite(woundSprite);
                    if (wound != null && sprite != null) wound.sprite = sprite;
                }

                if (sourceObject != null) Destroy(sourceObject);

                if (createScreenCrack && CameraFollow.cam != null)
                    CameraFollow.cam.CreateScreenCrack();
            }
        }
    }

    internal static SpriteRenderer FindLatestWound(LimbScript limb, Vector2 hitPoint)
    {
        SpriteRenderer best = null;
        var bestDistance = float.MaxValue;
        foreach (var renderer in limb.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer == null || renderer.gameObject.name != "gunshotwound") continue;
            var distance = ((Vector2)renderer.transform.position - hitPoint).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = renderer;
        }
        return best;
    }

    internal static bool BlockNetworkPlayerDrop(BodyScript body, bool allWeapons)
    {
        var player = PlayerScript.player;
        if ((!MultiplayerSession.IsConnected && !MultiplayerSession.IsHosting) || body == null) return false;
        var isLocalPlayer = player != null && body == player.bodyScript;
        var isStartingPlayerBody = body.GetComponentInParent<PlayerScript>() != null;
        if (!isLocalPlayer && !isStartingPlayerBody && !body.isPlayer && !NetworkAvatarManager.IsRemoteAvatarBody(body)) return false;
        if (allWeapons && MultiplayerSession.IsHost && (isLocalPlayer || NetworkAvatarManager.IsRemoteAvatarBody(body))) return false;
        if (isLocalPlayer && !MultiplayerSession.IsHost && body.isAlive && !allWeapons)
        {
            ClearDroppedWeapon(body, false);
            return true;
        }
        if (body.isAlive && !allWeapons) return false;
        ClearDroppedWeapon(body, allWeapons);
        return true;
    }

    internal static void ClearDroppedWeapon(BodyScript body, bool allWeapons)
    {
        if (body.weapons == null || body.weaponAmmos == null) return;
        if (allWeapons)
        {
            for (var index = 0; index < body.weapons.Count; index++)
            {
                body.weapons[index] = null;
                if (index < body.weaponAmmos.Count) body.weaponAmmos[index] = 0;
            }
        }
        else
        {
            var slot = body.currentWeapon;
            if (slot >= 0 && slot < body.weapons.Count) body.weapons[slot] = null;
            if (slot >= 0 && slot < body.weaponAmmos.Count) body.weaponAmmos[slot] = 0;
        }
        if (!body.unarmed) body.ChangeToUnarmed();
    }



    internal static void ApplyPvpDamage(BodyScript body, ushort senderId, PlayerDamagePacket packet)
    {
        if (!MultiplayerSession.PvpEnabled || body == null || TeamSystem.Same(MultiplayerSession.LocalPeerId, senderId)) return;
        var source = senderId == MultiplayerSession.LocalPeerId ? body : NetworkAvatarManager.RemoteBodyForPeer(senderId);
        RecordDamageSource(body, source);
        ApplyPlayerDamage(body, packet);
    }

    internal static void ClearReplicaBloodEffects(BodyScript body)
    {
        if (body == null) return;
        foreach (var transform in body.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null || transform == body.transform) continue;
            var name = transform.name;
            if (name != "gunshotwound" && !name.StartsWith("BloodSplash", StringComparison.Ordinal)) continue;
            Destroy(transform.gameObject);
        }
    }

    internal static void RestoreLocalPlayerSingleton()
    {
        PlayerScript.player = localPlayerInstance;
        PlayerScript.globalBody = localGlobalBody;
    }

    internal static void ApplyInitialLobbyScale(BodyScript body)
    {
        if (!MultiplayerSession.IsActive)
        {
            initialScaleAppliedBody = null;
            initialScaleBase = float.NaN;
            appliedInitialScale = float.NaN;
            return;
        }
        var target = MultiplayerSession.InitialScale;
        if (body == initialScaleAppliedBody && Mathf.Abs(appliedInitialScale - target) < 0.001f) return;
        if (body != initialScaleAppliedBody) initialScaleBase = body.characterScale;
        if (float.IsNaN(initialScaleBase) || initialScaleBase <= 0f) return;
        if (!AvatarScaleHandler.TrySet(body, initialScaleBase * target)) return;
        initialScaleAppliedBody = body;
        appliedInitialScale = target;
    }

    internal static bool IsCreatingRemoteAvatar()
    {
        return remoteAvatarCreationDepth > 0;
    }

    internal static IDisposable BeginRemoteAvatarCreation()
    {
        remoteAvatarCreationDepth++;
        return new RemoteAvatarCreationScope();
    }

    private sealed class RemoteAvatarCreationScope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                RestoreLocalPlayerSingleton();
            }
            finally
            {
                remoteAvatarCreationDepth--;
            }
        }
    }

    private static int remoteAvatarCreationDepth;
    private static BodyScript initialScaleAppliedBody;
    private static float initialScaleBase = float.NaN;
    private static float appliedInitialScale = float.NaN;
    internal static void RecordDamageSource(BodyScript victim)
    {
        if (victim == null) return;
        if (victim.isAlive) announcedDeaths.Remove(victim.GetInstanceID());
        var explosion = activeShotState != null && activeShotState.IsExplosion;
        if (explosion) RecordEnvironmentalDeathCause(victim, PlayerDeathCause.Explosion);
        if (applyingNetworkPlayerDamage || currentShooter == null || currentShooter == victim) return;
        SetDamageSource(victim, currentShooter,
            explosion ? WeaponName(currentShooter.weapon) : ActiveWeaponName(currentShooter));
    }

    internal static void TryCreateLocalKillBloodSplat(BodyScript victim)
    {
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost || victim == null || victim.isRobot ||
            player == null || player.bodyScript == null || currentShooter != player.bodyScript ||
            victim == player.bodyScript || victim.health > 0f || CameraFollow.cam == null ||
            CameraFollow.cam.DistanceFromCam(victim.transform.position) >= 4f) return;
        var id = victim.GetInstanceID();
        float previous;
        if (localKillBloodTimes.TryGetValue(id, out previous) && Time.unscaledTime - previous < 0.5f) return;
        localKillBloodTimes[id] = Time.unscaledTime;
        CameraFollow.cam.CreateBloodSplat(victim.transform.position, victim.bloodColor);
    }

    internal static void RecordDamageSource(BodyScript victim, BodyScript source)
    {
        if (victim == null) return;
        if (victim.isAlive) announcedDeaths.Remove(victim.GetInstanceID());
        if (source != null && source != victim) SetDamageSource(victim, source, WeaponName(source.weapon));
    }

    internal static void RecordGrabSource(BodyScript victim, BodyScript source)
    {
        if (victim == null || source == null || source == victim) return;
        SetDamageSource(victim, source, WeaponName(source.weapon));
        lastGrabSourceTimes[victim.GetInstanceID()] = Time.unscaledTime;
    }

    internal static BodyScript DamageSourceFor(BodyScript victim)
    {
        if (victim == null) return null;
        BodyScript source;
        return lastDamageSources.TryGetValue(victim.GetInstanceID(), out source) ? source : null;
    }

    internal static string DamageWeaponFor(BodyScript victim)
    {
        if (victim == null) return "";
        string weapon;
        return lastDamageWeapons.TryGetValue(victim.GetInstanceID(), out weapon) ? weapon : "";
    }

    internal static string DamageSourceNameFor(BodyScript victim)
    {
        if (victim == null) return "";
        string name;
        return lastDamageSourceNames.TryGetValue(victim.GetInstanceID(), out name) ? name : "";
    }

    internal static ushort DamageSourcePeerIdFor(BodyScript victim)
    {
        if (victim == null) return 0;
        ushort peerId;
        return lastDamageSourcePeerIds.TryGetValue(victim.GetInstanceID(), out peerId) ? peerId : (ushort)0;
    }

    internal static void SetDamageSource(BodyScript victim, BodyScript source, string weaponName)
    {
        var id = victim.GetInstanceID();
        lastDamageSources[id] = source;
        lastDamageSourceNames.Remove(id);
        var player = PlayerScript.player;
        var peerId = source == player?.bodyScript ? MultiplayerSession.LocalPeerId :
            NetworkAvatarManager.ReplicaForBody(source)?.remotePeerId ?? 0;
        if (peerId == 0) lastDamageSourcePeerIds.Remove(id);
        else lastDamageSourcePeerIds[id] = peerId;
        lastDamageSourceTimes[id] = Time.unscaledTime;
        if (string.IsNullOrEmpty(weaponName)) lastDamageWeapons.Remove(id);
        else lastDamageWeapons[id] = weaponName;
    }

    internal static void SetDamageSourceName(BodyScript victim, string sourceName, string weaponName)
    {
        if (victim == null) return;
        var id = victim.GetInstanceID();
        lastDamageSources.Remove(id);
        lastDamageSourcePeerIds.Remove(id);
        if (string.IsNullOrWhiteSpace(sourceName)) lastDamageSourceNames.Remove(id);
        else lastDamageSourceNames[id] = sourceName.Trim();
        lastDamageSourceTimes[id] = Time.unscaledTime;
        if (string.IsNullOrEmpty(weaponName)) lastDamageWeapons.Remove(id);
        else lastDamageWeapons[id] = weaponName;
    }

    private static void ClearDamageSource(BodyScript victim)
    {
        if (victim == null) return;
        var id = victim.GetInstanceID();
        lastDamageSources.Remove(id);
        lastDamageSourceNames.Remove(id);
        lastDamageSourcePeerIds.Remove(id);
        lastDamageWeapons.Remove(id);
        lastDamageSourceTimes.Remove(id);
        lastGrabSourceTimes.Remove(id);
    }

    internal static string ActiveWeaponName(BodyScript source)
    {
        return activeShotState != null && activeShotState.Weapon != null &&
            activeShotState.Weapon.body == source ? WeaponName(activeShotState.Weapon) : "";
    }

    internal static string WeaponName(WeaponScript weapon)
    {
        return weapon == null || weapon.stats == null || string.IsNullOrWhiteSpace(weapon.stats.name)
            ? "" : weapon.stats.name.Replace("(Clone)", "").Trim();
    }

    internal static void CaptureDeathCause(BodyScript body)
    {
        if (body == null) return;
        var id = body.GetInstanceID();
        float damageTime;
        float grabTime;
        var hasGrabSource = lastGrabSourceTimes.TryGetValue(id, out grabTime) && Time.unscaledTime - grabTime <= 5f;
        if (!lastDamageSourceTimes.TryGetValue(id, out damageTime) ||
            (Time.unscaledTime - damageTime > 0.25f && !hasGrabSource))
        {
            lastDamageSources.Remove(id);
            lastDamageSourceNames.Remove(id);
            lastDamageSourcePeerIds.Remove(id);
            lastDamageWeapons.Remove(id);
            lastDamageSourceTimes.Remove(id);
        }

        var hasRecentEnvironmentalCause =
            environmentalDeathCauses.TryGetValue(id, out var cause) &&
            environmentalDeathCauseTimes.TryGetValue(id, out var environmentalTime) &&
            Time.unscaledTime - environmentalTime <= 0.5f;

        if (!hasRecentEnvironmentalCause)
        {
            environmentalDeathCauses.Remove(id);
            environmentalDeathCauseTimes.Remove(id);
            cause = PlayerDeathCause.Unknown;
            if (body.burnIntensity > 0.01f)
                cause = PlayerDeathCause.Fire;
            else if (body.oxygen <= 0.01f && body.headInWater)
                cause = PlayerDeathCause.Drowning;
            else if (body.oxygen <= 0.01f && body.forcedOxyLoss > 0)
                cause = PlayerDeathCause.Suffocation;
            else if (body.fallDamageCooldown > 0f)
                cause = PlayerDeathCause.Fall;
        }

        if (hasGrabSource && cause == PlayerDeathCause.Fall)
            cause = PlayerDeathCause.Telekinesis;

        deathCauses[id] = cause;
    }

    internal static void RecordSawDamage(SawScript saw, Collision2D collision)
    {
        if (saw == null || collision == null) return;
        var limb = collision.gameObject.GetComponent<LimbScript>();
        var body = limb == null ? collision.gameObject.GetComponent<BodyScript>() : limb.body;
        RecordEnvironmentalDeathCause(body, IsHotPlate(saw) ? PlayerDeathCause.HotPlate : PlayerDeathCause.Saw);
    }

    private static bool IsHotPlate(SawScript saw)
    {
        for (var current = saw == null ? null : saw.transform; current != null; current = current.parent)
        {
            var name = current.name;
            if (name.IndexOf("hotplate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (name.IndexOf("hot", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 name.IndexOf("plate", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
        }
        return false;
    }

    internal static void RecordAcidDamage(WaterScript water, Collider2D collision)
    {
        if (water == null || water.damagePerSecond <= 0f || collision == null) return;
        var limb = collision.GetComponent<LimbScript>();
        RecordEnvironmentalDeathCause(limb == null ? null : limb.body, PlayerDeathCause.Acid);
    }

    internal static void RecordIncineratorDamage(Incinerator incinerator, Collider2D collision)
    {
        if (incinerator == null || collision == null) return;
        var body = collision.GetComponent<BodyScript>();
        if (body == null)
        {
            var limb = collision.GetComponent<LimbScript>();
            body = limb == null ? null : limb.body;
        }
        RecordEnvironmentalDeathCause(body, PlayerDeathCause.Incinerator);
    }



    public static void RecordEnvironmentalDeathCause(BodyScript body, PlayerDeathCause cause)
    {
        if (body == null) return;
        var id = body.GetInstanceID();
        environmentalDeathCauses[id] = cause;
        environmentalDeathCauseTimes[id] = Time.unscaledTime;
        lastDamageSources.Remove(id);
        lastDamageSourceNames.Remove(id);
        lastDamageWeapons.Remove(id);
        lastDamageSourceTimes.Remove(id);
    }

    internal static PlayerDeathCause DeathCauseFor(BodyScript body)
    {
        if (body == null) return PlayerDeathCause.Unknown;
        PlayerDeathCause cause;
        return deathCauses.TryGetValue(body.GetInstanceID(), out cause) ? cause : PlayerDeathCause.Unknown;
    }

    internal static void RouteNpcKillScreenEffect(BodyScript victim)
    {
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.IsHost || victim == null ||
            victim.isPlayer || !victim.isAlive) return;
        var killer = DamageSourceFor(victim);
        var replica = NetworkAvatarManager.ReplicaForBody(killer);
        if (replica == null || replica.remotePeerId == 0) return;

        MultiplayerSession.Send(new KillScreenEffectPacket(), replica.remotePeerId);
        suppressNpcKillEffectFor = victim;
    }

    internal static void RoutePlayerKillScreenEffect(ushort killerPeerId)
    {
        if (!MultiplayerSession.IsHosting || killerPeerId == 0) return;
        if (killerPeerId == MultiplayerSession.LocalPeerId)
        {
            PlayKillScreenEffect();
            return;
        }
        MultiplayerSession.Send(new KillScreenEffectPacket(), killerPeerId);
    }

    internal static void PlayKillScreenEffect()
    {
        if (ScreenFXManager.main != null) ScreenFXManager.main.OnKill(true);
    }

    internal static bool AllowNpcKillScreenEffect()
    {
        if (suppressNpcKillEffectFor == null) return true;
        suppressNpcKillEffectFor = null;
        return false;
    }

    internal static void EndNpcKillScreenEffect(BodyScript victim)
    {
        if (suppressNpcKillEffectFor == victim) suppressNpcKillEffectFor = null;
    }

    internal static bool BeginDeathAnnouncement(BodyScript victim)
    {
        if (victim == null) return false;
        var id = victim.GetInstanceID();
        if (victim.isAlive)
        {
            announcedDeaths.Remove(id);
            deathCauses.Remove(id);
            lastDamageSources.Remove(id);
            lastDamageSourceNames.Remove(id);
            lastDamageWeapons.Remove(id);
            lastDamageSourceTimes.Remove(id);
            environmentalDeathCauses.Remove(id);
            environmentalDeathCauseTimes.Remove(id);
            return false;
        }
        return announcedDeaths.Add(id);
    }

    internal static string RemoteNameTag(BodyScript body)
    {
        var replica = NetworkAvatarManager.ReplicaForBody(body);
        if (replica == null) return "Player";
        var ping = MultiplayerSession.PeerPing(replica.remotePeerId);
        var label = replica.remoteName + " [" + (ping < 0 ? "-" : ping.ToString()) + "]";
        if (!body.isAlive) return "DEAD " + label;
        if (!body.IsConsc()) return "K.O. " + label;
        return label;
    }

    internal static bool SuppressLocalShotScreenCrack()
    {
        var player = PlayerScript.player;
        return suppressedTargetScreenEffects > 0 ||
            Time.unscaledTime < suppressedCameraUntil ||
            (MultiplayerSession.IsConnected && MultiplayerSession.PvpEnabled &&
            player != null && player.bodyScript != null && currentShooter == player.bodyScript);
    }

    internal static bool SuppressTargetedScreenEffect()
    {
        return suppressedTargetScreenEffects > 0 || Time.unscaledTime < suppressedCameraUntil;
    }

    internal static TargetScreenEffectState BeginTargetScreenEffect(BodyScript target)
    {
        var state = new TargetScreenEffectState();
        var localPlayer = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || target == null || localPlayer == null ||
            localPlayer.bodyScript == null || target == localPlayer.bodyScript)
            return state;
        state.Suppress = true;
        suppressedTargetScreenEffects++;
        if (CameraFollow.cam != null) state.ScreenShake = CameraFollow.cam.screenShakeAmount;
        return state;
    }

    internal static void EndTargetScreenEffect(TargetScreenEffectState state)
    {
        if (state == null || !state.Suppress) return;
        if (CameraFollow.cam != null) CameraFollow.cam.screenShakeAmount = state.ScreenShake;
        suppressedCameraUntil = Time.unscaledTime + 0.35f;
        if (suppressedTargetScreenEffects > 0) suppressedTargetScreenEffects--;
    }

    internal static void ClearSuppressedCameraShake(CameraFollow camera)
    {
        if (camera == null || (suppressedTargetScreenEffects <= 0 &&
            Time.unscaledTime >= suppressedCameraUntil)) return;
        camera.screenShakeAmount = 0f;
    }

    private static readonly Dictionary<int, BodyScript> lastDamageSources = new();
    private static readonly Dictionary<int, string> lastDamageSourceNames = new();
    internal static readonly Dictionary<int, ushort> lastDamageSourcePeerIds = new();
    private static readonly Dictionary<int, string> lastDamageWeapons = new();
    private static readonly Dictionary<int, float> lastDamageSourceTimes = new();
    private static readonly Dictionary<int, float> lastGrabSourceTimes = new();
    private static readonly Dictionary<int, PlayerDeathCause> environmentalDeathCauses = new();
    private static readonly Dictionary<int, float> environmentalDeathCauseTimes = new();
    private static readonly Dictionary<int, PlayerDeathCause> deathCauses = new();
    private static readonly Dictionary<int, float> localKillBloodTimes = new();
    private static readonly HashSet<int> announcedDeaths = [];
    internal static readonly HashSet<string> exhaustedLivesLobbies = [];
    private static BodyScript suppressNpcKillEffectFor;
    internal static BodyScript currentShooter;
    internal static ShotState activeShotState;
    internal static bool applyingNetworkPlayerDamage;
    private static int suppressedTargetScreenEffects;
    private static float suppressedCameraUntil = -1f;
    internal static IEnumerable<string> SwapCharacterNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in knownCharacterPrefabs)
        {
            string displayName;
            if (characterDisplayNames.TryGetValue(path, out displayName) && !string.IsNullOrWhiteSpace(displayName))
                names.Add(displayName);
            else names.Add(path.Substring(path.LastIndexOf('/') + 1));
        }
        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
    }

    internal static bool TryResolveCharacterPrefab(string character, out string prefabPath, out string characterName)
    {
        prefabPath = "";
        characterName = "";
        var requested = (character ?? "").Trim();
        if (string.IsNullOrEmpty(requested)) return false;
        foreach (var path in knownCharacterPrefabs)
        {
            var prefab = Resources.Load<GameObject>(path);
            var body = prefab == null ? null : prefab.GetComponentInChildren<BodyScript>(true);
            if (body == null) continue;
            var prefabName = NetworkAvatarUtilities.CleanCloneName(prefab.name);
            string displayName;
            characterDisplayNames.TryGetValue(path, out displayName);
            if (!string.Equals(requested, body.characterName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(requested, prefabName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(requested, displayName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(requested, path.Substring(path.LastIndexOf('/') + 1), StringComparison.OrdinalIgnoreCase))
                continue;
            prefabPath = path;
            characterName = string.IsNullOrWhiteSpace(displayName)
                ? (string.IsNullOrWhiteSpace(body.characterName) ? prefabName : body.characterName) : displayName;
            return true;
        }
        return false;
    }

    internal static bool TryBroadcastSwapRequest(ushort senderId, string message)
    {
        if (!MultiplayerSession.IsHost || string.IsNullOrWhiteSpace(message)) return false;
        const string prefix = "/swap";
        if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            (message.Length > prefix.Length && !char.IsWhiteSpace(message[prefix.Length]))) return false;
        var requested = message.Length == prefix.Length ? "" : message.Substring(prefix.Length).Trim();
        if (!MultiplayerSession.AllowSwap) return true;
        if (!TryResolveCharacterPrefab(requested, out _, out var characterName)) return true;
        var playerName = senderId == MultiplayerSession.LocalPeerId
            ? MultiplayerSession.LocalPlayerName : MultiplayerSession.PlayerName(senderId);
        BroadcastSwapAnnouncement(playerName, characterName);
        return true;
    }

    internal static void BroadcastSwapAnnouncement(string playerName, string characterName)
    {
        var message = playerName + " will respawn as " + characterName + ".";
        MultiplayerHud.AddSystemMessage(message);
        ChatPacket packet;
        if (ChatService.TryCreate(message, true, out packet)) MultiplayerSession.Send(packet);
    }

    internal static void EjectRemoteVehicleOccupants(VehicleBase vehicle)
    {
        if (!MultiplayerSession.IsHost || vehicle == null) return;
        foreach (var replica in NetworkAvatarManager.replicas.Values)
        {
            if (replica == null || replica.remotePeerId == 0 || replica.remoteBody == null ||
                !replica.remoteBody.inVehicle || replica.remoteBody.curVehicle != vehicle) continue;
            var body = replica.remoteBody;
            if (vehicle.occupant != body)
                body.ExitVehicle();
            MultiplayerSession.Send(new VehicleEjectPacket(), replica.remotePeerId);
        }
    }

    internal static void ForceRefreshRemotePhysics()
    {
        foreach (var replica in NetworkAvatarManager.replicas.Values)
        {
            if (replica == null) continue;
            replica.remotePhysicsModeKnown = false;
            replica.UpdateRemotePhysicsMode(false);
        }
        RefreshRemotePropCollisions();
    }

    internal static void EnsurePlayerSingletonForUpdate()
    {
        LocalPlayerReplication.EnsureLocalPlayerSingleton();
    }

    internal static void CaptureCharacterMenu(MainMenuManager menu)
    {
        if (menu == null) return;
        var characters = menu.characters;
        if (characters == null) return;

        for (var index = 0; index < characters.Count; index++)
        {
            var path = characters[index] == null ? null : characters[index].prefabPath;
            if (!string.IsNullOrEmpty(path) && !knownCharacterPrefabs.Contains(path))
                knownCharacterPrefabs.Add(path);
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrWhiteSpace(characters[index].name))
                characterDisplayNames[path] = characters[index].name.Trim();
        }

        if (!knownCharacterPrefabs.Contains(ProtogenPrefabPath)) knownCharacterPrefabs.Add(ProtogenPrefabPath);
        characterDisplayNames[ProtogenPrefabPath] = "G4-A";
        if (!knownCharacterPrefabs.Contains(AlbinoPrefabPath)) knownCharacterPrefabs.Add(AlbinoPrefabPath);
        characterDisplayNames[AlbinoPrefabPath] = "Albino";

        var selectedIndex = menu.charIndex;
        if (selectedIndex >= 0 && selectedIndex < characters.Count)
        {
            var path = characters[selectedIndex] == null ? null : characters[selectedIndex].prefabPath;
            if (!string.IsNullOrEmpty(path)) selectedCharacterPrefab = path;
        }
    }

    internal static void RestoreCharacterSelection()
    {
        if (!string.IsNullOrEmpty(selectedCharacterPrefab))
            PlayerPrefs.SetString("charPrefab", selectedCharacterPrefab);
    }

    internal const string ProtogenPrefabPath = "Enemies/RobotEnemy";
    private const string AlbinoPrefabPath = "Enemies/AlbinoEnemy";
    internal static readonly List<string> knownCharacterPrefabs = [];
    private static readonly Dictionary<string, string> characterDisplayNames = new();
    private string identitySent = "";
    private float nextIdentity;

    private void Update()
    {
        var performanceStarted = MultiplayerPerformance.Start();
        try
        {
            if (!MultiplayerSession.IsHosting && !MultiplayerSession.IsConnected)
            {
                NetworkAvatarManager.DestroyAllReplicas();
                return;
            }

            NetworkAvatarManager.CleanupDisconnectedReplicas();
            var local = LocalPlayerReplication.Instance;
            if (local == null) return;
            if (!MultiplayerSession.IsConnected)
            {
                if (MultiplayerSession.IsHosting && PlayerScript.player != null)
                {
                    LobbyHealthRule.Apply(PlayerScript.player.bodyScript, MultiplayerSession.HealthFactor);
                    LobbyRegenRule.Apply(PlayerScript.player.bodyScript, MultiplayerSession.RegenFactor);
                }

                return;
            }

            LocalPlayerReplication.EnsureLocalPlayerSingleton();
            MultiplayerSession.UpdatePing();
            var player = PlayerScript.player;
            if (player == null || player.bodyScript == null)
                return;
            localPlayerInstance = player;
            localGlobalBody = PlayerScript.globalBody == null
                ? player.bodyScript.transform
                : PlayerScript.globalBody;
            ApplyInitialLobbyScale(player.bodyScript);
            LobbyHealthRule.Apply(player.bodyScript, MultiplayerSession.HealthFactor);
            LobbyRegenRule.Apply(player.bodyScript, MultiplayerSession.RegenFactor);
            local.ApplyPendingRespawnLobbyLoadout(player.bodyScript);
            local.ApplyStartingLobbyLoadout(player.bodyScript);
            local.ApplyStartingLobbyAmmo(player.bodyScript);
            local.UpdateLocalRespawn(player);
            player = PlayerScript.player;
            if (player == null || player.bodyScript == null)
                return;

            ushort senderId;
            PlayerTeleportPacket playerTeleport;
            while (MultiplayerSession.TryTakePlayerTeleport(out senderId, out playerTeleport))
                ApplyRemoteTeleport(player.bodyScript, playerTeleport);

            VehicleEjectPacket vehicleEject;
            while (MultiplayerSession.TryTakeVehicleEject(out senderId, out vehicleEject))
                ApplyVehicleEject(player.bodyScript);

            VehicleImpactPacket vehicleImpact;
            while (MultiplayerSession.TryTakeVehicleImpact(out senderId, out vehicleImpact))
                ApplyVehicleImpact(player.bodyScript, vehicleImpact);

            TeleportRequestPacket teleportRequest;
            while (MultiplayerSession.TryTakeTeleportRequest(out senderId, out teleportRequest))
                HandleTeleportRequest(senderId, teleportRequest);

            PlayerDamagePacket playerDamage;
            while (MultiplayerSession.TryTakePlayerDamage(out senderId, out playerDamage))
            {
                if (playerDamage.HasPlayerSource)
                    RecordNetworkPlayerDamageSource(player.bodyScript, playerDamage);
                else
                    SetDamageSourceName(player.bodyScript, playerDamage.SourceName, playerDamage.SourceWeapon);
                ApplyPlayerDamage(player.bodyScript, playerDamage);
            }

            PlayerDamagePacket pvpDamage;
            while (MultiplayerSession.TryTakePvpDamage(out senderId, out pvpDamage))
                ApplyPvpDamage(player.bodyScript, senderId, pvpDamage);
            ShotVisualPacket shotVisual;
            while (MultiplayerSession.TryTakeShotVisual(out senderId, out shotVisual))
            {
                if (MultiplayerSession.IsHost && !shotVisual.IsNpcShot)
                    NpcReplication.AlertForRemoteShot(senderId, new Vector2(shotVisual.OriginX, shotVisual.OriginY));
                var shooter = NetworkAvatarManager.GetOrCreateReplica(senderId);
                if (shooter != null)
                    shooter.PlayRemoteShot(shotVisual);
            }

            ReloadEffectPacket reloadEffect;
            while (MultiplayerSession.TryTakeReloadEffect(out senderId, out reloadEffect))
            {
                var reloader = NetworkAvatarManager.GetOrCreateReplica(senderId);
                if (reloader != null)
                    reloader.PlayRemoteReloadEffect(reloadEffect);
            }

            PlayerSoundPacket playerSound;
            while (MultiplayerSession.TryTakePlayerSound(out senderId, out playerSound))
            {
                var source = NetworkAvatarManager.GetOrCreateReplica(senderId);
                if (source != null)
                    source.PlayRemotePlayerSound(playerSound);
            }

            ProjectileImpactPacket projectileImpact;
            while (MultiplayerSession.TryTakeProjectileImpact(out senderId, out projectileImpact))
            {
                if (MultiplayerSession.IsHost && ApplyRemoteProjectileExplosion(senderId, projectileImpact))
                    continue;
                var shooter = NetworkAvatarManager.GetOrCreateReplica(senderId);
                if (shooter != null)
                    shooter.PlayRemoteProjectileImpact(projectileImpact);
            }

            VelvetWebPacket velvetWeb;
            while (MultiplayerSession.TryTakeVelvetWeb(out senderId, out velvetWeb))
            {
                var shooter = NetworkAvatarManager.GetOrCreateReplica(senderId);
                if (shooter != null)
                    shooter.PlayRemoteVelvetWeb(velvetWeb);
            }

            PlayerGrabPacket playerGrab;
            while (MultiplayerSession.TryTakePlayerGrab(out senderId, out playerGrab))
                local.ReceivePlayerGrab(senderId, playerGrab);
            local.UpdateLocalRespawn(player);
            player = PlayerScript.player;
            if (player == null)
                return;
            if (player.bodyScript == null)
                return;
            local.UpdateSpectator(player);

            var serverOnlyHost = GunsawMultiplayerPlugin.IsHeadlessServer;
            var prefab = local.ResolveLocalCharacterPrefab(player.bodyScript);
            var currentIdentity = local.localName + "\n" + prefab;
            var identityChanged = identitySent != currentIdentity;
            if (!serverOnlyHost && (identityChanged || Time.unscaledTime >= nextIdentity))
            {
                identitySent = currentIdentity;
                if (identityChanged)
                    local.forceFullInventoryUntil = Time.unscaledTime + 0.5f;
                nextIdentity = Time.unscaledTime + 2f;
                MultiplayerSession.Send(new IdentityPacket(local.localName, prefab));
            }

            string identity;
            while (MultiplayerSession.TryTakeIdentity(out senderId, out identity))
            {
                if (MultiplayerSession.IsHost)
                    WorldReplication.Instance?.SendFullEnvironment(senderId);
                var replica = NetworkAvatarManager.GetOrCreateReplica(senderId);
                if (replica != null)
                    replica.CreateRemote(identity, player.bodyScript);
            }

            if (!serverOnlyHost && Time.unscaledTime >= local.nextSnapshot)
            {
                local.nextSnapshot = Time.unscaledTime + SnapshotInterval;
                MultiplayerSession.Send(local.Serialize(PacketSequences.NextPlayerSnapshot(), player.bodyScript));
                if (local.pendingStatePacket.HasValue)
                    MultiplayerSession.Send(local.pendingStatePacket.Value);
                if (local.pendingSpecialLinesPacket.HasValue)
                    MultiplayerSession.Send(local.pendingSpecialLinesPacket.Value);
            }

            PlayerSnapshotPacket snapshot;
            while (MultiplayerSession.TryTakeSnapshot(out senderId, out snapshot))
            {
                NetworkAvatarReplication replica;
                if (NetworkAvatarManager.replicas.TryGetValue(senderId, out replica) && replica != null)
                    replica.Apply(snapshot);
            }

            PlayerStatePacket state;
            while (MultiplayerSession.TryTakePlayerState(out senderId, out state))
            {
                NetworkAvatarReplication replica;
                if (NetworkAvatarManager.replicas.TryGetValue(senderId, out replica) && replica != null)
                    replica.Apply(state);
            }

            PlayerSpecialLinesPacket specialLines;
            while (MultiplayerSession.TryTakePlayerSpecialLines(out senderId, out specialLines))
            {
                NetworkAvatarReplication replica;
                if (NetworkAvatarManager.replicas.TryGetValue(senderId, out replica) && replica != null)
                    replica.Apply(specialLines);
            }
        }
        finally
        {
            MultiplayerPerformance.AddAvatar(performanceStarted);
        }
    }
}
