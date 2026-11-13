using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(CustJoint), "FixedUpdate")]
internal static class TailScalePatch
{
    private static bool Prefix(CustJoint __instance)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost || NpcReplication.Instance == null) return true;
        var body = __instance.transConnect == null ? null : __instance.transConnect.GetComponentInParent<BodyScript>();
        return body == null || body.isPlayer || !NpcReplication.IsClientProxy(body);
    }

    /*
    [Harmony dif]
    --- a/CustJoint.cs
    +++ b/CustJoint.cs
    @@ private void FixedUpdate()
    -    Vector2 vector = (Vector2)transConnect.TransformPoint(origOffset) - oldPos;
    -    oldPos = transConnect.TransformPoint(origOffset);
    -    Vector2 vector2 = (transConnect.TransformPoint(origOffset) - transform.position) * (updateSpeed * 1.3f);
    +    Vector2 vector = (Vector2)TailScalePatch.GetConnectionPoint(transConnect, origOffset) - oldPos;
    +    oldPos = TailScalePatch.GetConnectionPoint(transConnect, origOffset);
    +    Vector2 vector2 = (TailScalePatch.GetConnectionPoint(transConnect, origOffset) - TailScalePatch.GetTailPosition(transform)) * (updateSpeed * 1.3f);
    @@
    -    joints[0].transform.localScale = new Vector2(1f, scaleObj.localScale.x);
    +    TailScalePatch.SetTailScale(joints[0].transform, new Vector2(1f, scaleObj.localScale.x));
    */
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.PropertySetter(typeof(Transform), nameof(Transform.localScale));
        var replacement = AccessTools.Method(typeof(TailScalePatch), nameof(SetTailScale));
        var positionGetter = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));
        var physicalPosition = AccessTools.Method(typeof(TailScalePatch), nameof(GetTailPosition));
        var transformPoint = AccessTools.Method(typeof(Transform), nameof(Transform.TransformPoint), new[] { typeof(Vector3) });
        var connectionPoint = AccessTools.Method(typeof(TailScalePatch), nameof(GetConnectionPoint));
        
        foreach (var i in instructions)
        {
            if (i.Calls(original))
            {
                i.opcode = OpCodes.Call;
                i.operand = replacement;
            }
            else if (i.Calls(positionGetter))
            {
                i.opcode = OpCodes.Call;
                i.operand = physicalPosition;
            }
            else if (i.Calls(transformPoint))
            {
                i.opcode = OpCodes.Call;
                i.operand = connectionPoint;
            }
            yield return i;
        }
    }

    private static Vector3 GetTailPosition(Transform tail)
    {
        var body = tail.GetComponent<CustJoint>().transConnect.GetComponentInParent<BodyScript>();
        return body != null && !body.isPlayer && NpcReplication.IsHostNpc(body) ? tail.GetComponent<Rigidbody2D>().position : tail.position;
    }

    private static Vector3 GetConnectionPoint(Transform connection, Vector3 point)
    {
        var visualPoint = connection.TransformPoint(point);
        if (!MultiplayerSession.IsHosting) return visualPoint;
        var body = connection.GetComponentInParent<BodyScript>();
        if (body == null || body.isPlayer || !NpcReplication.IsHostNpc(body)) return visualPoint;
        var reference = connection.GetComponentInParent<Rigidbody2D>();
        if (reference == null || !reference.simulated) reference = body.rb;
        if (reference == null) return visualPoint;
        var referenceTransform = reference.transform;
        var rotation = Mathf.DeltaAngle(referenceTransform.eulerAngles.z, reference.rotation);
        return reference.position + (Vector2)(Quaternion.Euler(0f, 0f, rotation) * (visualPoint - referenceTransform.position));
    }

    internal static void SetTailScale(Transform segment, Vector3 scale)
    {
        scale.y = Mathf.Sign(scale.y);
        scale.z = segment.localScale.z;
        if (segment.localScale != scale)
            segment.localScale = scale;
    }
}

[HarmonyPatch(typeof(CustJoint), "Start")]
internal static class NpcTailInitializationPatch
{
    private static bool Prefix(Rigidbody2D ___rb) => ___rb == null;

    private static void Postfix(CustJoint __instance) => NpcReplication.OnTailInitialized(__instance);
}
