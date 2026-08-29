using System.Runtime.InteropServices;
using System.Text;

namespace DiscordIPC.Internal
{
    internal sealed class UnixConnection : Connection
    {
        private const int AF_UNIX = 1;
        private const int SOCK_STREAM = 1;

        private int _fd = -1;
        private readonly object _writeSync = new();
        private readonly Thread _readerThread;

        public UnixConnection(string path, Action<Packet> packetHandler, Action disconnectHandler)
            : base(packetHandler, disconnectHandler)
        {
            _fd = Native.socket(AF_UNIX, SOCK_STREAM, 0);
            if (_fd < 0) throw LastIoException("socket");

            try
            {
                byte[] address = BuildSockAddr(path);
                if (Native.connect(_fd, address, (uint)address.Length) != 0)
                    throw LastIoException("connect");
            }
            catch
            {
                Native.close(_fd);
                _fd = -1;
                throw;
            }

            _readerThread = new Thread(ReadLoop);
            _readerThread.IsBackground = true;
            _readerThread.Name = "Discord IPC - Unix socket thread";
            _readerThread.Start();
        }

        protected override void WriteRaw(byte[] frame)
        {
            try
            {
                lock (_writeSync)
                {
                    if (Closed || _fd < 0) return;
                    int offset = 0;
                    while (offset < frame.Length)
                    {
                        int remaining = frame.Length - offset;
                        byte[] chunk;
                        if (offset == 0)
                        {
                            chunk = frame;
                        }
                        else
                        {
                            chunk = new byte[remaining];
                            Buffer.BlockCopy(frame, offset, chunk, 0, remaining);
                        }

                        long written = Native.write(_fd, chunk, new UIntPtr((uint)remaining)).ToInt64();
                        if (written <= 0) throw LastIoException("write");
                        offset += (int)written;
                    }
                }
            }
            catch
            {
                NotifyDisconnected();
            }
        }

        private void ReadLoop()
        {
            try
            {
                while (!Closed)
                {
                    byte[] header = ReadExact(8);
                    int opcodeValue = ReadInt32LE(header, 0);
                    int length = ReadInt32LE(header, 4);
                    if (opcodeValue < 0 || opcodeValue > 4) throw new IOException("Invalid Discord IPC opcode.");
                    if (length < 0 || length > 16 * 1024 * 1024) throw new IOException("Invalid Discord IPC payload length.");
                    Dispatch((Opcode)opcodeValue, ReadExact(length));
                }
            }
            catch
            {
                if (!Closed) NotifyDisconnected();
            }
        }

        private byte[] ReadExact(int count)
        {
            byte[] result = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int remaining = count - offset;
                byte[] chunk = new byte[remaining];
                long read = Native.read(_fd, chunk, new UIntPtr((uint)remaining)).ToInt64();
                if (read <= 0) throw new EndOfStreamException();
                Buffer.BlockCopy(chunk, 0, result, offset, (int)read);
                offset += (int)read;
            }
            return result;
        }

        private static byte[] BuildSockAddr(string path)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            bool mac = Environment.OSVersion.Platform == PlatformID.MacOSX ||
                       File.Exists("/System/Library/CoreServices/SystemVersion.plist");
            int maxPath = mac ? 103 : 107;
            if (pathBytes.Length > maxPath) throw new IOException("Unix socket path is too long: " + path);

            int length = 2 + pathBytes.Length + 1;
            byte[] address = new byte[length];
            if (mac)
            {
                address[0] = (byte)length;
                address[1] = AF_UNIX;
            }
            else
            {
                address[0] = AF_UNIX;
                address[1] = 0;
            }
            Buffer.BlockCopy(pathBytes, 0, address, 2, pathBytes.Length);
            return address;
        }

        private static IOException LastIoException(string operation)
        {
            return new IOException(operation + " failed. errno=" + Marshal.GetLastWin32Error());
        }

        public override void Close()
        {
            if (Closed) return;
            Closed = true;
            int fd = _fd;
            _fd = -1;
            if (fd >= 0)
            {
                try { Native.close(fd); } catch { }
            }
        }

        private static class Native
        {
            [DllImport("libc", SetLastError = true)]
            internal static extern int socket(int domain, int type, int protocol);

            [DllImport("libc", SetLastError = true)]
            internal static extern int connect(int sockfd, byte[] addr, uint addrlen);

            [DllImport("libc", SetLastError = true)]
            internal static extern IntPtr read(int fd, byte[] buffer, UIntPtr count);

            [DllImport("libc", SetLastError = true)]
            internal static extern IntPtr write(int fd, byte[] buffer, UIntPtr count);

            [DllImport("libc", SetLastError = true)]
            internal static extern int close(int fd);
        }
    }
}
