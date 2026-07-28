internal enum DisconnectReason : byte
{
    PeerLeft = 0,
    ClientClosed = 1
}

internal readonly struct DisconnectPacket : INetworkPacket
{
    internal readonly DisconnectReason Reason;
    internal readonly ushort PeerId;

    private DisconnectPacket(DisconnectReason reason, ushort peerId)
    {
        Reason = reason;
        PeerId = peerId;
    }

    internal static DisconnectPacket ClientClosed() => new DisconnectPacket(DisconnectReason.ClientClosed, 0);
    internal static DisconnectPacket PeerLeft(ushort peerId) => new DisconnectPacket(DisconnectReason.PeerLeft, peerId);

    public PacketType Type => PacketType.Disconnect;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteByte((byte)Reason);
        if (Reason == DisconnectReason.PeerLeft) writer.WriteUInt16(PeerId);
    }

    internal static DisconnectPacket Read(ref PacketReader reader)
    {
        var reason = (DisconnectReason)reader.ReadByte();
        return reason == DisconnectReason.PeerLeft
            ? PeerLeft(reader.ReadUInt16())
            : ClientClosed();
    }
}