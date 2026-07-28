using System;
using System.Threading;

internal static class ChatService
{
    private static int sequence;

    internal static bool TryCreate(string message, bool system, out ChatPacket packet)
    {
        packet = default(ChatPacket);
        if (string.IsNullOrWhiteSpace(message)) return false;
        var text = message.Trim();
        if (text.Length > 160) text = text.Substring(0, 160);
        packet = new ChatPacket(Interlocked.Increment(ref sequence), system, text);
        return true;
    }
}
