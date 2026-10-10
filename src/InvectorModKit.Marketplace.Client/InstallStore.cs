using System.IO;
using InvectorModKit.Charting.Marketplace;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>
    /// Downloaded charts in CustomSongs/.marketplace/{id}/, in the layout <see cref="PendingInstall"/> reads:
    /// they wait there until the player's copy of the song shows up and ChartGen places them on it.
    /// </summary>
    public sealed class InstallStore
    {
        public readonly string MarketplaceDir;

        public InstallStore(string customSongsDir)
        {
            MarketplaceDir = Path.Combine(customSongsDir, ".marketplace");
        }

        private string DirOf(string id) => Path.Combine(MarketplaceDir, id);

        /// <summary>Stores a download; a newer version replaces the older one and waits for the audio again.</summary>
        public void Install(ChartDownload d)
        {
            string dir = DirOf(d.Summary.Id);
            Directory.CreateDirectory(dir);
            var s = d.Summary;
            File.WriteAllText(Path.Combine(dir, PendingInstall.ChartFile), d.ChartJson);
            File.WriteAllText(Path.Combine(dir, PendingInstall.FingerprintFile), d.FingerprintJson);
            File.Delete(Path.Combine(dir, PendingInstall.InstalledFile));
            // Last, so a half-written download is never seen as complete.
            File.WriteAllText(Path.Combine(dir, PendingInstall.EntryFile), new JObject
            {
                ["id"] = s.Id, ["title"] = s.Title, ["artist"] = s.Artist, ["youtubeId"] = s.YoutubeId,
                ["author"] = s.Author, ["version"] = s.Version,
            }.ToString());
        }

        public bool IsDownloaded(string id) => File.Exists(Path.Combine(DirOf(id), PendingInstall.EntryFile));

        public void Remove(string id)
        {
            if (Directory.Exists(DirOf(id))) Directory.Delete(DirOf(id), true);
        }
    }
}
