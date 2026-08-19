using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DiscordIPC.Internal
{
    internal static class GameRegistration
    {
        private static readonly IntPtr HKeyCurrentUser = new IntPtr(unchecked((int)0x80000001));
        private const int KeyWrite = 0x20006;
        private const uint RegSz = 1;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegCreateKeyExW")]
        private static extern int RegCreateKeyEx(
            IntPtr hKey,
            string lpSubKey,
            int reserved,
            string lpClass,
            int dwOptions,
            int samDesired,
            IntPtr lpSecurityAttributes,
            out IntPtr phkResult,
            out int lpdwDisposition);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegSetValueExW")]
        private static extern int RegSetValueEx(
            IntPtr hKey,
            string lpValueName,
            int reserved,
            uint dwType,
            byte[] lpData,
            int cbData);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr hKey);

        internal static bool Register(long appId, string command)
        {
            PlatformID platform = Environment.OSVersion.Platform;
            if (platform == PlatformID.Win32NT || platform == PlatformID.Win32Windows ||
                platform == PlatformID.Win32S || platform == PlatformID.WinCE)
                return RegisterWindows(appId, command);

            if (platform == PlatformID.Unix)
                return RegisterLinux(appId, command);

            return false;
        }

        private static bool RegisterWindows(long appId, string command)
        {
            string app = appId.ToString(CultureInfo.InvariantCulture);
            string protocol = "discord-" + app;
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string openCommand = string.IsNullOrEmpty(command) ? Quote(exe) : command;
            string basePath = "Software\\Classes\\" + protocol;

            if (!SetRegistryValue(basePath, null, "URL:Run game " + app + " protocol")) return false;
            if (!SetRegistryValue(basePath, "URL Protocol", string.Empty)) return false;
            if (!SetRegistryValue(basePath + "\\DefaultIcon", null, exe)) return false;
            if (!SetRegistryValue(basePath + "\\shell\\open\\command", null, openCommand)) return false;

            return true;
        }

        private static bool SetRegistryValue(string path, string name, string value)
        {
            IntPtr key;
            int disposition;
            int result = RegCreateKeyEx(
                HKeyCurrentUser,
                path,
                0,
                null,
                0,
                KeyWrite,
                IntPtr.Zero,
                out key,
                out disposition);

            if (result != 0 || key == IntPtr.Zero)
                return false;

            try
            {
                byte[] data = Encoding.Unicode.GetBytes((value ?? string.Empty) + "\0");
                result = RegSetValueEx(key, name, 0, RegSz, data, data.Length);
                return result == 0;
            }
            finally
            {
                RegCloseKey(key);
            }
        }

        private static bool RegisterLinux(long appId, string command)
        {
            string home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home)) return false;

            string app = appId.ToString(CultureInfo.InvariantCulture);
            string protocol = "discord-" + app;
            string applications = Path.Combine(Path.Combine(Path.Combine(home, ".local"), "share"), "applications");
            Directory.CreateDirectory(applications);

            string exe = GetLinuxExecutable();
            string openCommand = string.IsNullOrEmpty(command) ? QuoteDesktop(exe) : command;
            string fileName = protocol + ".desktop";
            string path = Path.Combine(applications, fileName);
            string content = "[Desktop Entry]\n" +
                             "Name=Game " + app + "\n" +
                             "Exec=" + openCommand + " %u\n" +
                             "Type=Application\n" +
                             "NoDisplay=true\n" +
                             "Categories=Discord;Games;\n" +
                             "MimeType=x-scheme-handler/" + protocol + ";\n";
            File.WriteAllText(path, content);

            try
            {
                Process process = new Process();
                process.StartInfo.FileName = "xdg-mime";
                process.StartInfo.Arguments = "default " + fileName + " x-scheme-handler/" + protocol;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string GetLinuxExecutable()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }

        private static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.Length > 1 && value[0] == '"' && value[value.Length - 1] == '"') return value;
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string QuoteDesktop(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
