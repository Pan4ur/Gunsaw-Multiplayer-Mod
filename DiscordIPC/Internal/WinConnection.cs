using System.Runtime.InteropServices;

namespace DiscordIPC.Internal
{
    internal sealed class WinConnection : Connection
    {
        private const uint GENERIC_READ = 0x80000000u;
        private const uint GENERIC_WRITE = 0x40000000u;
        private const uint OPEN_EXISTING = 3u;
        private const int ERROR_PIPE_BUSY = 231;

        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        private readonly object _writeSync = new object();
        private readonly Thread _readerThread;
        private IntPtr _handle;

        public WinConnection(string pipeName, Action<Packet> packetHandler, Action disconnectHandler)
            : base(packetHandler, disconnectHandler)
        {
            _handle = OpenPipe(pipeName);
            if (_handle == InvalidHandleValue)
                throw new IOException("Unable to open Discord IPC pipe " + pipeName + ". Win32 error " + Marshal.GetLastWin32Error());

            _readerThread = new Thread(ReadLoop);
            _readerThread.IsBackground = true;
            _readerThread.Name = "Discord IPC - Pipe thread";
            _readerThread.Start();
        }

        protected override void WriteRaw(byte[] frame)
        {
            try
            {
                lock (_writeSync)
                {
                    if (Closed || _handle == InvalidHandleValue) return;

                    uint written;
                    bool ok = WriteFile(_handle, frame, (uint)frame.Length, out written, IntPtr.Zero);
                    if (!ok)
                        throw new IOException("Discord IPC WriteFile failed. Win32 error " + Marshal.GetLastWin32Error());

                    if (written != (uint)frame.Length)
                        throw new IOException("Discord IPC short write: " + written + "/" + frame.Length + " bytes.");
                }
            }
            catch
            {
                if (!Closed) NotifyDisconnected();
            }
        }

        private void ReadLoop()
        {
            try
            {
                byte[] header = new byte[8];

                while (!Closed)
                {
                    uint available;
                    uint bytesRead;

                    bool ok = PeekNamedPipe(
                        _handle,
                        header,
                        (uint)header.Length,
                        out bytesRead,
                        out available,
                        IntPtr.Zero);

                    if (!ok)
                        throw new IOException("Discord IPC PeekNamedPipe failed. Win32 error " + Marshal.GetLastWin32Error());

                    if (available < 8 || bytesRead < 8)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    int opcodeValue = ReadInt32LE(header, 0);
                    int length = ReadInt32LE(header, 4);

                    if (opcodeValue < 0 || opcodeValue > 4)
                        throw new IOException("Invalid Discord IPC opcode " + opcodeValue + ".");
                    if (length < 0 || length > 16 * 1024 * 1024)
                        throw new IOException("Invalid Discord IPC payload length " + length + ".");

                    long frameLength = 8L + length;
                    if ((long)available < frameLength)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    ReadExact(header, 8);
                    byte[] body = new byte[length];
                    if (length != 0) ReadExact(body, length);
                    Dispatch((Opcode)opcodeValue, body);
                }
            }
            catch
            {
                if (!Closed) NotifyDisconnected();
            }
        }

        private void ReadExact(byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                byte[] target = total == 0 ? buffer : new byte[count - total];
                uint read;
                bool ok = ReadFile(_handle, target, (uint)(count - total), out read, IntPtr.Zero);
                if (!ok)
                    throw new IOException("Discord IPC ReadFile failed. Win32 error " + Marshal.GetLastWin32Error());
                if (read == 0)
                    throw new EndOfStreamException();

                if (total != 0)
                    Buffer.BlockCopy(target, 0, buffer, total, (int)read);

                total += (int)read;
            }
        }

        public override void Close()
        {
            if (Closed) return;
            Closed = true;

            IntPtr handle = _handle;
            _handle = InvalidHandleValue;
            if (handle == InvalidHandleValue) return;

            try { CancelIoEx(handle, IntPtr.Zero); } catch { }
            try { CloseHandle(handle); } catch { }
        }

        private static IntPtr OpenPipe(string pipeName)
        {
            string[] paths =
            {
                "\\\\?\\pipe\\" + pipeName,
                "\\\\.\\pipe\\" + pipeName
            };

            for (int i = 0; i < paths.Length; i++)
            {
                IntPtr handle = CreateFileW(
                    paths[i],
                    GENERIC_READ | GENERIC_WRITE,
                    0,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle != InvalidHandleValue) return handle;

                int error = Marshal.GetLastWin32Error();
                if (error == ERROR_PIPE_BUSY)
                {
                    try { WaitNamedPipeW(paths[i], 100); } catch { }
                    handle = CreateFileW(
                        paths[i],
                        GENERIC_READ | GENERIC_WRITE,
                        0,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        0,
                        IntPtr.Zero);
                    if (handle != InvalidHandleValue) return handle;
                }
            }

            return InvalidHandleValue;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekNamedPipe(
            IntPtr hNamedPipe,
            [Out] byte[] lpBuffer,
            uint nBufferSize,
            out uint lpBytesRead,
            out uint lpTotalBytesAvail,
            IntPtr lpBytesLeftThisMessage);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadFile(
            IntPtr hFile,
            [Out] byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteFile(
            IntPtr hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CancelIoEx(IntPtr hFile, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WaitNamedPipeW(string lpNamedPipeName, uint nTimeOut);
    }
}
