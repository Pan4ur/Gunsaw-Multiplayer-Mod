using UnityEngine;

internal enum PlayerDeathCause : byte
{
    Unknown,
    Fire,
    Drowning,
    Suffocation,
    Fall,
    Saw,
    Acid,
    Restart,
    SelfKill,
    Explosion,
    HotPlate,
    Observer,
    Incinerator,
    Telekinesis,
    EatenByDune
}

internal readonly struct PlayerSnapshotPacket : INetworkPacket
{
    internal readonly int Sequence;
    internal readonly bool InVehicle;
    internal readonly ulong VehicleId;
    internal readonly bool IsVehicleDriver;
    internal readonly byte EntityState;
    internal readonly bool IsRight;
    internal readonly bool IsReflected;
    internal readonly bool IsActive;
    internal readonly float HeadRotation;
    internal readonly PlayerSnapshotPose Body;
    internal readonly PlayerSnapshotLimbState[] Limbs;
    internal readonly PlayerSnapshotTailState[] TailBases;
    internal readonly PlayerSnapshotTailState[] Tails;
    internal readonly PlayerSnapshotPose ArmsTransform;
    internal readonly PlayerSnapshotPose GunTransform;
    internal readonly PlayerSnapshotPose GunAnimationTransform;

    internal PlayerSnapshotPacket(int sequence, bool inVehicle, ulong vehicleId,
        bool isVehicleDriver, byte entityState, bool isRight, bool isReflected, bool isActive,
        float headRotation, PlayerSnapshotPose body,
        PlayerSnapshotPose armsTransform, PlayerSnapshotPose gunTransform,
        PlayerSnapshotPose gunAnimationTransform,
        PlayerSnapshotLimbState[] limbs, PlayerSnapshotTailState[] tailBases, PlayerSnapshotTailState[] tails)
    {
        Sequence = sequence;
        InVehicle = inVehicle;
        VehicleId = vehicleId;
        IsVehicleDriver = isVehicleDriver;
        EntityState = entityState;
        IsRight = isRight;
        IsReflected = isReflected;
        IsActive = isActive;
        HeadRotation = headRotation;
        Body = body;
        ArmsTransform = armsTransform;
        GunTransform = gunTransform;
        GunAnimationTransform = gunAnimationTransform;
        Limbs = limbs;
        TailBases = tailBases;
        Tails = tails;
    }

    public PacketType Type => PacketType.PlayerSnapshot;
    public DeliverySettings Settings => new (false, true, true);

    public void Write(ref PacketWriter writer)
    {
        WriteTypedState(ref writer);
    }

    private void WriteTypedState(ref PacketWriter writer)
    {
        writer.WriteInt32(Sequence);
        var flags = (byte)((InVehicle ? 1 : 0) | (IsVehicleDriver ? 2 : 0) | (IsRight ? 4 : 0) |
                           (IsReflected ? 8 : 0) | (IsActive ? 16 : 0));
        writer.WriteByte(flags);
        if (InVehicle) writer.WriteUInt64(VehicleId);
        writer.WriteByte(EntityState);
        WriteRotation(ref writer, HeadRotation);
        WritePose(ref writer, Body);
        writer.WriteUInt16((ushort)Limbs.Length);
        foreach (var limb in Limbs)
        {
            WriteLimb(ref writer, limb.Body, Body);
        }

        WriteTails(ref writer, TailBases);
        WriteTails(ref writer, Tails);
        WritePose(ref writer, ArmsTransform);
        WritePose(ref writer, GunTransform);
        WritePose(ref writer, GunAnimationTransform);
    }

    private static void WritePose(ref PacketWriter writer, PlayerSnapshotPose value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        WriteRotation(ref writer, value.Rotation);
    }

    private static void WriteTails(ref PacketWriter writer, PlayerSnapshotTailState[] values)
    {
        writer.WriteUInt16((ushort)values.Length);
        foreach (var value in values)
        {
            writer.WriteSingle(value.OffsetX);
            writer.WriteSingle(value.OffsetY);
            writer.WriteSingle(value.Rotation);
            writer.WriteBoolean(value.Flipped);
            var colors = value.Colors ?? new Color32[0];
            writer.WriteByte((byte)colors.Length);
            foreach (var color in colors)
            {
                writer.WriteByte(color.r); writer.WriteByte(color.g);
                writer.WriteByte(color.b); writer.WriteByte(color.a);
            }
        }
    }

    private static void WriteColor(ref PacketWriter writer, Color32 value)
    {
        writer.WriteByte(value.r);
        writer.WriteByte(value.g);
        writer.WriteByte(value.b);
        writer.WriteByte(value.a);
    }

    internal static void WriteLine(ref PacketWriter writer, PlayerSnapshotLineState value)
    {
        writer.WriteBoolean(value.Visible);
        if (!value.Visible) return;
        writer.WriteByte((byte)value.Points.Length);
        writer.WriteBoolean(value.UsesWorldSpace);
        WriteColor(ref writer, value.StartColor);
        WriteColor(ref writer, value.EndColor);
        writer.WriteSingle(value.StartWidth);
        writer.WriteSingle(value.EndWidth);
        foreach (var point in value.Points)
        {
            writer.WriteSingle(point.x);
            writer.WriteSingle(point.y);
            writer.WriteSingle(point.z);
        }
    }

    internal static void WriteScarf(ref PacketWriter writer, PlayerSnapshotScarfState value)
    {
        writer.WriteBoolean(value.Visible);
        if (value.Visible)
        {
            WriteColor(ref writer, value.StartColor);
            WriteColor(ref writer, value.EndColor);
        }
    }

    internal static void WriteVisualState(ref PacketWriter writer, PlayerSnapshotVisualState value)
    {
        var renderers = value.Renderers ?? new PlayerSnapshotRendererState[0];
        writer.WriteUInt16((ushort)renderers.Length);
        foreach (var item in renderers)
        {
            writer.WriteBinaryString(item.Path);
            writer.WriteBoolean(item.Visible);
            WriteColor(ref writer, item.Color);
            writer.WriteBoolean(item.FlipX);
            writer.WriteBoolean(item.FlipY);
        }

        var lights = value.Lights ?? new PlayerSnapshotLightState[0];
        writer.WriteUInt16((ushort)lights.Length);
        foreach (var item in lights)
        {
            writer.WriteBinaryString(item.Path);
            writer.WriteBoolean(item.Visible);
            writer.WriteSingle(item.Intensity);
            WriteColor(ref writer, item.Color);
        }

        var expressions = value.FacialExpressions ?? new byte[0];
        writer.WriteUInt16((ushort)expressions.Length);
        for (var index = 0; index < expressions.Length; index++) writer.WriteByte(expressions[index]);
    }

    internal static PlayerSnapshotPacket Read(ref PacketReader reader)
    {
        var sequence = reader.ReadInt32();
        var flags = reader.ReadByte();
        var inVehicle = (flags & 1) != 0;
        var vehicleId = inVehicle ? reader.ReadUInt64() : 0UL;
        var isVehicleDriver = (flags & 2) != 0;
        var entityState = reader.ReadByte();
        var isRight = (flags & 4) != 0;
        var isReflected = (flags & 8) != 0;
        var isActive = (flags & 16) != 0;
        var headRotation = ReadRotation(ref reader);
        var body = ReadPose(ref reader);
        var limbs = new PlayerSnapshotLimbState[reader.ReadUInt16()];
        for (var i = 0; i < limbs.Length; i++)
            limbs[i] = new PlayerSnapshotLimbState(ReadLimb(ref reader, body), false, false);
        var tailBases = ReadTails(ref reader);
        var tails = ReadTails(ref reader);
        var arms = ReadPose(ref reader);
        var gun = ReadPose(ref reader);
        var gunAnimation = ReadPose(ref reader);
        return new PlayerSnapshotPacket(sequence, inVehicle, vehicleId, isVehicleDriver, entityState, isRight,
            isReflected, isActive, headRotation, body, arms, gun, gunAnimation, limbs, tailBases, tails);
    }

    private static PlayerSnapshotPose ReadPose(ref PacketReader reader) => new (reader.ReadSingle(), reader.ReadSingle(), ReadRotation(ref reader));

    private static void WriteLimb(ref PacketWriter writer, PlayerSnapshotPose limb, PlayerSnapshotPose root)
    {
        WriteTailOffset(ref writer, limb.X - root.X);
        WriteTailOffset(ref writer, limb.Y - root.Y);
        WriteRotation(ref writer, limb.Rotation);
    }

    private static PlayerSnapshotPose ReadLimb(ref PacketReader reader, PlayerSnapshotPose root) =>
        new (root.X + ReadTailOffset(ref reader), root.Y + ReadTailOffset(ref reader), ReadRotation(ref reader));

    private static PlayerSnapshotTailState[] ReadTails(ref PacketReader reader)
    {
        var values = new PlayerSnapshotTailState[reader.ReadUInt16()];
        for (var i = 0; i < values.Length; i++)
        {
            var x = reader.ReadSingle();
            var y = reader.ReadSingle();
            var rotation = reader.ReadSingle();
            var flipped = reader.ReadBoolean();
            var colors = new Color32[reader.ReadByte()];
            for (var j = 0; j < colors.Length; j++)
            {
                var red = reader.ReadByte();
                var green = reader.ReadByte();
                var blue = reader.ReadByte();
                var alpha = reader.ReadByte();
                colors[j] = new Color32(red, green, blue, alpha);
            }
            values[i] = new PlayerSnapshotTailState(x, y, rotation, flipped, colors);
        }

        return values;
    }

    private static void WriteRotation(ref PacketWriter writer, float rotation)
    {
        var normalized = rotation % 360f;
        if (normalized < 0f) normalized += 360f;
        writer.WriteUInt16((ushort)Math.Round(normalized * (65535f / 360f)));
    }

    private static float ReadRotation(ref PacketReader reader) => reader.ReadUInt16() * (360f / 65535f);

    private static void WriteTailOffset(ref PacketWriter writer, float value) =>
        writer.WriteInt16((short)Math.Round(Math.Max(short.MinValue, Math.Min(short.MaxValue, value * 1024f))));

    private static float ReadTailOffset(ref PacketReader reader) => reader.ReadInt16() / 1024f;

    private static Color32 ReadColor(ref PacketReader reader) =>
        new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());

    internal static PlayerSnapshotLineState ReadLine(ref PacketReader reader)
    {
        var visible = reader.ReadBoolean();
        if (!visible)
            return new PlayerSnapshotLineState(false, false, default(Color32), default(Color32),
                0f, 0f, new Vector3[0]);
        var points = new Vector3[reader.ReadByte()];
        var world = reader.ReadBoolean();
        var start = ReadColor(ref reader);
        var end = ReadColor(ref reader);
        var startWidth = reader.ReadSingle();
        var endWidth = reader.ReadSingle();
        for (var i = 0; i < points.Length; i++)
            points[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return new PlayerSnapshotLineState(true, world, start, end, startWidth, endWidth, points);
    }

    internal static PlayerSnapshotScarfState ReadScarf(ref PacketReader reader)
    {
        var visible = reader.ReadBoolean();
        return visible
            ? new PlayerSnapshotScarfState(true, ReadColor(ref reader), ReadColor(ref reader))
            : new PlayerSnapshotScarfState(false, default(Color32), default(Color32));
    }

    internal static PlayerSnapshotVisualState ReadVisualState(ref PacketReader reader)
    {
        var renderers = new PlayerSnapshotRendererState[reader.ReadUInt16()];
        for (var i = 0; i < renderers.Length; i++)
            renderers[i] = new PlayerSnapshotRendererState(reader.ReadBinaryString(), reader.ReadBoolean(),
                ReadColor(ref reader), reader.ReadBoolean(), reader.ReadBoolean());
        var lights = new PlayerSnapshotLightState[reader.ReadUInt16()];
        for (var i = 0; i < lights.Length; i++)
            lights[i] = new PlayerSnapshotLightState(reader.ReadBinaryString(), reader.ReadBoolean(),
                reader.ReadSingle(), ReadColor(ref reader));
        var expressions = new byte[reader.ReadUInt16()];
        for (var i = 0; i < expressions.Length; i++) expressions[i] = reader.ReadByte();
        return new PlayerSnapshotVisualState(renderers, lights, expressions);
    }
}

internal readonly struct PlayerSnapshotPose
{
    internal readonly float X, Y, Rotation;

    internal PlayerSnapshotPose(float x, float y, float rotation)
    {
        X = x;
        Y = y;
        Rotation = rotation;
    }
}

internal readonly struct PlayerSnapshotLimbState
{
    internal readonly PlayerSnapshotPose Body;
    internal readonly bool Dismembered, Burning;

    internal PlayerSnapshotLimbState(PlayerSnapshotPose body, bool dismembered, bool burning)
    {
        Body = body;
        Dismembered = dismembered;
        Burning = burning;
    }
}

internal readonly struct PlayerSnapshotTailState
{
    internal readonly float OffsetX, OffsetY, Rotation;
    internal readonly bool Flipped;
    internal readonly Color32[] Colors;

    internal PlayerSnapshotTailState(float offsetX, float offsetY, float rotation, bool flipped, Color32[] colors)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        Rotation = rotation;
        Flipped = flipped;
        Colors = colors;
    }
}

internal readonly struct PlayerSnapshotLineState
{
    internal readonly bool Visible, UsesWorldSpace;
    internal readonly Color32 StartColor, EndColor;
    internal readonly float StartWidth, EndWidth;
    internal readonly Vector3[] Points;

    internal PlayerSnapshotLineState(bool visible, bool usesWorldSpace, Color32 startColor, Color32 endColor, float startWidth, float endWidth, Vector3[] points)
    {
        Visible = visible;
        UsesWorldSpace = usesWorldSpace;
        StartColor = startColor;
        EndColor = endColor;
        StartWidth = startWidth;
        EndWidth = endWidth;
        Points = points;
    }
}

internal readonly struct PlayerSnapshotScarfState
{
    internal readonly bool Visible;
    internal readonly Color32 StartColor, EndColor;

    internal PlayerSnapshotScarfState(bool visible, Color32 startColor, Color32 endColor)
    {
        Visible = visible;
        StartColor = startColor;
        EndColor = endColor;
    }
}

internal readonly struct PlayerSnapshotRendererState
{
    internal readonly string Path;
    internal readonly bool Visible, FlipX, FlipY;
    internal readonly Color32 Color;

    internal PlayerSnapshotRendererState(string path, bool visible, Color32 color, bool flipX, bool flipY)
    {
        Path = path;
        Visible = visible;
        Color = color;
        FlipX = flipX;
        FlipY = flipY;
    }
}

internal readonly struct PlayerSnapshotLightState
{
    internal readonly string Path;
    internal readonly bool Visible;
    internal readonly float Intensity;
    internal readonly Color32 Color;

    internal PlayerSnapshotLightState(string path, bool visible, float intensity, Color32 color)
    {
        Path = path;
        Visible = visible;
        Intensity = intensity;
        Color = color;
    }
}

internal readonly struct PlayerSnapshotVisualState
{
    internal readonly PlayerSnapshotRendererState[] Renderers;
    internal readonly PlayerSnapshotLightState[] Lights;
    internal readonly byte[] FacialExpressions;

    internal PlayerSnapshotVisualState(PlayerSnapshotRendererState[] renderers, PlayerSnapshotLightState[] lights, byte[] facialExpressions)
    {
        Renderers = renderers;
        Lights = lights;
        FacialExpressions = facialExpressions;
    }
}