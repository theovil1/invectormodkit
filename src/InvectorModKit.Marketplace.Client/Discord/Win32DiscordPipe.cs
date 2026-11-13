using System;
using System.IO;
using System.Runtime.InteropServices;

namespace InvectorModKit.Marketplace.Client.Discord
{
    /// <summary>
    /// Discord's pipe through kernel32 directly: Mono's NamedPipeClientStream is unreliable inside Unity.
    /// Tries discord-ipc-0 to discord-ipc-9, the first that opens wins.
    /// </summary>
    public sealed class Win32DiscordPipe : IDiscordPipe
    {
        private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, OpenExisting = 3;
        private static readonly IntPtr Invalid = new IntPtr(-1);

        private IntPtr _handle = Invalid;

        public bool IsOpen => _handle != Invalid;

        public bool TryOpen()
        {
            Close();
            for (int i = 0; i < 10; i++)
            {
                var h = CreateFile(@"\\.\pipe\discord-ipc-" + i, GenericRead | GenericWrite, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (h != Invalid) { _handle = h; return true; }
            }
            return false;
        }

        public void Write(byte[] data)
        {
            if (!IsOpen) throw new IOException("Discord pipe closed");
            if (!WriteFile(_handle, data, (uint)data.Length, out uint written, IntPtr.Zero) || written != data.Length)
            {
                Close();
                throw new IOException("Discord pipe write failed");
            }
        }

        public int ReadAvailable(byte[] buffer)
        {
            if (!IsOpen) return 0;
            if (!PeekNamedPipe(_handle, IntPtr.Zero, 0, IntPtr.Zero, out uint available, IntPtr.Zero))
            {
                Close();
                throw new IOException("Discord pipe read failed");
            }
            if (available == 0) return 0;
            uint want = Math.Min(available, (uint)buffer.Length);
            if (!ReadFile(_handle, buffer, want, out uint read, IntPtr.Zero))
            {
                Close();
                throw new IOException("Discord pipe read failed");
            }
            return (int)read;
        }

        public void Dispose() => Close();

        private void Close()
        {
            if (_handle == Invalid) return;
            CloseHandle(_handle);
            _handle = Invalid;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr handle, byte[] buffer, uint count, out uint written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr handle, byte[] buffer, uint count, out uint read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PeekNamedPipe(IntPtr handle, IntPtr buffer, uint size, IntPtr read, out uint available, IntPtr left);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
