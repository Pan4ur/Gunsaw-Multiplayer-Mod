using UnityEngine;

// https://youtu.be/-jC1soxzOYg
internal static class LoadDistanceSystem
{
    // Mechs now tick at the default distance specified in the settings - 1800 (sqr m)
    // I think most custom maps are designed around this distance
    // This should also prevent the bug we had today, where we had to move close to the elevator
    // ourselves to make it start moving.
    // For conveyor belts, I think we may need a mechanic that "wakes up" props and npcs
    // standing on them, so they can be carried along or slide off.
    // Otherwise, maps with enemy waves built around conveyor belt mechanics probably no longer work
    internal const float MechanismSleepDistanceSqr = 2400f;
    private const float DefaultTickDistanceSqr = 1000f;
    internal const float NpcPoseDistanceSqr = 600f;

    private const float SimulationRefreshInterval = 0.2f;
    private static readonly List<Vector2> playerPositions = [];
    private static readonly Dictionary<Rigidbody2D, bool> savedSimulationStates = new();
    private static readonly Dictionary<Rigidbody2D, float> worldSleepDistances = new();
    private static readonly Dictionary<Rigidbody2D, bool> worldNearPlayerStates = new();
    private static readonly Dictionary<BodyScript, bool> npcTickStates = new();
    private static readonly Dictionary<BodyScript, bool> npcNearPlayerStates = new();
    private static readonly Dictionary<Component, bool> worldTickStates = new();
    private static readonly List<Rigidbody2D> staleBodies = [];
    private static bool wasActive;
    private static bool hostSimulationActive;
    private static float activeDistanceSqr = DefaultTickDistanceSqr;
    private static float nextSimulationRefresh;

    internal static float WorldDistanceSqr => activeDistanceSqr;

    internal static void CopyPlayerPositions(List<Vector2> target)
    {
        if (target == null) return;
        target.Clear();
        target.AddRange(playerPositions);
    }

    internal static void Apply()
    {
        var performanceStarted = MultiplayerPerformance.Start();
        try
        {
        var active = MultiplayerSession.IsHosting || MultiplayerSession.IsConnected;
        if (active)
        {
            wasActive = true;
            hostSimulationActive = MultiplayerSession.IsConnected && MultiplayerSession.IsHost;
            activeDistanceSqr = DefaultTickDistanceSqr;
            ResourceManager.maxDistance = float.PositiveInfinity;
            ObjectUnloader.dist = float.PositiveInfinity;
            ObjectUnloader.distTwo = float.PositiveInfinity;
            RefreshPlayerPositions();
            RefreshSimulation();
            return;
        }

        if (!wasActive) return;
        var configuredDistance = PlayerPrefs.GetFloat("loadDistance", DefaultTickDistanceSqr);
        ResourceManager.maxDistance = configuredDistance;
        ObjectUnloader.dist = configuredDistance;
        ObjectUnloader.distTwo = configuredDistance - 300f;
        RestoreSimulation();
        playerPositions.Clear();
        npcTickStates.Clear();
        npcNearPlayerStates.Clear();
        worldTickStates.Clear();
        worldSleepDistances.Clear();
        worldNearPlayerStates.Clear();
        hostSimulationActive = false;
        nextSimulationRefresh = 0f;
        wasActive = false;
        }
        finally
        {
            MultiplayerPerformance.AddDistance(performanceStarted);
        }
    }

    internal static bool ShouldTickNpc(BodyScript body)
    {
        if (!IsHostSimulationActive() || body == null) return true;
        bool tick;
        if (npcTickStates.TryGetValue(body, out tick)) return tick;
        tick = body.isPlayer || body.GetComponentInParent<NetworkReplica>() != null ||
               IsNearAnyPlayer(body.transform.position);
        npcTickStates[body] = tick;
        return tick;
    }

    internal static bool IsNpcNearAnyPlayer(BodyScript body)
    {
        if (body == null) return false;
        if (!IsHostSimulationActive()) return IsNearAnyPlayer(body.transform.position);
        bool near;
        if (npcNearPlayerStates.TryGetValue(body, out near)) return near;
        near = IsNearAnyPlayer(body.transform.position);
        npcNearPlayerStates[body] = near;
        return near;
    }

    internal static bool ShouldSendNpcLimbPose(BodyScript body)
    {
        return body != null && IsNearAnyPlayer(body.transform.position, NpcPoseDistanceSqr);
    }

    internal static bool ShouldTickNpcTails(BodyScript body)
    {
        return body != null && IsNearAnyPlayer(body.transform.position, NpcPoseDistanceSqr);
    }

    internal static bool IsNpcNearLocalPlayer(Vector2 position)
    {
        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        if (body == null || !body.gameObject.activeInHierarchy) return true;
        var playerPosition =  GetPlayerPosition(body);
        return (position - playerPosition).sqrMagnitude < NpcPoseDistanceSqr;
    }

    internal static bool IsWorldNearAnyPlayer(Rigidbody2D body)
    {
        if (body == null) return false;
        bool near;
        if (worldNearPlayerStates.TryGetValue(body, out near)) return near;
        near = IsNearAnyPlayer(body.position, WorldSleepDistanceSqr(body));
        worldNearPlayerStates[body] = near;
        return near;
    }

    internal static bool IsWorldNearLocalPlayer(Rigidbody2D body)
    {
        var player = PlayerScript.player;
        var localBody = player == null ? null : player.bodyScript;
        if (body == null || localBody == null) return true;
        var localPosition = GetPlayerPosition(localBody);
        return (body.position - localPosition).sqrMagnitude < WorldSleepDistanceSqr(body);
    }

    internal static bool ShouldTickWorld(Component component)
    {
        if (!IsHostSimulationActive() || component == null) return true;
        bool tick;
        if (worldTickStates.TryGetValue(component, out tick)) return tick;
        tick = component.GetComponentInParent<BodyScript>() != null ||
               component.GetComponentInParent<PlayerScript>() != null ||
               component.GetComponentInParent<NetworkReplica>() != null ||
               IsNearAnyPlayer(component.transform.position, WorldSleepDistanceSqr(component));
        worldTickStates[component] = tick;
        return tick;
    }

    internal static void ApplyWorldBody(Rigidbody2D body)
    {
        if (body == null || !IsHostSimulationActive()) return;
        var door = body.GetComponentInParent<DoorScript>();
        var source = door == null ? null : door.GetComponent<AudioSource>();
        if (source != null && source.isPlaying)
        {
            worldNearPlayerStates[body] = true;
            SetSimulation(body, true);
            return;
        }
        var near = IsNearAnyPlayer(body.position, WorldSleepDistanceSqr(body));
        worldNearPlayerStates[body] = near;
        SetSimulation(body, near);
    }

    internal static void RegisterWorldBody(Rigidbody2D body)
    {
        if (body == null || worldSleepDistances.ContainsKey(body)) return;
        worldSleepDistances[body] = ResolveWorldSleepDistanceSqr(body);
    }

    internal static void UnregisterWorldBody(Rigidbody2D body)
    {
        if (body == null) return;
        worldSleepDistances.Remove(body);
        worldNearPlayerStates.Remove(body);
    }

    internal static void ApplyNpc(BodyScript body)
    {
        if (body == null || !IsHostSimulationActive() || body.isPlayer ||
            body.GetComponentInParent<NetworkReplica>() != null) return;
        var tick = IsNearAnyPlayer(body.transform.position);
        npcNearPlayerStates[body] = tick;
        var tickTails = tick && ShouldTickNpcTails(body);
        npcTickStates[body] = tick;
        NpcReplication.GetSimulationComponents(body, out var aiControllers, out var simulationBodies);
        foreach (var ai in aiControllers)
            if (ai != null && ai.body == body) ai.enabled = tick;
        foreach (var rigidbody in simulationBodies)
            SetSimulation(rigidbody, tick && (tickTails || !IsNpcTailBody(body, rigidbody)));
    }

    internal static bool IsSimulationCulled(Rigidbody2D body)
    {
        return body != null && savedSimulationStates.ContainsKey(body);
    }

    internal static bool IsNpcSimulationCulled(BodyScript body)
    {
        if (body == null) return false;
        NpcReplication.GetSimulationComponents(body, out _, out var simulationBodies);
        foreach (var rigidbody in simulationBodies)
            if (IsSimulationCulled(rigidbody)) return true;
        return false;
    }

    internal static bool TryApplyObjectUnloader(ObjectUnloader unloader)
    {
        if (!IsHostSimulationActive() || unloader == null) return false;
        ApplyWorldBody(unloader.GetComponent<Rigidbody2D>());
        return true;
    }

    private static void RefreshSimulation()
    {
        if (!MultiplayerSession.IsHost)
        {
            RestoreSimulation();
            return;
        }
        if (Time.unscaledTime < nextSimulationRefresh) return;
        nextSimulationRefresh = Time.unscaledTime + SimulationRefreshInterval;
        npcTickStates.Clear();
        worldTickStates.Clear();
        var world = WorldReplication.Instance;
        if (world != null) world.ApplyDistanceCulling();
        var npcs = NpcReplication.Instance;
        if (npcs != null) npcs.ApplyDistanceCulling();
    }

    private static bool IsHostSimulationActive()
    {
        return hostSimulationActive;
    }

    private static void RefreshPlayerPositions()
    {
        playerPositions.Clear();

        var localPlayer = PlayerScript.player;
        if (localPlayer != null)
            AddPlayerPosition(localPlayer.bodyScript);

        foreach (var remote in NetworkAvatarRegistry.RemotePlayers())
        {
            var body = remote.Body;
            if (body != null && body.inVehicle)
            {
                playerPositions.Add((Vector2)body.transform.position);
                continue;
            }

            if (remote.HasAuthoritativePosition)
                playerPositions.Add(remote.AuthoritativePosition);
            else
                AddPlayerPosition(body, true);
        }
    }

    private static void AddPlayerPosition(BodyScript body, bool allowInactive = false)
    {
        if (body == null || (!allowInactive && !body.gameObject.activeInHierarchy)) return;
        playerPositions.Add(GetPlayerPosition(body));
    }

    private static bool IsNearAnyPlayer(Vector2 position)
    {
        return IsNearAnyPlayer(position, activeDistanceSqr);
    }

    private static bool IsNearAnyPlayer(Vector2 position, float distanceSqr)
    {
        if (playerPositions.Count == 0) return true;
        foreach (var playerPosition in playerPositions)
            if ((position - playerPosition).sqrMagnitude < distanceSqr) return true;
        return false;
    }

    private static float WorldSleepDistanceSqr(Component component)
    {
        if (component == null) return activeDistanceSqr;
        var body = component as Rigidbody2D;
        if (body != null)
        {
            float distance;
            if (worldSleepDistances.TryGetValue(body, out distance)) return distance;
            distance = ResolveWorldSleepDistanceSqr(body);
            worldSleepDistances[body] = distance;
            return distance;
        }
        return ResolveWorldSleepDistanceSqr(component);
    }

    private static float ResolveWorldSleepDistanceSqr(Component component)
    {
        if (component.GetComponentInParent<VehiclePart>() != null) return float.PositiveInfinity;
        return component.GetComponentInParent<DoorScript>() != null ||
            component.GetComponentInParent<MiniCrateSpawner>() != null ||
            component.GetComponentInParent<RbMoveToObj>() != null ||
            component.GetComponentInParent<CustJoint>() != null
            ? MechanismSleepDistanceSqr
            : activeDistanceSqr;
    }

    private static bool IsNpcTailBody(BodyScript body, Rigidbody2D rigidbody)
    {
        if (body == null || rigidbody == null) return false;
        if (body.tailBases != null)
            foreach (Rigidbody2D tailBase in body.tailBases)
                if (tailBase == rigidbody) return true;
        if (body.tails != null)
            foreach (var tail in body.tails)
                if (tail != null && (rigidbody.transform == tail || rigidbody.transform.IsChildOf(tail))) return true;
        return false;
    }

    private static GameObject NpcRoot(BodyScript body)
    {
        var current = body.transform;
        while (current.parent != null)
        {
            var parent = current.parent;
            var bodies = parent.GetComponentsInChildren<BodyScript>(true);
            if (bodies.Length != 1 || bodies[0] != body) break;
            current = parent;
        }
        return current.gameObject;
    }

    private static void SetSimulation(Rigidbody2D body, bool simulated)
    {
        if (body == null) return;
        if (simulated)
        {
            bool original;
            if (!savedSimulationStates.TryGetValue(body, out original)) return;
            body.simulated = original;
            if (original) body.WakeUp();
            savedSimulationStates.Remove(body);
            return;
        }
        if (!savedSimulationStates.ContainsKey(body)) savedSimulationStates[body] = body.simulated;
        body.simulated = false;
    }

    private static void RestoreSimulation()
    {
        staleBodies.Clear();
        foreach (var pair in savedSimulationStates)
        {
            if (pair.Key == null) { staleBodies.Add(pair.Key); continue; }
            pair.Key.simulated = pair.Value;
            if (pair.Value) pair.Key.WakeUp();
            staleBodies.Add(pair.Key);
        }
        foreach (var body in staleBodies) savedSimulationStates.Remove(body);
        staleBodies.Clear();
    }
    
    private static Vector2 GetPlayerPosition(BodyScript body)
    {
        if (body == null)
            return Vector2.zero;

        if (body.inVehicle)
            return body.transform.position;

        return body.rb != null
            ? body.rb.position
            : (Vector2)body.transform.position;
    }
}
