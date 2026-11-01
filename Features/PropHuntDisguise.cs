using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;

[DefaultExecutionOrder(1000)]
internal sealed class PropHuntDisguise : MonoBehaviour
{
    private static readonly HashSet<PropHuntDisguise> active = new();
    private readonly Dictionary<Renderer, bool> hidden = new();
    private readonly Dictionary<Light2D, bool> hiddenLights = new();
    private readonly Dictionary<Collider2D, bool> hiddenColliders = new();
    private BodyScript body;
    private GameObject visual;
    private string prop;
    private float refreshAt;
    private Collider2D[] hitboxes = Array.Empty<Collider2D>();
    private readonly RaycastHit2D[] movementHits = new RaycastHit2D[32];
    private Rigidbody2D physics;
    private RigidbodyType2D playerBodyType;
    private float lastInputAt, playerFeetOffset, gravityUntil;
    private bool playerSimulated;
    internal bool Physical { get; private set; }
    internal PlayerSnapshotPose? SnapshotPose => visual == null ? null : new PlayerSnapshotPose(visual.transform.position.x, visual.transform.position.y, visual.transform.eulerAngles.z);
    internal ushort Peer;

    internal void Apply(BodyScript target, ushort peer, string name, GameObject template)
    {
        active.Add(this);
        body = target;
        
        if (Peer != peer) 
            refreshAt = 0f;
        
        Peer = peer;
        
        if (visual != null && prop == name) 
            return;
        
        SetPhysical(false);
        
        if (visual != null) 
            Destroy(visual);
        
        prop = name;
        visual = Instantiate(template);
        visual.name = "Prop Hunt disguise";
        physics = visual.GetComponent<Rigidbody2D>();
        lastInputAt = Time.unscaledTime;
       
        foreach (var hitbox in visual.GetComponentsInChildren<PropHuntHitbox>(true)) 
            hitbox.Peer = peer;
        
        visual.SetActive(true);
        hitboxes = visual.GetComponentsInChildren<Collider2D>(true);
        visual.transform.position = body.transform.position + Vector3.up * FeetOffset(body);
        
        if (MultiplayerSession.BlackoutEnabled) 
            BlackoutRule.ApplyToObject(visual);
       
        refreshAt = 0f;
        LateUpdate();
    }

    private void LateUpdate()
    {
        if (body == null || visual == null) 
            return;
       
        if (!PropHuntSystem.Enabled)
        {
            Destroy(this);
            return;
        }
        
        if (Time.unscaledTime >= refreshAt)
        {
            refreshAt = Time.unscaledTime + 0.5f;
            Hide(body.GetComponentsInChildren<Renderer>(true));
            HideLights(body.transform);
            HideRemoteColliders(body.transform);
            UpdateCollisions();
            foreach (var tail in body.tails ?? Array.Empty<Transform>())
                if (tail != null)
                {
                    Hide(tail.GetComponentsInChildren<Renderer>(true));
                    HideLights(tail);
                    HideRemoteColliders(tail);
                }
        }
        foreach (var renderer in hidden.Keys.OfType<Renderer>())
            renderer.forceRenderingOff = true;
       
        foreach (var light in hiddenLights.Keys.OfType<Light2D>()) 
            light.enabled = false;
        
        foreach (var collider in hiddenColliders.Keys.OfType<Collider2D>()) 
            collider.enabled = false;
        
        var offset = Peer == MultiplayerSession.LocalPeerId ? FeetOffset(body) : NetworkAvatarManager.ReplicaForBody(body)?.FeetOffsetY ?? float.NaN;
        if (float.IsNaN(offset)) 
            offset = FeetOffset(body);
        
        if (!body.isAlive)
        {
            SetPhysical(false);
            visual.SetActive(false);
            return;
        }
        
        visual.SetActive(true);
        UpdatePlayerCollisions();
        
        if (Peer == MultiplayerSession.LocalPeerId)
        {
            PrepareInput();
            
            if (body.isAlive && Time.unscaledTime - lastInputAt >= 0.5f) 
                SetPhysical(true);
            
            if (Physical) 
                FollowPhysics();
            else
                MoveVisual(body.transform.position + Vector3.up * offset, 0f);
        }
        else
        {
            SetLayer(LayerMask.NameToLayer("Ground"));
            var pose = NetworkAvatarManager.ReplicaForBody(body)?.DisguisePose;
            MoveVisual(pose.HasValue ? new Vector3(pose.Value.X, pose.Value.Y, body.transform.position.z) : body.transform.position + Vector3.up * offset, pose?.Rotation ?? 0f);
        }
        visual.transform.localScale = Vector3.one;
        visual.SetActive(body.isAlive);
    }

    private void FixedUpdate()
    {
        if (PropHuntSystem.Enabled) UpdatePlayerCollisions();
    }

    private void UpdatePlayerCollisions()
    {
        if (visual == null || !visual.activeInHierarchy) 
            return;
        
        SetCollisions(body, Peer);
        foreach (var other in active)
        {
            if (other == null || other == this || other.visual == null || !other.visual.activeInHierarchy) continue;
            SetCollisions(other.body, other.Peer);
            foreach (var collider in hitboxes)
                foreach (var otherCollider in other.hitboxes)
                    if (collider != null && otherCollider != null)
                        Physics2D.IgnoreCollision(collider, otherCollider, (!Physical && !other.Physical) ||
                            (Physics2D.GetIgnoreCollision(collider, otherCollider) && Physics2D.Distance(collider, otherCollider).isOverlapped));
        }
    }

    private void MoveVisual(Vector3 position, float rotation)
    {
        visual.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
        Physics2D.SyncTransforms();
        var hunter = PropHuntSystem.Hunter == MultiplayerSession.LocalPeerId ? PlayerScript.player?.bodyScript : NetworkAvatarManager.RemoteBodyForPeer(PropHuntSystem.Hunter);
        
        if (Peer != MultiplayerSession.LocalPeerId)
        {
            physics.position = position;
            visual.transform.position = position;
            Physics2D.SyncTransforms();
            if (hunter == PlayerScript.player?.bodyScript && hunter?.coll != null)
            {
                var push = hunter.CurrentState == BodyScript.EntityState.MoveRight ? 1f :
                    hunter.CurrentState == BodyScript.EntityState.MoveLeft ? -1f : 0f;
                foreach (var hitbox in hitboxes)
                {
                    var separation = Physics2D.Distance(hitbox, hunter.coll);
                    if (separation.distance < -0.1f) Physics2D.IgnoreCollision(hitbox, hunter.coll, true);
                    if (separation.distance > 0.06f || separation.normal.x * push >= -0.5f) continue;
                    PropHuntSystem.Push(Peer, push);
                    break;
                }
            }
            return;
        }
        var start = physics.position;
        var delta = (Vector2)position - start;
        var distance = delta.magnitude;
        var direction = distance > 0.0001f ? delta / distance : Vector2.zero;
        if (hunter != null && distance > 0.0001f)
            foreach (var hitbox in hitboxes)
            {
                var count = hitbox.Cast(direction, movementHits, distance);
                for (var index = 0; index < count; index++)
                {
                    var hit = movementHits[index];
                    if (hit.collider.isTrigger || hit.collider.GetComponentInParent<BodyScript>() != hunter || Vector2.Dot(hit.normal, direction) >= -0.01f) continue;
                    distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.005f));
                }
            }
        physics.position = start + direction * distance;
        Physics2D.SyncTransforms();
        if (hunter != null)
        {
            var colliders = hunter.GetComponentsInChildren<Collider2D>();
            for (var pass = 0; pass < 3; pass++)
                foreach (var hitbox in hitboxes)
                    foreach (var collider in colliders)
                    {
                        if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody == null || !collider.attachedRigidbody.simulated ||
                            Physics2D.GetIgnoreLayerCollision(hitbox.gameObject.layer, collider.gameObject.layer) || Physics2D.GetIgnoreCollision(hitbox, collider)) continue;
                        var separation = Physics2D.Distance(hitbox, collider);
                        if (!separation.isOverlapped) continue;
                        if (separation.normal.y > 0.5f && collider.bounds.center.y > hitbox.bounds.max.y) continue;
                        physics.position += separation.normal * (separation.distance - 0.005f);
                        Physics2D.SyncTransforms();
                    }
        }
        visual.transform.position = new Vector3(physics.position.x, physics.position.y, position.z);
        var correction = physics.position - (Vector2)position;
        if (Peer != MultiplayerSession.LocalPeerId || correction.sqrMagnitude < 0.000001f) return;
        var playerPosition = body.transform.position + (Vector3)correction;
        body.rb.position = playerPosition;
        body.transform.position = playerPosition;
        var normal = correction.normalized;
        var speed = Vector2.Dot(body.rb.velocity, normal);
        if (speed < 0f) body.rb.velocity -= normal * speed;
    }

    internal void PrepareInput()
    {
        if (body == null || Peer != MultiplayerSession.LocalPeerId || visual == null) return;
        var player = PlayerScript.player;
        var moving = !MultiplayerHud.IsTyping && !Controls.IsTextInputSelected &&
            (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Space) ||
             (player != null && player.keys != null && player.keys.Any(pair =>
                 (pair.Key == "MoveUp" || pair.Key == "MoveDown" || pair.Key == "MoveLeft" || pair.Key == "MoveRight" || pair.Key == "Jump") && Input.GetKey(pair.Value))));
        if (!moving) return;
        lastInputAt = Time.unscaledTime;
        if (Time.unscaledTime >= gravityUntil) SetPhysical(false);
    }

    internal void ApplyGravity(Vector2 point, Vector2 localPoint)
    {
        if (body == null || !body.isAlive || Peer != MultiplayerSession.LocalPeerId) return;
        gravityUntil = Time.unscaledTime + 0.2f;
        SetPhysical(true);
        var force = Vector2.ClampMagnitude(point - physics.position, 5f);
        physics.AddForceAtPosition(force * 100f, physics.transform.TransformPoint(localPoint));
        physics.angularVelocity *= 0.96f;
    }

    internal float DistanceFrom(Vector2 point) => hitboxes.Length == 0 ? float.PositiveInfinity :
        hitboxes.Min(hitbox => Vector2.Distance(point, hitbox.ClosestPoint(point)));

    internal void ApplyPush(float direction)
    {
        if (body == null || !body.isAlive || Peer != MultiplayerSession.LocalPeerId || (direction != -1f && direction != 1f)) return;
        gravityUntil = Time.unscaledTime + 0.15f;
        SetPhysical(true);
        var speed = Mathf.Max(0f, 3f - physics.velocity.x * direction);
        physics.AddForce(Vector2.right * (direction * speed * physics.mass), ForceMode2D.Impulse);
    }

    private void SetPhysical(bool enabled)
    {
        if (Physical == enabled || physics == null || body == null) return;
        Physical = enabled;
        if (enabled)
        {
            playerBodyType = body.rb.bodyType;
            playerSimulated = body.rb.simulated;
            playerFeetOffset = FeetOffset(body);
            var velocity = body.rb.velocity;
            body.rb.bodyType = RigidbodyType2D.Kinematic;
            body.rb.velocity = Vector2.zero;
            body.rb.simulated = false;
            SetLayer(LayerMask.NameToLayer("Ground"));
            physics.bodyType = RigidbodyType2D.Dynamic;
            physics.velocity = velocity;
            physics.WakeUp();
        }
        else
        {
            FollowPhysics();
            body.rb.bodyType = playerBodyType;
            body.rb.simulated = playerSimulated;
            FollowPhysics();
            body.rb.velocity = physics.velocity;
            physics.bodyType = RigidbodyType2D.Kinematic;
            physics.velocity = Vector2.zero;
            physics.angularVelocity = 0f;
            SetLayer(LayerMask.NameToLayer("Default"));
        }
        UpdateCollisions();
    }

    private void FollowPhysics()
    {
        if (hitboxes.Length == 0) return;
        var bounds = hitboxes[0].bounds;
        for (var index = 1; index < hitboxes.Length; index++) bounds.Encapsulate(hitboxes[index].bounds);
        var position = new Vector3(bounds.center.x, bounds.min.y - (body.rb.simulated ? FeetOffset(body) : playerFeetOffset), body.transform.position.z);
        body.rb.position = position;
        body.transform.position = position;
    }

    private void SetLayer(int layer)
    {
        foreach (var hitbox in hitboxes) hitbox.gameObject.layer = layer;
    }

    internal static float FeetOffset(BodyScript target)
    {
        var disguise = target.GetComponent<PropHuntDisguise>();
        if (disguise != null && disguise.Physical && !target.rb.simulated) return disguise.playerFeetOffset;
        return target.coll != null && target.coll.enabled
            ? target.coll.bounds.min.y - target.transform.position.y
            : -target.GetFullColliderSize().y * 0.5f;
    }

    private void UpdateCollisions()
    {
        SetCollisions(PlayerScript.player?.bodyScript, MultiplayerSession.LocalPeerId);
        foreach (var remote in NetworkAvatarManager.RemotePlayers()) SetCollisions(remote.Body, remote.PeerId);
        foreach (var worldBody in FindObjectsOfType<Rigidbody2D>()) SetWorldCollisions(worldBody);
        UpdatePlayerCollisions();
    }

    internal static void RegisterWorldBody(Rigidbody2D worldBody)
    {
        foreach (var disguise in active)
            if (disguise != null) disguise.SetWorldCollisions(worldBody);
    }

    private void SetWorldCollisions(Rigidbody2D worldBody)
    {
        if (worldBody == null || worldBody == physics) return;
        if (worldBody.GetComponent<PropHuntHitbox>() != null || !WorldBodyReplication.IsWorldBody(worldBody)) return;
        foreach (var collider in worldBody.GetComponentsInChildren<Collider2D>(true))
            foreach (var hitbox in hitboxes)
                Physics2D.IgnoreCollision(hitbox, collider, !Physical ||
                    (Physics2D.GetIgnoreCollision(hitbox, collider) && Physics2D.Distance(hitbox, collider).isOverlapped));
    }

    private void SetCollisions(BodyScript target, ushort peer)
    {
        if (target == null) return;
        foreach (var collider in target.GetComponentsInChildren<Collider2D>(true))
            foreach (var hitbox in hitboxes)
            {
                var ignore = peer == PropHuntSystem.Hunter
                    ? peer != MultiplayerSession.LocalPeerId && Physical
                    : peer == Peer || collider != target.coll || collider.bounds.min.y < hitbox.bounds.max.y - 0.1f;
                if (peer == PropHuntSystem.Hunter && peer == MultiplayerSession.LocalPeerId && Peer != peer &&
                    Physics2D.GetIgnoreCollision(hitbox, collider) && Physics2D.Distance(hitbox, collider).isOverlapped) ignore = true;
                Physics2D.IgnoreCollision(hitbox, collider, ignore);
            }
    }

    private void HideRemoteColliders(Transform root)
    {
        if (Peer == MultiplayerSession.LocalPeerId) return;
        foreach (var collider in root.GetComponentsInChildren<Collider2D>(true))
            if (!hiddenColliders.ContainsKey(collider)) hiddenColliders.Add(collider, collider.enabled);
    }

    private void HideLights(Transform root)
    {
        foreach (var light in root.GetComponentsInChildren<Light2D>(true))
            if (!hiddenLights.ContainsKey(light)) hiddenLights.Add(light, light.enabled);
    }

    private void Hide(Renderer[] renderers)
    {
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer.transform.IsChildOf(visual.transform)) continue;
            if (!hidden.ContainsKey(renderer)) hidden.Add(renderer, renderer.forceRenderingOff);
        }
    }

    private void OnDestroy()
    {
        active.Remove(this);
        SetPhysical(false);
        foreach (var pair in hidden)
            if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
        foreach (var pair in hiddenLights)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var pair in hiddenColliders)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        if (visual != null) Destroy(visual);
    }

    internal static GameObject CreateTemplate(Rigidbody2D source)
    {
        var root = new GameObject("Prop Hunt template " + source.name);
        root.SetActive(false);
        root.AddComponent<PropHuntHitbox>();
        var physics = root.AddComponent<Rigidbody2D>();
        physics.bodyType = RigidbodyType2D.Kinematic;
        physics.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        physics.gravityScale = source.gravityScale;
        physics.mass = source.mass;
        physics.drag = source.drag;
        physics.angularDrag = source.angularDrag;
        physics.constraints = source.constraints;
        var bounds = new Bounds();
        var first = true;
        foreach (var renderer in source.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer.sprite == null || !renderer.enabled) continue;
            var child = CopyTransform(root.transform, source.transform, renderer.transform);
            var copy = child.AddComponent<SpriteRenderer>();
            copy.sprite = renderer.sprite;
            copy.sharedMaterials = renderer.sharedMaterials;
            copy.color = renderer.color;
            copy.flipX = renderer.flipX;
            copy.flipY = renderer.flipY;
            copy.drawMode = renderer.drawMode;
            copy.size = renderer.size;
            copy.sortingLayerID = renderer.sortingLayerID;
            copy.sortingOrder = renderer.sortingOrder;
            if (first) { bounds = renderer.bounds; first = false; }
            else bounds.Encapsulate(renderer.bounds);
        }
        foreach (var collider in source.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider.isTrigger || !collider.enabled) continue;
            var child = CopyTransform(root.transform, source.transform, collider.transform);
            Collider2D copy;
            if (collider is BoxCollider2D box)
            {
                var target = child.AddComponent<BoxCollider2D>();
                target.size = box.size;
                target.edgeRadius = box.edgeRadius;
                copy = target;
            }
            else if (collider is CircleCollider2D circle)
            {
                var target = child.AddComponent<CircleCollider2D>();
                target.radius = circle.radius;
                copy = target;
            }
            else if (collider is CapsuleCollider2D capsule)
            {
                var target = child.AddComponent<CapsuleCollider2D>();
                target.size = capsule.size;
                target.direction = capsule.direction;
                copy = target;
            }
            else if (collider is PolygonCollider2D polygon)
            {
                var target = child.AddComponent<PolygonCollider2D>();
                target.pathCount = polygon.pathCount;
                for (var path = 0; path < polygon.pathCount; path++) target.SetPath(path, polygon.GetPath(path));
                copy = target;
            }
            else if (collider is EdgeCollider2D edge)
            {
                var target = child.AddComponent<EdgeCollider2D>();
                target.points = edge.points;
                target.edgeRadius = edge.edgeRadius;
                copy = target;
            }
            else { Destroy(child); continue; }
            copy.offset = collider.offset;
            copy.isTrigger = false;
            copy.sharedMaterial = collider.sharedMaterial;
            child.AddComponent<PropHuntHitbox>();
        }
        var offset = new Vector3(-bounds.center.x + source.position.x, source.position.y - bounds.min.y, 0f);
        foreach (Transform child in root.transform) child.localPosition += offset;
        return root;
    }

    private static GameObject CopyTransform(Transform root, Transform source, Transform original)
    {
        var child = new GameObject(original.name);
        child.transform.SetParent(root, false);
        child.transform.localPosition = Quaternion.Inverse(source.rotation) * (original.position - source.position);
        child.transform.localRotation = Quaternion.Inverse(source.rotation) * original.rotation;
        child.transform.localScale = original.lossyScale;
        child.layer = LayerMask.NameToLayer("Default");
        return child;
    }
}

internal sealed class PropHuntHitbox : MonoBehaviour
{
    internal ushort Peer;
}
