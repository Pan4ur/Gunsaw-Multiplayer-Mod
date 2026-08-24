internal readonly struct PlayerStatePacket : INetworkPacket
{
    internal readonly float Health, Stamina, BurnIntensity, SusnessMultiplier, CharacterScale;
    internal readonly bool IsAlive, CanBeGrabbed, HasNoLegs, IsDecapitated, InventoryChanged, IncludesVisualState;
    internal readonly byte ControlState;
    internal readonly PlayerDeathCause DeathCause;
    internal readonly int WeaponSlot, WeaponAmmo;
    internal readonly ulong[] InventorySpriteIds;
    internal readonly PlayerSnapshotLineState WeaponLaser;
    internal readonly PlayerSnapshotScarfState Scarf;
    internal readonly PlayerSnapshotVisualState? VisualState;
    internal readonly bool[] LimbDismembered, LimbBurning;
    internal readonly PlayerSnapshotTailState[] TailBases, Tails;

    internal PlayerStatePacket(float health, bool isAlive, float stamina, byte controlState, bool canBeGrabbed,
        float burnIntensity, bool hasNoLegs, bool isDecapitated, int weaponSlot, int weaponAmmo,
        ulong[] inventorySpriteIds, bool inventoryChanged, PlayerSnapshotLineState weaponLaser,
        PlayerSnapshotScarfState scarf, bool includesVisualState, PlayerSnapshotVisualState? visualState,
        PlayerDeathCause deathCause, float susnessMultiplier, float characterScale, PlayerSnapshotLimbState[] limbs,
        PlayerSnapshotTailState[] tailBases, PlayerSnapshotTailState[] tails)
    {
        Health = health;
        IsAlive = isAlive;
        Stamina = stamina;
        ControlState = controlState;
        CanBeGrabbed = canBeGrabbed;
        BurnIntensity = burnIntensity;
        HasNoLegs = hasNoLegs;
        IsDecapitated = isDecapitated;
        WeaponSlot = weaponSlot;
        WeaponAmmo = weaponAmmo;
        InventorySpriteIds = inventorySpriteIds ?? new ulong[0];
        InventoryChanged = inventoryChanged;
        WeaponLaser = weaponLaser;
        Scarf = scarf;
        IncludesVisualState = includesVisualState;
        VisualState = visualState;
        DeathCause = deathCause;
        SusnessMultiplier = susnessMultiplier;
        CharacterScale = characterScale;
        LimbDismembered = new bool[limbs == null ? 0 : limbs.Length];
        LimbBurning = new bool[LimbDismembered.Length];
        for (var i = 0; i < LimbDismembered.Length; i++)
        {
            LimbDismembered[i] = limbs[i].Dismembered;
            LimbBurning[i] = limbs[i].Burning;
        }

        TailBases = tailBases ?? new PlayerSnapshotTailState[0];
        Tails = tails ?? new PlayerSnapshotTailState[0];
    }

    public PacketType Type => PacketType.PlayerState;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(Health);
        writer.WriteBoolean(IsAlive);
        writer.WriteSingle(Stamina);
        writer.WriteByte(ControlState);
        writer.WriteBoolean(CanBeGrabbed);
        writer.WriteSingle(BurnIntensity);
        writer.WriteBoolean(HasNoLegs);
        writer.WriteBoolean(IsDecapitated);
        writer.WriteInt32(WeaponSlot);
        writer.WriteInt32(WeaponAmmo);
        writer.WriteUInt16((ushort)InventorySpriteIds.Length);
        writer.WriteBoolean(InventoryChanged);
        if (InventoryChanged)
            foreach (var id in InventorySpriteIds)
                writer.WriteUInt64(id);
        PlayerSnapshotPacket.WriteLine(ref writer, WeaponLaser);
        PlayerSnapshotPacket.WriteScarf(ref writer, Scarf);
        writer.WriteBoolean(IncludesVisualState);
        if (IncludesVisualState) PlayerSnapshotPacket.WriteVisualState(ref writer, VisualState.Value);
        writer.WriteByte((byte)DeathCause);
        writer.WriteSingle(SusnessMultiplier);
        writer.WriteSingle(CharacterScale);
        writer.WriteUInt16((ushort)LimbDismembered.Length);
        for (var i = 0; i < LimbDismembered.Length; i++)
        {
            writer.WriteBoolean(LimbDismembered[i]);
            writer.WriteBoolean(LimbBurning[i]);
        }

        WriteTailVisuals(ref writer, TailBases);
        WriteTailVisuals(ref writer, Tails);
    }

    internal static PlayerStatePacket Read(ref PacketReader reader)
    {
        var health = reader.ReadSingle();
        var alive = reader.ReadBoolean();
        var stamina = reader.ReadSingle();
        var control = reader.ReadByte();
        var canBeGrabbed = reader.ReadBoolean();
        var burn = reader.ReadSingle();
        var noLegs = reader.ReadBoolean();
        var decapitated = reader.ReadBoolean();
        var weaponSlot = reader.ReadInt32();
        var weaponAmmo = reader.ReadInt32();
        var inventory = new ulong[reader.ReadUInt16()];
        var inventoryChanged = reader.ReadBoolean();
        if (inventoryChanged)
            for (var i = 0; i < inventory.Length; i++)
                inventory[i] = reader.ReadUInt64();
        var laser = PlayerSnapshotPacket.ReadLine(ref reader);
        var scarf = PlayerSnapshotPacket.ReadScarf(ref reader);
        var includesVisual = reader.ReadBoolean();
        var visual = includesVisual
            ? (PlayerSnapshotVisualState?)PlayerSnapshotPacket.ReadVisualState(ref reader)
            : null;
        var deathCause = reader.Remaining > 0 ? (PlayerDeathCause)reader.ReadByte() : PlayerDeathCause.Unknown;
        var susness = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 1f;
        var scale = reader.Remaining >= sizeof(float) ? reader.ReadSingle() : 1f;
        var limbs = new PlayerSnapshotLimbState[reader.Remaining >= sizeof(ushort) ? reader.ReadUInt16() : 0];
        for (var i = 0; i < limbs.Length; i++)
            limbs[i] = new PlayerSnapshotLimbState(default(PlayerSnapshotBodyState), reader.ReadBoolean(),
                reader.ReadBoolean());
        var tailBases = ReadTailVisuals(ref reader);
        var tails = ReadTailVisuals(ref reader);
        return new PlayerStatePacket(health, alive, stamina, control, canBeGrabbed, burn, noLegs, decapitated,
            weaponSlot, weaponAmmo, inventory, inventoryChanged, laser, scarf, includesVisual, visual,
            deathCause, susness, scale, limbs, tailBases, tails);
    }

    private static void WriteTailVisuals(ref PacketWriter writer, PlayerSnapshotTailState[] tails)
    {
        writer.WriteUInt16((ushort)tails.Length);
        foreach (var tail in tails)
        {
            var colors = tail.Colors ?? new PlayerSnapshotByteColor[0];
            writer.WriteByte((byte)colors.Length);
            foreach (var color in colors)
            {
                writer.WriteByte(color.Red);
                writer.WriteByte(color.Green);
                writer.WriteByte(color.Blue);
                writer.WriteByte(color.Alpha);
            }
        }
    }

    private static PlayerSnapshotTailState[] ReadTailVisuals(ref PacketReader reader)
    {
        var tails = new PlayerSnapshotTailState[reader.ReadUInt16()];
        for (var i = 0; i < tails.Length; i++)
        {
            var colors = new PlayerSnapshotByteColor[reader.ReadByte()];
            for (var j = 0; j < colors.Length; j++)
                colors[j] = new PlayerSnapshotByteColor(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(),
                    reader.ReadByte());
            tails[i] = new PlayerSnapshotTailState(0f, 0f, 0f, false, colors);
        }

        return tails;
    }
}
