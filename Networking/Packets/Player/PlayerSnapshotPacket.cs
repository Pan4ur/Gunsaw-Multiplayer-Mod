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
    Incinerator
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
    internal readonly PlayerSnapshotBodyState Body;
    internal readonly PlayerSnapshotLimbState[] Limbs;
    internal readonly PlayerSnapshotTailState[] TailBases;
    internal readonly PlayerSnapshotTailState[] Tails;
    internal readonly PlayerSnapshotTransform ArmsTransform;
    internal readonly PlayerSnapshotTransform GunTransform;
    internal readonly PlayerSnapshotTransform GunAnimationTransform;

    internal PlayerSnapshotPacket(int sequence, bool inVehicle, ulong vehicleId,
        bool isVehicleDriver, byte entityState, bool isRight, bool isReflected, bool isActive,
        float headRotation, PlayerSnapshotBodyState body,
        PlayerSnapshotTransform armsTransform, PlayerSnapshotTransform gunTransform,
        PlayerSnapshotTransform gunAnimationTransform,
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
        WriteBody(ref writer, Body);
        writer.WriteUInt16((ushort)Limbs.Length);
        foreach (var limb in Limbs)
        {
            WriteLimb(ref writer, limb.Body, Body);
        }

        WriteTails(ref writer, TailBases);
        WriteTails(ref writer, Tails);
        WriteTransform(ref writer, ArmsTransform);
        WriteTransform(ref writer, GunTransform);
        WriteTransform(ref writer, GunAnimationTransform);
    }

    private static void WriteBody(ref PacketWriter writer, PlayerSnapshotBodyState value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        WriteRotation(ref writer, value.Rotation);
    }

    private static void WriteTransform(ref PacketWriter writer, PlayerSnapshotTransform value)
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
            var colors = value.Colors ?? new PlayerSnapshotByteColor[0];
            writer.WriteByte((byte)colors.Length);
            foreach (var color in colors)
            {
                writer.WriteByte(color.Red); writer.WriteByte(color.Green);
                writer.WriteByte(color.Blue); writer.WriteByte(color.Alpha);
            }
        }
    }

    private static void WriteColor(ref PacketWriter writer, PlayerSnapshotColor value)
    {
        writer.WriteSingle(value.Red);
        writer.WriteSingle(value.Green);
        writer.WriteSingle(value.Blue);
        writer.WriteSingle(value.Alpha);
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
            writer.WriteSingle(point.X);
            writer.WriteSingle(point.Y);
            writer.WriteSingle(point.Z);
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
        var body = ReadBody(ref reader);
        var limbs = new PlayerSnapshotLimbState[reader.ReadUInt16()];
        for (var i = 0; i < limbs.Length; i++)
            limbs[i] = new PlayerSnapshotLimbState(ReadLimb(ref reader, body), false, false);
        var tailBases = ReadTails(ref reader);
        var tails = ReadTails(ref reader);
        var arms = ReadTransform(ref reader);
        var gun = ReadTransform(ref reader);
        var gunAnimation = ReadTransform(ref reader);
        return new PlayerSnapshotPacket(sequence, inVehicle, vehicleId, isVehicleDriver, entityState, isRight,
            isReflected, isActive, headRotation, body, arms, gun, gunAnimation, limbs, tailBases, tails);
    }

    private static PlayerSnapshotBodyState ReadBody(ref PacketReader reader) => new (reader.ReadSingle(), reader.ReadSingle(), ReadRotation(ref reader));

    private static PlayerSnapshotTransform ReadTransform(ref PacketReader reader) => new (reader.ReadSingle(), reader.ReadSingle(), ReadRotation(ref reader));

    private static void WriteLimb(ref PacketWriter writer, PlayerSnapshotBodyState limb, PlayerSnapshotBodyState root)
    {
        WriteTailOffset(ref writer, limb.X - root.X);
        WriteTailOffset(ref writer, limb.Y - root.Y);
        WriteRotation(ref writer, limb.Rotation);
    }

    private static PlayerSnapshotBodyState ReadLimb(ref PacketReader reader, PlayerSnapshotBodyState root) =>
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
            var colors = new PlayerSnapshotByteColor[reader.ReadByte()];
            for (var j = 0; j < colors.Length; j++)
            {
                var red = reader.ReadByte();
                var green = reader.ReadByte();
                var blue = reader.ReadByte();
                var alpha = reader.ReadByte();
                colors[j] = new PlayerSnapshotByteColor(red, green, blue, alpha);
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

    private static PlayerSnapshotColor ReadColor(ref PacketReader reader) =>
        new PlayerSnapshotColor(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    internal static PlayerSnapshotLineState ReadLine(ref PacketReader reader)
    {
        var visible = reader.ReadBoolean();
        if (!visible)
            return new PlayerSnapshotLineState(false, false, default(PlayerSnapshotColor), default(PlayerSnapshotColor),
                0f, 0f, new PlayerSnapshotVector3[0]);
        var points = new PlayerSnapshotVector3[reader.ReadByte()];
        var world = reader.ReadBoolean();
        var start = ReadColor(ref reader);
        var end = ReadColor(ref reader);
        var startWidth = reader.ReadSingle();
        var endWidth = reader.ReadSingle();
        for (var i = 0; i < points.Length; i++)
            points[i] = new PlayerSnapshotVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return new PlayerSnapshotLineState(true, world, start, end, startWidth, endWidth, points);
    }

    internal static PlayerSnapshotScarfState ReadScarf(ref PacketReader reader)
    {
        var visible = reader.ReadBoolean();
        return visible
            ? new PlayerSnapshotScarfState(true, ReadColor(ref reader), ReadColor(ref reader))
            : new PlayerSnapshotScarfState(false, default(PlayerSnapshotColor), default(PlayerSnapshotColor));
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

internal readonly struct PlayerSnapshotBodyState
{
    internal readonly float X, Y, Rotation;

    internal PlayerSnapshotBodyState(float x, float y, float rotation)
    {
        X = x;
        Y = y;
        Rotation = rotation;
    }
}

internal readonly struct PlayerSnapshotLimbState
{
    internal readonly PlayerSnapshotBodyState Body;
    internal readonly bool Dismembered, Burning;

    internal PlayerSnapshotLimbState(PlayerSnapshotBodyState body, bool dismembered, bool burning)
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
    internal readonly PlayerSnapshotByteColor[] Colors;

    internal PlayerSnapshotTailState(float offsetX, float offsetY, float rotation, bool flipped,
        PlayerSnapshotByteColor[] colors)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        Rotation = rotation;
        Flipped = flipped;
        Colors = colors;
    }
}

internal readonly struct PlayerSnapshotByteColor
{
    internal readonly byte Red, Green, Blue, Alpha;

    internal PlayerSnapshotByteColor(byte red, byte green, byte blue, byte alpha)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }
}

internal readonly struct PlayerSnapshotColor
{
    internal readonly float Red, Green, Blue, Alpha;

    internal PlayerSnapshotColor(float red, float green, float blue, float alpha)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }
}

internal readonly struct PlayerSnapshotTransform
{
    internal readonly float X, Y, Rotation;

    internal PlayerSnapshotTransform(float x, float y, float rotation)
    {
        X = x;
        Y = y;
        Rotation = rotation;
    }
}

internal readonly struct PlayerSnapshotVector3
{
    internal readonly float X, Y, Z;

    internal PlayerSnapshotVector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

internal readonly struct PlayerSnapshotLineState
{
    internal readonly bool Visible, UsesWorldSpace;
    internal readonly PlayerSnapshotColor StartColor, EndColor;
    internal readonly float StartWidth, EndWidth;
    internal readonly PlayerSnapshotVector3[] Points;

    internal PlayerSnapshotLineState(bool visible, bool usesWorldSpace, PlayerSnapshotColor startColor,
        PlayerSnapshotColor endColor, float startWidth, float endWidth, PlayerSnapshotVector3[] points)
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
    internal readonly PlayerSnapshotColor StartColor, EndColor;

    internal PlayerSnapshotScarfState(bool visible, PlayerSnapshotColor startColor, PlayerSnapshotColor endColor)
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
    internal readonly PlayerSnapshotColor Color;

    internal PlayerSnapshotRendererState(string path, bool visible, PlayerSnapshotColor color, bool flipX, bool flipY)
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
    internal readonly PlayerSnapshotColor Color;

    internal PlayerSnapshotLightState(string path, bool visible, float intensity, PlayerSnapshotColor color)
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

    internal PlayerSnapshotVisualState(PlayerSnapshotRendererState[] renderers, PlayerSnapshotLightState[] lights,
        byte[] facialExpressions)
    {
        Renderers = renderers;
        Lights = lights;
        FacialExpressions = facialExpressions;
    }
}
