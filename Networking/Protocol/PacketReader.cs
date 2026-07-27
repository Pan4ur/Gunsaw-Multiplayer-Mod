using System;
using System.Text;

internal struct PacketReader
{
    private readonly byte[] data;
    private int offset;

    internal PacketReader(byte[] data) { this.data = data ?? new byte[0]; offset = 0; }

    internal int Remaining => data.Length - offset;

    internal byte ReadByte() => data[offset++];

    internal int ReadInt32() { var value = BitConverter.ToInt32(data, offset); offset += 4; return value; }

    internal long ReadInt64() { var value = BitConverter.ToInt64(data, offset); offset += 8; return value; }

    internal ushort ReadUInt16() { var value = BitConverter.ToUInt16(data, offset); offset += 2; return value; }

    internal byte[] ReadRemainingBytes() { var value = new byte[Remaining]; Buffer.BlockCopy(data, offset, value, 0, value.Length); offset = data.Length; return value; }

    internal string ReadRemainingUtf8() => Encoding.UTF8.GetString(ReadRemainingBytes());
}
