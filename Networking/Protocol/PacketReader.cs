using System.Text;

internal struct PacketReader
{
    private readonly byte[] data;
    private int offset;
    private readonly int end;

    internal PacketReader(byte[] data) : this(new ArraySegment<byte>(data ?? [])) { }
    internal PacketReader(ArraySegment<byte> data)
    {
        this.data = data.Array ?? [];
        offset = data.Offset;
        end = offset + data.Count;
    }

    internal int Remaining => end - offset;

    private void Require(int count)
    {
        if (count < 0 || count > Remaining) throw new InvalidDataException("Truncated packet.");
    }

    internal byte ReadByte() { Require(1); return data[offset++]; }

    internal int ReadInt32() { Require(4); var value = BitConverter.ToInt32(data, offset); offset += 4; return value; }

    internal short ReadInt16() { Require(2); var value = BitConverter.ToInt16(data, offset); offset += 2; return value; }

    internal long ReadInt64() { Require(8); var value = BitConverter.ToInt64(data, offset); offset += 8; return value; }

    internal ulong ReadUInt64() { Require(8); var value = BitConverter.ToUInt64(data, offset); offset += 8; return value; }

    internal ushort ReadUInt16() { Require(2); var value = BitConverter.ToUInt16(data, offset); offset += 2; return value; }

    internal uint ReadUInt32() { Require(4); var value = BitConverter.ToUInt32(data, offset); offset += 4; return value; }

    internal float ReadSingle() { Require(4); var value = BitConverter.ToSingle(data, offset); offset += 4; return value; }

    internal bool ReadBoolean() => ReadByte() != 0;

    internal byte[] ReadBytes(int length)
    {
        if (length < 0 || length > Remaining) throw new ArgumentOutOfRangeException(nameof(length));
        var value = new byte[length];
        if (length > 0) Buffer.BlockCopy(data, offset, value, 0, length);
        offset += length;
        return value;
    }

    internal string ReadBinaryString()
    {
        var length = 0;
        for (var shift = 0; shift <= 28; shift += 7)
        {
            var next = ReadByte();
            if (shift == 28 && (next & 0xF8) != 0)
                throw new InvalidDataException("Invalid string length.");
            length |= (next & 0x7F) << shift;
            if ((next & 0x80) != 0) continue;
            Require(length);
            var value = Encoding.UTF8.GetString(data, offset, length);
            offset += length;
            return value;
        }
        throw new InvalidDataException("Invalid string length.");
    }

    internal ArraySegment<byte> ReadRemainingSegment()
    {
        var value = new ArraySegment<byte>(data, offset, Remaining);
        offset = end;
        return value;
    }

    internal byte[] ReadRemainingBytes() => ReadBytes(Remaining);

    internal string ReadRemainingUtf8()
    {
        var value = Encoding.UTF8.GetString(data, offset, Remaining);
        offset = end;
        return value;
    }
}