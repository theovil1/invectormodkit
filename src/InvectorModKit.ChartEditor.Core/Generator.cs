using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using InvectorModKit.Charting;
using InvectorModKit.Charting.Audio;
using InvectorModKit.Charting.Generation;

namespace InvectorModKit.ChartEditor
{
    /// <summary>
    /// Automatic charts for the editor: the mod's generator (ChartGen.exe, with vocal separation) when the game
    /// and the mod are installed, otherwise the same generator in-process on the mix alone.
    /// </summary>
    public sealed class Generator
    {
        private readonly string _chartGen, _vocals;

        public Generator(string gameDir)
        {
            if (string.IsNullOrEmpty(gameDir)) return;
            string plugins = Path.Combine(gameDir, "BepInEx", "plugins");
            _chartGen = Path.Combine(plugins, "InvectorModKit", "chartgen", "InvectorModKit.ChartGen.exe");
            _vocals = Path.Combine(plugins, "InvectorModKit.CustomSongs", "vocals.fp16.onnx");
        }

        /// <summary>True when the mod's own generator (the best one) is used.</summary>
        public bool UsesModGenerator => _chartGen != null && File.Exists(_chartGen);

        /// <summary>A full automatic chart of this audio file (worker thread; 15-60 s).</summary>
        public Chart Generate(string audioPath)
        {
            if (UsesModGenerator)
            {
                string output = Path.Combine(Path.GetTempPath(), "invector-editor-" + Guid.NewGuid().ToString("N") + ".chart.json");
                try
                {
                    string args = Quote(audioPath) + " " + Quote(output) + " --threads=" + Math.Max(1, Environment.ProcessorCount / 2);
                    if (File.Exists(_vocals)) args += " " + Quote("--vocals=" + _vocals);
                    var start = new ProcessStartInfo(_chartGen, args)
                    {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, // stderr not redirected: reading one pipe to the end while the other fills up would hang
                        StandardOutputEncoding = Encoding.UTF8,
                    };
                    using (var p = Process.Start(start))
                    {
                        string stdout = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        if (!File.Exists(output)) throw new InvalidOperationException("generator: " + stdout.Trim());
                    }
                    return ChartJson.Deserialize(File.ReadAllText(output));
                }
                finally
                {
                    try { File.Delete(output); } catch (Exception) { }
                }
            }
            var audio = AudioFile.Decode(audioPath);
            return ChartGenerator.Generate(audio, AudioFile.Hash(audioPath));
        }

        /// <summary>An empty chart on the detected beat grid: one List zone, start where the music starts.</summary>
        public static Chart Blank(string audioPath)
        {
            var audio = AudioFile.Decode(audioPath);
            var features = AudioAnalyzer.AnalyzeOnsets(audio.ToMono(), audio.SampleRate);
            var tempo = TempoEstimator.Estimate(features);
            var (start, _) = ChartGenerator.AudibleRange(features);
            var chart = new Chart
            {
                Bpm = Math.Round(tempo.Bpm, 3),
                FirstBeat = Math.Round(tempo.FirstBeat, 4),
                StartTime = Math.Max(0, Math.Round(start, 3)),
                EndTime = Math.Round(audio.Duration, 3),
                PreviewStart = Math.Round(audio.Duration * 0.3, 3),
            };
            chart.Sections.Add(new Section(0, SectionMode.List));
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty))) chart.Notes[d] = new System.Collections.Generic.List<Note>();
            return chart;
        }

        private static string Quote(string arg) => "\"" + arg.Replace("\"", "\\\"") + "\"";
    }
}
