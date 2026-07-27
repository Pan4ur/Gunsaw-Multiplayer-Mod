internal static class PacketCodec
{
    internal static byte[] Encode<TPacket>(TPacket packet) where TPacket : struct, INetworkPacket
    {
        var writer = new PacketWriter(PacketHeader.Size);
        writer.WriteBytes(PacketHeader.Create(packet.Type));
        packet.Write(ref writer);
        return writer.ToArray();
    }

    internal static byte[] Payload(byte[] packet)
    {
        var payloadLength = packet.Length - PacketHeader.Size;
        var payload = new byte[payloadLength];
        if (payloadLength > 0) System.Buffer.BlockCopy(packet, PacketHeader.Size, payload, 0, payloadLength);
        return payload;
    }
}
