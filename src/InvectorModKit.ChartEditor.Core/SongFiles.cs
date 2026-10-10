using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InvectorModKit.Charting;
using InvectorModKit.Charting.Audio;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>A song on disk: its audio, its chart file, its title and artist.</summary>
    public sealed class SongFile
    {
        public string AudioPath, ChartPath, Title, Artist;
        public bool HasChart => File.Exists(ChartPath);
    }

    /// <summary>The CustomSongs folder as the mod reads it (same rules as Custom Songs' SongLibrary).</summary>
    public static class SongFiles
    {
        public static string ChartPathFor(string audioPath) =>
            Path.Combine(Path.GetDirectoryName(audioPath), Path.GetFileNameWithoutExtension(audioPath) + ".chart.json");

        /// <summary>Loose audio files in the root, then the first audio file of each song folder.</summary>
        public static List<SongFile> Enumerate(string root)
        {
            var songs = new List<SongFile>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return songs;
            foreach (string file in Directory.GetFiles(root).Where(AudioFile.IsSupported).OrderBy(f => f))
                songs.Add(Read(file));
            foreach (string dir in Directory.GetDirectories(root).Where(d => !Path.GetFileName(d).StartsWith(".")).OrderBy(d => d))
            {
                string audio = Directory.GetFiles(dir).Where(AudioFile.IsSupported).OrderBy(f => f).FirstOrDefault();
                if (audio != null) songs.Add(Read(audio));
            }
            return songs;
        }

        /// <summary>Title and artist: song.json in the song's folder, else ID3 tags, else "Artist - Title" in the file name.</summary>
        public static SongFile Read(string audioPath)
        {
            string title = null, artist = null;
            if (Path.GetExtension(audioPath).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var tags = Id3Tags.Read(audioPath);
                    title = tags.Title;
                    artist = tags.Artist;
                }
                catch (Exception) { } // tags are optional
            }
            var (fileArtist, fileTitle) = SongMetadata.SplitFileName(Path.GetFileNameWithoutExtension(audioPath));
            if (string.IsNullOrEmpty(title)) title = fileTitle;
            if (string.IsNullOrEmpty(artist)) artist = fileArtist;
            (artist, title) = SongMetadata.Normalize(artist, title);
            string info = Path.Combine(Path.GetDirectoryName(audioPath), "song.json");
            if (File.Exists(info))
            {
                try
                {
                    var o = JObject.Parse(File.ReadAllText(info));
                    title = (string)o["title"] ?? title;
                    artist = (string)o["artist"] ?? artist;
                }
                catch (Exception) { }
            }
            return new SongFile { AudioPath = audioPath, ChartPath = ChartPathFor(audioPath), Title = title ?? "", Artist = string.IsNullOrEmpty(artist) ? "Unknown artist" : artist };
        }

        public static bool IsInside(string folder, string path)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(path)) return false;
            string f = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"Artist - Title" usable as a folder name (Windows-safe, not too long).</summary>
        public static string FolderName(string artist, string title)
        {
            string name = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";
            if (string.IsNullOrWhiteSpace(name)) name = "Song";
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            name = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return name.Length > 80 ? name.Substring(0, 80).Trim() : name;
        }
    }
}
