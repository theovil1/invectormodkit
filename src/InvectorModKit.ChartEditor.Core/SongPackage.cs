using System.IO;
using System.IO.Compression;
using System.Text;
using InvectorModKit.Charting;
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
}
