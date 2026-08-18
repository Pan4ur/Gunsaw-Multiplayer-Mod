using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordIPC.Internal
{
    internal enum Opcode
    {
        Handshake = 0,
        Frame = 1,
        Close = 2,
        Ping = 3,
        Pong = 4
    }

    internal sealed class Packet
    {
        public Opcode Opcode;
        public Dictionary<string, object> Data;
        public string RawJson;
    }

    internal abstract class Connection : IDisposable
    {
        private readonly Action<Packet> _packetHandler;
        private readonly Action _disconnectHandler;
        private bool _disconnectNotified;
        protected volatile bool Closed;

        protected Connection(Action<Packet> packetHandler, Action disconnectHandler)
        {
            _packetHandler = packetHandler;
            _disconnectHandler = disconnectHandler;
        }

        public bool IsOpen { get { return !Closed; } }

        public void Write(Opcode opcode, Dictionary<string, object> payload)
        {
            Dictionary<string, object> copy = new Dictionary<string, object>(payload);
            copy["nonce"] = Guid.NewGuid().ToString();
            WriteJson(opcode, MiniJson.Serialize(copy));
        }

        public void WriteJson(Opcode opcode, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json ?? "{}");
            byte[] frame = new byte[8 + body.Length];
            WriteInt32LE(frame, 0, (int)opcode);
            WriteInt32LE(frame, 4, body.Length);
            Buffer.BlockCopy(body, 0, frame, 8, body.Length);
            WriteRaw(frame);
        }

        protected abstract void WriteRaw(byte[] frame);

        protected void Dispatch(Opcode opcode, byte[] body)
        {
            string json = Encoding.UTF8.GetString(body);
            Dictionary<string, object> data = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (data == null) data = new Dictionary<string, object>();
            _packetHandler(new Packet { Opcode = opcode, Data = data, RawJson = json });
        }

        protected void NotifyDisconnected()
        {
            lock (this)
            {
                if (_disconnectNotified) return;
                _disconnectNotified = true;
            }

            if (_disconnectHandler != null) _disconnectHandler();
        }

        protected static int ReadInt32LE(byte[] data, int offset)
        {
            return data[offset] |
                   (data[offset + 1] << 8) |
                   (data[offset + 2] << 16) |
                   (data[offset + 3] << 24);
        }

        private static void WriteInt32LE(byte[] data, int offset, int value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        public abstract void Close();

        public void Dispose()
        {
            Close();
        }
    }
}
