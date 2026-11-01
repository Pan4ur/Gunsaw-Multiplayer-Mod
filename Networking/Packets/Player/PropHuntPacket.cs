internal enum PropHuntAction : byte
{
    State,
    Change,
    Stun,
    Hit,
    Miss,
    Damage,
    Penalty,
    Reward,
    Shock,
    Push,
    KickMiss
}

internal readonly struct PropHuntPacket : INetworkPacket
{
    internal readonly int Epoch;
    internal readonly uint Sequence;
    internal readonly PropHuntAction Action;
    internal readonly ushort Player;
    internal readonly PropHuntPhase Phase;
    internal readonly float Value;
    internal readonly KeyValuePair<ushort, string>[] Props;
    internal readonly ushort[] Alive;

    internal PropHuntPacket(PropHuntAction action, ushort player = 0, float value = 0f,
        PropHuntPhase phase = PropHuntPhase.Waiting, KeyValuePair<ushort, string>[] props = null, ushort[] alive = null, int? epoch = null, uint sequence = 0)
    {
        Sequence = sequence;
        Epoch = epoch ?? MultiplayerSession.CurrentSceneEpoch;
        Action = action;
        Player = player;
        Value = value;
        Phase = phase;
        Props = props ?? Array.Empty<KeyValuePair<ushort, string>>();
        Alive = alive ?? Array.Empty<ushort>();
    }

    public PacketType Type => PacketType.PropHunt;
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteInt32(Epoch);
        writer.WriteUInt32(Sequence);
        writer.WriteByte((byte)Action);
        writer.WriteUInt16(Player);
        writer.WriteByte((byte)Phase);
        writer.WriteSingle(Value);
        writer.WriteByte((byte)Props.Length);
        foreach (var prop in Props)
        {
            writer.WriteUInt16(prop.Key);
            writer.WriteBinaryString(prop.Value);
        }
        writer.WriteByte((byte)Alive.Length);
        foreach (var player in Alive) writer.WriteUInt16(player);
    }

    internal static PropHuntPacket Read(ref PacketReader reader)
    {
        var epoch = reader.ReadInt32();
        var sequence = reader.ReadUInt32();
        var action = (PropHuntAction)reader.ReadByte();
        var player = reader.ReadUInt16();
        var phase = (PropHuntPhase)reader.ReadByte();
        var value = reader.ReadSingle();
        var count = reader.ReadByte();
        if (count > 64) throw new InvalidDataException("Too many props.");
        var props = new KeyValuePair<ushort, string>[count];
        for (var index = 0; index < count; index++) props[index] = new(reader.ReadUInt16(), reader.ReadBinaryString());
        count = reader.ReadByte();
        if (count > 64) throw new InvalidDataException("Too many players.");
        var alive = new ushort[count];
        for (var index = 0; index < count; index++) alive[index] = reader.ReadUInt16();
        return new PropHuntPacket(action, player, value, phase, props, alive, epoch, sequence);
    }
}
