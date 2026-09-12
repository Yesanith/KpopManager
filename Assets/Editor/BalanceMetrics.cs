using System;
using System.Collections.Generic;
using System.Linq;

namespace KpopManager.Editor
{
    /// <summary>Small, dependency-free statistics helpers for <see cref="BalanceRunner"/>. Pure
    /// functions over plain lists — nothing here touches <c>GameState</c> or Unity.</summary>
    internal static class BalanceMetrics
    {
        /// <summary>Gini coefficient over a set of non-negative values (e.g. per-group #1-week
        /// counts). 0 = perfectly equal, approaching 1 = maximally concentrated. Returns 0 if every
        /// value is zero (nothing to be unequal about).</summary>
        public static double Gini(IReadOnlyList<double> values)
        {
            int n = values.Count;
            if (n == 0) return 0d;

            double[] sorted = values.ToArray();
            Array.Sort(sorted);

            double sum = 0d;
            for (int i = 0; i < n; i++) sum += sorted[i];
            if (sum <= 0d) return 0d;

            double weightedSum = 0d;
            for (int i = 0; i < n; i++)
            {
                weightedSum += (i + 1) * sorted[i]; // 1-based rank
            }

            return (2d * weightedSum) / (n * sum) - (n + 1d) / n;
        }

        /// <summary>Pearson product-moment correlation coefficient. Returns 0 if either series has
        /// zero variance (undefined, but 0 is a safer default than NaN for a printed report).</summary>
        public static double PearsonR(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
        {
            int n = xs.Count;
            if (n == 0 || ys.Count != n) return 0d;

            double meanX = Mean(xs);
            double meanY = Mean(ys);

            double covariance = 0d, varX = 0d, varY = 0d;
            for (int i = 0; i < n; i++)
            {
                double dx = xs[i] - meanX;
                double dy = ys[i] - meanY;
                covariance += dx * dy;
                varX += dx * dx;
                varY += dy * dy;
            }

            if (varX <= 0d || varY <= 0d) return 0d;
            return covariance / Math.Sqrt(varX * varY);
        }

        /// <summary>Spearman rank correlation: Pearson's r computed on each series' ranks
        /// (average rank for ties) instead of raw values.</summary>
        public static double SpearmanRho(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
        {
            if (xs.Count != ys.Count || xs.Count == 0) return 0d;
            return PearsonR(Rank(xs), Rank(ys));
        }

        private static double Mean(IReadOnlyList<double> values)
        {
            double sum = 0d;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return sum / values.Count;
        }

        /// <summary>Average (fractional) rank per value, 1-based, ties sharing the mean of the
        /// positions they span.</summary>
        private static double[] Rank(IReadOnlyList<double> values)
        {
            int n = values.Count;
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;

            Array.Sort(order, (a, b) =>
            {
                int cmp = values[a].CompareTo(values[b]);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            double[] ranks = new double[n];
            int idx = 0;
            while (idx < n)
            {
                int runEnd = idx;
                while (runEnd + 1 < n && values[order[runEnd + 1]] == values[order[idx]]) runEnd++;

                // Positions idx..runEnd (0-based) share the average of ranks (idx+1)..(runEnd+1).
                double averageRank = (idx + 1 + runEnd + 1) / 2.0;
                for (int k = idx; k <= runEnd; k++)
                {
                    ranks[order[k]] = averageRank;
                }

                idx = runEnd + 1;
            }

            return ranks;
        }
    }
}
