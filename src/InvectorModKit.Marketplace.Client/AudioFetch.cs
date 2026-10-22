using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>
    /// Downloads a chart's song with the bundled yt-dlp + ffmpeg (shipped in BepInEx/plugins/InvectorModKit/
    /// audiotools): the file lands in CustomSongs as an ogg and ChartGen matches it to the pending install.
    /// A yt-dlp already on the PATH wins, so Wine/Proton players (who cannot always run the Windows exe) can
    /// have their own. Failure is not fatal: the message to drop the file by hand still applies.
    /// </summary>
    public static class AudioFetch
    {
        private const string YtDlpName = "yt-dlp.exe";
        public static readonly string[] LocalExtensions = { ".ogg", ".wav", ".mp3" };

        /// <summary>The player's file for this song, whatever audio type it has, or null.</summary>
        public static string Existing(string dir, string artist, string title)
        {
            string baseName = FileBase(artist, title);
            if (string.IsNullOrEmpty(baseName) || !Directory.Exists(dir)) return null;
            foreach (string ext in LocalExtensions)
            {
                string path = Path.Combine(dir, baseName + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>Where the downloaded audio goes: CustomSongs/Artist - Title.ogg.</summary>
        public static string Target(string dir, string artist, string title) => Path.Combine(dir, FileBase(artist, title) + ".ogg");

        /// <summary>
        /// The yt-dlp command to get that one video as an ogg (ffmpeg does the conversion). Note: output
        /// without extension, as yt-dlp's -x adds the audio type's extension itself (.ogg).
        /// </summary>
        public static string BuildArgs(string youtubeId, string outputWithoutExtension, string ffmpegLocation) => Arguments(new[]
        {
            "-x", "--audio-format", "vorbis", "--no-playlist", "--no-mtime",
            "--ffmpeg-location", ffmpegLocation, "-o", outputWithoutExtension, "https://youtu.be/" + youtubeId,
        });

        /// <summary>One command line with paths quoted the way the process creator expects.</summary>
        public static string Arguments(string[] args) => string.Join(" ", args.Select(Quote));

        private static string Quote(string arg) =>
            string.IsNullOrWhiteSpace(arg) ? "\"\"" : arg.Any(char.IsWhiteSpace) ? "\"" + arg + "\"" : arg;

        /// <summary>
        /// yt-dlp above the plugin dll, or a yt-dlp from the PATH. Returns null paths when nothing is there;
        /// without yt-dlp the player drops the song by hand, so this stays silent ("audio missing" as before).
        /// </summary>
        public static (string YtDlp, string FfmpegDir) Resolve(string pluginDir)
        {
            bool onWindows = Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.MacOSX;
            string bundledYtDlp = Path.Combine(pluginDir, "audiotools", YtDlpName);
            string bundledDir = Path.GetDirectoryName(bundledYtDlp);
            if (onWindows && File.Exists(bundledYtDlp)) return (bundledYtDlp, bundledDir);
            string PathOf(string name) => FindOnPath(name);
            if (PathOf("yt-dlp") is string pathYtDlp) return (pathYtDlp, pathYtDlp);
            if (!onWindows && File.Exists(bundledYtDlp)) return (bundledYtDlp, bundledDir);
            return (null, null);
        }

        private static string FindOnPath(string name)
        {
            string pathVar = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVar)) return null;
            char separator = Environment.OSVersion.Platform == PlatformID.Unix ? ':' : ';';
            string suffix = Environment.OSVersion.Platform == PlatformID.Unix ? "" : ".exe";
            foreach (string dir in pathVar.Split(separator).Where(p => p.Length > 0))
            {
                string candidate = Path.Combine(dir, name + suffix);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>Runs yt-dlp (which calls ffmpeg), feeding its last lines as progress (yt-dlp's own progress bars).</summary>
        public static void Download(string ytDlpPath, string argsLine, Action<string> log = null)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = argsLine,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            object gate = new object();
            string tail = "";
            DataReceivedEventHandler collect = (_, e) => { if (e.Data != null) lock (gate) tail = tail + e.Data + "\n"; };
            process.OutputDataReceived += collect;
            process.ErrorDataReceived += collect;
            Log(log, ytDlpPath + " " + argsLine);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds))
            {
                try { process.Kill(); } catch { }
                throw new AudioFetchException("yt-dlp did not finish in 10 minutes");
            }
            if (process.ExitCode != 0)
            {
                lock (gate) Log(log, tail.Length > 600 ? "…" + tail.Substring(tail.Length - 600) : tail);
                throw new AudioFetchException($"yt-dlp failed (exit {process.ExitCode})");
            }
        }

        /// <summary>Nothing is kept from a failed fetch: yt-dlp downloads into a temp folder, this moves the result.</summary>
        public static void Move(string from, string to)
        {
            if (File.Exists(to)) File.Delete(to);
            try { File.Move(from, to); }
            catch (System.IO.IOException) { File.Copy(from, to, true); File.Delete(from); }
        }

        /// <summary>
        /// (channel, title) of a video, to name its file "Channel - Title" without downloading anything.
        /// (null, null) when yt-dlp refused (video gone, no network): the caller has no name then.
        /// </summary>
        public static (string Channel, string Title) VideoName(string ytDlpPath, string youtubeId, Action<string> log = null)
        {
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = Arguments(new[] { "--no-playlist", "--no-download", "--print", "%(channel)s", "--print", "%(title)s", "https://youtu.be/" + youtubeId }),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            })
            {
                Log(log, ytDlpPath + " (name of " + youtubeId + ")");
                process.Start();
                var lines = process.StandardOutput.ReadToEnd().Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
                if (!process.WaitForExit(90_000) || process.ExitCode != 0 || lines.Length < 2) return (null, null);
                return (lines[0], lines.Last());
            }
        }

        private static string FileBase(string artist, string title)
        {
            string a = Sanitize(artist), t = Sanitize(title);
            if (t.Length == 0) return "";
            return a.Length == 0 ? t : a + " - " + t;
        }

        private static string Sanitize(string text)
        {
            string trimmed = text == null ? "" : text.Trim();
            var forbidden = new[] { '"', '<', '>', '|', ':', '/', '\\', '?', '*' };
            var kept = trimmed.Select(c => forbidden.Contains(c) ? '_' : c).ToArray();
            string name = new string(kept).Trim('.', ' ');
            return name.Length > 80 ? name.Substring(0, 80).Trim('.', ' ') : name;
        }

        private static void Log(Action<string> log, string message)
        {
            if (log != null) log.Invoke(message);
        }
    }

    public sealed class AudioFetchException : Exception
    {
        public AudioFetchException(string message) : base(message) { }
    }
}
