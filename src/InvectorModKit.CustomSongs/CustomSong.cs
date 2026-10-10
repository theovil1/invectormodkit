using HelloThere.Avicii;
using HelloThere.Modules.AudioModules;
using InvectorModKit.Charting;

namespace InvectorModKit.CustomSongs
{
    internal enum SongState { Pending, Processing, Ready, Failed }

    /// <summary>One song found in the CustomSongs folder, and the game objects built for it.</summary>
    internal sealed class CustomSong
    {
        public string Id;
        public string AudioPath;
        public string ChartPath;
        public string Folder;
        public string Title;
        public string Artist;
        public byte[] Cover;

        /// <summary>Length in seconds, known before the chart (from the chart file or the audio header).</summary>
        public double Duration;
        /// <summary>Higher = prepared sooner (set when the player selects or starts a song that is not ready).</summary>
        public long Priority;
        public bool HasMetadata;
        /// <summary>Set when the chart was installed from the marketplace.</summary>
        public string MarketplaceId;
        /// <summary>Who published that marketplace chart.</summary>
        public string MarketplaceAuthor;
        /// <summary>Version of that marketplace chart (its leaderboard is per version).</summary>
        public int MarketplaceVersion;

        public volatile SongState State = SongState.Pending;
        public string Error;
        public Chart Chart;
        public string PreviewPath;

        // Built on the main thread once the chart is ready.
        public LevelDefinition Level;
        public MusicDefinition Music;
        public AudioItem GameplayItem;
        public AudioItem PreviewItem;
        /// <summary>Seconds of silence added before the song so its beat grid starts at t = 0 in game.</summary>
        public double LeadIn;
        /// <summary>Bumped whenever gameplay audio is (re)loaded, so a delayed release of an older load does nothing.</summary>
        public int AudioGeneration;

        public string LevelId => "custom." + Id;
        public override string ToString() => $"{Artist} - {Title}";
    }
}
