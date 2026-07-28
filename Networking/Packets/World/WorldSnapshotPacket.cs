internal readonly struct WorldBodySnapshot
{
    internal readonly ulong Id;
    internal readonly bool Destroyed;
    internal readonly bool IsDroppedWeapon;
    internal readonly bool IsCrate;
    internal readonly string CratePrefabName;
    internal readonly float PositionX, PositionY, Rotation, VelocityX, VelocityY, AngularVelocity, GravityScale;
    internal readonly int Constraints;
    internal readonly byte BodyType;
    internal readonly bool Simulated, Awake, SafetyRailing, SafetyRailingAttached, IsVehiclePart;
    internal readonly float VehiclePartHealth, VehicleHealth;
    internal readonly bool VehicleEngineDisabled, VehicleJointAttached;
    internal readonly ulong WeaponId;
    internal readonly int Ammo;

    internal WorldBodySnapshot(ulong id, bool destroyed, bool isDroppedWeapon = false, bool isCrate = false,
        string cratePrefabName = "", float positionX = 0f, float positionY = 0f, float rotation = 0f,
        float velocityX = 0f, float velocityY = 0f, float angularVelocity = 0f, float gravityScale = 0f,
        int constraints = 0, byte bodyType = 0, bool simulated = false, bool awake = false,
        bool safetyRailing = false, bool safetyRailingAttached = false, bool isVehiclePart = false,
        float vehiclePartHealth = 0f, float vehicleHealth = 0f, bool vehicleEngineDisabled = false,
        bool vehicleJointAttached = false, ulong weaponId = 0, int ammo = 0)
    {
        Id = id; Destroyed = destroyed; IsDroppedWeapon = isDroppedWeapon; IsCrate = isCrate;
        CratePrefabName = cratePrefabName ?? ""; PositionX = positionX; PositionY = positionY; Rotation = rotation;
        VelocityX = velocityX; VelocityY = velocityY; AngularVelocity = angularVelocity; GravityScale = gravityScale;
        Constraints = constraints; BodyType = bodyType; Simulated = simulated; Awake = awake;
        SafetyRailing = safetyRailing; SafetyRailingAttached = safetyRailingAttached; IsVehiclePart = isVehiclePart;
        VehiclePartHealth = vehiclePartHealth; VehicleHealth = vehicleHealth;
        VehicleEngineDisabled = vehicleEngineDisabled; VehicleJointAttached = vehicleJointAttached;
        WeaponId = weaponId; Ammo = ammo;
    }

    internal void Write(ref PacketWriter writer)
    {
        writer.WriteUInt64(Id); writer.WriteBoolean(Destroyed);
        if (Destroyed) return;
        writer.WriteBoolean(IsDroppedWeapon); writer.WriteBoolean(IsCrate);
        if (IsCrate) writer.WriteBinaryString(CratePrefabName);
        writer.WriteSingle(PositionX); writer.WriteSingle(PositionY); writer.WriteSingle(Rotation);
        writer.WriteSingle(VelocityX); writer.WriteSingle(VelocityY); writer.WriteSingle(AngularVelocity);
        writer.WriteSingle(GravityScale); writer.WriteInt32(Constraints); writer.WriteByte(BodyType);
        writer.WriteBoolean(Simulated); writer.WriteBoolean(Awake); writer.WriteBoolean(SafetyRailing);
        writer.WriteBoolean(SafetyRailingAttached); writer.WriteBoolean(IsVehiclePart);
        if (IsVehiclePart)
        {
            writer.WriteSingle(VehiclePartHealth); writer.WriteSingle(VehicleHealth);
            writer.WriteBoolean(VehicleEngineDisabled); writer.WriteBoolean(VehicleJointAttached);
        }
        if (IsDroppedWeapon) { writer.WriteUInt64(WeaponId); writer.WriteInt32(Ammo); }
    }

    internal static WorldBodySnapshot Read(ref PacketReader reader)
    {
        var id = reader.ReadUInt64();
        if (reader.ReadBoolean()) return new WorldBodySnapshot(id, true);
        var dropped = reader.ReadBoolean(); var crate = reader.ReadBoolean();
        var crateName = crate ? reader.ReadBinaryString() : "";
        var positionX = reader.ReadSingle(); var positionY = reader.ReadSingle(); var rotation = reader.ReadSingle();
        var velocityX = reader.ReadSingle(); var velocityY = reader.ReadSingle(); var angularVelocity = reader.ReadSingle();
        var gravityScale = reader.ReadSingle(); var constraints = reader.ReadInt32(); var bodyType = reader.ReadByte();
        var simulated = reader.ReadBoolean(); var awake = reader.ReadBoolean(); var railing = reader.ReadBoolean();
        var railingAttached = reader.ReadBoolean(); var vehiclePart = reader.ReadBoolean();
        var vehiclePartHealth = 0f; var vehicleHealth = 0f; var engineDisabled = false; var jointAttached = false;
        if (vehiclePart)
        {
            vehiclePartHealth = reader.ReadSingle(); vehicleHealth = reader.ReadSingle();
            engineDisabled = reader.ReadBoolean(); jointAttached = reader.ReadBoolean();
        }
        var weaponId = dropped ? reader.ReadUInt64() : 0UL;
        var ammo = dropped ? reader.ReadInt32() : 0;
        return new WorldBodySnapshot(id, false, dropped, crate, crateName, positionX, positionY, rotation,
            velocityX, velocityY, angularVelocity, gravityScale, constraints, bodyType, simulated, awake,
            railing, railingAttached, vehiclePart, vehiclePartHealth, vehicleHealth, engineDisabled,
            jointAttached, weaponId, ammo);
    }
}

internal readonly struct WorldSnapshotPacket : INetworkPacket
{
    internal readonly int SceneEpoch;
    internal readonly WorldBodySnapshot[] Bodies;
    internal readonly bool IncludesEnvironment;
    internal readonly WorldEnvironmentPacket Environment;

    internal WorldSnapshotPacket(int sceneEpoch, WorldBodySnapshot[] bodies, bool includesEnvironment,
        WorldEnvironmentPacket environment)
    {
        SceneEpoch = sceneEpoch; Bodies = bodies ?? new WorldBodySnapshot[0];
        IncludesEnvironment = includesEnvironment; Environment = environment;
    }

    public PacketType Type => PacketType.WorldSnapshot;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(SceneEpoch);
        writer.WriteUInt16((ushort)System.Math.Min(Bodies.Length, ushort.MaxValue));
        for (var index = 0; index < Bodies.Length && index < ushort.MaxValue; index++) Bodies[index].Write(ref writer);
        writer.WriteBoolean(IncludesEnvironment);
        if (IncludesEnvironment) Environment.Write(ref writer);
    }

    internal static WorldSnapshotPacket Read(ref PacketReader reader)
    {
        var sceneEpoch = reader.ReadInt32();
        var bodies = new WorldBodySnapshot[reader.ReadUInt16()];
        for (var index = 0; index < bodies.Length; index++) bodies[index] = WorldBodySnapshot.Read(ref reader);
        var includesEnvironment = reader.ReadBoolean();
        var environment = includesEnvironment ? WorldEnvironmentPacket.Read(ref reader) : default(WorldEnvironmentPacket);
        return new WorldSnapshotPacket(sceneEpoch, bodies, includesEnvironment, environment);
    }
}
