using System;

namespace InvectorModKit.Charting.Generation
{
    /// <summary>
    /// Logistic model of "does this grid position get a note?", one weight vector per difficulty and
    /// metric level. Weights are learned from the official charts by <c>ChartLab train</c>, which
    /// rewrites <see cref="NoteModelWeights"/>.
    /// </summary>
    public static class NoteModel
    {
        /// <summary>[difficulty][level][feature]. Replaceable for experiments (ChartLab cross-validation).</summary>
        public static double[][][] Weights = NoteModelWeights.W;

        public static double Logit(Difficulty d, int level, float[] x)
        {
            var w = Weights[(int)d][level];
            double z = 0;
            for (int k = 0, n = Math.Min(w.Length, x.Length); k < n; k++) z += w[k] * x[k];
            return z;
        }

        public static double Probability(Difficulty d, int level, float[] x) => 1.0 / (1.0 + Math.Exp(-Logit(d, level, x)));

        /// <summary>
        /// Official distribution of the time between consecutive notes, in quarter beats (index = gap,
        /// 1..32, last bucket also covers longer gaps). Percentages measured on the official charts.
        /// </summary>
        public static readonly double[][] GapPercent =
        {
            Gaps((1, 4.2), (2, 23.4), (3, 11.0), (4, 31.1), (5, 4.4), (6, 7.1), (7, 1.2), (8, 10.1), (9, 0.5), (10, 1.8), (12, 1.1), (14, 0.5), (16, 1.5), (20, 0.4), (32, 0.7)),
            Gaps((1, 9.9), (2, 40.7), (3, 12.4), (4, 24.1), (5, 1.9), (6, 4.1), (7, 0.7), (8, 2.8), (10, 0.5), (12, 0.3), (16, 0.8), (32, 0.5)),
            Gaps((1, 21.6), (2, 42.4), (3, 10.4), (4, 17.4), (5, 1.1), (6, 2.4), (8, 1.8), (10, 0.4), (16, 0.7), (32, 0.3)),
            Gaps((1, 29.4), (2, 41.5), (3, 8.3), (4, 13.7), (5, 0.5), (6, 1.8), (8, 1.5), (16, 0.6)),
        };

        public const int MaxGap = 32;

        private static double[] Gaps(params (int gap, double pct)[] values)
        {
            var a = new double[MaxGap + 1];
            for (int i = 1; i <= MaxGap; i++) a[i] = 0.1; // floor for gaps the official charts rarely use
            foreach (var (gap, pct) in values) a[gap] = Math.Max(a[gap], pct);
            return a;
        }

        public static double[] GapLogPrior(Difficulty d)
        {
            var pct = GapPercent[(int)d];
            double sum = 0;
            for (int i = 1; i <= MaxGap; i++) sum += pct[i];
            var log = new double[MaxGap + 1];
            log[0] = double.NegativeInfinity;
            for (int i = 1; i <= MaxGap; i++) log[i] = Math.Log(pct[i] / sum);
            return log;
        }
    }
}
