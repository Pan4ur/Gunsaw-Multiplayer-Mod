internal static class UdpDatagram
{
    internal const int FragmentPayload = 1000;
    internal const int MaxPacketLength = 4 * 1024 * 1024;
    internal const int RelayMetadataOffset = 7;
    internal const int DirectMetadataOffset = 23;
    internal const int FragmentHeaderSize = 12;
    internal const int MaxDatagramLength = DirectMetadataOffset + FragmentHeaderSize + FragmentPayload;

    internal static int FragmentCount(int length)
    {
        if (length < 0 || length > MaxPacketLength)
            throw new InvalidDataException("UDP packet is too large.");
        return Math.Max(1, (length + FragmentPayload - 1) / FragmentPayload);
    }

    internal static int WriteFragment(byte[] buffer, OutboundPacket packet, int messageId, int index, ushort localPeerId, byte[] directKey = null)
    {
        var count = FragmentCount(packet.WireLength);
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        if (directKey != null && directKey.Length != 16) throw new ArgumentException("Invalid direct key.", nameof(directKey));
        var metadata = directKey == null ? RelayMetadataOffset : DirectMetadataOffset;
        var length = Math.Min(FragmentPayload, packet.WireLength - index * FragmentPayload);
        var datagramLength = metadata + FragmentHeaderSize + length;
        if (buffer.Length < datagramLength) throw new ArgumentException("Datagram buffer is too small.", nameof(buffer));
        buffer[0] = 0x47; buffer[1] = 0x55; buffer[2] = 0x44; buffer[3] = 0x50;
        buffer[4] = directKey == null ? (byte)3 : (byte)8;
        WriteUInt16(buffer, 5, directKey == null ? packet.TargetId : localPeerId);
        if (directKey != null) Buffer.BlockCopy(directKey, 0, buffer, 7, 16);
        WriteInt32(buffer, metadata, messageId);
        WriteUInt16(buffer, metadata + 4, (ushort)index);
        WriteUInt16(buffer, metadata + 6, (ushort)count);
        WriteInt32(buffer, metadata + 8, packet.WireLength);
        packet.CopyWireBytes(index * FragmentPayload, buffer, metadata + FragmentHeaderSize, length);
        return datagramLength;
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32(byte[] data, int offset, int value)
    {
        for (var i = 0; i < 4; i++) data[offset + i] = (byte)(value >> (8 * i));
    }
}