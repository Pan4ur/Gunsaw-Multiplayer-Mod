using System;
using System.Collections.Generic;
using System.Text;

internal struct PacketWriter
{
    private List<byte> data;
    internal PacketWriter(int capacity) => data = new List<byte>(capacity);
    internal void WriteByte(byte value) => data.Add(value);
    internal void WriteInt32(int value) => data.AddRange(BitConverter.GetBytes(value));
    internal void WriteInt64(long value) => data.AddRange(BitConverter.GetBytes(value));
    internal void WriteUInt16(ushort value) => data.AddRange(BitConverter.GetBytes(value));
    internal void WriteBytes(byte[] value) { if (value != null) data.AddRange(value); }
    internal void WriteUtf8(string value) => WriteBytes(Encoding.UTF8.GetBytes(value ?? ""));
    internal byte[] ToArray() => data.ToArray();
}
