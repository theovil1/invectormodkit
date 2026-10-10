using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using InvectorModKit.Charting;
using InvectorModKit.Charting.Audio;
using InvectorModKit.Charting.Generation;
using InvectorModKit.Charting.Marketplace;

namespace InvectorModKit.ChartGen
{
    /// <summary>
    /// Generates one song's chart outside the game: on the .NET Framework CLR it runs several times faster
    /// than on the game's Mono, and the large audio buffers never reach the game's garbage collector.
    ///
    /// usage: InvectorModKit.ChartGen.exe &lt;audio&gt; &lt;chart.json&gt; [--vocals=&lt;model.onnx&gt;] [--threads=N]
    ///        [--preview=&lt;out.wav&gt;] [--preview-seconds=25]
    /// Writes the chart (and the loudness-matched preview excerpt), then prints one line: "ok &lt;details&gt;"
    /// with exit code 0, or "error &lt;message&gt;" with exit code 1.
    /// </summary>
    internal static class Program
    {
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string path);

        private static int Main(string[] args)
        {
            // Everything but this exe and Newtonsoft.Json sits in the parent folder (the core plugin's).
            string here = AppDomain.CurrentDomain.BaseDirectory;
            string libs = Path.GetFullPath(Path.Combine(here, ".."));
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                string file = Path.Combine(libs, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
            // UTF-8 whatever the console code page (song names), and no console needed at all.
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
                WatchParent(args);
                string native = Path.Combine(libs, "onnxruntime.dll");
                if (File.Exists(native)) LoadLibrary(native);
                stdout.WriteLine("ok " + Run(args));
                return 0;
            }
            catch (Exception e)
            {
                stdout.WriteLine("error " + e.GetBaseException().Message.Replace('\n', ' ').Replace('\r', ' '));
                return 1;
            }
        }

        /// <summary>--parent=&lt;pid&gt;: stop as soon as the game closes instead of finishing a chart nobody waits for.</summary>
        private static void WatchParent(string[] args)
        {
            string arg = args.FirstOrDefault(a => a.StartsWith("--parent="));
            if (arg == null || !int.TryParse(arg.Substring(9), out int pid)) return;
            Process parent;
            try { parent = Process.GetProcessById(pid); }
            catch (ArgumentException) { Environment.Exit(2); return; }
            new System.Threading.Thread(() => { parent.WaitForExit(); Environment.Exit(2); }) { IsBackground = true }.Start();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string Run(string[] args)
        {
            if (args.Length >= 3 && args[0] == "fingerprint")
            {
                // fingerprint <audio> <out.json> [--fingerprint-cache=<dir>]: what the marketplace publishes alongside a chart.
                string cacheDir = args.Skip(3).Where(a => a.StartsWith("--fingerprint-cache=")).Select(a => a.Substring(20)).LastOrDefault();
                var fp = CachedFingerprint(args[1], AudioFile.Hash(args[1]), cacheDir, () => AudioFile.Decode(args[1]));
                WriteWhole(args[2], fp.ToJson());
                return string.Format(CultureInfo.InvariantCulture, "fingerprint {0:0.0}s", fp.Duration);
            }
            if (args.Length < 2) throw new ArgumentException("usage: <audio> <chart.json> [--vocals=<model.onnx>] [--threads=N] [--preview=<out.wav>] [--preview-seconds=25] [--match=<CustomSongs/.marketplace>] [--match-only] [--fingerprint-cache=<dir>] | fingerprint <audio> <out.json> [--fingerprint-cache=<dir>]");
            string Option(string name) => args.Skip(2).Where(a => a.StartsWith("--" + name + "=")).Select(a => a.Substring(name.Length + 3)).LastOrDefault();
            string audioPath = args[0], chartPath = args[1], model = Option("vocals"), preview = Option("preview"), marketplace = Option("match"), fpCache = Option("fingerprint-cache");
            bool matchOnly = args.Contains("--match-only");
            int threads = int.TryParse(Option("threads"), out int t) ? t : Math.Max(1, Environment.ProcessorCount / 2);
            double previewSeconds = double.TryParse(Option("preview-seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out double s) ? s : 25;

            var clock = Stopwatch.StartNew();
            string hash = AudioFile.Hash(audioPath);
            DecodedAudio decoded = null;
            double decode = 0;
            DecodedAudio Decoded()
            {
                if (decoded != null) return decoded;
                var started = clock.Elapsed.TotalSeconds;
                decoded = AudioFile.Decode(audioPath);
                decode += clock.Elapsed.TotalSeconds - started;
                return decoded;
            }
            if (args.Contains("--profile")) return Profile(Decoded(), hash, model, threads, decode);

            // A chart installed from the marketplace and waiting for this very song comes first.
            var pending = string.IsNullOrEmpty(marketplace) ? new List<PendingInstall>() : PendingInstall.Load(marketplace).ToList();
            if (matchOnly && pending.Count == 0) return "nomatch (nothing waiting)";
            var (chart, install, match, nearMiss) = pending.Count == 0 ? default : FindPending(CachedFingerprint(audioPath, hash, fpCache, Decoded), pending);
            if (matchOnly && chart == null) return nearMiss != null ? $"nomatch, nearmiss {nearMiss.Id}" : "nomatch";
            string summary;
            if (chart != null)
                summary = string.Format(CultureInfo.InvariantCulture, "matched {0} offset {1:0.000}s score {2:0.00}", install.Id, match.Offset, match.Score);
            else
            {
                string vocals = "estimated from the mix";
                VocalSeparator separator = null;
                try
                {
                    if (!string.IsNullOrEmpty(model))
                    {
                        try { separator = new VocalSeparator(model, threads); vocals = "separated"; }
                        catch (Exception e) { vocals = "estimated from the mix (separation unavailable: " + e.GetBaseException().Message + ")"; }
                    }
                    chart = ChartGenerator.Generate(Decoded(), hash, null, separator);
                }
                finally { separator?.Dispose(); }
                summary = string.Format(CultureInfo.InvariantCulture, "{0:0.##} BPM, {1} Ultra notes, vocals {2}, vocal focus {3}{4}",
                    chart.Bpm, chart[Difficulty.Ultra].Count, vocals, ChartGenerator.VocalFocusUsed, nearMiss != null ? $", nearmiss {nearMiss.Id}" : "");
            }
            chart.Gain = Math.Round(Loudness.MatchOfficialGainDb(Decoded()), 2); // this file's loudness; as stored in the file
            WriteWhole(chartPath, ChartJson.Serialize(chart));
            install?.MarkInstalled(audioPath, match.Offset);
            double analysis = clock.Elapsed.TotalSeconds - decode;

            if (!string.IsNullOrEmpty(preview))
            {
                Loudness.ApplyGainLimited(Decoded(), chart.Gain.Value);
                AudioFile.WriteWavExcerpt(decoded, preview, chart.PreviewStart, previewSeconds);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0}, gain {1:+0.0;-0.0} dB; decode {2:0.0}s, analysis {3:0.0}s, total {4:0.0}s",
                summary, chart.Gain, decode, analysis, clock.Elapsed.TotalSeconds);
        }

        /// <summary>
        /// The marketplace chart waiting for this audio, placed on it; otherwise, an install it resembles
        /// without being the same version (the player is told rather than given a misplaced chart).
        /// </summary>
        private static (Chart chart, PendingInstall install, FingerprintMatch match, PendingInstall nearMiss) FindPending(Fingerprint local, List<PendingInstall> pending)
        {
            PendingInstall nearMiss = null;
            foreach (var install in pending)
            {
                var m = Fingerprint.Match(install.Fingerprint, local);
                if (m.IsMatch) return (install.ChartFor(m), install, m, null);
                if (m.Score >= Fingerprint.MatchThreshold) nearMiss = install;
            }
            return (null, null, null, nearMiss);
        }

        /// <summary>This audio's fingerprint, kept in {cacheDir}/{hash}.fp.json so songs are analysed once.</summary>
        private static Fingerprint CachedFingerprint(string audioPath, string hash, string cacheDir, Func<DecodedAudio> decode)
        {
            string cached = string.IsNullOrEmpty(cacheDir) ? null : Path.Combine(cacheDir, hash + ".fp.json");
            if (cached != null && File.Exists(cached))
            {
                try { return Fingerprint.FromJson(File.ReadAllText(cached)); }
                catch (Exception e) when (e is FormatException || e is Newtonsoft.Json.JsonException) { }
            }
            var fp = Fingerprint.Of(decode());
            if (cached != null)
            {
                Directory.CreateDirectory(cacheDir);
                WriteWhole(cached, fp.ToJson());
            }
            return fp;
        }

        /// <summary>Written whole or not at all: a half-written chart would fail to load next time.</summary>
        private static void WriteWhole(string path, string text)
        {
            File.WriteAllText(path + ".tmp", text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
        }

        private static string Profile(DecodedAudio decoded, string hash, string model, int threads, double decode)
        {
            var clock = Stopwatch.StartNew();
            double Lap() { double s = clock.Elapsed.TotalSeconds; clock.Restart(); return s; }
            var f = AudioAnalyzer.Analyze(decoded, estimateVocals: false); double mix = Lap();
            float[] stem; using (var sep = new VocalSeparator(model, threads)) { Lap(); stem = sep.SeparateMono(decoded); } double separate = Lap();
            VocalFeatures.Inject(f, stem, decoded.SampleRate); double inject = Lap();
            var tempo = TempoEstimator.Estimate(f); double tempoTime = Lap();
            ChartGenerator.Generate(f, tempo, hash); double generate = Lap();
            return string.Format(CultureInfo.InvariantCulture, "decode {0:0.0}s, mix analysis {1:0.0}s, separation {2:0.0}s, stem analysis {3:0.0}s, tempo {4:0.0}s, charting {5:0.0}s (focus {6})",
                decode, mix, separate, inject, tempoTime, generate, ChartGenerator.VocalFocusUsed);
        }
    }
}
