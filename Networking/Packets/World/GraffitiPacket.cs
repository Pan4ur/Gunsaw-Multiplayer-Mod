internal readonly struct GraffitiPacket : INetworkPacket
{
    internal const int MetadataBytes = sizeof(int) + sizeof(ushort) + sizeof(uint) + sizeof(float) * 4 + sizeof(int);
    internal readonly int SceneEpoch;
    internal readonly ushort OwnerId;
    internal readonly uint Id;
    internal readonly float X, Y, Scale, Rotation;
    internal readonly byte[] Image;

    internal GraffitiPacket(int sceneEpoch, ushort ownerId, uint id, float x, float y, float scale, float rotation, byte[] image)
    {
        SceneEpoch = sceneEpoch;
        OwnerId = ownerId;
        Id = id;
        X = x;
        Y = y;
        Scale = scale;
        Rotation = rotation;
        Image = image ?? new byte[0];
    }

    public PacketType Type => PacketType.Graffiti;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(SceneEpoch);
        writer.WriteUInt16(OwnerId);
        writer.WriteUInt32(Id);
        writer.WriteSingle(X);
        writer.WriteSingle(Y);
        writer.WriteSingle(Scale);
        writer.WriteSingle(Rotation);
        writer.WriteInt32(Image.Length);
        writer.WriteBytes(Image);
    }

    internal static GraffitiPacket Read(ref PacketReader reader)
    {
        if (reader.Remaining < MetadataBytes) return default;
        var epoch = reader.ReadInt32();
        var ownerId = reader.ReadUInt16();
        var id = reader.ReadUInt32();
        var x = reader.ReadSingle();
        var y = reader.ReadSingle();
        var scale = reader.ReadSingle();
        var rotation = reader.ReadSingle();
        var length = reader.ReadInt32();
        if (length < 1 || length != reader.Remaining || length > GraffitiSystem.MaxImageBytes) return default;
        return new GraffitiPacket(epoch, ownerId, id, x, y, scale, rotation, reader.ReadBytes(length));
    }
}
