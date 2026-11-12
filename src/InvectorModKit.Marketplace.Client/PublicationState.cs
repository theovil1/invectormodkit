namespace InvectorModKit.Marketplace.Client
{
    public enum PublicationRow { UpToDate, Changed, Hidden, NotOnThisPc }

    /// <summary>What is known about one of the player's published charts.</summary>
    public sealed class PublicationFacts
    {
        /// <summary>The version on the marketplace.</summary>
        public int ServerVersion;
        public bool Hidden;
        public int Reports;
        /// <summary>The song this chart was published from is in CustomSongs.</summary>
        public bool LocalSongFound;
        /// <summary><see cref="ChartContent.Hash"/> of the local chart now, and of the chart when it was published (null: published before hashes were kept).</summary>
        public string LocalHash, PublishedHash;
        /// <summary>The local chart's note counts are the published version's (used when no hash was kept).</summary>
        public bool NotesMatch;
        public bool Publishing;
    }

    public sealed class PublicationView
    {
        public PublicationRow Row;
        /// <summary>The local chart differs from the published version.</summary>
        public bool Changed;
        public SheetButton Main;
    }

    /// <summary>My publications: whether a chart is up to date, changed since, hidden or not on this PC, and its main button.</summary>
    public static class PublicationState
    {
        public static PublicationView For(PublicationFacts f)
        {
            bool changed = f.LocalSongFound && (f.PublishedHash != null ? f.LocalHash != f.PublishedHash : !f.NotesMatch);
            SheetButton main;
            if (!f.LocalSongFound) main = new SheetButton { LabelKey = "main_not_on_pc", SubKey = "not_on_pc_sub" };
            else if (f.Publishing) main = new SheetButton { LabelKey = "main_publishing" };
            else if (changed)
                main = new SheetButton
                {
                    LabelKey = "main_publish_version", LabelArgs = new object[] { f.ServerVersion + 1 }, Enabled = true, Accent = true,
                    SubKey = "changed_warning", SubArgs = new object[] { f.ServerVersion },
                };
            else main = new SheetButton { LabelKey = "main_up_to_date" };
            var row = f.Hidden ? PublicationRow.Hidden : !f.LocalSongFound ? PublicationRow.NotOnThisPc : changed ? PublicationRow.Changed : PublicationRow.UpToDate;
            return new PublicationView { Row = row, Changed = changed, Main = main };
        }
    }
}
