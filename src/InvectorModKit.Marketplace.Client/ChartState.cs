using System;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>What the game knows about a marketplace chart on this PC.</summary>
    public sealed class ChartFacts
    {
        /// <summary>Published by the player (their own chart).</summary>
        public bool Mine;
        /// <summary>Playable: the chart and its music are both in CustomSongs.</summary>
        public bool Installed;
        /// <summary>The chart is downloaded and waits for its music.</summary>
        public bool Downloaded;
        public bool Downloading, Fetching;
        /// <summary>The player's copy of the song is another version than the author's.</summary>
        public bool OtherVersion;
        /// <summary>0..1 of the current step.</summary>
        public double Progress;
        /// <summary>Why the music could not be fetched, or null.</summary>
        public string FetchError;
        public int InstalledVersion, LatestVersion;
    }

    public enum MainKind { Install, Installing, Retry, Installed, Update, Mine }

    /// <summary>
    /// The sheet's one main button: its label says what happens next (install, progress, retry, installed, update),
    /// the line under it says why or where. Labels are Loc keys.
    /// </summary>
    public sealed class MainButton
    {
        public MainKind Kind;
        public bool Enabled;
        /// <summary>0..100, while installing.</summary>
        public int Percent;
        public string LabelKey, SubKey;
        /// <summary>Retry only: why the music could not be fetched (null for another version of the song).</summary>
        public string Reason;

        public static MainButton For(ChartFacts f)
        {
            if (f.Mine) return Make(MainKind.Mine, false, "main_mine", null);
            if (f.Downloading) return Installing(f, "main_step_chart");
            if (f.Fetching) return Installing(f, "main_step_music");
            if (f.Installed)
                return f.LatestVersion > f.InstalledVersion
                    ? Make(MainKind.Update, true, "main_update", "main_update_sub")
                    : Make(MainKind.Installed, true, "main_installed", "main_installed_sub");
            if (f.Downloaded)
            {
                var retry = Make(MainKind.Retry, true, "main_retry", f.OtherVersion ? "other_version" : "main_retry_sub");
                retry.Reason = f.OtherVersion ? null : f.FetchError;
                return retry;
            }
            return Make(MainKind.Install, true, "main_install", "main_install_sub");
        }

        private static MainButton Installing(ChartFacts f, string step)
        {
            var b = Make(MainKind.Installing, false, "main_installing", step);
            b.Percent = (int)Math.Round(Math.Max(0, Math.Min(1, f.Progress)) * 100);
            return b;
        }

        private static MainButton Make(MainKind kind, bool enabled, string label, string sub) =>
            new MainButton { Kind = kind, Enabled = enabled, LabelKey = label, SubKey = sub };
    }
}
