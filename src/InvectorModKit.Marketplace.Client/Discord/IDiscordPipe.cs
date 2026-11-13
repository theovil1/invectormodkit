using System;

namespace InvectorModKit.Marketplace.Client.Discord
{
    /// <summary>The local connection to the Discord client (a named pipe on Windows).</summary>
    public interface IDiscordPipe : IDisposable
    {
        /// <summary>Connects to a running Discord client; false when none answers.</summary>
        bool TryOpen();

        /// <summary>Sends bytes; throws <see cref="System.IO.IOException"/> when the pipe is gone.</summary>
        void Write(byte[] data);

        /// <summary>Copies what Discord has sent into <paramref name="buffer"/> without waiting; 0 when nothing waits.</summary>
        int ReadAvailable(byte[] buffer);

        bool IsOpen { get; }
    }
}
