using System.Runtime.InteropServices;

internal readonly struct WorldBodySnapshot
{
    [StructLayout(LayoutKind.Explicit)]
    private struct SingleBits
    {
        [FieldOffset(0)] internal float Single;
        [FieldOffset(0)] internal uint UInt32;
    }

    internal readonly ulong Id;
    internal readonly bool Destroyed;
    internal readonly bool IsDroppedWeapon;
    internal readonly bool IsCrate;
    internal readonly byte CratePrefabId;
    internal readonly float PositionX, PositionY, Rotation, VelocityX, VelocityY, AngularVelocity, GravityScale;
    internal readonly bool HasMechanismTarget;
    internal readonly float MechanismTargetX, MechanismTargetY;
    internal readonly int Constraints;
    internal readonly byte BodyType;
    internal readonly bool Simulated, Awake, SafetyRailing, SafetyRailingAttached, IsVehiclePart;
    internal readonly float VehiclePartHealth, VehicleHealth;
    internal readonly bool VehicleEngineDisabled, VehicleJointAttached;
    internal readonly ulong WeaponId;
    internal readonly int Ammo;

    internal WorldBodySnapshot(ulong id, bool destroyed, bool isDroppedWeapon = false, bool isCrate = false,
        byte cratePrefabId = 0, float positionX = 0f, float positionY = 0f, float rotation = 0f,
        float velocityX = 0f, float velocityY = 0f, float angularVelocity = 0f, bool hasMechanismTarget = false,
        float mechanismTargetX = 0f, float mechanismTargetY = 0f, float gravityScale = 0f,
        int constraints = 0, byte bodyType = 0, bool simulated = false, bool awake = false,
        bool safetyRailing = false, bool safetyRailingAttached = false, bool isVehiclePart = false,
        float vehiclePartHealth = 0f, float vehicleHealth = 0f, bool vehicleEngineDisabled = false,
        bool vehicleJointAttached = false, ulong weaponId = 0, int ammo = 0)
    {
        Id = id;
        Destroyed = destroyed;
        IsDroppedWeapon = isDroppedWeapon;
        IsCrate = isCrate;
        CratePrefabId = cratePrefabId;
        PositionX = positionX;
        PositionY = positionY;
        Rotation = rotation;
        VelocityX = velocityX;
        VelocityY = velocityY;
        AngularVelocity = angularVelocity;
        HasMechanismTarget = hasMechanismTarget;
        MechanismTargetX = mechanismTargetX;
        MechanismTargetY = mechanismTargetY;
        GravityScale = gravityScale;
        Constraints = constraints;
        BodyType = bodyType;
        Simulated = simulated;
        Awake = awake;
        SafetyRailing = safetyRailing;
        SafetyRailingAttached = safetyRailingAttached;
        IsVehiclePart = isVehiclePart;
        VehiclePartHealth = vehiclePartHealth;
        VehicleHealth = vehicleHealth;
        VehicleEngineDisabled = vehicleEngineDisabled;
        VehicleJointAttached = vehicleJointAttached;
        WeaponId = weaponId;
        Ammo = ammo;
    }

    internal bool WireEquals(WorldBodySnapshot other)
    {
        if (Id != other.Id || Destroyed != other.Destroyed) return false;
        if (Destroyed) return true;
        if (IsDroppedWeapon != other.IsDroppedWeapon || IsCrate != other.IsCrate ||
            HasMechanismTarget != other.HasMechanismTarget || IsVehiclePart != other.IsVehiclePart ||
            (IsCrate && CratePrefabId != other.CratePrefabId)) return false;
        if (!SameBits(PositionX, other.PositionX) || !SameBits(PositionY, other.PositionY) ||
            !SameBits(Rotation, other.Rotation) || !SameBits(VelocityX, other.VelocityX) ||
            !SameBits(VelocityY, other.VelocityY) || !SameBits(AngularVelocity, other.AngularVelocity) ||
            !SameBits(GravityScale, other.GravityScale)) return false;
        if (HasMechanismTarget && (!SameBits(MechanismTargetX, other.MechanismTargetX) ||
                                   !SameBits(MechanismTargetY, other.MechanismTargetY))) return false;
        if (Constraints != other.Constraints || BodyType != other.BodyType ||
            Simulated != other.Simulated || Awake != other.Awake ||
            SafetyRailing != other.SafetyRailing ||
            SafetyRailingAttached != other.SafetyRailingAttached) return false;
        if (IsVehiclePart && (!SameBits(VehiclePartHealth, other.VehiclePartHealth) ||
                              !SameBits(VehicleHealth, other.VehicleHealth) ||
                              VehicleEngineDisabled != other.VehicleEngineDisabled ||
                              VehicleJointAttached != other.VehicleJointAttached)) return false;
        if (IsDroppedWeapon && (WeaponId != other.WeaponId || Ammo != other.Ammo)) return false;
        return true;
    }

    private static bool SameBits(float left, float right)
    {
        return new SingleBits { Single = left }.UInt32 == new SingleBits { Single = right }.UInt32;
    }

    internal void Write(ref PacketWriter writer)
    {
        writer.WriteUInt64(Id);
        var flags = (ushort)((Destroyed ? 1 << 0 : 0) |
                             (IsDroppedWeapon ? 1 << 1 : 0) |
                             (IsCrate ? 1 << 2 : 0) |
                             (HasMechanismTarget ? 1 << 3 : 0) |
                             (Simulated ? 1 << 4 : 0) |
                             (Awake ? 1 << 5 : 0) |
                             (SafetyRailing ? 1 << 6 : 0) |
                             (SafetyRailingAttached ? 1 << 7 : 0) |
                             (IsVehiclePart ? 1 << 8 : 0) |
                             (VehicleEngineDisabled ? 1 << 9 : 0) |
                             (VehicleJointAttached ? 1 << 10 : 0) |
                             ((BodyType & 3) << 11) |
                             ((Constraints & 7) << 13));
        writer.WriteByte((byte)flags);
        if (Destroyed) return;
        writer.WriteByte((byte)(flags >> 8));
        if (IsCrate) writer.WriteByte(CratePrefabId);
        writer.WriteSingle(PositionX);
        writer.WriteSingle(PositionY);
        writer.WriteSingle(Rotation);
        writer.WriteSingle(VelocityX);
        writer.WriteSingle(VelocityY);
        writer.WriteSingle(AngularVelocity);
        if (HasMechanismTarget)
        {
            writer.WriteSingle(MechanismTargetX);
            writer.WriteSingle(MechanismTargetY);
        }
        writer.WriteSingle(GravityScale);
        if (IsVehiclePart)
        {
            writer.WriteSingle(VehiclePartHealth);
            writer.WriteSingle(VehicleHealth);
        }

        if (IsDroppedWeapon)
        {
            writer.WriteUInt64(WeaponId);
            writer.WriteInt32(Ammo);
        }
    }

    internal static WorldBodySnapshot Read(ref PacketReader reader)
    {
        var id = reader.ReadUInt64();
        var flags = (int)reader.ReadByte();
        if ((flags & (1 << 0)) != 0) return new WorldBodySnapshot(id, true);
        flags |= reader.ReadByte() << 8;
        var dropped = (flags & (1 << 1)) != 0;
        var crate = (flags & (1 << 2)) != 0;
        var cratePrefabId = crate ? reader.ReadByte() : (byte)0;
        var positionX = reader.ReadSingle();
        var positionY = reader.ReadSingle();
        var rotation = reader.ReadSingle();
        var velocityX = reader.ReadSingle();
        var velocityY = reader.ReadSingle();
        var angularVelocity = reader.ReadSingle();
        var hasMechanismTarget = (flags & (1 << 3)) != 0;
        var mechanismTargetX = hasMechanismTarget ? reader.ReadSingle() : 0f;
        var mechanismTargetY = hasMechanismTarget ? reader.ReadSingle() : 0f;
        var gravityScale = reader.ReadSingle();
        var constraints = (flags >> 13) & 7;
        var bodyType = (byte)((flags >> 11) & 3);
        var simulated = (flags & (1 << 4)) != 0;
        var awake = (flags & (1 << 5)) != 0;
        var railing = (flags & (1 << 6)) != 0;
        var railingAttached = (flags & (1 << 7)) != 0;
        var vehiclePart = (flags & (1 << 8)) != 0;
        var vehiclePartHealth = 0f;
        var vehicleHealth = 0f;
        var engineDisabled = false;
        var jointAttached = false;
        if (vehiclePart)
        {
            vehiclePartHealth = reader.ReadSingle();
            vehicleHealth = reader.ReadSingle();
            engineDisabled = (flags & (1 << 9)) != 0;
            jointAttached = (flags & (1 << 10)) != 0;
        }

        var weaponId = dropped ? reader.ReadUInt64() : 0UL;
        var ammo = dropped ? reader.ReadInt32() : 0;
        return new WorldBodySnapshot(id, false, dropped, crate, cratePrefabId, positionX, positionY, rotation,
            velocityX, velocityY, angularVelocity, hasMechanismTarget, mechanismTargetX, mechanismTargetY,
            gravityScale, constraints, bodyType, simulated, awake,
            railing, railingAttached, vehiclePart, vehiclePartHealth, vehicleHealth, engineDisabled,
            jointAttached, weaponId, ammo);
    }
}

internal readonly struct WorldSnapshotPacket : INetworkPacket
{
    internal readonly int SceneEpoch;
    internal readonly int Sequence;
    internal readonly WorldBodySnapshot[] Bodies;

    internal WorldSnapshotPacket(int sceneEpoch, int sequence, WorldBodySnapshot[] bodies)
    {
        SceneEpoch = sceneEpoch;
        Sequence = sequence;
        Bodies = bodies ?? [];
    }

    public PacketType Type => PacketType.WorldSnapshot;

    internal bool ContentEquals(WorldSnapshotPacket other)
    {
        if (Bodies.Length != other.Bodies.Length) return false;
        for (var index = 0; index < Bodies.Length; index++)
            if (!Bodies[index].WireEquals(other.Bodies[index])) return false;
        return true;
    }

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(SceneEpoch);
        writer.WriteInt32(Sequence);
        writer.WriteUInt16((ushort) Math.Min(Bodies.Length, ushort.MaxValue));
        for (var index = 0; index < Bodies.Length && index < ushort.MaxValue; index++) Bodies[index].Write(ref writer);
    }

    internal static bool TryReadSequence(byte[] data, out int sequence)
    {
        sequence = 0;
        if (data == null || data.Length < sizeof(int) * 2) return false;
        var reader = new PacketReader(data);
        ReadHeader(ref reader, out _, out sequence);
        return true;
    }

    internal static WorldSnapshotPacket Read(ref PacketReader reader)
    {
        ReadHeader(ref reader, out var sceneEpoch, out var sequence);
        return ReadBodies(ref reader, sceneEpoch, sequence);
    }

    internal static void ReadHeader(ref PacketReader reader, out int sceneEpoch, out int sequence)
    {
        sceneEpoch = reader.ReadInt32();
        sequence = reader.ReadInt32();
    }

    internal static WorldSnapshotPacket ReadBodies(ref PacketReader reader, int sceneEpoch, int sequence)
    {
        var count = reader.ReadUInt16();
        if (count > reader.Remaining / (sizeof(ulong) + sizeof(byte))) throw new EndOfStreamException();
        var bodies = new WorldBodySnapshot[count];
        for (var index = 0; index < bodies.Length; index++) bodies[index] = WorldBodySnapshot.Read(ref reader);
        return new WorldSnapshotPacket(sceneEpoch, sequence, bodies);
    }
}
