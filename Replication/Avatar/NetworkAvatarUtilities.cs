using System.Globalization;
using UnityEngine;

internal static class NetworkAvatarUtilities
{
internal const float SnapshotInterval = 1f / 50f;
    internal static LineRenderer AddFallbackTracer(GameObject visual)
    {
        var line = visual.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = 0.035f;
        line.endWidth = 0.018f;
        line.startColor = new Color(1f, 0.85f, 0.35f, 0.95f);
        line.endColor = new Color(1f, 0.45f, 0.1f, 0.75f);
        if (fallbackTracerMaterial == null)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) fallbackTracerMaterial = new Material(shader);
        }
        if (fallbackTracerMaterial != null) line.sharedMaterial = fallbackTracerMaterial;
        line.sortingOrder = 100;
        return line;
    }

    private static Material fallbackTracerMaterial;
    internal static void HideChildrenOfDisabledHeadAccessories(Transform root)
    {
        if (root == null) return;
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer == null || !renderer.enabled) continue;
            var ancestor = renderer.transform.parent;
            while (ancestor != null && ancestor != root)
            {
                var ancestorRenderer = ancestor.GetComponent<SpriteRenderer>();
                if (ancestorRenderer != null && !ancestorRenderer.enabled &&
                    ancestor.parent != null && ancestor.parent.name == "Head")
                {
                    renderer.enabled = false;
                    break;
                }
                ancestor = ancestor.parent;
            }
        }
    }

    internal static void InitializeSeasonalHats(GameObject avatar)
    {
        if (avatar == null) return;
        foreach (var hat in avatar.GetComponentsInChildren<SantaHatScript>(true))
        {
            if (DateTime.Now.Month != hat.month || PlayerPrefs.GetInt("seasonalHats") != 1)
            {
                UnityEngine.Object.DestroyImmediate(hat.gameObject);
                continue;
            }

            var renderer = hat.GetComponent<SpriteRenderer>();

            if (renderer != null)
                renderer.enabled = true;

            if (hat.transform.childCount == 0)
                continue;

            var pompom = hat.transform.GetChild(0).GetComponent<SpriteRenderer>();
            if (pompom != null)
                pompom.enabled = true;
        }
    }

    internal static void RemoveReplicaScarfArtifacts(GameObject avatar)
    {
        if (avatar == null) return;
        foreach (var scarf in avatar.GetComponentsInChildren<ScarfPhysics>(true))
            if (scarf != null) UnityEngine.Object.DestroyImmediate(scarf.gameObject);
        foreach (var renderer in avatar.GetComponentsInChildren<SpriteRenderer>(true))
            if (renderer != null && renderer.gameObject.name == "ScarfHold") UnityEngine.Object.DestroyImmediate(renderer.gameObject);
    }

    internal static void ApplyWeaponVisual(BodyScript body, ulong spriteName, int activeSlot, ulong[] inventorySprites)
    {
        var active = NetworkAvatarUtilities.FindSprite(spriteName);
        var holstered = new Sprite[2];
        var holsterIndex = 0;
        for (var index = 0; index < inventorySprites.Length && holsterIndex < holstered.Length; index++)
        {
            if (index == activeSlot || inventorySprites[index] == 0UL) continue;
            holstered[holsterIndex++] = NetworkAvatarUtilities.FindSprite(inventorySprites[index]);
        }
        foreach (var renderer in body.transform.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer.name == "testGun") renderer.sprite = active;
            else if (renderer.name == "BackWep1") renderer.sprite = holstered[0];
            else if (renderer.name == "BackWep2") renderer.sprite = holstered[1];
        }
    }

    internal static Rigidbody2D ResolveLocalPart(
        BodyScript body,
        byte kind,
        int index)
    {
        if (body == null)
            return null;

        if (kind == 0)
            return body.rb;

        if (kind == 1)
        {
            var limbs = body.limbs ?? [];

            if (index < 0 || index >= limbs.Count)
                return null;

            return limbs[index].rb;
        }

        if (kind == 2)
        {
            var tails = GetNetworkTailBodies(body);

            if (index < 0 || index >= tails.Count)
                return null;

            return tails[index];
        }

        return null;
    }

    internal static void ReadLocalRotationImmediately(
        BinaryReader reader,
        Transform transform)
    {
        reader.ReadSingle();
        reader.ReadSingle();

        var rotation = ReadQuantizedRotation(reader);

        if (transform == null)
            return;

        transform.localRotation =
            Quaternion.Euler(0f, 0f, rotation);
    }

    internal static void ReadVehicleRoot(BinaryReader reader, out Vector2 position, out float rotation)
    {
        position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        rotation = ReadQuantizedRotation(reader);
    }

    internal static float ReadQuantizedRotation(BinaryReader reader) => reader.ReadUInt16() * (360f / 65535f);

    internal static void SetRemoteLineTarget(LineRenderer line, GameObject container, PlayerSnapshotLineState state,
        RemoteLineInterpolation interpolation)
    {
        if (line == null) return;
        if (!state.Visible)
        {
            line.enabled = false;
            if (container != null) container.SetActive(false);
            interpolation.Active = false;
            return;
        }
        if (container != null) container.SetActive(true);
        line.gameObject.SetActive(true);
        line.enabled = true;
        line.useWorldSpace = state.UsesWorldSpace;
        line.startColor = new Color(state.StartColor.Red, state.StartColor.Green, state.StartColor.Blue, state.StartColor.Alpha);
        line.endColor = new Color(state.EndColor.Red, state.EndColor.Green, state.EndColor.Blue, state.EndColor.Alpha);
        line.startWidth = state.StartWidth;
        line.endWidth = state.EndWidth;
        var count = state.Points == null ? 0 : state.Points.Length;
        var target = new Vector3[count];
        for (var i = 0; i < count; i++) target[i] = new Vector3(state.Points[i].X, state.Points[i].Y, state.Points[i].Z);
        if (!interpolation.Active || line.positionCount != count)
        {
            line.positionCount = count;
            line.SetPositions(target);
            interpolation.From = target;
        }
        else
        {
            interpolation.From = new Vector3[count];
            line.GetPositions(interpolation.From);
        }
        interpolation.To = target;
        interpolation.StartedAt = Time.unscaledTime;
        interpolation.Active = true;
    }

    internal static void UpdateRemoteLineInterpolation(LineRenderer line, RemoteLineInterpolation interpolation)
    {
        if (line == null || !interpolation.Active || interpolation.To == null || interpolation.From == null) return;
        var progress = Mathf.Clamp01((Time.unscaledTime - interpolation.StartedAt) / SnapshotInterval);
        for (var i = 0; i < interpolation.To.Length; i++)
            line.SetPosition(i, Vector3.Lerp(interpolation.From[i], interpolation.To[i], progress));
    }

    internal sealed class RemoteLineInterpolation
    {
        internal Vector3[] From;
        internal Vector3[] To;
        internal float StartedAt;
        internal bool Active;
    }

    internal static bool IsBurning(LimbScript limb)
    {
        foreach (var fire in limb.GetComponentsInChildren<FireScript>(true))
            if (fire != null && fire.gameObject.activeInHierarchy && fire.GetComponentInParent<LimbScript>() == limb)
                return true;
        return false;
    }

    internal static List<Rigidbody2D> GetNetworkTailBodies(BodyScript body)
    {
        var result = new List<Rigidbody2D>();

        if (body == null || body.tails == null)
            return result;

        var added = new HashSet<Rigidbody2D>();

        foreach (var tailRoot in body.tails)
        {
            if (tailRoot == null)
                continue;

            CollectNetworkTailBodies(
                tailRoot,
                tailRoot,
                body.rb,
                result,
                added);
        }

        return result;
    }

    internal static void CollectNetworkTailBodies(
        Transform current,
        Transform tailRoot,
        Rigidbody2D bodyRigidbody,
        List<Rigidbody2D> result,
        HashSet<Rigidbody2D> added)
    {
        for (var index = 0; index < current.childCount; index++)
        {
            var child = current.GetChild(index);
            var rigidbody = child.GetComponent<Rigidbody2D>();

            if (rigidbody != null &&
                rigidbody != bodyRigidbody &&
                rigidbody.transform != tailRoot &&
                added.Add(rigidbody))
            {
                result.Add(rigidbody);
            }

            CollectNetworkTailBodies(
                child,
                tailRoot,
                bodyRigidbody,
                result,
                added);
        }
    }

    internal static byte FacialExpressionState(FacialExpression expression)
    {
        if (expression == null || expression.head == null) return 0;
        var sprite = expression.head.sprite;
        if (sprite == expression.normalFace) return 1;
        if (sprite == expression.worriedFace) return 2;
        if (sprite == expression.deadFace) return 3;
        if (sprite == expression.halfClosedFace) return 4;
        if (sprite == expression.sadFace) return 5;
        if (sprite == expression.alertFace) return 6;
        if (sprite == expression.fightFace) return 7;
        return sprite == expression.specialSprite ? (byte)8 : (byte)0;
    }

    internal static Sprite FacialExpressionSprite(FacialExpression expression, byte state)
    {
        if (expression == null) return null;
        switch (state)
        {
            case 1: return expression.normalFace;
            case 2: return expression.worriedFace;
            case 3: return expression.deadFace;
            case 4: return expression.halfClosedFace;
            case 5: return expression.sadFace;
            case 6: return expression.alertFace;
            case 7: return expression.fightFace;
            case 8: return expression.specialSprite;
            default: return null;
        }
    }

    internal static void WriteLineState(BinaryWriter writer, LineRenderer line)
    {
        var visible = line != null && line.enabled && line.gameObject.activeInHierarchy && line.positionCount > 0;
        writer.Write(visible);
        if (!visible) return;
        var count = Mathf.Min(line.positionCount, 16);
        writer.Write((byte)count);
        writer.Write(line.useWorldSpace);
        WriteColor(writer, line.startColor);
        WriteColor(writer, line.endColor);
        writer.Write(line.startWidth);
        writer.Write(line.endWidth);
        for (var index = 0; index < count; index++)
        {
            var point = line.GetPosition(index);
            writer.Write(point.x);
            writer.Write(point.y);
            writer.Write(point.z);
        }
    }

    internal static PlayerSnapshotLineState CreateWeaponLaserState(LineRenderer line)
    {
        var visible = line != null && line.enabled && line.gameObject.activeInHierarchy && line.positionCount > 0;
        if (!visible) return new PlayerSnapshotLineState(false, false, default(PlayerSnapshotColor),
            default(PlayerSnapshotColor), 0f, 0f, new PlayerSnapshotVector3[0]);

        var count = Mathf.Min(line.positionCount, 16);
        var points = new PlayerSnapshotVector3[count];
        for (var index = 0; index < count; index++)
        {
            var point = line.GetPosition(index);
            points[index] = new PlayerSnapshotVector3(point.x, point.y, point.z);
        }
        var startColor = line.startColor;
        var endColor = line.endColor;
        return new PlayerSnapshotLineState(true, line.useWorldSpace,
            new PlayerSnapshotColor(startColor.r, startColor.g, startColor.b, startColor.a),
            new PlayerSnapshotColor(endColor.r, endColor.g, endColor.b, endColor.a), line.startWidth,
            line.endWidth, points);
    }

    internal static void WriteLineState(BinaryWriter writer, PlayerSnapshotLineState state)
    {
        writer.Write(state.Visible);
        if (!state.Visible) return;
        writer.Write((byte)state.Points.Length);
        writer.Write(state.UsesWorldSpace);
        writer.Write(state.StartColor.Red); writer.Write(state.StartColor.Green);
        writer.Write(state.StartColor.Blue); writer.Write(state.StartColor.Alpha);
        writer.Write(state.EndColor.Red); writer.Write(state.EndColor.Green);
        writer.Write(state.EndColor.Blue); writer.Write(state.EndColor.Alpha);
        writer.Write(state.StartWidth); writer.Write(state.EndWidth);
        foreach (var point in state.Points)
        {
            writer.Write(point.X);
            writer.Write(point.Y);
            writer.Write(point.Z);
        }
    }

    internal static void ReadLineState(BinaryReader reader, LineRenderer line, GameObject container)
    {
        var visible = reader.ReadBoolean();
        if (!visible)
        {
            if (line != null) line.enabled = false;
            if (container != null) container.SetActive(false);
            return;
        }

        var count = reader.ReadByte();
        var useWorldSpace = reader.ReadBoolean();
        var startColor = ReadColor(reader);
        var endColor = ReadColor(reader);
        var startWidth = reader.ReadSingle();
        var endWidth = reader.ReadSingle();
        var points = new Vector3[count];
        for (var index = 0; index < count; index++)
            points[index] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        if (line == null) return;

        if (container != null) container.SetActive(true);
        line.gameObject.SetActive(true);
        line.enabled = true;
        line.useWorldSpace = useWorldSpace;
        line.startColor = startColor;
        line.endColor = endColor;
        line.startWidth = startWidth;
        line.endWidth = endWidth;
        line.positionCount = count;
        line.SetPositions(points);
    }

    internal static void WriteColor(BinaryWriter writer, Color color)
    {
        writer.Write(color.r);
        writer.Write(color.g);
        writer.Write(color.b);
        writer.Write(color.a);
    }

    internal static Color ReadColor(BinaryReader reader)
    {
        return new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    internal static void WriteVisualState(BinaryWriter writer, PlayerVisualState state)
    {
        var renderers = state == null ? new RendererVisualState[0] : state.Renderers;
        writer.Write((ushort)renderers.Length);
        for (var index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            writer.Write(renderer.Path);
            writer.Write(renderer.Visible);
            WriteColor(writer, renderer.Color);
            writer.Write(renderer.FlipX);
            writer.Write(renderer.FlipY);
        }

        var lights = state == null ? new LightVisualState[0] : state.Lights;
        writer.Write((ushort)lights.Length);
        for (var index = 0; index < lights.Length; index++)
        {
            var light = lights[index];
            writer.Write(light.Path);
            writer.Write(light.Visible);
            writer.Write(light.Intensity);
            WriteColor(writer, light.Color);
        }
        var expressions = state == null ? Array.Empty<byte>() : state.FacialExpressions;
        writer.Write((ushort)expressions.Length);
        for (var index = 0; index < expressions.Length; index++) writer.Write(expressions[index]);
    }

    internal static PlayerVisualState ReadVisualState(BinaryReader reader)
    {
        var rendererCount = reader.ReadUInt16();
        var renderers = new RendererVisualState[rendererCount];
        for (var index = 0; index < rendererCount; index++)
            renderers[index] = new RendererVisualState(reader.ReadString(), reader.ReadBoolean(), ReadColor(reader),
                reader.ReadBoolean(), reader.ReadBoolean());

        var lightCount = reader.ReadUInt16();
        var lights = new LightVisualState[lightCount];
        for (var index = 0; index < lightCount; index++)
            lights[index] = new LightVisualState(reader.ReadString(), reader.ReadBoolean(), reader.ReadSingle(),
                ReadColor(reader));
        var expressionStates = new byte[reader.ReadUInt16()];
        for (var index = 0; index < expressionStates.Length; index++) expressionStates[index] = reader.ReadByte();
        return new PlayerVisualState(renderers, lights, expressionStates);
    }

    internal static float ReadQuantizedTailOffset(BinaryReader reader) => reader.ReadInt16() / 1024f;

    internal static void ApplyTailSpriteFlip(SpriteRenderer[] sprites, bool flipped)
    {
        var sprite = sprites == null || sprites.Length == 0 ? null : sprites[0];
        if (sprite == null) return;
        var scale = sprite.transform.localScale;
        if ((scale.y < 0f) == flipped) return;
        scale.y = -scale.y;
        sprite.transform.localScale = scale;
    }

    internal static void ApplyTailSpriteColor(SpriteRenderer[] sprites, BinaryReader reader)
    {
        var count = reader.ReadByte();
        for (var index = 0; index < count; index++)
        {
            var color = (Color)new Color32(reader.ReadByte(), reader.ReadByte(),
                reader.ReadByte(), reader.ReadByte());
            if (sprites == null || index >= sprites.Length || sprites[index] == null) continue;
            sprites[index].color = color;
        }
    }

    internal static void SkipBody(BinaryReader reader)
    {
        reader.ReadSingle(); reader.ReadSingle(); reader.ReadUInt16();
    }

    internal static void SkipLimb(BinaryReader reader)
    {
        reader.ReadInt16(); reader.ReadInt16(); reader.ReadUInt16();
    }

    internal static void WriteWorldTransform(BinaryWriter writer, Transform transform)
    {
        if (transform == null) { writer.Write(0f); writer.Write(0f); writer.Write(0f); return; }
        writer.Write(transform.position.x);
        writer.Write(transform.position.y);
        writer.Write(transform.eulerAngles.z);
    }

    internal static void WriteLocalTransform(BinaryWriter writer, Transform transform)
    {
        if (transform == null) { writer.Write(0f); writer.Write(0f); writer.Write(0f); return; }
        writer.Write(transform.localPosition.x);
        writer.Write(transform.localPosition.y);
        writer.Write(transform.localEulerAngles.z);
    }

    internal static void WriteBody(BinaryWriter writer, Rigidbody2D body)
    {
        if (body == null)
        {
            writer.Write(0f); writer.Write(0f); writer.Write(0f);
            return;
        }
        writer.Write(body.position.x); writer.Write(body.position.y); writer.Write(body.rotation);
    }

    internal static void WriteTailTransform(BinaryWriter writer, Rigidbody2D reference,
        Transform transform, float rotation)
    {
        if (reference == null || transform == null)
        {
            writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(false);
            return;
        }
        var delta = (Vector2)transform.position - reference.position;
        writer.Write(delta.x);
        writer.Write(delta.y);
        writer.Write(Mathf.DeltaAngle(reference.rotation, rotation));
        var renderers = transform.GetComponentsInChildren<SpriteRenderer>(true);
        var sprite = renderers.Length == 0 ? null : renderers[0];
        writer.Write(sprite != null && sprite.transform.lossyScale.y < 0f);
        writer.Write((byte)renderers.Length);
        foreach (var renderer in renderers)
        {
            var color = (Color32)renderer.color;
            writer.Write(color.r); writer.Write(color.g); writer.Write(color.b); writer.Write(color.a);
        }
    }

    internal static PlayerSnapshotTailState CreateTailBaseState(Rigidbody2D reference, Transform transform,
        float rotation)
    {
        if (reference == null || transform == null)
            return new PlayerSnapshotTailState(0f, 0f, 0f, false, null);

        var delta = (Vector2)transform.position - reference.position;
        var renderers = transform.GetComponentsInChildren<SpriteRenderer>(true);
        var colors = new PlayerSnapshotByteColor[renderers.Length];
        for (var index = 0; index < renderers.Length; index++)
        {
            var color = (Color32)renderers[index].color;
            colors[index] = new PlayerSnapshotByteColor(color.r, color.g, color.b, color.a);
        }
        var sprite = renderers.Length == 0 ? null : renderers[0];
        return new PlayerSnapshotTailState(delta.x, delta.y, Mathf.DeltaAngle(reference.rotation, rotation),
            sprite != null && sprite.transform.localScale.y < 0f, colors);
    }

    private static List<Component> FindCharacterLights(Transform root)
    {
        var lights = new List<Component>();
        foreach (var component in root.GetComponentsInChildren<Component>(true))
            if (component != null && component.GetType().FullName == "UnityEngine.Experimental.Rendering.Universal.Light2D")
                lights.Add(component);
        return lights;
    }

    internal static VisualLayout GetVisualLayout(VisualLayout current, Transform root)
    {
        if (current != null && current.Root == root &&
            Time.unscaledTime < current.NextValidation) return current;
        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        var lights = FindCharacterLights(root);
        var layout = new VisualLayout
        {
            Root = root,
            Renderers = renderers,
            RendererPaths = new string[renderers.Length],
            Lights = lights.ToArray(),
            LightPaths = new string[lights.Count],
            RenderersByPath = new Dictionary<string, SpriteRenderer>(renderers.Length),
            LightsByPath = new Dictionary<string, Component>(lights.Count),
            NextValidation = Time.unscaledTime + 1f
        };
        for (var index = 0; index < renderers.Length; index++)
        {
            var path = RendererPath(root, renderers[index]);
            layout.RendererPaths[index] = path;
            layout.RenderersByPath[path] = renderers[index];
        }
        for (var index = 0; index < layout.Lights.Length; index++)
        {
            var path = HierarchyPath(root, layout.Lights[index].transform);
            layout.LightPaths[index] = path;
            layout.LightsByPath[path] = layout.Lights[index];
        }
        foreach (var renderer in renderers)
            if (renderer != null && renderer.name == "testGun")
            {
                layout.WeaponRenderer = renderer;
                break;
            }
        return layout;
    }

    private static string HierarchyPath(Transform root, Transform transform)
    {
        if (transform == root) return "";
        var indices = new List<int>();
        var current = transform;
        while (current != null && current != root)
        {
            indices.Add(current.GetSiblingIndex());
            current = current.parent;
        }
        indices.Reverse();
        return string.Join("/", indices);
    }

    private static string RendererPath(Transform root, SpriteRenderer renderer)
    {
        if (renderer == null) return "";
        var renderers = renderer.GetComponents<SpriteRenderer>();
        var componentIndex = 0;
        for (; componentIndex < renderers.Length; componentIndex++)
            if (renderers[componentIndex] == renderer) break;
        return HierarchyPath(root, renderer.transform) + "#" + componentIndex;
    }

    internal sealed class VisualLayout
    {
        internal Transform Root;
        internal SpriteRenderer[] Renderers;
        internal string[] RendererPaths;
        internal Component[] Lights;
        internal string[] LightPaths;
        internal Dictionary<string, SpriteRenderer> RenderersByPath;
        internal Dictionary<string, Component> LightsByPath;
        internal SpriteRenderer WeaponRenderer;
        internal float NextValidation;
    }

    internal readonly struct RendererVisualState
    {
        internal readonly string Path;
        internal readonly bool Visible;
        internal readonly Color Color;
        internal readonly bool FlipX;
        internal readonly bool FlipY;

        internal RendererVisualState(string path, bool visible, Color color, bool flipX, bool flipY)
        {
            Path = path ?? "";
            Visible = visible;
            Color = color;
            FlipX = flipX;
            FlipY = flipY;
        }
    }

    internal readonly struct LightVisualState
    {
        internal readonly string Path;
        internal readonly bool Visible;
        internal readonly float Intensity;
        internal readonly Color Color;

        internal LightVisualState(string path, bool visible, float intensity, Color color)
        {
            Path = path ?? "";
            Visible = visible;
            Intensity = intensity;
            Color = color;
        }
    }

    internal sealed class PlayerVisualState
    {
        internal readonly RendererVisualState[] Renderers;
        internal readonly LightVisualState[] Lights;
        internal readonly byte[] FacialExpressions;

        internal PlayerVisualState(RendererVisualState[] renderers, LightVisualState[] lights, byte[] facialExpressions)
        {
            Renderers = renderers ?? new RendererVisualState[0];
            Lights = lights ?? new LightVisualState[0];
            FacialExpressions = facialExpressions ?? Array.Empty<byte>();
        }

        internal static bool Equals(PlayerVisualState left, PlayerVisualState right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Renderers.Length != right.Renderers.Length ||
                left.Lights.Length != right.Lights.Length || left.FacialExpressions.Length != right.FacialExpressions.Length)
                return false;
            for (var index = 0; index < left.Renderers.Length; index++)
            {
                var a = left.Renderers[index]; var b = right.Renderers[index];
                if (a.Path != b.Path || a.Visible != b.Visible || a.Color != b.Color || a.FlipX != b.FlipX ||
                    a.FlipY != b.FlipY)
                    return false;
            }
            for (var index = 0; index < left.Lights.Length; index++)
            {
                var a = left.Lights[index]; var b = right.Lights[index];
                if (a.Path != b.Path || a.Visible != b.Visible || a.Intensity != b.Intensity || a.Color != b.Color)
                    return false;
            }
            for (var index = 0; index < left.FacialExpressions.Length; index++)
                if (left.FacialExpressions[index] != right.FacialExpressions[index]) return false;
            return true;
        }
    }

    internal struct AvatarWireBreakdown
    {
        internal int Core;
        internal int Limbs;
        internal int Rig;
        internal int Weapons;
        internal int Effects;
        internal int Visual;
    }

    internal static int TransformDepth(Transform transform)
    {
        var depth = 0;
        while (transform != null)
        {
            depth++;
            transform = transform.parent;
        }
        return depth;
    }

    internal static PlayerSnapshotScarfState CreateScarfState(BodyScript body)
    {
        var scarf = body.GetComponentInChildren<ScarfPhysics>(true);
        var visible = scarf != null && scarf.gameObject.activeInHierarchy && scarf.pointRenderer != null;
        if (!visible) return new PlayerSnapshotScarfState(false, default(PlayerSnapshotColor),
            default(PlayerSnapshotColor));
        var startColor = scarf.pointRenderer.startColor;
        var endColor = scarf.pointRenderer.endColor;
        return new PlayerSnapshotScarfState(true, new PlayerSnapshotColor(startColor.r, startColor.g, startColor.b,
            startColor.a), new PlayerSnapshotColor(endColor.r, endColor.g, endColor.b, endColor.a));
    }

    internal static void WriteScarfState(BinaryWriter writer, PlayerSnapshotScarfState state)
    {
        writer.Write(state.Visible);
        if (!state.Visible) return;
        writer.Write(state.StartColor.Red); writer.Write(state.StartColor.Green);
        writer.Write(state.StartColor.Blue); writer.Write(state.StartColor.Alpha);
        writer.Write(state.EndColor.Red); writer.Write(state.EndColor.Green);
        writer.Write(state.EndColor.Blue); writer.Write(state.EndColor.Alpha);
    }

    private static readonly Dictionary<string, Sprite> spriteCache = new();
    private static readonly Dictionary<Sprite, string> spriteIdCache = new();
    private static readonly Dictionary<Texture2D, string> textureSignatureCache = new();

    internal static string CleanCloneName(string name)
    {
        const string suffix = "(Clone)";
        if (name != null && name.EndsWith(suffix, StringComparison.Ordinal))
            return name.Substring(0, name.Length - suffix.Length).Trim();
        return name == null ? "" : name.Trim();
    }

    internal static string SanitizePlayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Player";
        name = name.Replace("<", "").Replace(">", "").Replace("\r", " ").Replace("\n", " ").Trim();
        return name.Length > 32 ? name.Substring(0, 32) : name;
    }

    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static float SpreadValue(int seed, int index)
    {
        if (seed == 0) return 0f;
        unchecked
        {
            var value = (uint)seed + 0x9E3779B9u * (uint)(index + 1);
            value ^= value >> 16;
            value *= 0x85EBCA6Bu;
            value ^= value >> 13;
            value *= 0xC2B2AE35u;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 8388607.5f - 1f;
        }
    }

    internal static string SpriteId(Sprite sprite)
    {
        if (sprite == null) return "";
        string cached;
        if (spriteIdCache.TryGetValue(sprite, out cached)) return cached;
        cached = BaseSpriteId(sprite) + "\n" + TextureSignature(sprite.texture);
        spriteIdCache[sprite] = cached;
        return cached;
    }

    private static string BaseSpriteId(Sprite sprite)
    {
        if (sprite == null) return "";
        var textureName = sprite.texture == null ? "" : sprite.texture.name;
        return sprite.name + "\n" + textureName + "\n" +
            sprite.rect.x.ToString(CultureInfo.InvariantCulture) + "," +
            sprite.rect.y.ToString(CultureInfo.InvariantCulture) + "," +
            sprite.rect.width.ToString(CultureInfo.InvariantCulture) + "," +
            sprite.rect.height.ToString(CultureInfo.InvariantCulture);
    }

    private static string TextureSignature(Texture2D texture)
    {
        if (texture == null) return "";
        string cached;
        if (textureSignatureCache.TryGetValue(texture, out cached)) return cached;
        try
        {
            var hash = 2166136261u;
            foreach (var pixel in texture.GetPixels32())
            {
                hash = unchecked((hash ^ pixel.r) * 16777619u);
                hash = unchecked((hash ^ pixel.g) * 16777619u);
                hash = unchecked((hash ^ pixel.b) * 16777619u);
                hash = unchecked((hash ^ pixel.a) * 16777619u);
            }
            cached = texture.width + "x" + texture.height + ":" + hash.ToString("X8");
        }
        catch (UnityException)
        {
            cached = "";
        }
        textureSignatureCache[texture] = cached;
        return cached;
    }

    internal static Sprite FindSprite(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Sprite cached;
        if (spriteCache.TryGetValue(id, out cached) && cached != null) return cached;
        var separator = id.LastIndexOf('\n');
        if (separator < 1 || separator == id.Length - 1) return null;
        var baseId = id.Substring(0, separator);
        var textureSignature = id.Substring(separator + 1);
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (BaseSpriteId(sprite) != baseId || TextureSignature(sprite.texture) != textureSignature) continue;
            spriteCache[id] = sprite;
            return sprite;
        }
        return null;
    }

    internal static Sprite FindSprite(ulong spriteId)
    {
        if (spriteId == 0UL) return null;
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            if (sprite != null && NetworkWireId.FromString(SpriteId(sprite)) == spriteId) return sprite;
        return null;
    }
}
