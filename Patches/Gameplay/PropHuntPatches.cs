using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(CrystalTongue), nameof(CrystalTongue.Tongue))]
internal static class PropHuntCrystalTonguePatch
{
    private static bool Prefix(CrystalTongue __instance) => !PropHuntSystem.IsProp(__instance.GetComponent<BodyScript>());
}

[HarmonyPatch(typeof(WeaponScript), nameof(WeaponScript.Shoot))]
internal static class PropHuntShotPatch
{
    private static WeaponScript activeWeapon;

    private static bool Prefix(WeaponScript __instance, out WeaponScript __state)
    {
        __state = activeWeapon;
        activeWeapon = __instance;
        return PropHuntSystem.CanShoot(__instance);
    }

    private static Exception Finalizer(Exception __exception, WeaponScript __state)
    {
        activeWeapon = __state;
        return __exception;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var raycast = AccessTools.Method(typeof(Physics2D), nameof(Physics2D.RaycastAll), new[] { typeof(Vector2), typeof(Vector2), typeof(float), typeof(int) });
        var replacement = AccessTools.Method(typeof(PropHuntShotPatch), nameof(Raycast));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(raycast)) instruction.operand = replacement;
            yield return instruction;
        }
    }

    internal static RaycastHit2D[] Raycast(Vector2 origin, Vector2 direction, float distance, int mask)
    {
        if (!PropHuntSystem.Enabled) return Physics2D.RaycastAll(origin, direction, distance, mask);
        var triggers = Physics2D.queriesHitTriggers;
        RaycastHit2D[] hits;
        try
        {
            Physics2D.SyncTransforms();
            Physics2D.queriesHitTriggers = true;
            hits = Physics2D.RaycastAll(origin, direction, distance, mask);
        }
        finally
        {
            Physics2D.queriesHitTriggers = triggers;
        }
        
        hits = hits.Where(hit => !PropHuntSystem.IsProp(hit.collider.GetComponentInParent<BodyScript>()) || hit.collider.GetComponent<PropHuntHitbox>() != null).ToArray();
        if (activeWeapon == null || activeWeapon.body != PlayerScript.player?.bodyScript || !PropHuntSystem.CanShoot(activeWeapon)) 
            return hits;
        
        foreach (var hit in hits)
        {
            var collider = hit.collider;
            if (collider.isTrigger && collider.GetComponent<PropHuntHitbox>() == null) continue;
            if (collider.GetComponentInParent<BodyScript>() == activeWeapon.body || activeWeapon.ignoredCols.Contains(collider)) continue;
            if (collider.TryGetComponent<PropHuntHitbox>(out var prop))
                PropHuntSystem.ShotHit(prop.Peer, hit.distance);
            else if (IsOrdinaryProp(collider)) PropHuntSystem.ShotHit(0, hit.distance);
            break;
        }
        return hits;
    }

    private static bool IsOrdinaryProp(Collider2D collider)
    {
        var body = collider.attachedRigidbody;
        return collider.GetComponentInParent<CrateScript>() != null || (body != null &&
            (WorldBodyReplication.IsSafetyRailingBody(body) || WorldBodyReplication.IsChainlinkFenceBody(body)));
    }
}

[HarmonyPatch]
internal static class PropHuntDyingThresholdPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(BodyScript), "Update");
        yield return AccessTools.Method(typeof(BodyScript), "FixedUpdate");
        yield return AccessTools.Method(typeof(BodyScript), nameof(BodyScript.Damaged));
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var threshold = AccessTools.Field(typeof(BodyScript), nameof(BodyScript.dyingStateTreshold));
        var replacement = AccessTools.Method(typeof(PropHuntDyingThresholdPatch), nameof(Threshold));
        foreach (var instruction in instructions)
        {
            if (instruction.LoadsField(threshold))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }
            yield return instruction;
        }
    }

    internal static float Threshold(BodyScript body) => PropHuntSystem.Enabled &&
        NetworkAvatarManager.PeerIdOfBody(body) != 0 ? 0f : body.dyingStateTreshold;
}

[HarmonyPatch(typeof(MissionManager), "Update")]
internal static class PropHuntTimerPatch
{
    private static TMPro.TMP_Text timer;
    private static TMPro.TMP_Text counter;
    private static bool wasVisible;

    private static void Postfix(MissionManager __instance)
    {
        if (PropHuntSystem.Enabled)
        {
            counter = __instance.killsText;
            if (counter != null) counter.text = $"Props: {PropHuntSystem.AliveProps}/{PropHuntSystem.TotalProps}";
            var current = __instance.ingameTimeText;
            if (current == null) return;
            if (timer != current)
            {
                timer = current;
                wasVisible = timer.gameObject.activeSelf;
            }
            timer.gameObject.SetActive(true);
            timer.text = Mathf.CeilToInt(PropHuntSystem.SecondsRemaining) + "s";
        }
        else
        {
            if (timer != null)
            {
                timer.gameObject.SetActive(wasVisible);
                timer = null;
            }
            if (counter != null)
            {
                counter.text = $"Enemies: {__instance.totalEnemyCount - __instance.killAmount}/{__instance.totalEnemyCount}";
                counter = null;
            }
        }
    }
}


[HarmonyPatch(typeof(PlayerScript), "Update")]
internal static class PropHuntMovementPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(PlayerScript __instance)
    {
        if (PropHuntSystem.Enabled && __instance == PlayerScript.player)
            __instance.bodyScript?.GetComponent<PropHuntDisguise>()?.PrepareInput();
    }
}

[HarmonyPatch]
internal static class PropHuntTelekinesisRaycastPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(LevitatorScript), "TryGrab");
        yield return AccessTools.Method(typeof(LevitatorScript), "FixedUpdate");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var i in instructions)
        {
            yield return i;
            if (i.operand is MethodInfo method && method.DeclaringType == typeof(Physics2D) && method.Name == nameof(Physics2D.LinecastAll))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PropHuntTelekinesisRaycastPatch), nameof(ExcludeSelf)));
            }
        }
    }

    internal static RaycastHit2D[] ExcludeSelf(RaycastHit2D[] hits, LevitatorScript levitator)
    {
        if (!PropHuntSystem.IsProp(levitator.refBody))
            return hits;
        
        var peer = NetworkAvatarManager.PeerIdOfBody(levitator.refBody);
        return [.. hits.Where(hit => hit.collider == null || hit.collider.GetComponentInParent<PropHuntHitbox>()?.Peer != peer)];
    }
}

internal static class PropHuntGravityPatch
{
    internal static bool Tick(LevitatorScript levitator)
    {
        var target = levitator.currentlyLevitating;
        if (target == null || target.GetComponent<PropHuntHitbox>() == null) return true;
        if (!PropHuntSystem.CanUseGravity(levitator.refBody))
        {
            levitator.UnGrab();
            return false;
        }
        var end = target.transform.TransformPoint(levitator.localGrabPoint);
        foreach (var hit in Physics2D.LinecastAll(levitator.rb.position, end, levitator.levitMask))
        {
            if (hit.rigidbody == target || hit.collider.GetComponentInParent<BodyScript>() == levitator.refBody) continue;
            levitator.UnGrab();
            return false;
        }
        var force = Vector2.ClampMagnitude(levitator.point - target.position, 5f);
        levitator.rb.AddForce(-force * 100f);
        if (force.magnitude > 2f && levitator.refBody.controlState == BodyScript.RagdollState.FullControl)
            levitator.refBody.EnterHalfControl();
        if (levitator.levitSource != null)
        {
            levitator.levitSource.volume = Mathf.Clamp01(force.magnitude * 0.1f) * 0.6f;
            levitator.levitSource.pitch = 0.6f + force.magnitude * 0.086f;
        }
        return false;
    }
}
