using System;
using System.Collections.Generic;

namespace InvectorModKit.Charting.Generation
{
    /// <summary>
    /// Chooses which candidate grid positions become notes: maximises the sum of per-position
    /// scores plus a log-prior on the gap between consecutive notes (so rhythms look like the
    /// official charts), with a per-note bonus tuned by bisection to hit a target note count.
    /// </summary>
    public static class RhythmDecoder
    {
        /// <param name="candidates">Sorted grid indices that may hold a note.</param>
        /// <param name="score">Score (log-odds) of each candidate.</param>
        /// <param name="gapLog">Log-prior indexed by gap in quarter beats (1..MaxGap; MaxGap also covers longer).</param>
        /// <param name="gapWeight">How strongly the gap prior shapes the rhythm.</param>
        /// <param name="minGap">Smallest allowed distance between two notes, in quarter beats.</param>
        public static List<int> Decode(int[] candidates, double[] score, double[] gapLog, double gapWeight, int targetCount, int minGap = 1)
        {
            if (candidates.Length == 0 || targetCount <= 0) return new List<int>();
            if (targetCount >= candidates.Length && minGap <= 1) return new List<int>(candidates);

            double lo = -30, hi = 30;
            List<int> best = null;
            for (int it = 0; it < 40; it++)
            {
                double bonus = (lo + hi) / 2;
                var sel = Run(candidates, score, gapLog, gapWeight, bonus, minGap);
                if (best == null || Math.Abs(sel.Count - targetCount) < Math.Abs(best.Count - targetCount)) best = sel;
                if (sel.Count == targetCount) break;
                if (sel.Count < targetCount) lo = bonus; else hi = bonus;
            }
            return best;
        }

        private static List<int> Run(int[] c, double[] score, double[] gapLog, double w, double bonus, int minGap)
        {
            int n = c.Length, maxGap = gapLog.Length - 1;
            var dp = new double[n];
            var from = new int[n];
            double farBest = double.NegativeInfinity;
            int farIdx = -1, farPtr = 0;
            int bestEnd = -1;
            double bestEndVal = 0; // choosing nothing scores 0

            for (int i = 0; i < n; i++)
            {
                while (farPtr < i && c[i] - c[farPtr] > maxGap)
                {
                    if (dp[farPtr] > farBest) { farBest = dp[farPtr]; farIdx = farPtr; }
                    farPtr++;
                }
                double prev = 0;
                int arg = -1;
                if (farIdx >= 0)
                {
                    double v = farBest + w * gapLog[maxGap];
                    if (v > prev) { prev = v; arg = farIdx; }
                }
                for (int j = farPtr; j < i; j++)
                {
                    int gap = c[i] - c[j];
                    if (gap < minGap) continue;
                    double v = dp[j] + w * gapLog[gap];
                    if (v > prev) { prev = v; arg = j; }
                }
                dp[i] = score[i] + bonus + prev;
                from[i] = arg;
                if (dp[i] > bestEndVal) { bestEndVal = dp[i]; bestEnd = i; }
            }

            var result = new List<int>();
            for (int i = bestEnd; i >= 0; i = from[i]) result.Add(c[i]);
            result.Reverse();
            return result;
        }
    }
}
