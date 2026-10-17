using System.IO.Compression;

internal static class PacketCodec
{
    internal const int MaxPayloadLength = 16 * 1024 * 1024;
    private const int CompressionThreshold = 1024;

    internal static byte[] Encode<TPacket>(TPacket packet) where TPacket : struct, INetworkPacket
    {
        var writer = new PacketWriter(128);
        PacketHeader.Write(ref writer, packet.Type);
        packet.Write(ref writer);
        return FinishEncoding(writer.ToArray(), packet.Type, packet.Settings.Compress);
    }

    internal static byte[] Encode(INetworkPacket packet)
    {
        if (packet == null) return [];
        var writer = new PacketWriter(128);
        PacketHeader.Write(ref writer, packet.Type);
        packet.Write(ref writer);
        return FinishEncoding(writer.ToArray(), packet.Type, packet.Settings.Compress);
    }

    private static byte[] FinishEncoding(byte[] data, PacketType type, bool compress)
    {
        var payloadLength = data.Length - PacketHeader.Size;
        if (payloadLength > PayloadLimit(type)) throw new InvalidDataException("Packet payload is too large.");
        if (!compress || payloadLength < CompressionThreshold) return data;
        using var output = new MemoryStream();
        using (var compressor = new DeflateStream(output, CompressionLevel.Fastest, true))
            compressor.Write(data, PacketHeader.Size, payloadLength);
        if (output.Length + sizeof(int) >= payloadLength) return data;
        var writer = new PacketWriter((int)output.Length + PacketHeader.Size + sizeof(int));
        PacketHeader.Write(ref writer, type, compressed: true);
        writer.WriteInt32(payloadLength);
        writer.WriteBytes(output.ToArray());
        return writer.ToArray();
    }

    internal static bool TryDecode(ArraySegment<byte> data, out PayloadPacket packet)
    {
        packet = default;
        if (data.Count < PacketHeader.Size || !PacketHeader.TryRead(data.Array, data.Offset, out var header)) return false;
        var payload = new ArraySegment<byte>(data.Array!, data.Offset + PacketHeader.Size, data.Count - PacketHeader.Size);
        if (payload.Count > PayloadLimit(header.Type)) return false;
        if (!header.Compressed)
        {
            packet = new PayloadPacket(header.Type, payload);
            return true;
        }
        if (header.Type is PacketType.Reliable or PacketType.ReliableAck || payload.Count < sizeof(int)) return false;
        try
        {
            var reader = new PacketReader(payload);
            var length = reader.ReadInt32();
            if (length < 0 || length > MaxPayloadLength) return false;
            using var input = new MemoryStream(data.Array!, payload.Offset + sizeof(int), payload.Count - sizeof(int), false);
            using var inflater = new DeflateStream(input, CompressionMode.Decompress);
            var decoded = new byte[length];
            var offset = 0;
            while (offset < decoded.Length)
            {
                var count = inflater.Read(decoded, offset, decoded.Length - offset);
                if (count == 0) return false;
                offset += count;
            }
            if (inflater.ReadByte() != -1) return false;
            packet = new PayloadPacket(header.Type, new ArraySegment<byte>(decoded));
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is ArgumentException)
        {
            return false;
        }
    }

    private static int PayloadLimit(PacketType type) => MaxPayloadLength + (type == PacketType.Reliable ? ReliablePacket.HeaderSize : 0);
}