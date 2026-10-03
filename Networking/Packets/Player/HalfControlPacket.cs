internal readonly struct HalfControlPacket : INetworkPacket
{
    internal readonly float Duration;

    internal HalfControlPacket(float duration)
    {
        Duration = duration;
    }

    public PacketType Type => PacketType.HalfControl;
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer) => writer.WriteSingle(Duration);

    internal static HalfControlPacket Read(ref PacketReader reader) => new(reader.ReadSingle());
}