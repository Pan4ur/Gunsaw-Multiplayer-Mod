internal enum WorldInteractionOperation : byte
{
    WeaponPickup = 1,
    WeaponAmmoGet = 2,
    ButtonActivate = 3,
    DoorActivate = 4,
    ZoneActivate = 5,
    GlassDamage = 6,
    VehicleDamage = 7,
    DroneDamage = 8
}

internal readonly struct WorldInteractionPacket : INetworkPacket
{
    internal readonly WorldInteractionOperation Operation;
    internal readonly ulong TargetId;
    internal readonly int WeaponSlot;
    internal readonly ulong PreviousWeaponId;
    internal readonly int PreviousAmmo;
    internal readonly bool ClientOwnsWeapon;
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly float PositionZ;
    internal readonly float Damage;
    internal readonly bool Manual;
    internal readonly bool Collision;

    private WorldInteractionPacket(WorldInteractionOperation operation, ulong targetId, int weaponSlot = 0,
        ulong previousWeaponId = 0, int previousAmmo = 0, bool clientOwnsWeapon = false,
        float positionX = 0f, float positionY = 0f, float positionZ = 0f, float damage = 0f,
        bool manual = false, bool collision = false)
    {
        Operation = operation;
        TargetId = targetId;
        WeaponSlot = weaponSlot;
        PreviousWeaponId = previousWeaponId;
        PreviousAmmo = previousAmmo;
        ClientOwnsWeapon = clientOwnsWeapon;
        PositionX = positionX;
        PositionY = positionY;
        PositionZ = positionZ;
        Damage = damage;
        Manual = manual;
        Collision = collision;
    }

    internal static WorldInteractionPacket WeaponPickup(ulong targetId, int weaponSlot, ulong previousWeaponId,
        int previousAmmo, bool clientOwnsWeapon, float positionX, float positionY)
        => new WorldInteractionPacket(WorldInteractionOperation.WeaponPickup, targetId, weaponSlot,
            previousWeaponId, previousAmmo, clientOwnsWeapon, positionX, positionY);

    internal static WorldInteractionPacket WeaponAmmoGet(ulong targetId, int weaponSlot, ulong previousWeaponId,
        int previousAmmo, bool clientOwnsWeapon, float positionX, float positionY)
        => new WorldInteractionPacket(WorldInteractionOperation.WeaponAmmoGet, targetId, weaponSlot,
            previousWeaponId, previousAmmo, clientOwnsWeapon, positionX, positionY);

    internal static WorldInteractionPacket ButtonActivate(ulong targetId)
        => new WorldInteractionPacket(WorldInteractionOperation.ButtonActivate, targetId);

    internal static WorldInteractionPacket DoorActivate(ulong targetId)
        => new WorldInteractionPacket(WorldInteractionOperation.DoorActivate, targetId);

    internal static WorldInteractionPacket ZoneActivate(ulong targetId, bool manual)
        => new WorldInteractionPacket(WorldInteractionOperation.ZoneActivate, targetId, manual: manual);

    internal static WorldInteractionPacket GlassDamage(ulong targetId, float damage, float positionX,
        float positionY, float positionZ)
        => new WorldInteractionPacket(WorldInteractionOperation.GlassDamage, targetId, positionX: positionX,
            positionY: positionY, positionZ: positionZ, damage: damage);

    internal static WorldInteractionPacket VehicleDamage(ulong targetId, float damage, bool collision)
        => new WorldInteractionPacket(WorldInteractionOperation.VehicleDamage, targetId, damage: damage,
            collision: collision);

    internal static WorldInteractionPacket DroneDamage(ulong targetId, float damage)
        => new WorldInteractionPacket(WorldInteractionOperation.DroneDamage, targetId, damage: damage);

    public PacketType Type => PacketType.WorldInteraction;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteByte((byte)Operation);
        writer.WriteUInt64(TargetId);
        switch (Operation)
        {
            case WorldInteractionOperation.ButtonActivate:
            case WorldInteractionOperation.DoorActivate:
                return;
            case WorldInteractionOperation.ZoneActivate:
                writer.WriteBoolean(Manual);
                return;
            case WorldInteractionOperation.GlassDamage:
                writer.WriteSingle(Damage);
                writer.WriteSingle(PositionX);
                writer.WriteSingle(PositionY);
                writer.WriteSingle(PositionZ);
                return;
            case WorldInteractionOperation.VehicleDamage:
                writer.WriteSingle(Damage);
                writer.WriteBoolean(Collision);
                return;
            case WorldInteractionOperation.DroneDamage:
                writer.WriteSingle(Damage);
                return;
            default:
                writer.WriteInt32(WeaponSlot);
                writer.WriteUInt64(PreviousWeaponId);
                writer.WriteInt32(PreviousAmmo);
                writer.WriteBoolean(ClientOwnsWeapon);
                writer.WriteSingle(PositionX);
                writer.WriteSingle(PositionY);
                return;
        }
    }

    internal static WorldInteractionPacket Read(ref PacketReader reader)
    {
        var operation = (WorldInteractionOperation)reader.ReadByte();
        var targetId = reader.ReadUInt64();
        switch (operation)
        {
            case WorldInteractionOperation.ButtonActivate: return ButtonActivate(targetId);
            case WorldInteractionOperation.DoorActivate: return DoorActivate(targetId);
            case WorldInteractionOperation.ZoneActivate: return ZoneActivate(targetId, reader.Remaining > 0 && reader.ReadBoolean());
            case WorldInteractionOperation.GlassDamage:
                return GlassDamage(targetId, reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            case WorldInteractionOperation.VehicleDamage: return VehicleDamage(targetId, reader.ReadSingle(), reader.ReadBoolean());
            case WorldInteractionOperation.DroneDamage: return DroneDamage(targetId, reader.ReadSingle());
            case WorldInteractionOperation.WeaponPickup:
                return WeaponPickup(targetId, reader.ReadInt32(), reader.ReadUInt64(), reader.ReadInt32(),
                    reader.ReadBoolean(), reader.ReadSingle(), reader.ReadSingle());
            case WorldInteractionOperation.WeaponAmmoGet:
                return WeaponAmmoGet(targetId, reader.ReadInt32(), reader.ReadUInt64(), reader.ReadInt32(),
                    reader.ReadBoolean(), reader.ReadSingle(), reader.ReadSingle());
            default: throw new System.IO.InvalidDataException("Unknown world interaction operation.");
        }
    }
}
