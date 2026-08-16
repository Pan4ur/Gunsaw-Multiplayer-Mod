internal static class PacketCodec
{
    internal static byte[] Encode<TPacket>(TPacket packet) where TPacket : struct, INetworkPacket
    {
        var writer = new PacketWriter(PacketHeader.Size);
        writer.WriteBytes(PacketHeader.Create(packet.Type));
        packet.Write(ref writer);
        return writer.ToArray();
    }

    internal static byte[] Encode(INetworkPacket packet)
    {
        if (packet == null) return new byte[0];
        var writer = new PacketWriter(PacketHeader.Size);
        writer.WriteBytes(PacketHeader.Create(packet.Type));
        packet.Write(ref writer);
        return writer.ToArray();
    }

    internal static byte[] Payload(byte[] packet)
    {
        if (packet == null || packet.Length < PacketHeader.Size) return new byte[0];
        var payloadLength = packet.Length - PacketHeader.Size;
        var payload = new byte[payloadLength];
        if (payloadLength > 0) System.Buffer.BlockCopy(packet, PacketHeader.Size, payload, 0, payloadLength);
        return payload;
    }

    internal static byte[] Encode(PacketType type, byte[] payload)
    {
        switch (type)
        {
            case PacketType.Disconnect: return Encode(new PayloadPacket(type, payload));
            case PacketType.Scene: return Encode(new PayloadPacket(type, payload));
            case PacketType.Identity: return Encode(new PayloadPacket(type, payload));
            case PacketType.WorldSnapshot: return Encode(new PayloadPacket(type, payload));
            case PacketType.WorldInput: return Encode(new PayloadPacket(type, payload));
            case PacketType.WorldDamage: return Encode(new PayloadPacket(type, payload));
            case PacketType.NpcSnapshot: return Encode(new PayloadPacket(type, payload));
            case PacketType.NpcDamage: return Encode(new PayloadPacket(type, payload));
            case PacketType.WorldInteraction: return Encode(new PayloadPacket(type, payload));
            case PacketType.PlayerDamage: return Encode(new PayloadPacket(type, payload));
            case PacketType.PvpDamage: return Encode(new PayloadPacket(type, payload));
            case PacketType.Settings: return Encode(new PayloadPacket(type, payload));
            case PacketType.ShotVisual: return Encode(new PayloadPacket(type, payload));
            case PacketType.PlayerGrab:
                var grabReader = new PacketReader(payload);
                return Encode(PlayerGrabPacket.Read(ref grabReader));
            case PacketType.NpcGrab: return Encode(new PayloadPacket(type, payload));
            case PacketType.Chat: return Encode(new PayloadPacket(type, payload));
            case PacketType.CustomLevel: return Encode(new PayloadPacket(type, payload));
            case PacketType.NpcPossession: return Encode(new PayloadPacket(type, payload));
            case PacketType.NpcSpeech: return Encode(new PayloadPacket(type, payload));
            case PacketType.Reliable: return Encode(new PayloadPacket(type, payload));
            case PacketType.ReliableAck: return Encode(new PayloadPacket(type, payload));
            case PacketType.WorldEnvironment: return Encode(new WorldEnvironmentPacket(payload));
            case PacketType.DoorState: return Encode(new PayloadPacket(type, payload));
            case PacketType.ProjectileImpact: return Encode(new PayloadPacket(type, payload));
            case PacketType.VelvetWeb: return Encode(new PayloadPacket(type, payload));
            case PacketType.PlayerTeleport: return Encode(new PayloadPacket(type, payload));
            case PacketType.HostFps:
                var hostFpsReader = new PacketReader(payload);
                return Encode(HostFpsPacket.Read(ref hostFpsReader));
            case PacketType.Ping:
                return payload != null && payload.Length == sizeof(long)
                    ? Encode(new PingPacket(System.BitConverter.ToInt64(payload, 0)))
                    : Encode(new PayloadPacket(type, payload));
            case PacketType.Pong:
                return payload != null && payload.Length == sizeof(long)
                    ? Encode(new PongPacket(System.BitConverter.ToInt64(payload, 0)))
                    : Encode(new PayloadPacket(type, payload));
            default: return Encode(new PayloadPacket(type, payload));
        }
    }

    internal static bool TryDecode(byte[] data, out PayloadPacket packet)
    {
        PacketHeader header;
        if (!PacketHeader.TryRead(data, out header))
        {
            packet = default(PayloadPacket);
            return false;
        }

        packet = new PayloadPacket(header.Type, Payload(data));
        return true;
    }
}
