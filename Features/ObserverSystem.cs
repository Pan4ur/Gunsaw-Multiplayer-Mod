using UnityEngine;

internal static class ObserverSystem
{
    private static int pendingActivation;
    private static int pendingDeath;
    private static bool applyingActivation;
    private static bool suppressObserverError;
    private static bool observerActive;
    private static bool suppressStatesUntilActivation;
    private static readonly object stateLock = new();
    private static ObserverStatePacket latestState;
    private static bool hasState;

    internal static bool AllowActivation()
    {
        if (!MultiplayerSession.IsActive || applyingActivation) return true;
        if (!MultiplayerSession.AllowObserver) return false;
        MultiplayerSession.Send(new ObserverEventPacket(), MultiplayerSession.IsHost ? (ushort)0 : MultiplayerSession.HostPeerId);
        return true;
    }

    internal static void ResetActivationSequence()
    {
        if (PlayerScript.player != null) PlayerScript.player.sequence2Index = 0;
    }

    internal static void QueueActivation()
    {
        suppressStatesUntilActivation = false;
        if (MultiplayerSession.AllowObserver) Interlocked.Exchange(ref pendingActivation, 1);
    }

    internal static void QueueDeath() => Interlocked.Exchange(ref pendingDeath, 1);

    internal static void QueueState(ObserverStatePacket state)
    {
        if (suppressStatesUntilActivation && state.Active) return;
        if (!state.Active)
        {
            ResetForLevelChange();
            return;
        }
        lock (stateLock)
        {
            latestState = state;
            hasState = true;
            if (state.Active) Interlocked.Exchange(ref pendingActivation, 1);
        }
    }

    internal static void SendCurrentState(ushort peerId)
    {
        if (!MultiplayerSession.IsHost || peerId == 0) return;
        lock (stateLock)
            MultiplayerSession.Send(new ObserverStatePacket(latestState.PositionX, latestState.PositionY,
                latestState.Rotation, observerActive), peerId);
    }

    internal static void BeginOriginalActivation() => suppressObserverError = true;
    internal static void EndOriginalActivation() => suppressObserverError = false;
    internal static bool SuppressError(object message) => suppressObserverError && message is string text && text == "why did you do that";

    internal static void MarkActive()
    {
        if (PlayerScript.player != null && PlayerScript.player.observed) observerActive = true;
    }

    internal static void BroadcastResetForLevelChange()
    {
        if (MultiplayerSession.IsHost) MultiplayerSession.Send(new ObserverStatePacket(0f, 0f, 0f, false));
        ResetForLevelChange();
    }

    internal static void ResetForLevelChange(bool suppressStates = true)
    {
        Interlocked.Exchange(ref pendingActivation, 0);
        Interlocked.Exchange(ref pendingDeath, 0);
        observerActive = false;
        suppressStatesUntilActivation = suppressStates;
        lock (stateLock) hasState = false;
        if (PlayerScript.player != null) PlayerScript.player.observed = false;
        Application.targetFrameRate = -1;
    }

    internal static void Tick()
    {
        if (!MultiplayerSession.IsActive)
        {
            Interlocked.Exchange(ref pendingActivation, 0);
            Interlocked.Exchange(ref pendingDeath, 0);
            observerActive = false;
            lock (stateLock) hasState = false;
            return;
        }
        if (Interlocked.Exchange(ref pendingDeath, 0) != 0)
        {
            var body = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
            if (body != null && body.isAlive)
            {
                NetworkAvatarReplication.RecordEnvironmentalDeathCause(body, PlayerDeathCause.Observer);
                body.Death();
            }
        }
        if (Interlocked.CompareExchange(ref pendingActivation, 0, 0) == 0) return;
        var player = PlayerScript.player;
        if (player == null) return;
        Interlocked.Exchange(ref pendingActivation, 0);
        applyingActivation = true;
        try { player.Observify(); }
        finally { applyingActivation = false; }
        observerActive = player.observed;
        Application.targetFrameRate = -1;
    }

    internal static void UpdateTarget(PlayerScript player)
    {
        if (!MultiplayerSession.IsActive || player == null || !player.observed || player.observer == null) return;
        if (!MultiplayerSession.IsHost)
        {
            lock (stateLock)
            {
                if (!hasState) return;
                player.observer.position = new Vector3(latestState.PositionX, latestState.PositionY, player.observer.position.z);
                player.observer.eulerAngles = new Vector3(0f, 0f, latestState.Rotation);
            }
            return;
        }
        var replica = FindNearestReplica(player.bodyScript, player.observer);
        var target = replica == null ? player.bodyScript : replica.remoteBody;
        if (target == null) return;
        var targetPosition = replica == null
            ? target.transform.position
            : new Vector3(replica.lastAuthoritativePosition.x, replica.lastAuthoritativePosition.y,
                target.transform.position.z);
        player.observer.position = Vector3.MoveTowards(player.observer.position, targetPosition, Time.unscaledDeltaTime * 3f);
        var direction = (Vector2)targetPosition - (Vector2)player.observer.position;
        if (direction.sqrMagnitude > 0.0001f)
            player.observer.eulerAngles = new Vector3(0f, 0f, Mathf.Atan2(direction.x, -direction.y) * Mathf.Rad2Deg - 90f);
    
        var state = new ObserverStatePacket(player.observer.position.x, player.observer.position.y,
            player.observer.eulerAngles.z, true);
        lock (stateLock)
        {
            latestState = state;
            hasState = true;
        }
        MultiplayerSession.Send(state);
      
        if (!MultiplayerSession.IsHost || direction.sqrMagnitude >= 1f) return;
        
        if (target == PlayerScript.player.bodyScript && target.isAlive)
        {
         
            NetworkAvatarReplication.RecordEnvironmentalDeathCause(target, PlayerDeathCause.Observer);
            target.Death();
            return;
        }
        
        if (replica != null) MultiplayerSession.Send(new ObserverKillPacket(), replica.remotePeerId);
    }

    internal static bool AllowQuit()
    {
        if (!MultiplayerSession.IsActive || PlayerScript.player == null || !PlayerScript.player.observed) return true;
        return false;
    }

    private static NetworkAvatarReplication FindNearestReplica(BodyScript localBody, Transform observer)
    {
        if (observer == null) return null;
        var closestDistance = localBody != null && localBody.isAlive
            ? ((Vector2)localBody.transform.position - (Vector2)observer.position).sqrMagnitude
            : float.MaxValue;
        NetworkAvatarReplication closest = null;
        foreach (var replica in NetworkAvatarRegistry.replicas.Values)
        {
            var body = replica == null ? null : replica.remoteBody;
            if (body == null || !body.isAlive || !body.gameObject.activeInHierarchy || replica.remotePeerId == 0) continue;
            var position = replica.hasAuthoritativePosition
                ? replica.lastAuthoritativePosition
                : (Vector2)body.transform.position;
            var distance = (position - (Vector2)observer.position).sqrMagnitude;
            if (distance >= closestDistance) continue;
            closest = replica;
            closestDistance = distance;
        }
        return closest;
    }
}
