internal readonly struct WorldInputState
{
    internal readonly ulong BodyId;
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly float Rotation;
    internal readonly float VelocityX;
    internal readonly float VelocityY;
    internal readonly float AngularVelocity;

    internal WorldInputState(ulong bodyId, float positionX, float positionY, float rotation,
        float velocityX, float velocityY, float angularVelocity)
    {
        BodyId = bodyId;
        PositionX = positionX;
        PositionY = positionY;
        Rotation = rotation;
        VelocityX = velocityX;
        VelocityY = velocityY;
        AngularVelocity = angularVelocity;
    }
}

internal readonly struct WorldInputPacket : INetworkPacket
{
    internal readonly WorldInputState[] States;

    internal WorldInputPacket(WorldInputState[] states) => States = states ?? new WorldInputState[0];

    public PacketType Type => PacketType.WorldInput;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteUInt16((ushort)System.Math.Min(States.Length, ushort.MaxValue));
        for (var index = 0; index < States.Length && index < ushort.MaxValue; index++)
        {
            var state = States[index];
            writer.WriteUInt64(state.BodyId);
            writer.WriteSingle(state.PositionX);
            writer.WriteSingle(state.PositionY);
            writer.WriteSingle(state.Rotation);
            writer.WriteSingle(state.VelocityX);
            writer.WriteSingle(state.VelocityY);
            writer.WriteSingle(state.AngularVelocity);
        }
    }

    internal static WorldInputPacket Read(ref PacketReader reader)
    {
        var states = new WorldInputState[reader.ReadUInt16()];
        for (var index = 0; index < states.Length; index++)
            states[index] = new WorldInputState(reader.ReadUInt64(), reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return new WorldInputPacket(states);
    }
}
