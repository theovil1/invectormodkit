using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using InvectorModKit.Charting;
using InvectorModKit.Charting.Audio;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>
    /// A song shared as one zip, to play it on another PC without the marketplace: its audio, its chart (hand-made,
    /// so never regenerated there), its cover and song.json, all at the zip's root. Dropped loose into CustomSongs or
    /// extracted into a folder of its own, the mod reads it the same way.
    /// </summary>
    public static class SongPackage
    {
        private static readonly string[] FolderCovers = { "cover.png", "cover.jpg", "cover.jpeg", "folder.jpg", "folder.png" };
        private static readonly string[] ImageExtensions = { ".jpg", ".png", ".jpeg" };

        /// <summary>Writes the zip (through a temporary file, so a failed export leaves no broken zip at <paramref name="zipPath"/>).</summary>
        public static void Export(string audioPath, Chart chart, string title, string artist, string customSongsDir, string zipPath)
        {
            string name = Path.GetFileNameWithoutExtension(audioPath);
            chart.Generator = null;
            chart.Source = null;
            string cover = CoverOf(audioPath, customSongsDir), tmp = zipPath + ".tmp";
            try
            {
                using (var zip = new ZipArchive(File.Create(tmp), ZipArchiveMode.Create))
                {
                    AddFile(zip, Path.GetFileName(audioPath), audioPath);
                    AddText(zip, name + ".chart.json", ChartJson.Serialize(chart));
                    if (cover != null) AddFile(zip, name + Path.GetExtension(cover).ToLowerInvariant(), cover);
                    AddText(zip, "song.json", new JObject { ["title"] = title ?? "", ["artist"] = artist ?? "" }.ToString());
                }
                File.Copy(tmp, zipPath, true);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        /// <summary>
        /// The picture the game shows for that song (its SongLibrary's order): cover.* or folder.* in the song's own
        /// folder of CustomSongs, else the image named like the audio. Null when there is none.
        /// </summary>
        public static string CoverOf(string audioPath, string customSongsDir)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(audioPath));
            if (SongFiles.InOwnFolder(customSongsDir, audioPath))
                foreach (string name in FolderCovers)
                    if (File.Exists(Path.Combine(folder, name))) return Path.Combine(folder, name);
            string like = Path.Combine(folder, Path.GetFileNameWithoutExtension(audioPath));
            foreach (string ext in ImageExtensions)
                if (File.Exists(like + ext)) return like + ext;
            return null;
        }

        /// <summary>A song is a few files: more entries or bytes than this is not one (or is a zip bomb).</summary>
        public const int MaxEntries = 50;
        public const long MaxBytes = 300L * 1024 * 1024;

        /// <summary>
        /// Puts a shared song zip into CustomSongs, in a folder of its own ("Artist - Title" from its song.json, else the
        /// zip's name, numbered when taken), and returns its audio's path. The zip is checked before anything is
        /// written (refusals throw <see cref="SongPackageException"/>). Files are taken by name only, never by the
        /// zip's paths, into a hidden folder (the game skips names starting with "."), then moved into place at once:
        /// the game never sees half a song, and a failure leaves nothing behind.
        /// </summary>
        public static string Import(string zipPath, string customSongsDir, long maxBytes = MaxBytes)
        {
            string tmp = null;
            try
            {
                using (var file = File.OpenRead(zipPath))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    var song = Pick(zip.Entries, maxBytes);
                    var (title, artist) = ReadInfo(song.Info);
                    string folderName = string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist)
                        ? SongFiles.FolderName(null, Path.GetFileNameWithoutExtension(zipPath))
                        : SongFiles.FolderName(artist, title);

                    tmp = Path.Combine(customSongsDir, ".import-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tmp);
                    long budget = maxBytes;
                    string audioName = NameOf(song.Audio), like = Path.GetFileNameWithoutExtension(audioName);
                    if (song.Chart != null) Extract(song.Chart, Path.Combine(tmp, like + ".chart.json"), ref budget);
                    if (song.Image != null) Extract(song.Image, Path.Combine(tmp, like + Path.GetExtension(NameOf(song.Image)).ToLowerInvariant()), ref budget);
                    if (song.Info != null) File.WriteAllText(Path.Combine(tmp, "song.json"), new JObject { ["title"] = title ?? "", ["artist"] = artist ?? "" }.ToString());
                    Extract(song.Audio, Path.Combine(tmp, audioName), ref budget);

                    string folder = SongFiles.FreeFolder(customSongsDir, folderName);
                    Directory.Move(tmp, folder);
                    tmp = null;
                    return Path.Combine(folder, audioName);
                }
            }
            catch (InvalidDataException)
            {
                throw new SongPackageException(ImportRefusal.NotZip);
            }
            finally
            {
                if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true);
            }
        }

        private sealed class Song
        {
            public ZipArchiveEntry Audio, Chart, Image, Info;
        }

        /// <summary>The song's entries, or a refusal: unsafe paths, too big, not exactly one audio file.</summary>
        private static Song Pick(IReadOnlyCollection<ZipArchiveEntry> entries, long maxBytes)
        {
            if (entries.Count > MaxEntries || entries.Sum(e => e.Length) > maxBytes) throw new SongPackageException(ImportRefusal.TooBig);
            var audio = new List<ZipArchiveEntry>();
            var charts = new List<ZipArchiveEntry>();
            var images = new List<ZipArchiveEntry>();
            ZipArchiveEntry info = null;
            foreach (var e in entries)
            {
                string path = e.FullName.Replace('\\', '/');
                string[] parts = path.Split('/');
                if (path.StartsWith("/") || path.Contains(":") || parts.Contains("..")) throw new SongPackageException(ImportRefusal.Unsafe);
                string name = parts[parts.Length - 1];
                // folders, and what macOS adds to the zips it makes (__MACOSX/, ._ files, .DS_Store)
                if (name.Length == 0 || name.StartsWith(".") || parts[0].Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new SongPackageException(ImportRefusal.Unsafe);
                if (AudioFile.IsSupported(name)) audio.Add(e);
                else if (name.EndsWith(".chart.json", StringComparison.OrdinalIgnoreCase)) charts.Add(e);
                else if (ImageExtensions.Contains(Path.GetExtension(name).ToLowerInvariant())) images.Add(e);
                else if (name.Equals("song.json", StringComparison.OrdinalIgnoreCase)) info = info ?? e;
            }
            if (audio.Count == 0) throw new SongPackageException(ImportRefusal.NoSong);
            if (audio.Count > 1) throw new SongPackageException(ImportRefusal.SeveralSongs);
            string like = Path.GetFileNameWithoutExtension(NameOf(audio[0]));
            bool NamedLike(ZipArchiveEntry e, string suffix) => NameOf(e).Equals(like + suffix, StringComparison.OrdinalIgnoreCase);
            return new Song
            {
                Audio = audio[0],
                Chart = charts.FirstOrDefault(c => NamedLike(c, ".chart.json")) ?? charts.FirstOrDefault(),
                Image = images.FirstOrDefault(i => NamedLike(i, Path.GetExtension(NameOf(i)))) ?? images.FirstOrDefault(),
                Info = info,
            };
        }

        private static string NameOf(ZipArchiveEntry e) => e.FullName.Replace('\\', '/').Split('/').Last();

        /// <summary>Title and artist from song.json; nulls when it is missing, too big or not JSON.</summary>
        private static (string Title, string Artist) ReadInfo(ZipArchiveEntry info)
        {
            if (info == null) return (null, null);
            try
            {
                using (var reader = new StreamReader(info.Open(), Encoding.UTF8))
                {
                    var text = new char[64 * 1024];
                    int n = reader.ReadBlock(text, 0, text.Length);
                    if (n == text.Length) return (null, null);
                    var o = JObject.Parse(new string(text, 0, n));
                    return ((string)o["title"], (string)o["artist"]);
                }
            }
            catch (Exception e) when (e is Newtonsoft.Json.JsonException || e is InvalidCastException || e is ArgumentException)
            {
                return (null, null);
            }
        }

        /// <summary>Copies an entry, counting the bytes really unpacked: a zip that lies about its sizes still stops at the limit.</summary>
        private static void Extract(ZipArchiveEntry entry, string path, ref long budget)
        {
            using (var source = entry.Open())
            using (var target = File.Create(path))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    budget -= n;
                    if (budget < 0) throw new SongPackageException(ImportRefusal.TooBig);
                    target.Write(buffer, 0, n);
                }
            }
        }

        private static void AddFile(ZipArchive zip, string entry, string path)
        {
            using (var source = File.OpenRead(path))
            using (var target = zip.CreateEntry(entry).Open())
                source.CopyTo(target);
        }

        private static void AddText(ZipArchive zip, string entry, string text)
        {
            using (var writer = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false)))
                writer.Write(text);
        }
    }

    public enum ImportRefusal { NotZip, Unsafe, TooBig, NoSong, SeveralSongs }

    public sealed class SongPackageException : Exception
    {
        public readonly ImportRefusal Reason;
        public SongPackageException(ImportRefusal reason) : base("song zip refused: " + reason) => Reason = reason;
    }
}
