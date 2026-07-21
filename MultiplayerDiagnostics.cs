using System;
using System.Diagnostics;
using System.IO;
using BepInEx;

internal static class MultiplayerDiagnostics
{
    private static readonly bool Enabled = false;
    private static readonly object Gate = new object();
    private static readonly int ProcessId = Process.GetCurrentProcess().Id;
    private static string FilePath
    {
        get { return Path.Combine(Paths.BepInExRootPath, "mp-debug-" + ProcessId + ".log"); }
    }

    internal static void Write(string message)
    {
        if (!Enabled) return;
        WriteCore(message);
    }

    private static void WriteCore(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        try
        {
            var role = MultiplayerSession.IsHost ? "host" : "client";
            var peer = MultiplayerSession.LocalPeerId;
            lock (Gate)
                File.AppendAllText(FilePath,
                    DateTime.UtcNow.ToString("O") + " [pid=" + ProcessId +
                    " role=" + role + " peer=" + peer + "] " + message +
                    Environment.NewLine);
        }
        catch { }
    }
}
