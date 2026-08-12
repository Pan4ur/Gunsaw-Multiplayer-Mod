using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal struct PacketWriter
{
    [StructLayout(LayoutKind.Explicit)]
    private struct SingleBits
    {
        [FieldOffset(0)] internal float Single;
        [FieldOffset(0)] internal uint UInt32;
    }

    private List<byte> data;
    internal PacketWriter(int capacity) => data = new List<byte>(capacity);
    internal void WriteByte(byte value) => data.Add(value);
    internal void WriteInt32(int value) => WriteUInt32((uint)value);
    internal void WriteInt16(short value) => WriteUInt16((ushort)value);
    internal void WriteInt64(long value) => WriteUInt64((ulong)value);
    internal void WriteUInt64(ulong value)
    {
        data.Add((byte)value); data.Add((byte)(value >> 8)); data.Add((byte)(value >> 16)); data.Add((byte)(value >> 24));
        data.Add((byte)(value >> 32)); data.Add((byte)(value >> 40)); data.Add((byte)(value >> 48)); data.Add((byte)(value >> 56));
    }
    internal void WriteUInt16(ushort value)
    {
        data.Add((byte)value); data.Add((byte)(value >> 8));
    }
    internal void WriteUInt32(uint value)
    {
        data.Add((byte)value); data.Add((byte)(value >> 8)); data.Add((byte)(value >> 16)); data.Add((byte)(value >> 24));
    }
    internal void WriteSingle(float value) => WriteUInt32(new SingleBits { Single = value }.UInt32);
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
