internal static class PacketCodec
{
    internal static byte[] Encode<TPacket>(TPacket packet) where TPacket : struct, INetworkPacket
    {
        var writer = new PacketWriter(PacketHeader.Size);
        writer.WriteBytes(PacketHeader.Create(packet.Type));
        packet.Write(ref writer);
        return writer.ToArray();
    }

    internal static byte[] Encode(INetworkPacket packet)
    {
        if (packet == null) return new byte[0];
        var writer = new PacketWriter(PacketHeader.Size);
        writer.WriteBytes(PacketHeader.Create(packet.Type));
        packet.Write(ref writer);
        return writer.ToArray();
    }

    internal static byte[] Payload(byte[] packet)
    {
        if (packet == null || packet.Length < PacketHeader.Size) return new byte[0];
        var payloadLength = packet.Length - PacketHeader.Size;
        var payload = new byte[payloadLength];
        if (payloadLength > 0) Buffer.BlockCopy(packet, PacketHeader.Size, payload, 0, payloadLength);
        return payload;
    }

    internal static bool TryDecode(byte[] data, out PayloadPacket packet)
    {
        PacketHeader header;
        if (!PacketHeader.TryRead(data, out header))
        {
            packet = default;
            return false;
        }

        packet = new PayloadPacket(header.Type, Payload(data));
        return true;
    }
}
