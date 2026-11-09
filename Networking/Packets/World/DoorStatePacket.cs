internal enum DoorStateOpCode : byte
{
    RequestSnapshot = 1,
    States = 2
}

internal readonly struct DoorStateEntry
{
    internal readonly ulong Id;
    internal readonly uint Revision;
    internal readonly bool FollowingFirstPoint;
    internal readonly bool IsMoving;
    internal readonly float TargetX;
    internal readonly float TargetY;
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly float Rotation;

    internal DoorStateEntry(ulong id, uint revision, bool followingFirstPoint, bool isMoving, float targetX,
        float targetY, float positionX = 0f, float positionY = 0f, float rotation = 0f)
    {
        Id = id;
        Revision = revision;
        FollowingFirstPoint = followingFirstPoint;
        IsMoving = isMoving;
        TargetX = targetX;
        TargetY = targetY;
        PositionX = positionX;
        PositionY = positionY;
        Rotation = rotation;
    }
}

internal readonly struct DoorStatePacket : INetworkPacket
{
    internal readonly DoorStateOpCode OpCode;
    internal readonly int SceneEpoch;
    internal readonly bool IncludesPositions;
    internal readonly DoorStateEntry[] States;

    private DoorStatePacket(DoorStateOpCode opCode, int sceneEpoch, bool includesPositions, DoorStateEntry[] states)
    {
        OpCode = opCode;
        SceneEpoch = sceneEpoch;
        IncludesPositions = includesPositions;
        States = states ?? [];
    }

    internal static DoorStatePacket RequestSnapshot(int sceneEpoch)
        => new (DoorStateOpCode.RequestSnapshot, sceneEpoch, false, null);

    internal static DoorStatePacket StatesUpdate(int sceneEpoch, bool includesPositions, DoorStateEntry[] states)
        => new (DoorStateOpCode.States, sceneEpoch, includesPositions, states);

    public PacketType Type => PacketType.DoorState;

    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteByte((byte)OpCode);
        writer.WriteInt32(SceneEpoch);
        if (OpCode == DoorStateOpCode.RequestSnapshot) return;
        writer.WriteBoolean(IncludesPositions);
        writer.WriteUInt16((ushort) Math.Min(States.Length, ushort.MaxValue));
        for (var index = 0; index < States.Length && index < ushort.MaxValue; index++)
        {
            var state = States[index];
            writer.WriteUInt64(state.Id);
            writer.WriteUInt32(state.Revision);
            writer.WriteBoolean(state.FollowingFirstPoint);
            writer.WriteBoolean(state.IsMoving);
            writer.WriteSingle(state.TargetX);
            writer.WriteSingle(state.TargetY);
            if (!IncludesPositions) continue;
            writer.WriteSingle(state.PositionX);
            writer.WriteSingle(state.PositionY);
            writer.WriteSingle(state.Rotation);
        }
    }

    internal static DoorStatePacket Read(ref PacketReader reader)
    {
        var opCode = (DoorStateOpCode) reader.ReadByte();
        var sceneEpoch = reader.ReadInt32();
        
        if (opCode == DoorStateOpCode.RequestSnapshot)
            return RequestSnapshot(sceneEpoch);
       
        if (opCode != DoorStateOpCode.States)
            throw new InvalidDataException("Invalid door state opcode.");
        
        var includesPositions = reader.ReadBoolean();
        var count = reader.ReadUInt16();
        var entrySize = sizeof(ulong) + sizeof(uint) + 2 + sizeof(float) * (includesPositions ? 5 : 2);
        
        if (count > reader.Remaining / entrySize)
            throw new InvalidDataException("Truncated door states.");
        
        var states = new DoorStateEntry[count];
        for (var index = 0; index < states.Length; index++)
        {
            var id = reader.ReadUInt64();
            var revision = reader.ReadUInt32();
            var followingFirstPoint = reader.ReadBoolean();
            var isMoving = reader.ReadBoolean();
            var targetX = reader.ReadSingle();
            var targetY = reader.ReadSingle();
            var x = includesPositions ? reader.ReadSingle() : 0f;
            var y = includesPositions ? reader.ReadSingle() : 0f;
            var rotation = includesPositions ? reader.ReadSingle() : 0f;
            states[index] = new DoorStateEntry(id, revision, followingFirstPoint, isMoving, targetX, targetY, x, y, rotation);
        }
        return StatesUpdate(sceneEpoch, includesPositions, states);
    }
}