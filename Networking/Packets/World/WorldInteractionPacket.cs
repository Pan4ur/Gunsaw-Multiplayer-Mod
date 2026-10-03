internal enum InteractionType : byte
{
    WeaponPickup = 1,
    WeaponAmmoGet = 2,
    ButtonActivate = 3,
    DoorActivate = 4,
    ZoneActivate = 5,
    VehicleDamage = 7,
    DroneDamage = 8,
    WeaponDrop = 9,
    LampBreak = 10,
    LampHistoryRequest = 11
}

internal readonly struct WorldInteractionPacket : INetworkPacket
{
    internal readonly InteractionType InteractionType;
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

    public WorldInteractionPacket(InteractionType type, ulong targetId, int weaponSlot = 0, ulong previousWeaponId = 0, int previousAmmo = 0,
        bool clientOwnsWeapon = false, float positionX = 0f, float positionY = 0f, float positionZ = 0f, float damage = 0f, bool manual = false, bool collision = false)
    {
        InteractionType = type;
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

    public PacketType Type => PacketType.WorldInteraction;
    
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteByte((byte)InteractionType);
        writer.WriteUInt64(TargetId);
        switch (InteractionType)
        {
            case InteractionType.ButtonActivate:
            case InteractionType.DoorActivate:
                return;
            case InteractionType.ZoneActivate:
                writer.WriteBoolean(Manual);
                return;
            case InteractionType.VehicleDamage:
                writer.WriteSingle(Damage);
                writer.WriteBoolean(Collision);
                return;
            case InteractionType.DroneDamage:
                writer.WriteSingle(Damage);
                return;
            case InteractionType.LampBreak:
                writer.WriteSingle(PositionX); 
                writer.WriteSingle(PositionY);
                return;
            case InteractionType.LampHistoryRequest:
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
        var operation = (InteractionType)reader.ReadByte();
        var targetId = reader.ReadUInt64();
        switch (operation)
        {
            case InteractionType.ButtonActivate: return new WorldInteractionPacket(InteractionType.ButtonActivate, targetId);
            case InteractionType.DoorActivate: return new WorldInteractionPacket(InteractionType.DoorActivate, targetId);
            case InteractionType.ZoneActivate: return new WorldInteractionPacket(InteractionType.ZoneActivate,targetId, manual: reader.ReadBoolean());
            case InteractionType.VehicleDamage: return new WorldInteractionPacket(InteractionType.VehicleDamage, targetId, damage: reader.ReadSingle(), collision: reader.ReadBoolean());
            case InteractionType.DroneDamage: return new WorldInteractionPacket(InteractionType.DroneDamage, targetId, damage: reader.ReadSingle());
            case InteractionType.LampBreak: return new WorldInteractionPacket(InteractionType.LampBreak, targetId, positionX: reader.ReadSingle(), positionY: reader.ReadSingle());
            case InteractionType.LampHistoryRequest: return new WorldInteractionPacket(InteractionType.LampHistoryRequest, 0UL);
            case InteractionType.WeaponPickup: return new WorldInteractionPacket(InteractionType.WeaponPickup, targetId, reader.ReadInt32(), reader.ReadUInt64(), reader.ReadInt32(), reader.ReadBoolean(), reader.ReadSingle(), reader.ReadSingle());
            case InteractionType.WeaponAmmoGet: return new WorldInteractionPacket(InteractionType.WeaponAmmoGet, targetId, reader.ReadInt32(), reader.ReadUInt64(), reader.ReadInt32(), reader.ReadBoolean(), reader.ReadSingle(), reader.ReadSingle());
            case InteractionType.WeaponDrop: return new WorldInteractionPacket(InteractionType.WeaponDrop, 0UL, reader.ReadInt32(), reader.ReadUInt64(), reader.ReadInt32(), reader.ReadBoolean() ? false : false, reader.ReadSingle(), reader.ReadSingle());
            
            default: throw new InvalidDataException("Unknown world interaction operation.");
        }
    }
}