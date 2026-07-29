using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

[HarmonyPatch(typeof(LimbScript), "OnCollisionStay2D")]
internal static class LimbCrateCollisionPatch
{
    private static void Postfix(LimbScript __instance, Collision2D collision)
    {
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueuePush(__instance, collision);
    }
}

[HarmonyPatch(typeof(LevitatorScript), "FixedUpdate")]
internal static class ClientLevitatorPropPatch
{
    private static void Prefix(LevitatorScript __instance)
    {
        NetworkAvatarReplication.ValidateRemoteGrab(__instance);
    }

    private static void Postfix(LevitatorScript __instance)
    {
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueLevitated(__instance.currentlyLevitating);
        NetworkAvatarReplication.QueueRemoteGrab(__instance);
        NpcReplication.QueueClientCorpseGrab(__instance);
    }
}

[HarmonyPatch(typeof(CrystalTongue), "FixedUpdate")]
internal static class ClientCrystalTonguePropPatch
{
    private static void Postfix(CrystalTongue __instance)
    {
        if (__instance == null || __instance.tongueProgress >= 1f || __instance.pullTrans == null ||
            GunsawMultiplayerPlugin.World == null) return;
        GunsawMultiplayerPlugin.World.QueueLevitated(__instance.pullTrans.GetComponent<Rigidbody2D>());
    }
}

[HarmonyPatch(typeof(CrystalTongue), "Tongue")]
internal static class ClientCrystalTongueRemotePlayerPatch
{
    private static void Postfix(CrystalTongue __instance)
    {
        if (!MultiplayerSession.IsConnected || __instance == null || __instance.tongueProgress >= 1f ||
            __instance.pullTrans != null) return;
        var body = __instance.GetComponent<BodyScript>();
        var player = PlayerScript.player;
        if (body == null || player == null || body != player.bodyScript || body.headTransform == null) return;
        var origin = (Vector2)body.headTransform.position;
        var direction = body.targetLookPos - origin;
        var distance = direction.magnitude;
        if (distance < 0.0001f) return;
        direction /= distance;
        var closestDistance = 13f;
        Collider2D closestCollider = null;
        Vector2 closestPoint = Vector2.zero;
        foreach (var remote in NetworkAvatarReplication.RemotePlayers())
        {
            if (remote.Body == null) continue;
            foreach (var collider in remote.Body.GetComponentsInChildren<Collider2D>(true))
            {
                if (collider == null) continue;
                if (!TryHitBounds(origin, direction, collider.bounds, out var hitDistance) ||
                    hitDistance >= closestDistance) continue;
                closestDistance = hitDistance;
                closestCollider = collider;
                closestPoint = origin + direction * hitDistance;
            }
        }
        if (closestCollider == null) return;
        __instance.pullTrans = closestCollider.transform;
        __instance.pullPos = closestCollider.transform.InverseTransformPoint(closestPoint);
    }

    private static bool TryHitBounds(Vector2 origin, Vector2 direction, Bounds bounds, out float hitDistance)
    {
        var enter = 0f;
        var exit = 13f;
        if (!ClipAxis(origin.x, direction.x, bounds.min.x, bounds.max.x, ref enter, ref exit) ||
            !ClipAxis(origin.y, direction.y, bounds.min.y, bounds.max.y, ref enter, ref exit))
        {
            hitDistance = 0f;
            return false;
        }
        hitDistance = enter;
        return true;
    }

    private static bool ClipAxis(float origin, float direction, float minimum, float maximum, ref float enter,
        ref float exit)
    {
        if (Mathf.Abs(direction) < 0.00001f) return origin >= minimum && origin <= maximum;
        var first = (minimum - origin) / direction;
        var second = (maximum - origin) / direction;
        if (first > second)
        {
            var swap = first;
            first = second;
            second = swap;
        }
        enter = Mathf.Max(enter, first);
        exit = Mathf.Min(exit, second);
        return enter <= exit && exit >= 0f;
    }
}

[HarmonyPatch(typeof(LevitatorScript), "TryGrab")]
internal static class MultiplayerPlayerGrabPatch
{
    private static void Postfix(LevitatorScript __instance)
    {
        NetworkAvatarReplication.TryGrabRemotePlayer(__instance);
        NpcReplication.TryGrabClientCorpse(__instance);
    }
}

[HarmonyPatch(typeof(PlayerScript), "Update")]
internal static class MultiplayerPlayerSlowmoPatch
{
    private static bool Prefix(PlayerScript __instance, out MultiplayerTimeControl.SlowmoKeyState __state)
    {
        __state = default(MultiplayerTimeControl.SlowmoKeyState);
        if (MultiplayerSession.IsConnected)
        {
            NetworkAvatarReplication.EnsurePlayerSingletonForUpdate();
            if (!NetworkAvatarReplication.PrepareLocalPlayerUpdate(__instance))
                return false;
        }

        MultiplayerTimeControl.KeepMultiplayerActive();
        __state = MultiplayerTimeControl.BeginPlayerUpdate(__instance);
        return !MultiplayerHud.IsTyping;
    }

    private static Exception Finalizer(PlayerScript __instance, Exception __exception,
        MultiplayerTimeControl.SlowmoKeyState __state)
    {
        MultiplayerTimeControl.EndPlayerUpdate(__instance, __state);
        return __exception;
    }

    private static void Postfix(PlayerScript __instance)
    {
        NetworkAvatarReplication.SuppressSpectatorDeathEffects(__instance);
    }
}

[HarmonyPatch(typeof(GameManager), "Update")]
internal static class MultiplayerGameManagerFocusPatch
{
    private static void Prefix()
    {
        MultiplayerLoadDistance.Apply();
        MultiplayerTimeControl.KeepMultiplayerActive();
    }

    private static void Postfix()
    {
        MultiplayerLoadDistance.Apply();
    }
}

[HarmonyPatch(typeof(GameManager), "MainMenu")]
internal static class MultiplayerClientMainMenuPatch
{
    private static void Prefix()
    {
        if (SceneLoader.main != null)
        {
            SceneLoader.main.levelEditString = "";
            SceneLoader.main.hadEditorWarning = false;
        }

        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHosting) return;
        MultiplayerSession.Shutdown();
    }
}

[HarmonyPatch(typeof(GameManager), "BackToEditor")]
internal static class MultiplayerBackToEditorRedirectPatch
{
    private static bool Prefix()
    {
        if (MultiplayerSession.IsHosting)
            MultiplayerSession.EndHostCustomLevel("LevelSelect");
        if (SceneLoader.main != null)
        {
            SceneLoader.main.levelEditString = "";
            SceneLoader.main.hadEditorWarning = false;
            SceneLoader.main.LoadScene("LevelSelect");
        }
        else SceneManager.LoadScene("LevelSelect");
        if (MultiplayerSession.IsConnected && !MultiplayerSession.IsHosting)
            MultiplayerSession.Shutdown();
        return false;
    }
}

internal static class CustomLevelSpawnSelection
{
    private static int selectionDepth;
    private static GameObject selectedSpawn;
    private static readonly List<Vector3> spawnPositions = new List<Vector3>();
    private static int spawnScene = int.MinValue;

    internal static void Begin()
    {
        if (selectionDepth++ == 0)
        {
            selectedSpawn = null;
            spawnPositions.Clear();
            spawnScene = SceneManager.GetActiveScene().handle;
        }
    }

    internal static void End()
    {
        if (selectionDepth > 0) selectionDepth--;
        if (selectionDepth == 0) selectedSpawn = null;
    }

    internal static void ReplacePlayerSpawn(ref GameObject result)
    {
        if (selectionDepth == 0) return;
        if (selectedSpawn == null)
        {
            var spawns = GameObject.FindGameObjectsWithTag("PlayerSpawn");
            if (spawns == null || spawns.Length == 0) return;
            foreach (var spawn in spawns)
                if (spawn != null) spawnPositions.Add(spawn.transform.position);
            selectedSpawn = spawns[UnityEngine.Random.Range(0, spawns.Length)];
        }
        result = selectedSpawn;
    }

    internal static void Capture(Level level)
    {
        if (level == null || level.parts == null) return;
        spawnPositions.Clear();
        foreach (var part in level.parts)
            if (part != null && part.path == "Building/PlayerSpawn") spawnPositions.Add(part.pos);
        spawnScene = SceneManager.GetActiveScene().handle;
    }

    internal static bool TryGetRandomSpawnPosition(out Vector3 position)
    {
        position = default(Vector3);
        if (spawnScene != SceneManager.GetActiveScene().handle) return false;
        if (spawnPositions.Count == 0)
        {
            foreach (var spawn in GameObject.FindGameObjectsWithTag("PlayerSpawn"))
                if (spawn != null) spawnPositions.Add(spawn.transform.position);
        }
        if (spawnPositions.Count == 0) return false;
        position = spawnPositions[UnityEngine.Random.Range(0, spawnPositions.Count)];
        return true;
    }
}

[HarmonyPatch(typeof(LevelLoader), "Start")]
internal static class MultiplayerCustomLevelSpawnScopePatch
{
    private static void Prefix(out bool __state)
    {
        __state = MultiplayerSession.IsHosting || MultiplayerSession.IsConnected;
        if (__state) CustomLevelSpawnSelection.Begin();
    }

    private static Exception Finalizer(Level ___level, bool __state, Exception __exception)
    {
        if (__state)
        {
            CustomLevelSpawnSelection.Capture(___level);
            CustomLevelSpawnSelection.End();
        }
        return __exception;
    }
}

[HarmonyPatch(typeof(GameObject), "FindGameObjectWithTag", new[] { typeof(string) })]
internal static class MultiplayerCustomLevelSpawnLookupPatch
{
    private static void Postfix(string tag, ref GameObject __result)
    {
        if (tag == "PlayerSpawn") CustomLevelSpawnSelection.ReplacePlayerSpawn(ref __result);
    }
}

[HarmonyPatch(typeof(ResourceManager), "Awake")]
internal static class MultiplayerResourceLoadDistancePatch
{
    private static void Postfix()
    {
        MultiplayerLoadDistance.Apply();
    }
}

[HarmonyPatch(typeof(GameManager), "Switch")]
internal static class MultiplayerVanillaBodySwitchPatch
{
    private static bool Prefix(LimbScript limb)
    {
        if (!MultiplayerSession.IsConnected) return true;
        var player = PlayerScript.player;
        if (player != null && player.bodyScript != null && !player.bodyScript.isAlive) return false;
        return !NpcReplication.TryPossessLocalPlayer(limb);
    }
}

[HarmonyPatch(typeof(GameManager), "IsOnscreen", new[] { typeof(BodyScript) })]
internal static class MultiplayerNpcOnScreenPatch
{
    private static void Postfix(BodyScript body, ref bool __result)
    {
        if (body != null && MultiplayerSession.IsHosting && !body.isPlayer)
        {
            body.onScreen = true;
            __result = true;
        }
    }
}

[HarmonyPatch(typeof(LimbScript), "OnWillRenderObject")]
internal static class MultiplayerLimbAnimationPatch
{
    private static bool Prefix(LimbScript __instance)
    {
        var body = __instance == null ? null : __instance.body;
        if (body == null || NpcReplication.IsPossessionRenderGuard(body) ||
            NpcReplication.IsClientProxy(body) || NetworkAvatarReplication.IsRemoteAvatarBody(body))
            return false;

        if (NpcReplication.IsHostNpc(body)) return NpcReplication.IsEvaluatingAuthoritativePose;
        return true;
    }

}

[HarmonyPatch(typeof(ScreenFXManager), "Update")]
internal static class MultiplayerScreenTimePatch
{
    private static void Prefix(ScreenFXManager __instance)
    {
        MultiplayerTimeControl.SuppressTimeSlowdown(__instance);
    }

    private static void Postfix(ScreenFXManager __instance)
    {
        MultiplayerTimeControl.SuppressTimeSlowdown(__instance);
    }
}

[HarmonyPatch(typeof(ScreenFXManager), "OnKill")]
internal static class MultiplayerNpcKillScreenEffectPatch
{
    private static bool Prefix()
    {
        return NetworkAvatarReplication.AllowNpcKillScreenEffect();
    }
}

[HarmonyPatch(typeof(CameraFollow), "CreateScreenCrack")]
internal static class MultiplayerPvpScreenCrackPatch
{
    private static bool Prefix()
    {
        return !NetworkAvatarReplication.SuppressLocalShotScreenCrack();
    }
}

[HarmonyPatch(typeof(CameraFollow), "CreateBloodSplat")]
internal static class MultiplayerTargetBloodSplatPatch
{
    private static bool Prefix()
    {
        return !NetworkAvatarReplication.SuppressTargetedScreenEffect();
    }
}

[HarmonyPatch(typeof(CameraFollow), "AddOffset")]
internal static class MultiplayerTargetCameraOffsetPatch
{
    private static bool Prefix()
    {
        return !NetworkAvatarReplication.SuppressTargetedScreenEffect();
    }
}

[HarmonyPatch(typeof(CameraFollow), "AddRot")]
internal static class MultiplayerTargetCameraRotationPatch
{
    private static bool Prefix()
    {
        return !NetworkAvatarReplication.SuppressTargetedScreenEffect();
    }
}

[HarmonyPatch(typeof(CameraFollow), "Update")]
internal static class MultiplayerTargetCameraShakeUpdatePatch
{
    private static void Prefix(CameraFollow __instance)
    {
        NetworkAvatarReplication.ClearSuppressedCameraShake(__instance);
    }
}

internal static class MultiplayerTimeControl
{
    internal sealed class SlowmoKeyState
    {
        internal Dictionary<string, KeyCode> Keys;
        internal KeyCode Key;
        internal bool Restore;
    }

    internal static SlowmoKeyState BeginPlayerUpdate(PlayerScript player)
    {
        var state = new SlowmoKeyState();
        if (!MultiplayerSession.IsConnected || player == null) return state;
        state.Keys = player.keys;
        if (state.Keys != null && state.Keys.TryGetValue("Slowmo", out state.Key))
        {
            state.Restore = true;
            state.Keys["Slowmo"] = KeyCode.None;
        }
        if (DisablePlayerSlowmo(player)) ResetSlowmoContrast(ScreenFXManager.main);
        ForceNormalTime();
        return state;
    }

    internal static void EndPlayerUpdate(PlayerScript player, SlowmoKeyState state)
    {
        if (state != null && state.Restore && state.Keys != null)
        {
            state.Keys["Slowmo"] = state.Key;
            state.Restore = false;
        }
        if (!MultiplayerSession.IsConnected) return;
        if (DisablePlayerSlowmo(player)) ResetSlowmoContrast(ScreenFXManager.main);
        ForceNormalTime();
    }

    internal static void SuppressTimeSlowdown(ScreenFXManager screen)
    {
        if (!MultiplayerSession.IsConnected) return;
        if (screen != null)
        {
            screen.slowmoTime = 0f;
            screen.fullStopTime = 0f;
            screen.slowmo = false;
        }
        if (DisablePlayerSlowmo(PlayerScript.player)) ResetSlowmoContrast(screen);
        ForceNormalTime();
    }

    internal static void KeepMultiplayerActive()
    {
        if (!MultiplayerSession.IsConnected || Application.isFocused) return;
        var manager = GameManager.main;
        if (manager != null) manager.paused = false;
        Time.timeScale = 1f;
    }

    private static bool DisablePlayerSlowmo(PlayerScript player)
    {
        if (player == null) return false;
        var wasInSlowmo = player.inSlowmo;
        player.inSlowmo = false;
        var source = player.slowmoSource;
        if (source != null && source.isPlaying) source.Stop();
        var secondaryBar = player.secondarySlowmoBarImage;
        if (secondaryBar != null && secondaryBar.transform.parent != null)
            secondaryBar.transform.parent.gameObject.SetActive(false);
        return wasInSlowmo;
    }

    private static void ResetSlowmoContrast(ScreenFXManager screen)
    {
        var manager = GameManager.main;
        if (screen != null && manager != null)
            screen.contrastAmount = -manager.fogIntensity * 45f;
    }

    private static void ForceNormalTime()
    {
        var manager = GameManager.main;
        var paused = manager != null && manager.paused;
        if (!paused) Time.timeScale = 1f;
    }
}

[HarmonyPatch(typeof(CrateScript), "Damage")]
internal static class ClientCrateDamagePatch
{
    private sealed class CrateDebrisCapture
    {
        internal readonly HashSet<int> ExistingBodies = new HashSet<int>();
    }

    private static bool Prefix(CrateScript __instance, float dmg, out CrateDebrisCapture __state)
    {
        __state = null;
        if (!MultiplayerSession.IsConnected) return true;
        if (GunsawMultiplayerPlugin.World != null &&
            GunsawMultiplayerPlugin.World.TryProtectNetworkCrateDebrisDamage(__instance, dmg)) return false;
        if (MultiplayerSession.IsHost)
        {
            if (__instance != null && __instance.breakType == CrateScript.BreakType.None &&
                __instance.objOnDestroy != null && __instance.health - dmg <= 0f)
            {
                __state = new CrateDebrisCapture();
                foreach (var body in UnityEngine.Object.FindObjectsOfType<Rigidbody2D>())
                    if (body != null) __state.ExistingBodies.Add(body.GetInstanceID());
            }
            return true;
        }
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueDamage(__instance, dmg);
        return false;
    }

    private static void Postfix(CrateScript __instance, CrateDebrisCapture __state)
    {
        if (MultiplayerSession.IsConnected && MultiplayerSession.IsHost)
        {
            WorldReplication.TrackDroppedWeapons();
            if (__state == null || GunsawMultiplayerPlugin.World == null) return;
            var created = new List<Rigidbody2D>();
            foreach (var body in UnityEngine.Object.FindObjectsOfType<Rigidbody2D>())
                if (body != null && !__state.ExistingBodies.Contains(body.GetInstanceID())) created.Add(body);
            GunsawMultiplayerPlugin.World.RegisterDestroyedCrateDebris(__instance, created.ToArray());
        }
    }
}

[HarmonyPatch(typeof(CrateScript), "OnWillRenderObject")]
internal static class ClientPalletDebrisAutoBreakPatch
{
    private static bool Prefix(CrateScript __instance)
    {
        if (!MultiplayerSession.IsConnected || GunsawMultiplayerPlugin.World == null) return true;
        return !GunsawMultiplayerPlugin.World.IsNetworkCrateDebris(__instance);
    }
}

[HarmonyPatch(typeof(ButtonScript), "Activated")]
internal static class MultiplayerWorldButtonPatch
{
    private static bool Prefix(ButtonScript __instance)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueButtonActivation(__instance);
        return false;
    }

    private static void Postfix(ButtonScript __instance)
    {
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.NotifyButtonActivated(__instance);
    }
}

[HarmonyPatch(typeof(BodyScript), "Damaged")]
internal static class ClientNpcDamagePatch
{
    private static bool Prefix(BodyScript __instance, bool isCrit,
        out NetworkAvatarReplication.TargetScreenEffectState __state)
    {
        __state = NetworkAvatarReplication.BeginTargetScreenEffect(__instance);
        NetworkAvatarReplication.RecordDamageSource(__instance);
        if (NetworkAvatarReplication.HandleHostRemoteDamaged(__instance, isCrit)) return false;
        return !NpcReplication.HandleClientDamaged(__instance, isCrit);
    }

    private static Exception Finalizer(Exception __exception,
        NetworkAvatarReplication.TargetScreenEffectState __state)
    {
        NetworkAvatarReplication.EndTargetScreenEffect(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(BodyScript), "Death")]
internal static class ClientNpcDeathPatch
{
    private static bool Prefix(BodyScript __instance)
    {
        if (MultiplayerSession.IsConnected && __instance != null && __instance.isPlayer)
            __instance.dropWeapon = false;
        if (NetworkAvatarReplication.BlockLocalRespawnDeath(__instance)) return false;
        if (NetworkAvatarReplication.HandleHostRemoteDeath(__instance)) return false;
        NpcReplication.PrepareAuthoritativeNpcDeath(__instance);
        if (NpcReplication.HandleClientDeath(__instance)) return false;
        NetworkAvatarReplication.RouteNpcKillScreenEffect(__instance);
        return true;
    }

    private static void Postfix(BodyScript __instance)
    {
        NetworkAvatarReplication.EndNpcKillScreenEffect(__instance);
        Announce(__instance);
    }

    private static Exception Finalizer(BodyScript __instance, Exception __exception)
    {
        NetworkAvatarReplication.EndNpcKillScreenEffect(__instance);
        return __exception;
    }

    internal static void Announce(BodyScript __instance)
    {
        if (!MultiplayerSession.IsHosting || __instance == null ||
            NetworkAvatarReplication.IsCreatingRemoteAvatar() ||
            NetworkAvatarReplication.IsRemoteReplicaBody(__instance) ||
            (!__instance.isPlayer && !NpcReplication.IsHostNpc(__instance)) ||
            !NetworkAvatarReplication.BeginDeathAnnouncement(__instance)) return;
        var victimName = DeathDisplayName(__instance);
        var killer = NetworkAvatarReplication.DamageSourceFor(__instance);
        var message = killer == null
            ? victimName + " died."
            : DeathDisplayName(killer) + " killed " + victimName + ".";
        MultiplayerHud.AddSystemMessage(message);
        ChatPacket packet;
        if (ChatService.TryCreate(message, true, out packet)) MultiplayerSession.Send(packet);
    }

    private static string DeathDisplayName(BodyScript body)
    {
        if (body == null) return "Environment";
        if (body.isPlayer)
        {
            var localPlayer = PlayerScript.player;
            if (localPlayer != null && body == localPlayer.bodyScript)
                return MultiplayerSession.LocalPlayerName;
            var remoteName = NetworkAvatarReplication.RemoteNameForBody(body);
            return string.IsNullOrEmpty(remoteName) ? "Player" : remoteName;
        }
        var characterName = body.characterName;
        if (!string.IsNullOrWhiteSpace(characterName)) return characterName.Trim();
        var objectName = body.gameObject == null ? "Bot" : body.gameObject.name;
        objectName = objectName.Replace("(Clone)", "").Trim();
        return string.IsNullOrEmpty(objectName) ? "Bot" : objectName;
    }
}

[HarmonyPatch(typeof(BodyScript), "DropWeapon")]
internal static class ClientNpcDropWeaponPatch
{
    private static bool Prefix(BodyScript __instance, out bool __state)
    {
        __state = __instance != null && __instance.dropWeapon && !__instance.unarmed;
        if (NetworkAvatarReplication.BlockNetworkPlayerDrop(__instance, false)) return false;
        return !NpcReplication.BlockClientWeaponDrop(__instance);
    }

    private static void Postfix(BodyScript __instance, bool __state)
    {
        if (__state) NetworkAvatarReplication.ConsumeLocalDeathWeapon(__instance, false);
        WorldReplication.TrackDroppedWeapons();
    }
}

[HarmonyPatch(typeof(BodyScript), "DropWeaponSingle")]
internal static class ClientNpcDropWeaponSinglePatch
{
    private static bool Prefix(BodyScript __instance)
    {
        if (NetworkAvatarReplication.BlockNetworkPlayerDrop(__instance, false)) return false;
        return !NpcReplication.BlockClientWeaponDrop(__instance);
    }

    private static void Postfix()
    {
        WorldReplication.TrackDroppedWeapons();
    }
}

[HarmonyPatch(typeof(BodyScript), "DropAllWeapons")]
internal static class ClientNpcDropAllWeaponsPatch
{
    private static bool Prefix(BodyScript __instance)
    {
        if (NetworkAvatarReplication.BlockNetworkPlayerDrop(__instance, true)) return false;
        return !NpcReplication.BlockClientWeaponDrop(__instance);
    }

    private static void Postfix()
    {
        WorldReplication.TrackDroppedWeapons();
    }
}

[HarmonyPatch(typeof(LimbScript), "OnCollisionEnter2D")]
internal static class ClientNpcLimbCollisionPatch
{
    private static bool Prefix(LimbScript __instance)
    {
        return __instance == null ||
            (!NpcReplication.IsClientProxy(__instance.body) &&
             !NpcReplication.IsLocallyPossessedBody(__instance.body) &&
             !NetworkAvatarReplication.IsRemoteAvatarBody(__instance.body));
    }
}

[HarmonyPatch(typeof(SawScript), "OnCollisionEnter2D")]
internal static class ClientSawCollisionEnterPatch
{
    private static bool Prefix(SawScript __instance, Collision2D collision)
    {
        return ClientSawCollisionPatch.ShouldRun(__instance, collision);
    }
}

[HarmonyPatch(typeof(SawScript), "OnCollisionStay2D")]
internal static class ClientSawCollisionStayPatch
{
    private static bool Prefix(SawScript __instance, Collision2D collision)
    {
        return ClientSawCollisionPatch.ShouldRun(__instance, collision);
    }
}

[HarmonyPatch(typeof(BloodBars), "FixedUpdate")]
internal static class MultiplayerDamageBarsPatch
{
    private static void Prefix(BloodBars __instance)
    {
        var player = PlayerScript.player;
        if (!MultiplayerSession.IsConnected || __instance == null || player == null ||
            __instance.body != player.bodyScript) return;

        __instance.constTreshold = float.NegativeInfinity;
    }
}

internal static class ClientSawCollisionPatch
{
    internal static bool ShouldRun(SawScript saw, Collision2D collision)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost) return true;
        var player = PlayerScript.player;
        if (saw == null || collision == null || player == null || player.bodyScript == null) return false;
        var collider = collision.collider;
        var hitBody = collider == null ? null : collider.GetComponentInParent<BodyScript>();
        return hitBody == player.bodyScript;
    }
}

[HarmonyPatch(typeof(DroppedWeapon), "PickupWeapon")]
internal static class ClientDroppedWeaponPickupPatch
{
    private static void Prefix(DroppedWeapon __instance, BodyScript body)
    {
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueWeaponInteraction(__instance, body, WorldReplication.WeaponPickup);
    }
}

[HarmonyPatch(typeof(DroppedWeapon), "AmmoGet")]
internal static class ClientDroppedWeaponAmmoPatch
{
    private static void Prefix(DroppedWeapon __instance, BodyScript body)
    {
        if (!MultiplayerSession.IsHost && __instance != null) __instance.pickupCool = -1f;
        if (GunsawMultiplayerPlugin.World != null)
            GunsawMultiplayerPlugin.World.QueueWeaponInteraction(__instance, body, WorldReplication.WeaponAmmoGet);
    }
}

[HarmonyPatch(typeof(WeaponScript), "Shoot")]
internal static class MultiplayerWeaponShotPatch
{
    private static bool Prefix(WeaponScript __instance, out NetworkAvatarReplication.ShotState __state)
    {
        __state = NetworkAvatarReplication.BeginWeaponShot(__instance);
        return !MultiplayerSession.IsConnected || MultiplayerSession.IsHost ||
            __instance == null || (__instance.GetComponentInParent<NpcNetworkReplica>() == null &&
            !NpcReplication.IsClientProxy(__instance.body));
    }

    private static Exception Finalizer(Exception __exception, NetworkAvatarReplication.ShotState __state)
    {
        NetworkAvatarReplication.CompleteWeaponShot(__state, __exception == null);
        return __exception;
    }
}

[HarmonyPatch(typeof(VelvetScript), "Shoot")]
internal static class MultiplayerVelvetWebPatch
{
    private static void Postfix(VelvetScript __instance)
    {
        NetworkAvatarReplication.ReplicateVelvetWeb(__instance);
    }
}

[HarmonyPatch(typeof(TeleportZone), "Activate")]
internal static class MultiplayerTeleportZonePatch
{
    private static void Prefix(TeleportZone __instance, int idd, out List<BodyScript> __state)
    {
        NetworkAvatarReplication.ReplicateTeleportZone(__instance, idd);
        __state = NetworkAvatarReplication.SuppressRemoteTeleportEffects(__instance, idd);
    }

    private static void Postfix(List<BodyScript> __state)
    {
        NetworkAvatarReplication.RestoreRemoteTeleportEffects(__state);
    }
}

[HarmonyPatch(typeof(BodyScript), "Kick")]
internal static class MultiplayerPlayerKickPatch
{
    private static void Prefix(BodyScript __instance, out NetworkAvatarReplication.ShotState __state)
    {
        __state = NetworkAvatarReplication.BeginMeleeAttack(__instance);
    }

    private static Exception Finalizer(Exception __exception, NetworkAvatarReplication.ShotState __state)
    {
        NetworkAvatarReplication.EndMeleeAttack(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(AIScript), "FixedUpdate")]
internal static class MultiplayerNpcTargetPatch
{
    private static bool Prefix(AIScript __instance)
    {
        if (__instance == null || !MultiplayerLoadDistance.ShouldTickNpc(__instance.body)) return false;
        NetworkAvatarReplication.PrepareNpcTarget(__instance);
        return true;
    }
}

[HarmonyPatch(typeof(AIScript), "Update")]
internal static class MultiplayerAllyPvpTargetPatch
{
    private static void Prefix(AIScript __instance)
    {
        if (__instance == null) return;
        var body = __instance.body;
        var target = __instance.targetBody;
        if (!MultiplayerSession.IsConnected || !MultiplayerSession.PvpEnabled || body == null ||
            (!body.wasAlly && body.team != "goodguys") || target == null || !target.isPlayer) return;
        __instance.targetBody = null;
        __instance.alerted = false;
        __instance.seesPlayer = false;
        __instance.susness = 0f;
        __instance.timeSinceLastSeen = 50f;
        __instance.timeSinceFirstSaw = 0f;
    }
}

[HarmonyPatch(typeof(GrenadeScript), "SetBody")]
internal static class MultiplayerGrenadeOwnerPatch
{
    private static void Postfix(GrenadeScript __instance, BodyScript body)
    {
        NetworkAvatarReplication.ConfigureProjectileCollisions(__instance, body);
    }
}

[HarmonyPatch(typeof(RocketProjectile), "SetBody")]
internal static class MultiplayerRocketOwnerPatch
{
    private static void Postfix(RocketProjectile __instance, BodyScript body)
    {
        NetworkAvatarReplication.ConfigureProjectileCollisions(__instance, body);
    }
}

[HarmonyPatch(typeof(RocketProjectile), "Update")]
internal static class MultiplayerRocketUpdatePatch
{
    private static void Prefix(RocketProjectile __instance, out RocketProjectile __state)
    {
        __state = NetworkAvatarReplication.BeginRocketUpdate(__instance);
    }

    private static Exception Finalizer(Exception __exception, RocketProjectile __state)
    {
        NetworkAvatarReplication.EndRocketUpdate(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(ExplosionHandler), "CreateExplosion")]
internal static class MultiplayerExplosionPatch
{
    private static void Prefix(GameObject explosionObj, Vector2 pos, float range, float force,
        out NetworkAvatarReplication.ShotState __state)
    {
        var projectile = NetworkAvatarReplication.ResolveExplosionProjectile(explosionObj);
        __state = NetworkAvatarReplication.BeginProjectileExplosion(projectile);
        NetworkAvatarReplication.ReplicateProjectileImpact(projectile, pos);
        NetworkAvatarReplication.ReplicateExplosionImpulse(projectile, pos, range, force);
    }

    private static Exception Finalizer(Exception __exception, NetworkAvatarReplication.ShotState __state)
    {
        NetworkAvatarReplication.EndWeaponShot(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(PlayerScript), "DoBodyMouseOver")]
internal static class RemotePlayerMouseOverPatch
{
    private static bool Prefix(PlayerScript __instance, BodyScript body)
    {
        if (body == null || body.GetComponentInParent<NetworkReplica>() == null) return true;
        if (__instance != null && __instance.mouseOverText != null)
            __instance.mouseOverText.color = Color.clear;
        return false;
    }
}

[HarmonyPatch(typeof(Chatter), "AllyDied")]
internal static class NetworkChatterAllyDiedPatch
{
    private static bool Prefix(Chatter __instance)
    {
        return __instance != null && __instance.body != null && __instance.ai != null &&
            __instance.GetComponentInParent<NetworkReplica>() == null;
    }
}

[HarmonyPatch(typeof(Chatter), "Died")]
internal static class NetworkChatterDiedPatch
{
    private static bool Prefix(Chatter __instance)
    {
        return __instance != null && __instance.body != null && __instance.ai != null;
    }
}

[HarmonyPatch(typeof(SceneLoader), "LoadScene")]
internal static class HostSceneReloadNotifyPatch
{
    private static void Postfix(string scene)
    {
        if (!MultiplayerSession.IsHosting) return;
        if (scene != SceneManager.GetActiveScene().name) return;
        MultiplayerSession.NotifyHostSceneReload(scene);
    }
}
