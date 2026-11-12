using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InvectorModKit.ChartEditor
{
    /// <summary>
    /// The level editor runs once per Windows session. A later start (the game's "Level editor" button, a second double
    /// click) hands its song to the window already open, over a named pipe, and closes: two windows would share
    /// WebView2's data folder, and the second one fails.
    /// </summary>
    public sealed class EditorInstance : IDisposable
    {
        public const string DefaultName = "InvectorLevelEditor";

        private readonly Mutex _mutex;
        private readonly string _name;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        private EditorInstance(Mutex mutex, string name)
        {
            _mutex = mutex;
            _name = name;
        }

        /// <summary>The song to open, from the editor's command line: its first argument that is not an option (--…).</summary>
        public static string SongArgument(string[] args) =>
            args?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a) && !a.StartsWith("--", StringComparison.Ordinal));

        /// <summary>This start's claim on the editor, or null when an editor window already holds it.</summary>
        public static EditorInstance TryClaim(string name)
        {
            var mutex = new Mutex(false, @"Local\" + name, out bool created);
            if (created) return new EditorInstance(mutex, name);
            mutex.Dispose();
            return null;
        }

        /// <summary>Hears the songs later starts hand over ("" for none), on a background thread, until disposed.</summary>
        public void Listen(Action<string> received)
        {
            var token = _stop.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using (var pipe = new NamedPipeServerStream(PipeName(_name), PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                        {
                            await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                            string song;
                            using (var reader = new StreamReader(pipe, Encoding.UTF8)) song = reader.ReadLine() ?? "";
                            received(song);
                        }
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception)
                    {
                        // a broken connection or a failing handler: listen again
                        if (!token.IsCancellationRequested) await Task.Delay(200).ConfigureAwait(false);
                    }
                }
            });
        }

        /// <summary>Hands a song ("" for none) to the editor window already open; false when none answers in time.</summary>
        public static bool Send(string name, string song, int timeoutMs)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName(name), PipeDirection.Out))
                {
                    pipe.Connect(timeoutMs);
                    byte[] line = Encoding.UTF8.GetBytes((song ?? "").Replace("\r", "").Replace("\n", "") + "\n");
                    pipe.Write(line, 0, line.Length);
                    pipe.Flush();
                    return true;
                }
            }
            catch (Exception) { return false; } // nobody listening (TimeoutException), or the window closing (IOException)
        }

        /// <summary>One pipe per Windows session, like the Local\ mutex.</summary>
        private static string PipeName(string name)
        {
            using (var self = Process.GetCurrentProcess()) return name + "." + self.SessionId;
        }

        public void Dispose()
        {
            _stop.Cancel();
            _mutex.Dispose();
        }
    }
}
