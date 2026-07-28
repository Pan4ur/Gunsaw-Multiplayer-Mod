using System.Text;

internal readonly struct ScenePacket : INetworkPacket
{
    internal readonly string Scene;

    internal ScenePacket(string scene) => Scene = scene ?? "";

    public PacketType Type => PacketType.Scene;

    public void Write(ref PacketWriter writer) => writer.WriteUtf8(Scene);

    internal static ScenePacket Read(ref PacketReader reader) => new ScenePacket(reader.ReadRemainingUtf8());
}
