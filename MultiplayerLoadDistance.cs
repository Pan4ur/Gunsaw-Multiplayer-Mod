using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// https://youtu.be/-jC1soxzOYg
internal static class MultiplayerLoadDistance
{
    private const float DefaultTickDistanceSqr = 1000f;
    internal const float NpcPoseDistanceSqr = 600f;

    private const float SimulationRefreshInterval = 0.2f;
    private static readonly List<Vector2> playerPositions = new List<Vector2>();
    private static readonly Dictionary<Rigidbody2D, bool> savedSimulationStates =
        new Dictionary<Rigidbody2D, bool>();
    private static readonly Dictionary<BodyScript, bool> npcTickStates =
        new Dictionary<BodyScript, bool>();
    private static readonly Dictionary<Component, bool> worldTickStates =
        new Dictionary<Component, bool>();
    private static readonly List<Rigidbody2D> staleBodies = new List<Rigidbody2D>();
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
        worldTickStates.Clear();
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
        return body != null && IsNearAnyPlayer(body.transform.position);
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
        var playerPosition = body.rb == null ? (Vector2)body.transform.position : body.rb.position;
        return (position - playerPosition).sqrMagnitude < NpcPoseDistanceSqr;
    }

    internal static bool IsWorldNearAnyPlayer(Rigidbody2D body)
    {
        return body != null && IsNearAnyPlayer(body.position);
    }

    internal static bool IsWorldNearLocalPlayer(Rigidbody2D body)
    {
        var player = PlayerScript.player;
        var localBody = player == null ? null : player.bodyScript;
        if (body == null || localBody == null) return true;
        var localPosition = localBody.rb == null ? (Vector2)localBody.transform.position : localBody.rb.position;
        return (body.position - localPosition).sqrMagnitude < activeDistanceSqr;
    }

    internal static bool ShouldTickWorld(Component component)
    {
        if (!IsHostSimulationActive() || component == null) return true;
        bool tick;
        if (worldTickStates.TryGetValue(component, out tick)) return tick;
        tick = component.GetComponentInParent<BodyScript>() != null ||
               component.GetComponentInParent<PlayerScript>() != null ||
               component.GetComponentInParent<NetworkReplica>() != null ||
               IsNearAnyPlayer(component.transform.position);
        worldTickStates[component] = tick;
        return tick;
    }

    internal static void ApplyWorldBody(Rigidbody2D body)
    {
        if (body == null || !IsHostSimulationActive()) return;
        SetSimulation(body, IsNearAnyPlayer(body.position));
    }

    internal static void ApplyNpc(BodyScript body)
    {
        if (body == null || !IsHostSimulationActive() || body.isPlayer ||
            body.GetComponentInParent<NetworkReplica>() != null) return;
        var tick = IsNearAnyPlayer(body.transform.position);
        var tickTails = tick && ShouldTickNpcTails(body);
        npcTickStates[body] = tick;
        var root = NpcRoot(body);
        foreach (var ai in root.GetComponentsInChildren<AIScript>(true))
            if (ai != null && ai.body == body) ai.enabled = tick;
        foreach (var rigidbody in root.GetComponentsInChildren<Rigidbody2D>(true))
            SetSimulation(rigidbody, tick && (tickTails || !IsNpcTailBody(body, rigidbody)));
    }

    internal static bool IsSimulationCulled(Rigidbody2D body)
    {
        return body != null && savedSimulationStates.ContainsKey(body);
    }

    internal static bool IsNpcSimulationCulled(BodyScript body)
    {
        if (body == null) return false;
        foreach (var rigidbody in NpcRoot(body).GetComponentsInChildren<Rigidbody2D>(true))
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
        if (localPlayer != null) AddPlayerPosition(localPlayer.bodyScript);
        foreach (var remote in NetworkAvatarReplication.RemotePlayers())
        {
            if (remote.HasAuthoritativePosition) playerPositions.Add(remote.AuthoritativePosition);
            else AddPlayerPosition(remote.Body, true);
        }
    }

    private static void AddPlayerPosition(BodyScript body, bool allowInactive = false)
    {
        if (body == null || (!allowInactive && !body.gameObject.activeInHierarchy)) return;
        playerPositions.Add(body.rb == null ? (Vector2)body.transform.position : body.rb.position);
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
}
