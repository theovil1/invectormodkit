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

        public SongLibrary(string root)
        {
            Root = root;
            CacheDir = Path.Combine(root, ".cache");
        }

        public void Scan()
        {
            Songs.Clear();
            if (!Directory.Exists(Root)) return;
            foreach (string file in Directory.GetFiles(Root).Where(AudioFile.IsSupported).OrderBy(f => f))
                Songs.Add(Create(file, Root));
            foreach (string dir in Directory.GetDirectories(Root).Where(d => !Path.GetFileName(d).StartsWith(".")).OrderBy(d => d))
            {
                string audio = Directory.GetFiles(dir).Where(AudioFile.IsSupported).OrderBy(f => f).FirstOrDefault();
                if (audio != null) Songs.Add(Create(audio, dir));
            }
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

        /// <summary>Runs on a worker thread. Never touches Unity objects.</summary>
        public void Prepare(CustomSong song, Action<string> log)
        {
            song.State = SongState.Processing;
            try
            {
                ReadMetadata(song);
                string hash = AudioFile.Hash(song.AudioPath);
                DecodedAudio decoded = null;

                Chart chart = null;
                if (File.Exists(song.ChartPath))
                {
                    chart = ChartJson.Deserialize(File.ReadAllText(song.ChartPath));
                    bool stale = chart.Generator != null && (chart.Generator.Version != ChartGenerator.Version || chart.Generator.AudioHash != hash);
                    if (stale)
                    {
                        log($"{song}: auto chart is out of date, regenerating");
                        chart = null;
                    }
                }
                if (chart == null)
                {
                    var t0 = DateTime.UtcNow;
                    decoded = AudioFile.Decode(song.AudioPath);
                    var t1 = DateTime.UtcNow;
                    chart = ChartGenerator.Generate(decoded.ToMono(), decoded.SampleRate, hash);
                    chart.Gain = Loudness.MatchOfficialGainDb(decoded);
                    File.WriteAllText(song.ChartPath, ChartJson.Serialize(chart));
                    log($"{song}: generated chart ({chart.Bpm:0.##} BPM, {chart[ChartDifficulty.Ultra].Count} Ultra notes, gain {chart.Gain:+0.0;-0.0} dB; decode {(t1 - t0).TotalSeconds:0.0}s, analysis {(DateTime.UtcNow - t1).TotalSeconds:0.0}s)");
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
                if (!File.Exists(song.PreviewPath))
                {
                    decoded = decoded ?? AudioFile.Decode(song.AudioPath);
                    Loudness.ApplyGainLimited(decoded, chart.Gain.Value);
                    foreach (var old in Directory.Exists(CacheDir) ? Directory.GetFiles(CacheDir, song.Id + "-*.preview.wav") : new string[0])
                        File.Delete(old);
                    AudioFile.WriteWavExcerpt(decoded, song.PreviewPath, chart.PreviewStart, PreviewSeconds);
                }

                song.Chart = chart;
                song.State = SongState.Ready;
            }
            catch (Exception e)
            {
                song.Error = e.Message;
                song.State = SongState.Failed;
                log($"{Path.GetFileName(song.AudioPath)}: FAILED - {e}");
            }
        }

        private static readonly System.Text.RegularExpressions.Regex VideoJunk = new System.Text.RegularExpressions.Regex(
            @"\s*[\(\[][^\)\]]*\b(official|video|audio|lyrics?|clip|visuali[sz]er|hd|hq|4k|remaster(ed)?|explicit|music)\b[^\)\]]*[\)\]]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>"Coldplay - Viva La Vida (Official Video)" by "Coldplay" becomes "Viva La Vida".</summary>
        internal static string CleanTitle(string title, string artist)
        {
            string t = VideoJunk.Replace(title ?? "", "").Trim();
            if (!string.IsNullOrEmpty(artist) && t.StartsWith(artist + " - ", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(artist.Length + 3).Trim();
            return string.IsNullOrEmpty(t) ? title : t;
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
            string baseName = Path.GetFileNameWithoutExtension(song.AudioPath);
            int dash = baseName.IndexOf(" - ", StringComparison.Ordinal);
            if (string.IsNullOrEmpty(song.Title)) song.Title = dash > 0 ? baseName.Substring(dash + 3).Trim() : baseName;
            if (string.IsNullOrEmpty(song.Artist)) song.Artist = dash > 0 ? baseName.Substring(0, dash).Trim() : "Unknown artist";
            song.Title = CleanTitle(song.Title, song.Artist);

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
