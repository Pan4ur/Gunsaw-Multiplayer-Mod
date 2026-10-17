using UnityEngine;

internal readonly struct EditorCursorPacket : INetworkPacket
{
    internal readonly ushort Peer;
    internal readonly uint Sequence;
    internal readonly Vector2 Position;
    internal readonly ulong CharacterPrefabId;
    internal readonly bool LeftMouseDown;

    internal EditorCursorPacket(ushort peer, uint sequence, Vector2 position, ulong characterPrefabId, bool leftMouseDown)
    {
        Peer = peer;
        Sequence = sequence;
        Position = position;
        CharacterPrefabId = characterPrefabId;
        LeftMouseDown = leftMouseDown;
    }

    public PacketType Type => PacketType.EditorCursor;
   
    public DeliverySettings Settings => new (false, false, false);

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt16(Peer);
        writer.WriteUInt32(Sequence);
        writer.WriteSingle(Position.x);
        writer.WriteSingle(Position.y);
        writer.WriteUInt64(CharacterPrefabId);
        writer.WriteBoolean(LeftMouseDown);
    }

    internal static EditorCursorPacket Read(ref PacketReader reader) => new(reader.ReadUInt16(), reader.ReadUInt32(), new Vector2(reader.ReadSingle(), reader.ReadSingle()), reader.ReadUInt64(), reader.ReadBoolean());
}