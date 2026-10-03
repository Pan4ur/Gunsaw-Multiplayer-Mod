internal static class PacketCodec
{
    internal static byte[] Encode<TPacket>(TPacket packet) where TPacket : struct, INetworkPacket
    {
        var writer = new PacketWriter(128);
        PacketHeader.Write(ref writer, packet.Type);
        packet.Write(ref writer);
        return writer.ToArray();
    }

    internal static byte[] Encode(INetworkPacket packet)
    {
        if (packet == null) return [];
        var writer = new PacketWriter(128);
        PacketHeader.Write(ref writer, packet.Type);
        packet.Write(ref writer);
        return writer.ToArray();
    }

    internal static bool TryDecode(ArraySegment<byte> data, out PayloadPacket packet)
    {
        if (data.Count < PacketHeader.Size || !PacketHeader.TryRead(data.Array, data.Offset, out var header))
        {
            packet = default;
            return false;
        }
        packet = new PayloadPacket(header.Type, new ArraySegment<byte>(data.Array, data.Offset + PacketHeader.Size, data.Count - PacketHeader.Size));
        return true;
    }
}