internal readonly struct VehicleImpactPacket : INetworkPacket
{
    internal readonly float Impact;
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly bool Ragdoll;

    internal VehicleImpactPacket(float impact, float positionX, float positionY, bool ragdoll)
    {
        Impact = impact;
        PositionX = positionX;
        PositionY = positionY;
        Ragdoll = ragdoll;
    }

    public PacketType Type => PacketType.VehicleImpact;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(Impact);
        writer.WriteSingle(PositionX);
        writer.WriteSingle(PositionY);
        writer.WriteBoolean(Ragdoll);
    }

    internal static VehicleImpactPacket Read(ref PacketReader reader)
        => new VehicleImpactPacket(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadBoolean());
}
