internal readonly struct VehicleImpactPacket : INetworkPacket
{
    internal readonly float Impact;
    internal readonly float PositionX;
    internal readonly float PositionY;
    internal readonly bool Ragdoll;
    internal readonly float VelocityX;
    internal readonly float VelocityY;

    internal VehicleImpactPacket(float impact, float positionX, float positionY, bool ragdoll, float velocityX = 0f, float velocityY = 0f)
    {
        Impact = impact;
        PositionX = positionX;
        PositionY = positionY;
        Ragdoll = ragdoll;
        VelocityX = velocityX;
        VelocityY = velocityY;
    }

    public PacketType Type => PacketType.VehicleImpact;
    
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer)
    {
        writer.WriteSingle(Impact);
        writer.WriteSingle(PositionX);
        writer.WriteSingle(PositionY);
        writer.WriteBoolean(Ragdoll);
        writer.WriteSingle(VelocityX);
        writer.WriteSingle(VelocityY);
    }

    internal static VehicleImpactPacket Read(ref PacketReader reader)
    {
        var impact = reader.ReadSingle();
        var positionX = reader.ReadSingle();
        var positionY = reader.ReadSingle();
        var ragdoll = reader.ReadBoolean();
        if (reader.Remaining == 0) return new VehicleImpactPacket(impact, positionX, positionY, ragdoll);
        return new VehicleImpactPacket(impact, positionX, positionY, ragdoll, reader.ReadSingle(), reader.ReadSingle());
    }
}