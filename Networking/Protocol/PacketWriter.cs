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

    private byte[] data;
    private int length;

    internal PacketWriter(int capacity)
    {
        data = new byte[Math.Max(1, capacity)];
        length = 0;
    }

    private void Reserve(int count)
    {
        var required = checked(length + count);
        if (required > data.Length) Array.Resize(ref data, Math.Max(required, data.Length * 2));
    }

    internal void WriteByte(byte value)
    {
        Reserve(1);
        data[length++] = value;
    }

    internal void WriteInt32(int value) => WriteUInt32((uint)value);
    
    internal void WriteInt16(short value) => WriteUInt16((ushort)value);
    
    internal void WriteInt64(long value) => WriteUInt64((ulong)value);
    
    internal void WriteUInt16(ushort value)
    {
        Reserve(2);
        data[length++] = (byte)value;
        data[length++] = (byte)(value >> 8);
    }
    
    internal void WriteUInt32(uint value)
    {
        Reserve(4);
        for (var i = 0; i < 4; i++) data[length++] = (byte)(value >> (8 * i));
    }
    
    internal void WriteUInt64(ulong value)
    {
        Reserve(8);
        for (var i = 0; i < 8; i++) data[length++] = (byte)(value >> (8 * i));
    }
    
    internal void WriteSingle(float value) => WriteUInt32(new SingleBits { Single = value }.UInt32);
    
    internal void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);
    
    internal void WriteBinaryString(string value)
    {
        value = value ?? "";
        var count = (uint)Encoding.UTF8.GetByteCount(value);
        while (count >= 0x80) { WriteByte((byte)(count | 0x80)); count >>= 7; }
        WriteByte((byte)count);
        WriteUtf8(value);
    }
    
    internal void WriteBytes(byte[] value)
    {
        if (value != null) WriteBytes(new ArraySegment<byte>(value));
    }
    
    internal void WriteBytes(ArraySegment<byte> value)
    {
        if (value.Count == 0) return;
        Reserve(value.Count);
        Buffer.BlockCopy(value.Array, value.Offset, data, length, value.Count);
        length += value.Count;
    }
    
    internal void WriteUtf8(string value)
    {
        value = value ?? "";
        Reserve(Encoding.UTF8.GetByteCount(value));
        length += Encoding.UTF8.GetBytes(value, 0, value.Length, data, length);
    }
    
    internal byte[] ToArray()
    {
        if (length == data.Length) return data;
        var result = new byte[length];
        Buffer.BlockCopy(data, 0, result, 0, length);
        return result;
    }
}