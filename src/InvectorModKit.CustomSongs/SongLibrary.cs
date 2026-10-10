using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using InvectorModKit.Charting;
using InvectorModKit.Charting.Audio;
using InvectorModKit.Charting.Generation;
using Newtonsoft.Json.Linq;
using ChartDifficulty = InvectorModKit.Charting.Difficulty;

namespace InvectorModKit.CustomSongs
{
    /// <summary>
    /// Finds songs in the CustomSongs folder and prepares them off the main thread: metadata, chart
    /// (loaded, or generated and saved next to the audio) and a short preview excerpt.
    ///
    /// Layouts: a loose "Artist - Title.mp3" in the root, or a sub-folder per song holding one audio
    /// file plus optional cover.png/jpg, song.json ({"title": "...", "artist": "..."}) and chart.
    /// </summary>
    internal sealed class SongLibrary
    {
        public const double PreviewSeconds = 25;
        private static readonly string[] CoverNames = { "cover.png", "cover.jpg", "cover.jpeg", "folder.jpg", "folder.png" };

        public readonly string Root;
        public readonly string CacheDir;
        public readonly List<CustomSong> Songs = new List<CustomSong>();
        /// <summary>Spleeter vocal model; null or missing file = chart from the mix alone.</summary>
        public string VocalModelPath;
        /// <summary>InvectorModKit.ChartGen.exe; null or missing = charts are generated in the game process.</summary>
        public string GeneratorPath;
        private VocalSeparator _separator;
        private bool _separatorFailed, _generatorFailed;

        public SongLibrary(string root)
        {
            Root = root;
            CacheDir = Path.Combine(root, ".cache");
        }

        public void Scan()
        {
            Songs.Clear();
            foreach (var (audio, folder) in Enumerate()) Songs.Add(Create(audio, folder));
        }

        /// <summary>
        /// Worker thread, while the game runs: songs whose audio appeared since <paramref name="known"/> was
        /// listed (only once fully written), and known songs whose audio is gone.
        /// </summary>
        public (List<CustomSong> Added, List<CustomSong> Removed) FindChanges(IList<CustomSong> known)
        {
            var knownPaths = new HashSet<string>(known.Select(s => s.AudioPath), StringComparer.OrdinalIgnoreCase);
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var added = new List<CustomSong>();
            foreach (var (audio, folder) in Enumerate())
            {
                present.Add(audio);
                if (!knownPaths.Contains(audio) && IsComplete(audio)) added.Add(Create(audio, folder));
            }
            return (added, known.Where(s => !present.Contains(s.AudioPath)).ToList());
        }

        /// <summary>Loose audio files in the root, then the first audio file of each song folder.</summary>
        private IEnumerable<(string Audio, string Folder)> Enumerate()
        {
            if (!Directory.Exists(Root)) yield break;
            foreach (string file in Directory.GetFiles(Root).Where(AudioFile.IsSupported).OrderBy(f => f))
                yield return (file, Root);
            foreach (string dir in Directory.GetDirectories(Root).Where(d => !Path.GetFileName(d).StartsWith(".")).OrderBy(d => d))
            {
                string audio = Directory.GetFiles(dir).Where(AudioFile.IsSupported).OrderBy(f => f).FirstOrDefault();
                if (audio != null) yield return (audio, dir);
            }
        }

        /// <summary>Not still being copied: the size holds still and nobody else is writing to it.</summary>
        private static bool IsComplete(string path)
        {
            try
            {
                long size = new FileInfo(path).Length;
                System.Threading.Thread.Sleep(300);
                if (size == 0 || new FileInfo(path).Length != size) return false;
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) { }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private CustomSong Create(string audioPath, string folder)
        {
            string rel = audioPath.Substring(Root.Length).TrimStart('\\', '/').Replace('\\', '/').ToLowerInvariant();
            string id;
            using (var sha = SHA1.Create())
                id = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(rel))).Replace("-", "").Substring(0, 12).ToLowerInvariant();
            return new CustomSong
            {
                Id = id,
                AudioPath = audioPath,
                Folder = folder,
                ChartPath = Path.Combine(Path.GetDirectoryName(audioPath), Path.GetFileNameWithoutExtension(audioPath) + ".chart.json"),
            };
        }

        /// <summary>
        /// Fast pass (worker thread): title, artist, cover and length, so the song can be listed before
        /// its chart is ready. Reads the cached chart for the length when there is one.
        /// </summary>
        public void ReadInfo(CustomSong song, Action<string> log)
        {
            try
            {
                ReadMetadata(song);
                song.HasMetadata = true;
                if (File.Exists(song.ChartPath))
                {
                    try { song.Duration = ChartJson.Deserialize(File.ReadAllText(song.ChartPath)).EndTime; }
                    catch (Exception) { }
                }
                if (song.Duration <= 0) song.Duration = AudioFile.ProbeDuration(song.AudioPath);
            }
            catch (Exception e)
            {
                song.Error = e.Message;
                song.State = SongState.Failed;
                log($"{Path.GetFileName(song.AudioPath)}: FAILED - {e.Message}");
            }
        }

        /// <summary>Runs on a worker thread. Never touches Unity objects.</summary>
        public void Prepare(CustomSong song, Action<string> log)
        {
            song.State = SongState.Processing;
            try
            {
                if (!song.HasMetadata) ReadMetadata(song);
                string hash = AudioFile.Hash(song.AudioPath);
                DecodedAudio decoded = null;

                Chart chart = null;
                if (File.Exists(song.ChartPath))
                {
                    chart = ChartJson.Deserialize(File.ReadAllText(song.ChartPath));
                    bool stale = ChartGenerator.IsStale(chart, hash);
                    if (stale)
                    {
                        log($"{song}: auto chart is out of date, regenerating");
                        chart = null;
                    }
                }
                if (chart == null && GenerateOutOfProcess(song, hash, log))
                    chart = ChartJson.Deserialize(File.ReadAllText(song.ChartPath));
                if (chart == null)
                {
                    var t0 = DateTime.UtcNow;
                    decoded = AudioFile.Decode(song.AudioPath);
                    var t1 = DateTime.UtcNow;
                    var separator = GetSeparator(log);
                    chart = ChartGenerator.Generate(decoded, hash, null, separator);
                    chart.Gain = Math.Round(Loudness.MatchOfficialGainDb(decoded), 2); // as stored in the file, so later sessions agree
                    File.WriteAllText(song.ChartPath, ChartJson.Serialize(chart));
                    log($"{song}: generated chart ({chart.Bpm:0.##} BPM, {chart[ChartDifficulty.Ultra].Count} Ultra notes, gain {chart.Gain:+0.0;-0.0} dB, vocals {(separator != null ? "separated" : "estimated from the mix")}; decode {(t1 - t0).TotalSeconds:0.0}s, analysis {(DateTime.UtcNow - t1).TotalSeconds:0.0}s)");
                }
                if (chart[ChartDifficulty.Casual].Count == 0 && chart[ChartDifficulty.Ultra].Count == 0)
                    throw new InvalidDataException("the chart has no notes");
                if (!chart.Gain.HasValue)
                {
                    // Hand-made chart without a "gain": measure it (not written back to the user's file).
                    decoded = decoded ?? AudioFile.Decode(song.AudioPath);
                    chart.Gain = Loudness.MatchOfficialGainDb(decoded);
                }
                foreach (var issue in Enum.GetValues(typeof(ChartDifficulty)).Cast<ChartDifficulty>().SelectMany(d => Playability.Validate(chart, d).Take(3).Select(i => d + " " + i)))
                    log($"{song}: playability warning: {issue}");

                song.PreviewPath = Path.Combine(CacheDir, $"{song.Id}-{hash}-{chart.Gain.Value:0.0}dB.preview.wav");
                string generatedPreview = PendingPreviewPath(song, hash);
                if (!File.Exists(song.PreviewPath) && File.Exists(generatedPreview))
                {
                    foreach (var old in Directory.GetFiles(CacheDir, song.Id + "-*.preview.wav")) File.Delete(old);
                    File.Move(generatedPreview, song.PreviewPath);
                }
                if (File.Exists(generatedPreview)) File.Delete(generatedPreview);
                if (!File.Exists(song.PreviewPath))
                {
                    decoded = decoded ?? AudioFile.Decode(song.AudioPath);
                    Loudness.ApplyGainLimited(decoded, chart.Gain.Value);
                    foreach (var old in Directory.Exists(CacheDir) ? Directory.GetFiles(CacheDir, song.Id + "-*.preview.wav") : new string[0])
                        File.Delete(old);
                    AudioFile.WriteWavExcerpt(decoded, song.PreviewPath, chart.PreviewStart, PreviewSeconds);
                }

                song.Chart = chart;
                song.Duration = chart.EndTime;
                song.State = SongState.Ready;
            }
            catch (Exception e)
            {
                song.Error = e.Message;
                song.State = SongState.Failed;
                log($"{Path.GetFileName(song.AudioPath)}: FAILED - {e}");
            }
        }

        private string PendingPreviewPath(CustomSong song, string hash) => Path.Combine(CacheDir, $"{song.Id}-{hash}.preview.tmp");

        /// <summary>
        /// Generates the chart (and preview) with the external generator: the .NET Framework that ships with
        /// Windows runs it several times faster than the game's Mono, and none of its memory or CPU time
        /// competes with the game. False (after logging why) when it cannot, so the caller generates in process.
        /// </summary>
        private bool GenerateOutOfProcess(CustomSong song, string hash, Action<string> log)
        {
            if (_generatorFailed || string.IsNullOrEmpty(GeneratorPath) || !File.Exists(GeneratorPath)) return false;
            try
            {
                Directory.CreateDirectory(CacheDir);
                var args = new List<string> { song.AudioPath, song.ChartPath, "--preview=" + PendingPreviewPath(song, hash), "--preview-seconds=" + PreviewSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--threads=" + Math.Max(1, Environment.ProcessorCount / 2), "--parent=" + System.Diagnostics.Process.GetCurrentProcess().Id };
                if (!string.IsNullOrEmpty(VocalModelPath) && File.Exists(VocalModelPath)) args.Add("--vocals=" + VocalModelPath);
                var start = new System.Diagnostics.ProcessStartInfo(GeneratorPath, string.Join(" ", args.Select(Quote)))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                string result;
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    result = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                }
                if (result.StartsWith("ok ") && File.Exists(song.ChartPath))
                {
                    log($"{song}: generated chart ({result.Substring(3)})");
                    return true;
                }
                log($"{song}: the external chart generator failed ({result}), generating in game instead");
            }
            catch (Exception e)
            {
                // Typically: the generator cannot start at all. Don't retry it for every song.
                _generatorFailed = true;
                log($"External chart generator unavailable ({e.GetBaseException().Message}), generating in game instead");
            }
            return false;
        }

        /// <summary>Command-line quoting for CreateProcess (paths with spaces; no trailing backslashes in ours).</summary>
        private static string Quote(string arg) => "\"" + arg.Replace("\"", "\\\"") + "\"";

        /// <summary>Created on first use (worker thread); any failure falls back to charting from the mix alone.</summary>
        private VocalSeparator GetSeparator(Action<string> log)
        {
            if (_separator != null || _separatorFailed) return _separator;
            try
            {
                if (string.IsNullOrEmpty(VocalModelPath) || !File.Exists(VocalModelPath)) throw new FileNotFoundException("vocal model not found", VocalModelPath);
                _separator = new VocalSeparator(VocalModelPath, Math.Max(1, Environment.ProcessorCount / 2));
            }
            catch (Exception e)
            {
                _separatorFailed = true;
                log("Vocal separation unavailable, charts follow the mix only: " + e.GetBaseException().Message);
            }
            return _separator;
        }

        private void ReadMetadata(CustomSong song)
        {
            // Priority: song.json > ID3 tags > "Artist - Title" file name.
            if (Path.GetExtension(song.AudioPath).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                var tags = Id3Tags.Read(song.AudioPath);
                song.Title = tags.Title;
                song.Artist = tags.Artist;
                song.Cover = tags.Cover;
            }
            var (fileArtist, fileTitle) = SongMetadata.SplitFileName(Path.GetFileNameWithoutExtension(song.AudioPath));
            if (string.IsNullOrEmpty(song.Title)) song.Title = fileTitle;
            if (string.IsNullOrEmpty(song.Artist)) song.Artist = fileArtist;
            (song.Artist, song.Title) = SongMetadata.Normalize(song.Artist, song.Title);
            if (string.IsNullOrEmpty(song.Artist)) song.Artist = "Unknown artist";

            // song.json and cover images belong to a song's own folder, never to the shared root.
            if (string.Equals(Path.GetFullPath(song.Folder), Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase)) return;
            string info = Path.Combine(song.Folder, "song.json");
            if (File.Exists(info))
            {
                var o = JObject.Parse(File.ReadAllText(info));
                song.Title = (string)o["title"] ?? song.Title;
                song.Artist = (string)o["artist"] ?? song.Artist;
            }
            foreach (string name in CoverNames)
            {
                string p = Path.Combine(song.Folder, name);
                if (File.Exists(p)) { song.Cover = File.ReadAllBytes(p); break; }
            }
        }
    }
}
