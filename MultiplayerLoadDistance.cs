using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

internal static class MultiplayerLoadDistance
{
    private const float DefaultTickDistanceSqr = 1800f;
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

    internal static void Apply()
    {
        var performanceStarted = MultiplayerPerformance.Start();
        try
        {
        var active = MultiplayerSession.IsHosting || MultiplayerSession.IsConnected;
        if (active)
        {
            ResourceManager.maxDistance = float.PositiveInfinity;
            ObjectUnloader.dist = float.PositiveInfinity;
            ObjectUnloader.distTwo = float.PositiveInfinity;
            wasActive = true;
            hostSimulationActive = MultiplayerSession.IsConnected && MultiplayerSession.IsHost;
            activeDistanceSqr = ReadTickDistance();
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
        npcTickStates[body] = tick;
        foreach (var rigidbody in body.GetComponentsInChildren<Rigidbody2D>(true))
            SetSimulation(rigidbody, tick);
    }

    internal static bool IsSimulationCulled(Rigidbody2D body)
    {
        return body != null && savedSimulationStates.ContainsKey(body);
    }

    internal static bool IsNpcSimulationCulled(BodyScript body)
    {
        if (body == null) return false;
        foreach (var rigidbody in body.GetComponentsInChildren<Rigidbody2D>(true))
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
        if (playerPositions.Count == 0) return true;
        foreach (var playerPosition in playerPositions)
            if ((position - playerPosition).sqrMagnitude < activeDistanceSqr) return true;
        return false;
    }

    private static float ReadTickDistance()
    {
        var distanceSqr = PlayerPrefs.GetFloat("loadDistance", DefaultTickDistanceSqr);
        return distanceSqr <= 0f || float.IsNaN(distanceSqr) || float.IsInfinity(distanceSqr)
            ? DefaultTickDistanceSqr
            : distanceSqr;
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

[HarmonyPatch(typeof(ObjectUnloader), "CheckDistance")]
internal static class MultiplayerObjectUnloaderPatch
{
    private static bool Prefix(ObjectUnloader __instance)
    {
        return !MultiplayerLoadDistance.TryApplyObjectUnloader(__instance);
    }
}

[HarmonyPatch(typeof(BodyScript), "FixedUpdate")]
internal static class MultiplayerNpcBodyFixedUpdateCullPatch
{
    private static bool Prefix(BodyScript __instance)
    {
        return MultiplayerLoadDistance.ShouldTickNpc(__instance);
    }
}

[HarmonyPatch(typeof(BodyScript), "Update")]
internal static class MultiplayerNpcBodyUpdateCullPatch
{
    private static bool Prefix(BodyScript __instance)
    {
        return MultiplayerLoadDistance.ShouldTickNpc(__instance);
    }
}

[HarmonyPatch(typeof(LimbScript), "FixedUpdate")]
internal static class MultiplayerNpcLimbFixedUpdateCullPatch
{
    private static bool Prefix(LimbScript __instance)
    {
        return __instance == null || MultiplayerLoadDistance.ShouldTickNpc(__instance.body);
    }
}

[HarmonyPatch(typeof(CrateScript), "FixedUpdate")]
internal static class MultiplayerCrateTickCullPatch
{
    private static bool Prefix(CrateScript __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(DroppedWeapon), "Update")]
internal static class MultiplayerDroppedWeaponTickCullPatch
{
    private static bool Prefix(DroppedWeapon __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(DoorScript), "FixedUpdate")]
internal static class MultiplayerDoorTickCullPatch
{
    private static bool Prefix(DoorScript __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(MovingBelt), "FixedUpdate")]
internal static class MultiplayerBeltTickCullPatch
{
    private static bool Prefix(MovingBelt __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(RbMoveToObj), "FixedUpdate")]
internal static class MultiplayerRbMoveTickCullPatch
{
    private static bool Prefix(RbMoveToObj __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(SawScript), "Update")]
internal static class MultiplayerSawTickCullPatch
{
    private static bool Prefix(SawScript __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(CustJoint), "FixedUpdate")]
internal static class MultiplayerJointTickCullPatch
{
    private static bool Prefix(CustJoint __instance)
    {
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}

[HarmonyPatch(typeof(FireScript), "Update")]
internal static class MultiplayerFireTickCullPatch
{
    private static bool Prefix(FireScript __instance)
    {
        if (MultiplayerSession.IsConnected && !MultiplayerSession.IsHost)
            return WorldReplication.ShouldTickClientFire(__instance);
        return MultiplayerLoadDistance.ShouldTickWorld(__instance);
    }
}
