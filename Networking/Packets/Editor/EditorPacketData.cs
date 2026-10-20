using UnityEngine;

internal static class EditorPacketData
{
    internal static void WriteChanges(ref PacketWriter writer, EditorObjectChange[] changes)
    {
        writer.WriteInt32(changes.Length);
        foreach (var change in changes)
        {
            writer.WriteBytes(change.Id.ToByteArray());
            writer.WriteInt32(change.Version);
            writer.WriteBoolean(change.Part != null);
            if (change.Part == null) continue;
            var part = change.Part;
            writer.WriteSingle(part.pos.x);
            writer.WriteSingle(part.pos.y);
            writer.WriteSingle(part.rot);
            writer.WriteBinaryString(part.path);
            writer.WriteInt32(part.id);
            writer.WriteInt32(part.activId);
            writer.WriteBinaryString(part.team);
            writer.WriteSingle(part.size.x);
            writer.WriteSingle(part.size.y);
            writer.WriteSingle(part.force.x);
            writer.WriteSingle(part.force.y);
            writer.WriteSingle(change.Scale.x);
            writer.WriteSingle(change.Scale.y);
            writer.WriteSingle(change.Scale.z);
        }
    }

    internal static EditorObjectChange[] ReadChanges(ref PacketReader reader)
    {
        var count = ReadNonNegative(ref reader);
        if (count > 100000 || count > reader.Remaining / 21)
            throw new InvalidDataException("Invalid editor change count.");
        
        var changes = new EditorObjectChange[count];
        var ids = new HashSet<Guid>();
        for (var index = 0; index < count; index++)
        {
            var id = ReadSession(ref reader);
            var version = ReadNonNegative(ref reader);
            if (!ids.Add(id))
                throw new InvalidDataException("Duplicate editor object identity.");
            
            LevelPart? part = null;
            var scale = Vector3.one;
            if (reader.ReadBoolean())
            {
                part = new LevelPart(new Vector2(ReadFinite(ref reader), ReadFinite(ref reader)),ReadFinite(ref reader), reader.ReadBinaryString())
                {
                    id = reader.ReadInt32(), 
                    activId = reader.ReadInt32(), 
                    team = reader.ReadBinaryString(),
                    size = new Vector2(ReadFinite(ref reader), ReadFinite(ref reader)),
                    force = new Vector2(ReadFinite(ref reader), ReadFinite(ref reader))
                };
                
                if (string.IsNullOrEmpty(part.path) || part.path.Length > 512 || part.team.Length > 1024 * 1024)
                    throw new InvalidDataException("Invalid editor object data.");
                scale = new Vector3(ReadFinite(ref reader), ReadFinite(ref reader), ReadFinite(ref reader));
            }
            changes[index] = new EditorObjectChange(id, version, part, scale);
        }
        return changes;
    }

    internal static void WriteEnvironment(ref PacketWriter writer, Level environment)
    {
        writer.WriteSingle(environment.lightIntensity);
        writer.WriteSingle(environment.lightColor.r);
        writer.WriteSingle(environment.lightColor.g);
        writer.WriteSingle(environment.lightColor.b);
        writer.WriteSingle(environment.lightColor.a);
        writer.WriteBoolean(environment.hasBackground);
    }

    internal static Level ReadEnvironment(ref PacketReader reader) => new()
    {
        lightIntensity = ReadFinite(ref reader),
        lightColor = new Color(ReadFinite(ref reader), ReadFinite(ref reader), ReadFinite(ref reader), ReadFinite(ref reader)),
        hasBackground = reader.ReadBoolean()
    };

    internal static void WriteOptionalEnvironment(ref PacketWriter writer, Level? environment)
    {
        writer.WriteBoolean(environment != null);
        if (environment != null)
            WriteEnvironment(ref writer, environment);
    }

    internal static Level? ReadOptionalEnvironment(ref PacketReader reader) => reader.ReadBoolean() ? ReadEnvironment(ref reader) : null;

    internal static Guid ReadSession(ref PacketReader reader)
    {
        var id = new Guid(reader.ReadBytes(16));
        if (id == Guid.Empty)
            throw new InvalidDataException("Missing editor identity.");
        return id;
    }

    internal static int ReadNonNegative(ref PacketReader reader)
    {
        var value = reader.ReadInt32();
        if (value < 0) 
            throw new InvalidDataException("Invalid editor revision.");
        return value;
    }

    internal static string ReadChecksum(ref PacketReader reader)
    {
        var checksum = reader.ReadBinaryString();
        if (checksum.Length != 44)
            throw new InvalidDataException("Invalid editor checksum.");
        return checksum;
    }

    private static float ReadFinite(ref PacketReader reader)
    {
        var value = reader.ReadSingle();
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidDataException("Invalid editor number.");
        return value;
    }
}