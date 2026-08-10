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
    HotPlate
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
    internal readonly PlayerSnapshotTransform WeaponTransform;
    internal readonly float Health;
    internal readonly bool IsAlive;
    internal readonly PlayerDeathCause DeathCause;
    internal readonly float SusnessMultiplier;
    internal readonly float Stamina;
    internal readonly byte ControlState;
    internal readonly bool CanBeGrabbed;
    internal readonly float BurnIntensity;
    internal readonly bool HasNoLegs;
    internal readonly bool IsDecapitated;
    internal readonly int WeaponSlot;
    internal readonly int WeaponAmmo;
    internal readonly ulong WeaponSpriteId;
    internal readonly ulong[] InventorySpriteIds;
    internal readonly bool InventoryChanged;
    internal readonly PlayerSnapshotLineState WeaponLaser;
    internal readonly PlayerSnapshotLineState LevitatorLaser;
    internal readonly PlayerSnapshotLineState CrystalTongue;
    internal readonly PlayerSnapshotScarfState Scarf;
    internal readonly bool IncludesVisualState;
    internal readonly PlayerSnapshotVisualState? VisualState;

    internal PlayerSnapshotPacket(int sequence, bool inVehicle, ulong vehicleId,
        bool isVehicleDriver, byte entityState, bool isRight, bool isReflected, bool isActive,
        float headRotation, PlayerSnapshotBodyState body, float health, bool isAlive, PlayerDeathCause deathCause,
        float susnessMultiplier, float stamina,
        byte controlState, bool canBeGrabbed, float burnIntensity, bool hasNoLegs, bool isDecapitated,
        PlayerSnapshotTransform armsTransform, PlayerSnapshotTransform gunTransform,
        PlayerSnapshotTransform gunAnimationTransform, PlayerSnapshotTransform weaponTransform,
        PlayerSnapshotLimbState[] limbs, PlayerSnapshotTailState[] tailBases, PlayerSnapshotTailState[] tails,
        int weaponSlot, int weaponAmmo, ulong weaponSpriteId, ulong[] inventorySpriteIds, bool inventoryChanged,
        PlayerSnapshotScarfState scarf, PlayerSnapshotLineState weaponLaser, PlayerSnapshotLineState levitatorLaser,
        PlayerSnapshotLineState crystalTongue,
        bool includesVisualState, PlayerSnapshotVisualState? visualState)
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
        Health = health;
        IsAlive = isAlive;
        DeathCause = deathCause;
        SusnessMultiplier = susnessMultiplier;
        Stamina = stamina;
        ControlState = controlState;
        CanBeGrabbed = canBeGrabbed;
        BurnIntensity = burnIntensity;
        HasNoLegs = hasNoLegs;
        IsDecapitated = isDecapitated;
        ArmsTransform = armsTransform;
        GunTransform = gunTransform;
        GunAnimationTransform = gunAnimationTransform;
        WeaponTransform = weaponTransform;
        Limbs = limbs;
        TailBases = tailBases;
        Tails = tails;
        WeaponSlot = weaponSlot;
        WeaponAmmo = weaponAmmo;
        WeaponSpriteId = weaponSpriteId;
        InventorySpriteIds = inventorySpriteIds;
        InventoryChanged = inventoryChanged;
        Scarf = scarf;
        WeaponLaser = weaponLaser;
        LevitatorLaser = levitatorLaser;
        CrystalTongue = crystalTongue;
        IncludesVisualState = includesVisualState;
        VisualState = visualState;
    }

    public PacketType Type => PacketType.PlayerSnapshot;

    public void Write(ref PacketWriter writer)
    {
        WriteTypedState(ref writer);
    }

    private void WriteTypedState(ref PacketWriter writer)
    {
        writer.WriteInt32(Sequence);
        writer.WriteBoolean(InVehicle); writer.WriteUInt64(VehicleId); writer.WriteBoolean(IsVehicleDriver);
        writer.WriteByte(EntityState); writer.WriteBoolean(IsRight); writer.WriteBoolean(IsReflected);
        writer.WriteBoolean(IsActive); writer.WriteSingle(HeadRotation); WriteBody(ref writer, Body);
        writer.WriteUInt16((ushort)Limbs.Length);
        foreach (var limb in Limbs) { WriteBody(ref writer, limb.Body); writer.WriteBoolean(limb.Dismembered); writer.WriteBoolean(limb.Burning); }
        WriteTails(ref writer, TailBases); WriteTails(ref writer, Tails);
        WriteTransform(ref writer, ArmsTransform); WriteTransform(ref writer, GunTransform);
        WriteTransform(ref writer, GunAnimationTransform); WriteTransform(ref writer, WeaponTransform);
        writer.WriteSingle(Health); writer.WriteBoolean(IsAlive); writer.WriteSingle(Stamina); writer.WriteByte(ControlState);
        writer.WriteBoolean(CanBeGrabbed); writer.WriteSingle(BurnIntensity); writer.WriteBoolean(HasNoLegs);
        writer.WriteBoolean(IsDecapitated); writer.WriteInt32(WeaponSlot); writer.WriteInt32(WeaponAmmo);
        writer.WriteUInt64(WeaponSpriteId); writer.WriteUInt16((ushort)InventorySpriteIds.Length);
        writer.WriteBoolean(InventoryChanged);
        if (InventoryChanged) foreach (var id in InventorySpriteIds) writer.WriteUInt64(id);
        WriteLine(ref writer, WeaponLaser); WriteLine(ref writer, LevitatorLaser); WriteScarf(ref writer, Scarf);
        WriteLine(ref writer, CrystalTongue);
        writer.WriteBoolean(IncludesVisualState);
        if (IncludesVisualState) WriteVisualState(ref writer, VisualState.Value);
        writer.WriteByte((byte)DeathCause);
        writer.WriteSingle(SusnessMultiplier);
    }

    private static void WriteBody(ref PacketWriter writer, PlayerSnapshotBodyState value)
    { writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.Rotation); }
    private static void WriteTransform(ref PacketWriter writer, PlayerSnapshotTransform value)
    { writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.Rotation); }
    private static void WriteTails(ref PacketWriter writer, PlayerSnapshotTailState[] values)
    {
        writer.WriteUInt16((ushort)values.Length);
        foreach (var value in values) { writer.WriteSingle(value.OffsetX); writer.WriteSingle(value.OffsetY); writer.WriteSingle(value.Rotation); writer.WriteBoolean(value.Flipped); if (value.Colors == null) continue; writer.WriteByte((byte)value.Colors.Length); foreach (var color in value.Colors) { writer.WriteByte(color.Red); writer.WriteByte(color.Green); writer.WriteByte(color.Blue); writer.WriteByte(color.Alpha); } }
    }
    private static void WriteColor(ref PacketWriter writer, PlayerSnapshotColor value)
    { writer.WriteSingle(value.Red); writer.WriteSingle(value.Green); writer.WriteSingle(value.Blue); writer.WriteSingle(value.Alpha); }
    private static void WriteLine(ref PacketWriter writer, PlayerSnapshotLineState value)
    {
        writer.WriteBoolean(value.Visible); if (!value.Visible) return; writer.WriteByte((byte)value.Points.Length);
        writer.WriteBoolean(value.UsesWorldSpace); WriteColor(ref writer, value.StartColor); WriteColor(ref writer, value.EndColor);
        writer.WriteSingle(value.StartWidth); writer.WriteSingle(value.EndWidth);
        foreach (var point in value.Points) { writer.WriteSingle(point.X); writer.WriteSingle(point.Y); writer.WriteSingle(point.Z); }
    }
    private static void WriteScarf(ref PacketWriter writer, PlayerSnapshotScarfState value)
    { writer.WriteBoolean(value.Visible); if (value.Visible) { WriteColor(ref writer, value.StartColor); WriteColor(ref writer, value.EndColor); } }
    private static void WriteVisualState(ref PacketWriter writer, PlayerSnapshotVisualState value)
    {
        var renderers = value.Renderers ?? new PlayerSnapshotRendererState[0]; writer.WriteUInt16((ushort)renderers.Length);
        foreach (var item in renderers) { writer.WriteBinaryString(item.Path); writer.WriteBoolean(item.Visible); WriteColor(ref writer, item.Color); writer.WriteBoolean(item.FlipX); writer.WriteBoolean(item.FlipY); }
        var lights = value.Lights ?? new PlayerSnapshotLightState[0]; writer.WriteUInt16((ushort)lights.Length);
        foreach (var item in lights) { writer.WriteBinaryString(item.Path); writer.WriteBoolean(item.Visible); writer.WriteSingle(item.Intensity); WriteColor(ref writer, item.Color); }
    }

    internal static PlayerSnapshotPacket Read(ref PacketReader reader)
    {
        var sequence = reader.ReadInt32();
        var inVehicle = reader.ReadBoolean(); var vehicleId = reader.ReadUInt64(); var isVehicleDriver = reader.ReadBoolean();
        var entityState = reader.ReadByte(); var isRight = reader.ReadBoolean(); var isReflected = reader.ReadBoolean();
        var isActive = reader.ReadBoolean(); var headRotation = reader.ReadSingle(); var body = ReadBody(ref reader);
        var limbs = new PlayerSnapshotLimbState[reader.ReadUInt16()];
        for (var i = 0; i < limbs.Length; i++) limbs[i] = new PlayerSnapshotLimbState(ReadBody(ref reader), reader.ReadBoolean(), reader.ReadBoolean());
        var tailBases = ReadTails(ref reader); var tails = ReadTails(ref reader);
        var arms = ReadTransform(ref reader); var gun = ReadTransform(ref reader); var gunAnimation = ReadTransform(ref reader); var weaponTransform = ReadTransform(ref reader);
        var health = reader.ReadSingle(); var isAlive = reader.ReadBoolean(); var stamina = reader.ReadSingle(); var controlState = reader.ReadByte();
        var canBeGrabbed = reader.ReadBoolean(); var burnIntensity = reader.ReadSingle(); var hasNoLegs = reader.ReadBoolean(); var isDecapitated = reader.ReadBoolean();
        var weaponSlot = reader.ReadInt32(); var weaponAmmo = reader.ReadInt32(); var weaponSpriteId = reader.ReadUInt64();
        var inventory = new ulong[reader.ReadUInt16()]; var inventoryChanged = reader.ReadBoolean();
        if (inventoryChanged) for (var i = 0; i < inventory.Length; i++) inventory[i] = reader.ReadUInt64();
        var weaponLaser = ReadLine(ref reader); var levitatorLaser = ReadLine(ref reader); var scarf = ReadScarf(ref reader);
        var crystalTongue = ReadLine(ref reader);
        var includesVisualState = reader.ReadBoolean(); var visualState = includesVisualState ? (PlayerSnapshotVisualState?)ReadVisualState(ref reader) : null;
        var deathCause = reader.Remaining > 0 ? (PlayerDeathCause)reader.ReadByte() : PlayerDeathCause.Unknown;
        var susnessMultiplier = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 1f;
        return new PlayerSnapshotPacket(sequence, inVehicle, vehicleId, isVehicleDriver, entityState, isRight, isReflected, isActive,
            headRotation, body, health, isAlive, deathCause, susnessMultiplier, stamina, controlState, canBeGrabbed, burnIntensity, hasNoLegs, isDecapitated,
            arms, gun, gunAnimation, weaponTransform, limbs, tailBases, tails, weaponSlot, weaponAmmo, weaponSpriteId,
            inventory, inventoryChanged, scarf, weaponLaser, levitatorLaser, crystalTongue, includesVisualState, visualState);
    }

    private static PlayerSnapshotBodyState ReadBody(ref PacketReader reader) => new PlayerSnapshotBodyState(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    private static PlayerSnapshotTransform ReadTransform(ref PacketReader reader) => new PlayerSnapshotTransform(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    private static PlayerSnapshotTailState[] ReadTails(ref PacketReader reader)
    {
        var values = new PlayerSnapshotTailState[reader.ReadUInt16()];
        for (var i = 0; i < values.Length; i++) { var x = reader.ReadSingle(); var y = reader.ReadSingle(); var rotation = reader.ReadSingle(); var flipped = reader.ReadBoolean(); var colors = new PlayerSnapshotByteColor[reader.ReadByte()]; for (var j = 0; j < colors.Length; j++) colors[j] = new PlayerSnapshotByteColor(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()); values[i] = new PlayerSnapshotTailState(x, y, rotation, flipped, colors); }
        return values;
    }
    private static PlayerSnapshotColor ReadColor(ref PacketReader reader) => new PlayerSnapshotColor(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    private static PlayerSnapshotLineState ReadLine(ref PacketReader reader) { var visible = reader.ReadBoolean(); if (!visible) return new PlayerSnapshotLineState(false, false, default(PlayerSnapshotColor), default(PlayerSnapshotColor), 0f, 0f, new PlayerSnapshotVector3[0]); var points = new PlayerSnapshotVector3[reader.ReadByte()]; var world = reader.ReadBoolean(); var start = ReadColor(ref reader); var end = ReadColor(ref reader); var startWidth = reader.ReadSingle(); var endWidth = reader.ReadSingle(); for (var i = 0; i < points.Length; i++) points[i] = new PlayerSnapshotVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); return new PlayerSnapshotLineState(true, world, start, end, startWidth, endWidth, points); }
    private static PlayerSnapshotScarfState ReadScarf(ref PacketReader reader) { var visible = reader.ReadBoolean(); return visible ? new PlayerSnapshotScarfState(true, ReadColor(ref reader), ReadColor(ref reader)) : new PlayerSnapshotScarfState(false, default(PlayerSnapshotColor), default(PlayerSnapshotColor)); }
    private static PlayerSnapshotVisualState ReadVisualState(ref PacketReader reader) { var renderers = new PlayerSnapshotRendererState[reader.ReadUInt16()]; for (var i = 0; i < renderers.Length; i++) renderers[i] = new PlayerSnapshotRendererState(reader.ReadBinaryString(), reader.ReadBoolean(), ReadColor(ref reader), reader.ReadBoolean(), reader.ReadBoolean()); var lights = new PlayerSnapshotLightState[reader.ReadUInt16()]; for (var i = 0; i < lights.Length; i++) lights[i] = new PlayerSnapshotLightState(reader.ReadBinaryString(), reader.ReadBoolean(), reader.ReadSingle(), ReadColor(ref reader)); return new PlayerSnapshotVisualState(renderers, lights); }
}

internal readonly struct PlayerSnapshotBodyState
{
    internal readonly float X, Y, Rotation;
    internal PlayerSnapshotBodyState(float x, float y, float rotation) { X = x; Y = y; Rotation = rotation; }
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

    internal PlayerSnapshotVisualState(PlayerSnapshotRendererState[] renderers, PlayerSnapshotLightState[] lights)
    {
        Renderers = renderers;
        Lights = lights;
    }
}
