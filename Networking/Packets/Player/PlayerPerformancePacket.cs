internal readonly struct PlayerPerformancePacket : INetworkPacket
{
    internal readonly ushort PlayerId;
    internal readonly int HitShots, MissedShots, HeadShots, Kills, Deaths;
    internal readonly float DamageDealt, DamageReceived;

    internal PlayerPerformancePacket(ushort playerId, int hitShots, int missedShots, int headShots, float damageDealt, float damageReceived, int kills, int deaths)
    { PlayerId = playerId; HitShots = hitShots; MissedShots = missedShots; HeadShots = headShots; DamageDealt = damageDealt; DamageReceived = damageReceived; Kills = kills; Deaths = deaths; }
    public PacketType Type => PacketType.PlayerPerformance;
    public void Write(ref PacketWriter writer) { writer.WriteUInt16(PlayerId); writer.WriteInt32(HitShots); writer.WriteInt32(MissedShots); writer.WriteInt32(HeadShots); writer.WriteSingle(DamageDealt); writer.WriteSingle(DamageReceived); writer.WriteInt32(Kills); writer.WriteInt32(Deaths); }
    internal static PlayerPerformancePacket Read(ref PacketReader reader) => new(reader.ReadUInt16(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadInt32(), reader.ReadInt32());
}
