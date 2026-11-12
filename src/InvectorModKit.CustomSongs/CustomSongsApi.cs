using System.Collections.Generic;
using System.Linq;

namespace InvectorModKit.CustomSongs
{
    /// <summary>A custom song as other plugins see it (snapshot).</summary>
    public sealed class LocalSong
    {
        public string Id, Title, Artist, AudioPath, ChartPath;
        /// <summary>Set when its chart was installed from the marketplace.</summary>
        public string MarketplaceId;
        /// <summary>Who published that marketplace chart.</summary>
        public string MarketplaceAuthor;
        /// <summary>Version of that marketplace chart.</summary>
        public int MarketplaceVersion;
        /// <summary>Highscore table prefix of its level (0: no leaderboard).</summary>
        public int BoardPrefix;
        /// <summary>Notes per difficulty (Casual, Normal, Hard, Ultra) of the chart being played; empty before it is ready.</summary>
        public int[] Notes = new int[0];
        public double Duration;
        /// <summary>The excerpt song select plays (a wav of a few seconds from <see cref="PreviewStart"/>), null until made.</summary>
        public string PreviewPath;
        /// <summary>Where that excerpt starts in the song (seconds).</summary>
        public double PreviewStart;
        /// <summary>Song-select cover (null until the song is listed).</summary>
        public UnityEngine.Sprite Cover;
        /// <summary>Charted and playable.</summary>
        public bool Ready;
    }

    /// <summary>What the Custom Songs plugin offers other plugins (the marketplace).</summary>
    public static class CustomSongsApi
    {
        public static string SongsFolder => CustomSongsPlugin.Library?.Root;
        public static string MarketplaceDir => CustomSongsPlugin.Library?.MarketplaceDir;

        /// <summary>Main thread: the songs currently listed.</summary>
        public static List<LocalSong> Songs() =>
            CustomSongsPlugin.Library == null ? new List<LocalSong>() : CustomSongsPlugin.Library.Songs.Select(s => new LocalSong
            {
                Id = s.Id, Title = s.Title, Artist = s.Artist, AudioPath = s.AudioPath, ChartPath = s.ChartPath,
                MarketplaceId = s.MarketplaceId, MarketplaceAuthor = s.MarketplaceAuthor, MarketplaceVersion = s.MarketplaceVersion,
                BoardPrefix = s.Level != null ? s.Level.highscoreTablePrefix : 0,
                Notes = s.Chart == null ? new int[0] : System.Enum.GetValues(typeof(InvectorModKit.Charting.Difficulty)).Cast<InvectorModKit.Charting.Difficulty>().Select(d => s.Chart[d].Count).ToArray(),
                Duration = s.Duration, Ready = s.State == SongState.Ready, Cover = s.Music != null ? s.Music.ArtistImage : null,
                PreviewPath = s.PreviewPath != null && System.IO.File.Exists(s.PreviewPath) ? s.PreviewPath : null,
                PreviewStart = s.Chart != null ? s.Chart.PreviewStart : 0,
            }).ToList();

        /// <summary>
        /// Main thread: "id:marketplace id" of each playable song, cheap enough to check often whether songs came or
        /// went (<see cref="Songs"/> copies every song).
        /// </summary>
        public static IEnumerable<string> ReadySongKeys() =>
            CustomSongsPlugin.Library == null ? Enumerable.Empty<string>()
                : CustomSongsPlugin.Library.Songs.Where(s => s.State == SongState.Ready).Select(s => s.Id + ":" + s.MarketplaceId);

        /// <summary>The mixer group the game's song previews play through, so the marketplace's excerpts follow the music volume; null until resolved.</summary>
        public static UnityEngine.Audio.AudioMixerGroup PreviewMixerGroup => GameAssets.PreviewGroup;

        /// <summary>
        /// After a download: checks the songs already on disk against the charts waiting for their audio, in
        /// the background; matched songs get the marketplace chart and song select refreshes.
        /// </summary>
        public static void RequestMarketplaceMatch() => CustomSongsPlugin.RequestMarketplaceMatch();

        /// <summary>That check is asked for or still running: a song may not carry its marketplace chart yet.</summary>
        public static bool MarketplaceMatchPending => CustomSongsPlugin.MarketplaceMatchPending;

        /// <summary>Main thread: re-reads title, artist and cover of the listed songs (a picture may have appeared next to an audio file).</summary>
        public static void RequestMetadataReload() => CustomSongsPlugin.RequestMetadataReload();

        /// <summary>
        /// Main thread: deletes the audio, its chart and everything the mod added for the songs brought by
        /// that marketplace chart. The chart stays on the marketplace; installing brings the audio back.
        /// </summary>
        public static bool RemoveMarketplaceSong(string marketplaceId)
        {
            var library = CustomSongsPlugin.Library;
            if (library == null) return false;
            bool any = library.RemoveMarketplaceSong(marketplaceId, msg => CustomSongsPlugin.Log.LogInfo(msg));
            if (any) CustomSongsPlugin.RefreshListedSongs();
            return any;
        }

        /// <summary>
        /// Main thread, after publishing one's own song: its chart becomes that marketplace chart (frozen, with a
        /// leaderboard). <paramref name="chartId"/> null clears it (after withdrawing the publication).
        /// </summary>
        public static void SetMarketplaceSource(string songId, string chartId, int version, string author)
        {
            var song = CustomSongsPlugin.Library?.Songs.FirstOrDefault(s => s.Id == songId)
                       ?? throw new KeyNotFoundException("no custom song " + songId);
            CustomSongsPlugin.Library.SetSource(song, chartId == null ? null : new InvectorModKit.Charting.ChartSource { MarketplaceId = chartId, Version = version, Author = author });
            GameAssets.ApplyBoard(song);
        }

        /// <summary>Worker thread only (runs ChartGen): the song's fingerprint JSON, for publishing.</summary>
        public static string FingerprintJson(string songId)
        {
            var song = CustomSongsPlugin.Library?.Songs.FirstOrDefault(s => s.Id == songId)
                       ?? throw new KeyNotFoundException("no custom song " + songId);
            return CustomSongsPlugin.Library.FingerprintJson(song);
        }
    }
}
