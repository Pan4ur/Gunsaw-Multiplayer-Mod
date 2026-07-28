internal readonly struct IdentityPacket : INetworkPacket
{
    internal readonly string Name;
    internal readonly string Prefab;

    internal IdentityPacket(string name, string prefab)
    {
        Name = name ?? "";
        Prefab = prefab ?? "";
    }

    public PacketType Type => PacketType.Identity;

    public void Write(ref PacketWriter writer) => writer.WriteUtf8(Name + "\n" + Prefab);

    internal static IdentityPacket Read(ref PacketReader reader)
    {
        var value = reader.ReadRemainingUtf8();
        var split = value.IndexOf('\n');
        return split < 0 ? new IdentityPacket(value, "") : new IdentityPacket(value.Substring(0, split), value.Substring(split + 1));
    }
}
