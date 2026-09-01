internal readonly struct EnvironmentButtonState
{
    internal readonly ulong Id;
    internal readonly bool Active;
    internal readonly uint Activations;
    internal EnvironmentButtonState(ulong id, bool active, uint activations) { Id = id; Active = active; Activations = activations; }
}

internal readonly struct EnvironmentLampPowerState
{
    internal readonly ulong Id;
    internal readonly bool Powered;
    internal EnvironmentLampPowerState(ulong id, bool powered) { Id = id; Powered = powered; }
}

internal readonly struct EnvironmentFireState
{
    internal readonly ulong Id;
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly float Rotation;
    internal readonly float Fuel;
    internal readonly bool CanIgnite;
    internal readonly float DamageMultiplier;
    internal readonly float FuelConsumptionMultiplier;
    internal EnvironmentFireState(ulong id, float positionX, float positionY, float rotation, float fuel,
        bool canIgnite, float damageMultiplier, float fuelConsumptionMultiplier)
    {
        Id = id; PositionX = positionX; PositionY = positionY; Rotation = rotation; Fuel = fuel;
        CanIgnite = canIgnite; DamageMultiplier = damageMultiplier;
        FuelConsumptionMultiplier = fuelConsumptionMultiplier;
    }
}

internal readonly struct EnvironmentAudioState
{
    internal readonly ulong Id;
    internal readonly bool IsPlaying;
    internal readonly bool Loop;
    internal readonly float Volume;
    internal readonly float Pitch;
    internal EnvironmentAudioState(ulong id, bool isPlaying, bool loop, float volume, float pitch)
    {
        Id = id; IsPlaying = isPlaying; Loop = loop; Volume = volume; Pitch = pitch;
    }
}

internal readonly struct WorldEnvironmentPacket : INetworkPacket
{
    internal readonly int SceneEpoch;
    internal readonly float GravityX;
    internal readonly float GravityY;
    internal readonly EnvironmentButtonState[] Buttons;
    internal readonly ulong[] DestroyedGlassIds;
    internal readonly EnvironmentFireState[] Fires;
    internal readonly EnvironmentAudioState[] Audio;
    internal readonly ulong[] DestroyedDroneIds;
    internal readonly float RainIntensity;
    internal readonly float SnowIntensity;
    internal readonly float FogIntensity;
    internal readonly int EnemyKills;
    internal readonly int EnemyTotal;
    internal readonly EnvironmentLampPowerState[] LampPower;

    internal WorldEnvironmentPacket(int sceneEpoch, float gravityX, float gravityY, EnvironmentButtonState[] buttons,
        ulong[] destroyedGlassIds, EnvironmentFireState[] fires,
        EnvironmentAudioState[] audio, ulong[] destroyedDroneIds, float rainIntensity, float snowIntensity,
        float fogIntensity, int enemyKills = -1, int enemyTotal = -1, EnvironmentLampPowerState[] lampPower = null)
    {
        SceneEpoch = sceneEpoch; GravityX = gravityX; GravityY = gravityY;
        Buttons = buttons ?? new EnvironmentButtonState[0];
        DestroyedGlassIds = destroyedGlassIds ?? new ulong[0];
        Fires = fires ?? new EnvironmentFireState[0];
        Audio = audio ?? new EnvironmentAudioState[0];
        DestroyedDroneIds = destroyedDroneIds ?? new ulong[0];
        RainIntensity = rainIntensity; SnowIntensity = snowIntensity; FogIntensity = fogIntensity;
        EnemyKills = enemyKills; EnemyTotal = enemyTotal;
        LampPower = lampPower ?? new EnvironmentLampPowerState[0];
    }

    internal WorldEnvironmentPacket(byte[] payload)
    {
        var reader = new PacketReader(payload);
        this = Read(ref reader);
    }
    public PacketType Type => PacketType.WorldEnvironment;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(SceneEpoch); writer.WriteSingle(GravityX); writer.WriteSingle(GravityY);
        WriteButtons(ref writer, Buttons); WriteIds(ref writer, DestroyedGlassIds);
        WriteFires(ref writer, Fires); WriteAudio(ref writer, Audio); WriteIds(ref writer, DestroyedDroneIds);
        writer.WriteSingle(RainIntensity); writer.WriteSingle(SnowIntensity); writer.WriteSingle(FogIntensity);
        writer.WriteInt32(EnemyKills); writer.WriteInt32(EnemyTotal);
        WriteLampPower(ref writer, LampPower);
    }

    internal static WorldEnvironmentPacket Read(ref PacketReader reader)
    {
        var sceneEpoch = reader.ReadInt32();
        var gravityX = reader.ReadSingle(); var gravityY = reader.ReadSingle();
        var buttons = ReadButtons(ref reader); var glass = ReadIds(ref reader);
        var fires = ReadFires(ref reader); var audio = ReadAudio(ref reader); var drones = ReadIds(ref reader);
        var rain = reader.ReadSingle(); var snow = reader.ReadSingle(); var fog = reader.ReadSingle();
        var enemyKills = reader.Remaining >= sizeof(int) * 2 ? reader.ReadInt32() : -1;
        var enemyTotal = reader.Remaining >= sizeof(int) ? reader.ReadInt32() : -1;
        var lampPower = reader.Remaining >= sizeof(ushort) ? ReadLampPower(ref reader) : new EnvironmentLampPowerState[0];
        return new WorldEnvironmentPacket(sceneEpoch, gravityX, gravityY, buttons, glass, fires, audio, drones,
            rain, snow, fog, enemyKills, enemyTotal, lampPower);
    }

    private static void WriteButtons(ref PacketWriter writer, EnvironmentButtonState[] values)
    {
        writer.WriteUInt16((ushort)System.Math.Min(values.Length, ushort.MaxValue));
        for (var index = 0; index < values.Length && index < ushort.MaxValue; index++)
        { writer.WriteUInt64(values[index].Id); writer.WriteBoolean(values[index].Active); writer.WriteUInt32(values[index].Activations); }
    }

    private static EnvironmentButtonState[] ReadButtons(ref PacketReader reader)
    {
        var values = new EnvironmentButtonState[reader.ReadUInt16()];
        for (var index = 0; index < values.Length; index++)
            values[index] = new EnvironmentButtonState(reader.ReadUInt64(), reader.ReadBoolean(), reader.ReadUInt32());
        return values;
    }

    private static void WriteIds(ref PacketWriter writer, ulong[] values)
    {
        writer.WriteUInt16((ushort)System.Math.Min(values.Length, ushort.MaxValue));
        for (var index = 0; index < values.Length && index < ushort.MaxValue; index++) writer.WriteUInt64(values[index]);
    }

    private static ulong[] ReadIds(ref PacketReader reader)
    {
        var values = new ulong[reader.ReadUInt16()];
        for (var index = 0; index < values.Length; index++) values[index] = reader.ReadUInt64();
        return values;
    }

    private static void WriteLampPower(ref PacketWriter writer, EnvironmentLampPowerState[] values)
    {
        writer.WriteUInt16((ushort)System.Math.Min(values.Length, ushort.MaxValue));
        for (var index = 0; index < values.Length && index < ushort.MaxValue; index++)
        {
            writer.WriteUInt64(values[index].Id);
            writer.WriteBoolean(values[index].Powered);
        }
    }

    private static EnvironmentLampPowerState[] ReadLampPower(ref PacketReader reader)
    {
        var values = new EnvironmentLampPowerState[reader.ReadUInt16()];
        for (var index = 0; index < values.Length; index++)
            values[index] = new EnvironmentLampPowerState(reader.ReadUInt64(), reader.ReadBoolean());
        return values;
    }

    private static void WriteFires(ref PacketWriter writer, EnvironmentFireState[] values)
    {
        writer.WriteUInt16((ushort)System.Math.Min(values.Length, ushort.MaxValue));
        for (var index = 0; index < values.Length && index < ushort.MaxValue; index++)
        {
            var value = values[index]; writer.WriteUInt64(value.Id); writer.WriteSingle(value.PositionX); writer.WriteSingle(value.PositionY);
            writer.WriteSingle(value.Rotation); writer.WriteSingle(value.Fuel); writer.WriteBoolean(value.CanIgnite);
            writer.WriteSingle(value.DamageMultiplier); writer.WriteSingle(value.FuelConsumptionMultiplier);
        }
    }

    private static EnvironmentFireState[] ReadFires(ref PacketReader reader)
    {
        var values = new EnvironmentFireState[reader.ReadUInt16()];
        for (var index = 0; index < values.Length; index++) values[index] = new EnvironmentFireState(reader.ReadUInt64(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadBoolean(),
            reader.ReadSingle(), reader.ReadSingle());
        return values;
    }

    private static void WriteAudio(ref PacketWriter writer, EnvironmentAudioState[] values)
    {
        writer.WriteUInt16((ushort)System.Math.Min(values.Length, ushort.MaxValue));
        for (var index = 0; index < values.Length && index < ushort.MaxValue; index++)
        { var value = values[index]; writer.WriteUInt64(value.Id); writer.WriteBoolean(value.IsPlaying); writer.WriteBoolean(value.Loop); writer.WriteSingle(value.Volume); writer.WriteSingle(value.Pitch); }
    }

    private static EnvironmentAudioState[] ReadAudio(ref PacketReader reader)
    {
        var values = new EnvironmentAudioState[reader.ReadUInt16()];
        for (var index = 0; index < values.Length; index++) values[index] = new EnvironmentAudioState(reader.ReadUInt64(),
            reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadSingle(), reader.ReadSingle());
        return values;
    }
}
