using System;
using System.Collections.Generic;
using InvectorModKit.Charting.Audio;

namespace InvectorModKit.Charting.Generation
{
    public sealed class GeneratorOptions
    {
        /// <summary>How strongly the official gap distribution shapes rhythms (0 = onsets only).</summary>
        public double GapWeight = 0.0;
        /// <summary>Multiplier on the model's expected note count, per difficulty.</summary>
        public double[] Density = { 1.0, 1.0, 1.0, 1.0 };
    }

    /// <summary>Builds a complete four-difficulty chart from decoded audio.</summary>
    public static class ChartGenerator
    {
        /// <summary>Bump whenever generation changes, so cached auto-generated charts are rebuilt.</summary>
        public const int Version = 1;

        public static Chart Generate(float[] mono, int sampleRate, string audioHash, GeneratorOptions options = null)
        {
            var f = AudioAnalyzer.Analyze(mono, sampleRate);
            return Generate(f, TempoEstimator.Estimate(f), audioHash, options);
        }

        public static Chart Generate(AudioFeatures f, TempoEstimate tempo, string audioHash, GeneratorOptions options = null)
        {
            options = options ?? new GeneratorOptions();
            var rng = Rng.FromString(audioHash);
            var (songStart, songEnd) = AudibleRange(f);

            var grid = BeatGrid.Build(f, tempo.Bpm, tempo.FirstBeat, songStart, songEnd);
            var sections = StructureAnalyzer.Analyze(f, grid, songStart, songEnd, rng);

            // Ultra first, over every audible grid position; each easier difficulty keeps a subset
            // of the next harder one (as in the official charts, where 99% of notes are shared).
            var rhythms = new List<int>[4];
            int[] candidates = null;
            for (int d = (int)Difficulty.Ultra; d >= 0; d--)
            {
                var diff = (Difficulty)d;
                if (candidates == null)
                {
                    var all = new List<int>();
                    int from = Math.Max(0, grid.IndexOf(songStart)), to = Math.Min(grid.Count - 1, grid.IndexOf(songEnd - 0.5));
                    for (int i = from; i <= to; i++)
                        if (grid.X[i][BeatGrid.FSilent] < 0.5f) all.Add(i);
                    candidates = all.ToArray();
                }
                double expected = 0;
                for (int i = Math.Max(0, grid.IndexOf(songStart)); i <= Math.Min(grid.Count - 1, grid.IndexOf(songEnd)); i++)
                    expected += NoteModel.Probability(diff, BeatGrid.Level(i), grid.X[i]);
                var score = new double[candidates.Length];
                for (int k = 0; k < candidates.Length; k++)
                    score[k] = NoteModel.Logit(diff, BeatGrid.Level(candidates[k]), grid.X[candidates[k]]);
                int target = (int)Math.Round(expected * options.Density[d]);
                rhythms[d] = RhythmDecoder.Decode(candidates, score, NoteModel.GapLogPrior(diff), options.GapWeight, target);
                candidates = rhythms[d].ToArray();
            }

            var patterns = new PatternAssigner(grid, sections, rng);
            patterns.AssignShared(rhythms);
            var chart = new Chart
            {
                Bpm = tempo.Bpm,
                FirstBeat = tempo.FirstBeat,
                StartTime = songStart,
                EndTime = songEnd,
                Sections = sections,
                Generator = new GeneratorStamp { Version = Version, AudioHash = audioHash },
            };
            for (int d = 0; d < 4; d++) chart.Notes[(Difficulty)d] = patterns.Build((Difficulty)d, rhythms[d]);
            return chart;
        }

        /// <summary>First and last moments the song is clearly audible (ignores leading/trailing silence).</summary>
        public static (double start, double end) AudibleRange(AudioFeatures f)
        {
            float median = BeatGrid.MedianLoudness(f);
            float threshold = median * 0.08f;
            int a = 0, b = f.Frames - 1;
            while (a < b && f.Rms[a] < threshold) a++;
            while (b > a && f.Rms[b] < threshold) b--;
            return (f.TimeOf(a), f.TimeOf(b));
        }
    }
}
