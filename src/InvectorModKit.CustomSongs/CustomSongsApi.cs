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
        public double Duration;
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
                MarketplaceId = s.MarketplaceId, Duration = s.Duration, Ready = s.State == SongState.Ready, Cover = s.Music != null ? s.Music.ArtistImage : null,
            }).ToList();

        /// <summary>
        /// After a download: checks the songs already on disk against the charts waiting for their audio, in
        /// the background; matched songs get the marketplace chart and song select refreshes.
        /// </summary>
        public static void RequestMarketplaceMatch() => CustomSongsPlugin.RequestMarketplaceMatch();

        /// <summary>Worker thread only (runs ChartGen): the song's fingerprint JSON, for publishing.</summary>
        public static string FingerprintJson(string songId)
        {
            var song = CustomSongsPlugin.Library?.Songs.FirstOrDefault(s => s.Id == songId)
                       ?? throw new KeyNotFoundException("no custom song " + songId);
            return CustomSongsPlugin.Library.FingerprintJson(song);
        }
    }
}
