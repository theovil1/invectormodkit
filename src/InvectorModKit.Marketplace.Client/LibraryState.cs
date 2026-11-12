using System;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>What is known about a song of My library (or a song being added from a link).</summary>
    public sealed class LibraryFacts
    {
        /// <summary>Charted by the player (generated, made in the editor, or their own chart reinstalled).</summary>
        public bool Mine;
        public bool Published;
        public int PublishedVersion;
        /// <summary>The link field holds a YouTube video link.</summary>
        public bool LinkValid;
        public bool Publishing;
        /// <summary>The Loc key of the publishing step (fingerprinting, sending).</summary>
        public string PublishStep;
        /// <summary>A song being added from a link: downloading, then the game preparing it.</summary>
        public bool Adding, Preparing;
        /// <summary>0..1 of the download.</summary>
        public double Progress;
        /// <summary>The Loc key of why adding failed, or null.</summary>
        public string AddError;
        /// <summary>The chart's author, for a song from the marketplace.</summary>
        public string Author;
    }

    /// <summary>The main button of a My library sheet: publish the player's chart, or say where the song stands.</summary>
    public static class LibraryState
    {
        public static SheetButton For(LibraryFacts f)
        {
            if (f.Adding && !f.Preparing)
            {
                int percent = (int)Math.Round(Math.Max(0, Math.Min(1, f.Progress)) * 100);
                return new SheetButton { LabelKey = "main_adding", LabelArgs = new object[] { percent }, Percent = percent, SubKey = "add_step_download" };
            }
            if (f.Preparing)
                return new SheetButton { LabelKey = "main_adding", LabelArgs = new object[] { 100 }, Percent = 100, SubKey = "main_step_prepare" };
            if (f.AddError != null)
                return new SheetButton { LabelKey = "main_add_failed", Warning = true, SubKey = f.AddError };
            if (!f.Mine)
                return new SheetButton { LabelKey = "main_installed", SubKey = "origin_marketplace", SubArgs = new object[] { f.Author } };
            if (f.Published)
                return new SheetButton { LabelKey = "main_published", SubKey = "main_published_sub", SubArgs = new object[] { f.PublishedVersion } };
            if (f.Publishing)
                return new SheetButton { LabelKey = "main_publishing", SubKey = f.PublishStep };
            return new SheetButton { LabelKey = "main_publish", Accent = true, Enabled = f.LinkValid, SubKey = f.LinkValid ? "only_chart_sent" : "link_needed" };
        }
    }
}
