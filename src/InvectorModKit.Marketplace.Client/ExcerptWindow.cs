using System;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>
    /// The part of a song a chart's excerpt plays: from the chart's preview time (its strongest part, placed by its
    /// author or chosen by the chart generator), 20 seconds, never past the song's end. The player fades it in and
    /// out and loops it; it starts once the player has rested on a chart for <see cref="Dwell"/> seconds.
    /// </summary>
    public readonly struct ExcerptWindow
    {
        public const double Length = 20, FadeIn = 0.6, FadeOut = 1.2, Dwell = 0.8;

        public readonly double Start, End;

        private ExcerptWindow(double start, double end)
        {
            Start = start;
            End = end;
        }

        /// <summary>A missing preview time, or one outside the song, is 30 % of the song, as charts do.</summary>
        public static ExcerptWindow For(double? previewTime, double songLength)
        {
            double length = Math.Max(0, songLength);
            double start = previewTime is double p && p >= 0 && p <= length ? p : length * 0.3;
            start = Math.Max(0, Math.Min(start, length - Length));
            return new ExcerptWindow(start, Math.Min(length, start + Length));
        }
    }
}
