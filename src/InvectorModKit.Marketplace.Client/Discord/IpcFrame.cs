using System;
using System.Text;

namespace InvectorModKit.Marketplace.Client.Discord
{
    /// <summary>What a frame on Discord's local pipe carries.</summary>
    public enum IpcOpcode { Handshake = 0, Frame = 1, Close = 2, Ping = 3, Pong = 4 }

    /// <summary>Discord's pipe framing: int32 LE opcode, int32 LE byte length, then UTF-8 JSON.</summary>
    public static class IpcFrame
    {
        private const int Header = 8;

        public static byte[] Encode(IpcOpcode op, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json ?? "");
            var bytes = new byte[Header + payload.Length];
            WriteInt(bytes, 0, (int)op);
            WriteInt(bytes, 4, payload.Length);
            Buffer.BlockCopy(payload, 0, bytes, Header, payload.Length);
            return bytes;
        }

        /// <summary>Reads the first whole frame of <paramref name="buffer"/>; false (nothing consumed) while it is incomplete.</summary>
        public static bool TryDecode(byte[] buffer, int count, out IpcOpcode op, out string json, out int consumed)
        {
            op = IpcOpcode.Frame;
            json = null;
            consumed = 0;
            if (count < Header) return false;
            int length = ReadInt(buffer, 4);
            if (length < 0 || count < Header + length) return false;
            op = (IpcOpcode)ReadInt(buffer, 0);
            json = Encoding.UTF8.GetString(buffer, Header, length);
            consumed = Header + length;
            return true;
        }

        private static void WriteInt(byte[] b, int at, int v)
        {
            b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24);
        }

        private static int ReadInt(byte[] b, int at) => b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24;
    }
}
