internal readonly struct HostFpsPacket : INetworkPacket
{
    internal readonly ushort FPS;

    internal HostFpsPacket(ushort fps)
    {
        FPS = fps;
    }

    public PacketType Type => PacketType.HostFps;

    public void Write(ref PacketWriter writer) => writer.WriteUInt16(FPS);

    internal static HostFpsPacket Read(ref PacketReader reader) => new(reader.ReadUInt16());
}
