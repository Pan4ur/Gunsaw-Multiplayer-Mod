internal readonly struct WorldFirePacket : INetworkPacket
{
    internal readonly bool Exists;
    internal readonly string Id;
    internal readonly string ParentId;
    internal readonly float PositionX, PositionY, Rotation, Fuel, DamageMult, FuelConsMult;
    internal readonly float LocalPositionX, LocalPositionY, LocalRotation;
    internal readonly bool CanIgnite;

    internal WorldFirePacket(bool exists, string id, float positionX = 0f, float positionY = 0f,
        float rotation = 0f, float fuel = 0f, bool canIgnite = false, float damageMult = 1f,
        float fuelConsMult = 1f, string parentId = "", float localPositionX = 0f,
        float localPositionY = 0f, float localRotation = 0f)
    {
        Exists = exists;
        Id = id ?? "";
        ParentId = parentId ?? "";
        PositionX = positionX;
        PositionY = positionY;
        Rotation = rotation;
        Fuel = fuel;
        CanIgnite = canIgnite;
        DamageMult = damageMult;
        FuelConsMult = fuelConsMult;
        LocalPositionX = localPositionX;
        LocalPositionY = localPositionY;
        LocalRotation = localRotation;
    }

    public PacketType Type => PacketType.WorldFire;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteBoolean(Exists);
        writer.WriteBinaryString(Id);
        if (!Exists) return;
        writer.WriteSingle(PositionX);
        writer.WriteSingle(PositionY);
        writer.WriteSingle(Rotation);
        writer.WriteSingle(Fuel);
        writer.WriteBoolean(CanIgnite);
        writer.WriteSingle(DamageMult);
        writer.WriteSingle(FuelConsMult);
        writer.WriteBinaryString(ParentId);
        writer.WriteSingle(LocalPositionX);
        writer.WriteSingle(LocalPositionY);
        writer.WriteSingle(LocalRotation);
    }

    internal static WorldFirePacket Read(ref PacketReader reader)
    {
        var exists = reader.ReadBoolean();
        var id = reader.ReadBinaryString();
        if (!exists) return new WorldFirePacket(false, id);
        return new WorldFirePacket(true, id, reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadBoolean(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadBinaryString(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}