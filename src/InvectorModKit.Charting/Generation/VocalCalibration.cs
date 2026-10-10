using System;
using System.Collections.Generic;
using InvectorModKit.Charting.Audio;

namespace InvectorModKit.Charting.Generation
{
    /// <summary>
    /// How much a chart follows the singing: among notes placed while someone sings, the share that sit
    /// on a sung attack. Measured the same way on the official charts (ChartLab "vshare") to get the
    /// targets below, so each generated song can be pulled towards what the base game does.
    /// </summary>
    public static class VocalCalibration
    {
        /// <summary>
        /// Official upper quartile per difficulty (Casual, Normal, Hard, Ultra), measured with Share on Spleeter
        /// tracks: the target for songs being pulled up. Higher than the median because Spleeter tracks keep a
        /// little accompaniment, which makes this measure read high on exactly the songs that need help.
        /// </summary>
        public static readonly double[] OfficialShare = { 0.76, 0.71, 0.67, 0.66 };
        /// <summary>Official median: songs below it follow the singing less than a typical official chart and get pulled up.</summary>
        public static readonly double[] OfficialFloor = { 0.68, 0.66, 0.63, 0.62 };

        /// <summary>"Someone is singing": the vocal track within 24 dB of its loud level (same as the offline analysis).</summary>
        public const float SungActivity = 0.063f;
        private const double Tolerance = 0.05;

        /// <summary>Share of notes, among those placed while someone sings, that are within 50 ms of a clear sung attack (-1 if too few).</summary>
        public static double Share(BeatGrid grid, AudioFeatures f, IList<int> notes)
        {
            if (f.VocalPeakTimes == null) return -1;
            int sung = 0, onVocal = 0;
            foreach (int i in notes)
            {
                if (i < 0 || i >= grid.Count || grid.X[i][BeatGrid.FVocalActivity] < SungActivity) continue;
                sung++;
                double t = grid.TimeOf(i);
                int k = Array.BinarySearch(f.VocalPeakTimes, t);
                if (k < 0) k = ~k;
                if ((k < f.VocalPeakTimes.Length && f.VocalPeakTimes[k] - t <= Tolerance) || (k > 0 && t - f.VocalPeakTimes[k - 1] <= Tolerance)) onVocal++;
            }
            return sung < 20 ? -1 : onVocal / (double)sung;
        }    }
}
