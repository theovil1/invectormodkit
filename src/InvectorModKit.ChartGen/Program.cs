using System;
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
            if (args.Length < 2) throw new ArgumentException("usage: <audio> <chart.json> [--vocals=<model.onnx>] [--threads=N] [--preview=<out.wav>] [--preview-seconds=25]");
            string Option(string name) => args.Skip(2).Where(a => a.StartsWith("--" + name + "=")).Select(a => a.Substring(name.Length + 3)).LastOrDefault();
            string audioPath = args[0], chartPath = args[1], model = Option("vocals"), preview = Option("preview");
            int threads = int.TryParse(Option("threads"), out int t) ? t : Math.Max(1, Environment.ProcessorCount / 2);
            double previewSeconds = double.TryParse(Option("preview-seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out double s) ? s : 25;

            var clock = Stopwatch.StartNew();
            string hash = AudioFile.Hash(audioPath);
            var decoded = AudioFile.Decode(audioPath);
            double decode = clock.Elapsed.TotalSeconds;
            if (args.Contains("--profile")) return Profile(decoded, hash, model, threads, decode);

            Chart chart;
            string vocals = "estimated from the mix";
            VocalSeparator separator = null;
            try
            {
                if (!string.IsNullOrEmpty(model))
                {
                    try { separator = new VocalSeparator(model, threads); vocals = "separated"; }
                    catch (Exception e) { vocals = "estimated from the mix (separation unavailable: " + e.GetBaseException().Message + ")"; }
                }
                chart = ChartGenerator.Generate(decoded, hash, null, separator);
            }
            finally { separator?.Dispose(); }
            chart.Gain = Math.Round(Loudness.MatchOfficialGainDb(decoded), 2); // as stored in the file
            // Written whole or not at all: a half-written chart would fail to load next time.
            File.WriteAllText(chartPath + ".tmp", ChartJson.Serialize(chart));
            if (File.Exists(chartPath)) File.Delete(chartPath);
            File.Move(chartPath + ".tmp", chartPath);
            double generate = clock.Elapsed.TotalSeconds - decode;

            if (!string.IsNullOrEmpty(preview))
            {
                Loudness.ApplyGainLimited(decoded, chart.Gain.Value);
                AudioFile.WriteWavExcerpt(decoded, preview, chart.PreviewStart, previewSeconds);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0:0.##} BPM, {1} Ultra notes, gain {2:+0.0;-0.0} dB, vocals {3}, vocal focus {4}; decode {5:0.0}s, analysis {6:0.0}s, total {7:0.0}s",
                chart.Bpm, chart[Difficulty.Ultra].Count, chart.Gain, vocals, ChartGenerator.VocalFocusUsed, decode, generate, clock.Elapsed.TotalSeconds);
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
