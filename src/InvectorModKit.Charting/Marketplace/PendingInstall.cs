using System;
using System.Collections.Generic;
using System.IO;
using InvectorModKit.Charting.Audio;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Charting.Marketplace
{
    /// <summary>
    /// A chart downloaded from the marketplace, waiting in CustomSongs/.marketplace/{id}/ for the player's
    /// own copy of the song: entry.json (who and what), chart.json (on the author's audio), fingerprint.json.
    /// installed.json appears once an audio file matched.
    /// </summary>
    public sealed class PendingInstall
    {
        public const string EntryFile = "entry.json", ChartFile = "chart.json", FingerprintFile = "fingerprint.json", InstalledFile = "installed.json";

        public string Id, Title, Artist, YoutubeId, Author;
        public int Version;
        public string Directory;
        public Fingerprint Fingerprint;

        /// <summary>Complete downloads still waiting for their audio; unreadable ones are skipped.</summary>
        public static IEnumerable<PendingInstall> Load(string marketplaceDir)
        {
            if (!System.IO.Directory.Exists(marketplaceDir)) yield break;
            foreach (string dir in System.IO.Directory.GetDirectories(marketplaceDir))
            {
                if (File.Exists(Path.Combine(dir, InstalledFile))) continue;
                PendingInstall install = null;
                try { install = Read(dir); }
                catch (Exception e) when (e is IOException || e is FormatException || e is Newtonsoft.Json.JsonException || e is NullReferenceException) { }
                if (install != null) yield return install;
            }
        }

        private static PendingInstall Read(string dir)
        {
            string entry = Path.Combine(dir, EntryFile), chart = Path.Combine(dir, ChartFile), fingerprint = Path.Combine(dir, FingerprintFile);
            if (!File.Exists(entry) || !File.Exists(chart) || !File.Exists(fingerprint)) return null;
            var o = JObject.Parse(File.ReadAllText(entry));
            return new PendingInstall
            {
                Id = (string)o["id"],
                Title = (string)o["title"],
                Artist = (string)o["artist"],
                YoutubeId = (string)o["youtubeId"],
                Author = (string)o["author"],
                Version = (int?)o["version"] ?? 1,
                Directory = dir,
                Fingerprint = Fingerprint.FromJson(File.ReadAllText(fingerprint)),
            };
        }

        /// <summary>The author's chart placed on the matched audio, marked as coming from the marketplace.</summary>
        public Chart ChartFor(FingerprintMatch match)
        {
            var chart = ChartJson.Deserialize(File.ReadAllText(Path.Combine(Directory, ChartFile))).Shifted(match.Offset);
            chart.Generator = null;
            chart.Source = new ChartSource { MarketplaceId = Id, Version = Version, Author = Author };
            return chart;
        }

        public void MarkInstalled(string audioPath, double offset) =>
            File.WriteAllText(Path.Combine(Directory, InstalledFile),
                new JObject { ["audio"] = audioPath, ["offset"] = Math.Round(offset, 4), ["at"] = DateTime.UtcNow.ToString("o") }.ToString());
    }
}
