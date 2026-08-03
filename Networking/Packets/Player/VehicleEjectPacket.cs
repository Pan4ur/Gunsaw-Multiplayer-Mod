internal readonly struct VehicleEjectPacket : INetworkPacket
{
    public VehicleEjectPacket() { }

    public PacketType Type => PacketType.VehicleEject;

    public void Write(ref PacketWriter writer) { }

    internal static VehicleEjectPacket Read(ref PacketReader reader) => new VehicleEjectPacket();
}
