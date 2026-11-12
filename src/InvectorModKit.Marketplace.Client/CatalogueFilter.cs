using System.Collections.Generic;
using System.Linq;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>Which charts Discover shows: all of them, or only those installed or not installed on this PC.</summary>
    public enum Shown { All, NotInstalled, Installed }

    /// <summary>Which songs My library shows.</summary>
    public enum LibraryShown { All, Mine, FromMarketplace, Published }

    /// <summary>A song of My library, as the filters see it.</summary>
    public sealed class LibraryItem
    {
        public string Id, Title, Artist;
        /// <summary>Charted by the player (not a chart installed from someone else).</summary>
        public bool Mine;
        public bool Published;
    }

    /// <summary>Discover's one-click filters, applied to the charts the server sent, in their order.</summary>
    public static class CatalogueFilter
    {
        /// <param name="difficulty">0 Casual, 1 Normal, 2 Hard, 3 Ultra: only charts with notes there; null for any.</param>
        public static List<ChartSummary> Apply(IEnumerable<ChartSummary> items, ISet<string> installedIds, Shown shown, int? difficulty) =>
            items.Where(c => shown == Shown.All || installedIds.Contains(c.Id) == (shown == Shown.Installed))
                 .Where(c => difficulty == null || NotesIn(c, difficulty.Value) > 0)
                 .ToList();

        /// <summary>My library's filters, sorted by artist then title (case ignored).</summary>
        public static List<LibraryItem> Library(IEnumerable<LibraryItem> items, LibraryShown shown) =>
            items.Where(s => shown == LibraryShown.All
                          || (shown == LibraryShown.Mine && s.Mine)
                          || (shown == LibraryShown.FromMarketplace && !s.Mine)
                          || (shown == LibraryShown.Published && s.Published))
                 .OrderBy(s => s.Artist ?? "", System.StringComparer.OrdinalIgnoreCase)
                 .ThenBy(s => s.Title ?? "", System.StringComparer.OrdinalIgnoreCase)
                 .ToList();

        private static int NotesIn(ChartSummary c, int difficulty)
        {
            var n = c.Notes ?? new NoteCounts();
            switch (difficulty)
            {
                case 0: return n.Casual;
                case 1: return n.Normal;
                case 2: return n.Hard;
                default: return n.Ultra;
            }
        }
    }
}
