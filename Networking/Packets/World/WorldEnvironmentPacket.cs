using UnityEngine;

internal readonly struct EnvironmentButtonState
{
    internal readonly ulong Id;
    internal readonly bool Active;
    internal readonly uint Activations;

    internal EnvironmentButtonState(ulong id, bool active, uint activations)
    {
        Id = id;
        Active = active;
        Activations = activations;
    }
}

internal readonly struct EnvironmentLampPowerState
{
    internal readonly ulong Id;
    internal readonly bool Powered;
    internal readonly float Intensity;
    internal readonly Color32 Color;
    
    internal EnvironmentLampPowerState(ulong id, bool powered, float intensity, Color32 color)
    {
        Id = id;
        Powered = powered;
        Intensity = intensity;
        Color = color;
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
        Id = id;
        IsPlaying = isPlaying;
        Loop = loop;
        Volume = volume;
        Pitch = pitch;
    }
}

internal readonly struct WorldEnvironmentPacket : INetworkPacket
{
    internal readonly int SceneEpoch;
    internal readonly float GravityX;
    internal readonly float GravityY;
    internal readonly EnvironmentButtonState[] Buttons;
    internal readonly EnvironmentAudioState[] Audio;
    internal readonly ulong[] DestroyedDroneIds;
    internal readonly float RainIntensity;
    internal readonly float SnowIntensity;
    internal readonly float FogIntensity;
    internal readonly int EnemyKills;
    internal readonly int EnemyTotal;
    internal readonly EnvironmentLampPowerState[] LampPower;

    internal WorldEnvironmentPacket(int sceneEpoch, float gravityX, float gravityY, EnvironmentButtonState[] buttons,
        EnvironmentAudioState[] audio, ulong[] destroyedDroneIds,
        float rainIntensity, float snowIntensity,
        float fogIntensity, int enemyKills = -1, int enemyTotal = -1, EnvironmentLampPowerState[] lampPower = null)
    {
        SceneEpoch = sceneEpoch; GravityX = gravityX; GravityY = gravityY;
        Buttons = buttons ?? [];
        Audio = audio ?? [];
        DestroyedDroneIds = destroyedDroneIds ?? [];
        RainIntensity = rainIntensity; SnowIntensity = snowIntensity; FogIntensity = fogIntensity;
        EnemyKills = enemyKills; EnemyTotal = enemyTotal;
        LampPower = lampPower ?? [];
    }

    public PacketType Type => PacketType.WorldEnvironment;
    
    public DeliverySettings Settings => new (true, false, false);

    internal bool ContentEquals(WorldEnvironmentPacket other)
    {
        return SceneEpoch == other.SceneEpoch && GravityX.Equals(other.GravityX) && GravityY.Equals(other.GravityY) &&
            ArraysEqual(Buttons, other.Buttons) && ArraysEqual(Audio, other.Audio) &&
            ArraysEqual(DestroyedDroneIds, other.DestroyedDroneIds) && RainIntensity.Equals(other.RainIntensity) &&
            SnowIntensity.Equals(other.SnowIntensity) && FogIntensity.Equals(other.FogIntensity) &&
            EnemyKills == other.EnemyKills && EnemyTotal == other.EnemyTotal && ArraysEqual(LampPower, other.LampPower);
    }

    private static bool ArraysEqual<T>(T[] left, T[] right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++)
            if (!EqualityComparer<T>.Default.Equals(left[index], right[index])) return false;
        return true;
    }

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(SceneEpoch);
        writer.WriteSingle(GravityX);
        writer.WriteSingle(GravityY);
        WriteButtons(ref writer, Buttons);
        WriteAudio(ref writer, Audio);
        WriteIds(ref writer, DestroyedDroneIds);
        writer.WriteSingle(RainIntensity);
        writer.WriteSingle(SnowIntensity);
        writer.WriteSingle(FogIntensity);
        writer.WriteInt32(EnemyKills);
        writer.WriteInt32(EnemyTotal);
        WriteLampPower(ref writer, LampPower);
    }

    internal static WorldEnvironmentPacket Read(ref PacketReader reader)
    {
        var sceneEpoch = reader.ReadInt32();
        var gravityX = reader.ReadSingle();
        var gravityY = reader.ReadSingle();
        var buttons = ReadButtons(ref reader);
        var audio = ReadAudio(ref reader);
        var drones = ReadIds(ref reader);
        var rain = reader.ReadSingle();
        var snow = reader.ReadSingle();
        var fog = reader.ReadSingle();
        var enemyKills = reader.ReadInt32();
        var enemyTotal = reader.ReadInt32();
        var lampPower = ReadLampPower(ref reader);
        return new WorldEnvironmentPacket(sceneEpoch, gravityX, gravityY, buttons, audio, drones, rain, snow, fog, enemyKills, enemyTotal, lampPower);
    }

    private static void WriteButtons(ref PacketWriter writer, EnvironmentButtonState[] values)
    {
        writer.WriteUInt16((ushort) Math.Min(values.Length, ushort.MaxValue));
        for (var index = 0; index < values.Length && index < ushort.MaxValue; index++)
        {
            writer.WriteUInt64(values[index].Id);
            writer.WriteBoolean(values[index].Active);
            writer.WriteUInt32(values[index].Activations);
        }
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
        writer.WriteUInt16((ushort) Math.Min(values.Length, ushort.MaxValue));
        for (var index = 0; index < values.Length && index < ushort.MaxValue; index++) writer.WriteUInt64(values[index]);
    }

    private static ulong[] ReadIds(ref PacketReader reader)
    {
        var values = new ulong[reader.ReadUInt16()];
        for (var index = 0; index < values.Length; index++) 
            values[index] = reader.ReadUInt64();
        return values;
    }

    private static void WriteLampPower(ref PacketWriter writer, EnvironmentLampPowerState[] values)
    {
        writer.WriteUInt16((ushort) Math.Min(values.Length, ushort.MaxValue));
        for (var index = 0; index < values.Length && index < ushort.MaxValue; index++)
        {
            writer.WriteUInt64(values[index].Id);
            writer.WriteBoolean(values[index].Powered);
            writer.WriteSingle(values[index].Intensity);
            writer.WriteByte(values[index].Color.r);
            writer.WriteByte(values[index].Color.g);
            writer.WriteByte(values[index].Color.b);
            writer.WriteByte(values[index].Color.a);
        }
    }

    private static EnvironmentLampPowerState[] ReadLampPower(ref PacketReader reader)
    {
        var values = new EnvironmentLampPowerState[reader.ReadUInt16()];
        for (var index = 0; index < values.Length; index++)
            values[index] = new EnvironmentLampPowerState(reader.ReadUInt64(), reader.ReadBoolean(), reader.ReadSingle(), new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()));
        return values;
    }

    private static void WriteAudio(ref PacketWriter writer, EnvironmentAudioState[] values)
    {
        writer.WriteUInt16((ushort)Math.Min(values.Length, ushort.MaxValue));
        for (var index = 0; index < values.Length && index < ushort.MaxValue; index++)
        {
            var value = values[index];
            writer.WriteUInt64(value.Id);
            writer.WriteBoolean(value.IsPlaying);
            writer.WriteBoolean(value.Loop);
            writer.WriteSingle(value.Volume);
            writer.WriteSingle(value.Pitch);
        }
    }

    private static EnvironmentAudioState[] ReadAudio(ref PacketReader reader)
    {
        var values = new EnvironmentAudioState[reader.ReadUInt16()];
        for (var index = 0; index < values.Length; index++)
            values[index] = new EnvironmentAudioState(reader.ReadUInt64(), reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadSingle(), reader.ReadSingle());
        return values;
    }
}