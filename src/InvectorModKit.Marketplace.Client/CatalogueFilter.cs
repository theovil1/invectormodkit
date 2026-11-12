using System.Collections.Generic;
using System.Linq;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>Which charts Discover shows: all of them, or only those installed or not installed on this PC.</summary>
    public enum Shown { All, NotInstalled, Installed }

    /// <summary>Discover's one-click filters, applied to the charts the server sent, in their order.</summary>
    public static class CatalogueFilter
    {
        /// <param name="difficulty">0 Casual, 1 Normal, 2 Hard, 3 Ultra: only charts with notes there; null for any.</param>
        public static List<ChartSummary> Apply(IEnumerable<ChartSummary> items, ISet<string> installedIds, Shown shown, int? difficulty) =>
            items.Where(c => shown == Shown.All || installedIds.Contains(c.Id) == (shown == Shown.Installed))
                 .Where(c => difficulty == null || NotesIn(c, difficulty.Value) > 0)
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
