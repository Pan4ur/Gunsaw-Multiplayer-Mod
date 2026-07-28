using System;
using System.Collections.Generic;
using System.Text;

internal struct PacketWriter
{
    private List<byte> data;
    internal PacketWriter(int capacity) => data = new List<byte>(capacity);
    internal void WriteByte(byte value) => data.Add(value);
    internal void WriteInt32(int value) => data.AddRange(BitConverter.GetBytes(value));
    internal void WriteInt16(short value) => data.AddRange(BitConverter.GetBytes(value));
    internal void WriteInt64(long value) => data.AddRange(BitConverter.GetBytes(value));
    internal void WriteUInt64(ulong value) => data.AddRange(BitConverter.GetBytes(value));
    internal void WriteUInt16(ushort value) => data.AddRange(BitConverter.GetBytes(value));
    internal void WriteUInt32(uint value) => data.AddRange(BitConverter.GetBytes(value));
    internal void WriteSingle(float value) => data.AddRange(BitConverter.GetBytes(value));
    internal void WriteBoolean(bool value) => data.Add(value ? (byte)1 : (byte)0);
    internal void WriteBinaryString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        var length = (uint)bytes.Length;
        while (length >= 0x80) { data.Add((byte)(length | 0x80)); length >>= 7; }
        data.Add((byte)length);
        WriteBytes(bytes);
    }
    internal void WriteBytes(byte[] value) { if (value != null) data.AddRange(value); }
    internal void WriteUtf8(string value) => WriteBytes(Encoding.UTF8.GetBytes(value ?? ""));
    internal byte[] ToArray() => data.ToArray();
}
