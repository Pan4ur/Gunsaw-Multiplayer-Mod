internal readonly struct TeleportRequestPacket : INetworkPacket
{
    internal readonly ushort TargetPeerId;

    internal TeleportRequestPacket(ushort targetPeerId)
    {
        TargetPeerId = targetPeerId;
    }

    public PacketType Type => PacketType.TeleportRequest;

    public void Write(ref PacketWriter writer) => writer.WriteUInt16(TargetPeerId);

    internal static TeleportRequestPacket Read(ref PacketReader reader)
        => new TeleportRequestPacket(reader.ReadUInt16());
}
