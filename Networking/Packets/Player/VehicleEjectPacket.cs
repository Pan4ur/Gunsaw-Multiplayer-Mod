internal readonly struct VehicleEjectPacket : INetworkPacket
{
    public VehicleEjectPacket() { }

    public PacketType Type => PacketType.VehicleEject;
    
    public DeliverySettings Settings => DeliverySettings.Lossless;

    public void Write(ref PacketWriter writer) { }

    internal static VehicleEjectPacket Read(ref PacketReader reader) => new VehicleEjectPacket();
}