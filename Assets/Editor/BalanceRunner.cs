using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KpopManager.Core;
using KpopManager.Core.Systems.Chart;
using KpopManager.Core.Systems.Generation;
using KpopManager.Editor.Generation;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace KpopManager.Editor
{
    // Runs the chart simulation headless for N years and reports whether it's balanced — the half
    // of Phase 3a that isn't the simulation itself. This tool does not tune anything; it measures.
    // See Docs/BALANCE.md for where the tuning conversation actually happens.
    public static class BalanceRunner
    {
        private const int DefaultYears = 50;
        private const ulong DefaultSeed = 12345UL;

        // Five arbitrary, fixed seeds — not tuned, just "different enough to catch a formula that
        // only works by luck on one of them." Kept as a named constant so a sweep is always the
        // same five runs, reproducibly.
        private static readonly ulong[] SweepSeeds = { 11111UL, 22222UL, 33333UL, 44444UL, 55555UL };

        [MenuItem("KpopManager/Run Balance Simulation")]
        public static void RunBalanceSimulation()
        {
            SimulationResult result = RunOne(DefaultSeed, DefaultYears);
            MetricReport report = ComputeMetrics(result);

            PrintReport(result, report);
            PrintOccupancySummary(result);
            PrintPromoSpendPercentiles(result);
            WriteReleasesCsv(result);
            WriteChartHistoryCsv(result);
            WriteChartOccupancyCsv(result);
        }

        [MenuItem("KpopManager/Run Balance Sweep")]
        public static void RunBalanceSweep()
        {
            List<(ulong seed, MetricReport report)> all = new List<(ulong, MetricReport)>();

            for (int i = 0; i < SweepSeeds.Length; i++)
            {
                SimulationResult result = RunOne(SweepSeeds[i], DefaultYears);
                MetricReport report = ComputeMetrics(result);

                PrintReport(result, report);
                WriteReleasesCsv(result);
                WriteChartHistoryCsv(result);
                WriteChartOccupancyCsv(result);

                all.Add((SweepSeeds[i], report));
            }

            PrintSweepSummary(all);
        }

        // -----------------------------------------------------------------------------------
        // Simulation
        // -----------------------------------------------------------------------------------

        private static SimulationResult RunOne(ulong seed, int years)
        {
            SimEngine engine = new SimEngine(seed);
            WorldData data = ContentLoader.LoadWorldData();
            WorldGenerator.Generate(engine.State, data);

            // Ticked week-by-week (not AdvanceYears(1) in a loop) so chart_occupancy can capture
            // one row per simulated week, and so year-boundary snapshots land on exactly the right
            // week regardless of how many years are requested.
            Dictionary<int, Dictionary<int, GroupSnapshot>> snapshotsByYear = new Dictionary<int, Dictionary<int, GroupSnapshot>>();
            List<OccupancyRow> occupancy = new List<OccupancyRow>();

            int totalWeeks = years * SimDate.WeeksPerYear;
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int week = 1; week <= totalWeeks; week++)
            {
                engine.AdvanceWeek();
                occupancy.Add(CaptureOccupancy(engine.State));

                if (engine.WeeksElapsed % SimDate.WeeksPerYear == 0)
                {
                    int completedYear = engine.WeeksElapsed / SimDate.WeeksPerYear;
                    snapshotsByYear[completedYear] = SnapshotGroups(engine.State);
                }
            }

            stopwatch.Stop();

            return new SimulationResult
            {
                Seed = seed,
                Years = years,
                State = engine.State,
                Config = engine.State.ChartConfig,
                SnapshotsByYear = snapshotsByYear,
                Occupancy = occupancy,
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }

        private sealed class GroupSnapshot
        {
            public long FandomSize;
            public GroupTier Tier;
            public bool IsActive;
        }

        private static Dictionary<int, GroupSnapshot> SnapshotGroups(GameState state)
        {
            Dictionary<int, GroupSnapshot> snapshot = new Dictionary<int, GroupSnapshot>(state.Groups.Count);
            for (int i = 0; i < state.Groups.Count; i++)
            {
                Group g = state.Groups[i];
                snapshot[g.Id] = new GroupSnapshot { FandomSize = g.Fandom.Size, Tier = g.Tier, IsActive = g.IsActive };
            }
            return snapshot;
        }

        private sealed class OccupancyRow
        {
            public int Year, Week;
            public int ActiveGroups, ActiveReleases, ChartedReleases, Top10Entries;
            public float PointsAtPos1, PointsAtPos10, PointsAtPos100;
        }

        // Captures the week that state.Date's tick JUST simulated — SimEngine.AdvanceWeek runs the
        // systems for the current date, then advances it, so state.Date is already next week's by
        // the time control returns here.
        private static OccupancyRow CaptureOccupancy(GameState state)
        {
            SimDate simulatedWeek = state.Date.AdvanceWeeks(-1);

            int activeGroups = 0;
            for (int i = 0; i < state.Groups.Count; i++)
            {
                if (state.Groups[i].IsActive) activeGroups++;
            }

            int activeReleases = 0, chartedReleases = 0, top10 = 0;
            float pos1 = 0f, pos10 = 0f, pos100 = 0f;

            for (int i = 0; i < state.Releases.Count; i++)
            {
                Release r = state.Releases[i];
                if (!r.IsCharting) continue;
                activeReleases++;

                int lastIndex = r.WeeklyPositions.Count - 1;
                if (lastIndex < 0) continue;

                int position = r.WeeklyPositions[lastIndex];
                float points = r.WeeklyPoints[lastIndex];
                if (position <= 0) continue;

                chartedReleases++;
                if (position <= 10) top10++;
                if (position == 1) pos1 = points;
                if (position == 10) pos10 = points;
                if (position == 100) pos100 = points;
            }

            return new OccupancyRow
            {
                Year = simulatedWeek.Year, Week = simulatedWeek.Week,
                ActiveGroups = activeGroups, ActiveReleases = activeReleases,
                ChartedReleases = chartedReleases, Top10Entries = top10,
                PointsAtPos1 = pos1, PointsAtPos10 = pos10, PointsAtPos100 = pos100
            };
        }

        // -----------------------------------------------------------------------------------
        // Metrics
        // -----------------------------------------------------------------------------------

        private sealed class SimulationResult
        {
            public ulong Seed;
            public int Years;
            public GameState State;
            public ChartConfig Config;
            public Dictionary<int, Dictionary<int, GroupSnapshot>> SnapshotsByYear;
            public List<OccupancyRow> Occupancy;
            public double ElapsedMs;
        }

        private sealed class MetricValue
        {
            public string Name;
            public double Value;
            public double Low;
            public double High;
            public string Verdict;
            public bool IsProvisional;
        }

        private sealed class MetricReport
        {
            public MetricValue NumberOneConcentration;
            public MetricValue HitLongevity;
            public MetricValue QualityToPeak;
            public MetricValue QualityToLongevity;
            public MetricValue TierMobility;
            public MetricValue Persistence;
            public MetricValue Top10Rate;
            public MetricValue NumberOneRate;
            public MetricValue FandomSpread;

            public IEnumerable<MetricValue> All()
            {
                yield return NumberOneConcentration;
                yield return HitLongevity;
                yield return QualityToPeak;
                yield return QualityToLongevity;
                yield return TierMobility;
                yield return Persistence;
                yield return Top10Rate;
                yield return NumberOneRate;
                yield return FandomSpread;
            }
        }

        private static MetricValue Evaluate(string name, double value, double low, double high, bool provisional = false)
        {
            string verdict = value < low ? "LOW" : (value > high ? "HIGH" : "PASS");
            return new MetricValue { Name = name, Value = value, Low = low, High = high, Verdict = verdict, IsProvisional = provisional };
        }

        private static MetricReport ComputeMetrics(SimulationResult result)
        {
            GameState state = result.State;

            Dictionary<int, int> numberOneCounts = new Dictionary<int, int>();
            for (int i = 0; i < state.Groups.Count; i++) numberOneCounts[state.Groups[i].Id] = 0;
            for (int i = 0; i < state.Releases.Count; i++)
            {
                Release release = state.Releases[i];
                for (int w = 0; w < release.WeeklyPositions.Count; w++)
                {
                    if (release.WeeklyPositions[w] == 1)
                    {
                        numberOneCounts[release.GroupId] = numberOneCounts.TryGetValue(release.GroupId, out int c) ? c + 1 : 1;
                    }
                }
            }
            double gini = BalanceMetrics.Gini(numberOneCounts.Values.Select(v => (double)v).ToList());
            MetricValue numberOne = Evaluate("#1 concentration (Gini)", gini, 0.45, 0.70);

            // Provisional: this band assumed a contested chart. Phase 3a's baseline had 99.8% of
            // releases reaching top 10 and mean 20.9 weeks charted — the metric was passing because
            // there was no scarcity for it to measure, not because longevity was actually healthy.
            List<Release> hits = state.Releases.Where(r => r.WeeksInTop10 > 0).ToList();
            double meanLongevity = hits.Count > 0 ? hits.Average(r => (double)r.WeeksInTop10) : 0d;
            MetricValue longevity = Evaluate("Hit longevity (mean weeks in top 10)", meanLongevity, 4d, 12d, provisional: true);

            List<Release> charted = state.Releases.Where(r => r.PeakPosition > 0).ToList();
            List<double> qualities = charted.Select(r => (double)(state.GetTrack(r.TitleTrackId)?.Quality ?? 0f)).ToList();
            List<double> peaks = charted.Select(r => (double)r.PeakPosition).ToList();
            List<double> top10Weeks = charted.Select(r => (double)r.WeeksInTop10).ToList();

            double rPeak = BalanceMetrics.PearsonR(qualities, peaks);
            MetricValue qualityToPeak = Evaluate("Quality -> Peak (Pearson r)", rPeak, -0.40d, -0.15d);

            double rLongevity = BalanceMetrics.PearsonR(qualities, top10Weeks);
            MetricValue qualityToLongevity = Evaluate("Quality -> Longevity (Pearson r)", rLongevity, 0.55d, 1.0d);

            int tierChangeCount = state.Log.Entries.Count(e => e.Category == LogCategory.Group && e.Severity == LogSeverity.Notable);
            double perDecade = result.Years > 0 ? tierChangeCount / (result.Years / 10.0) : 0d;
            MetricValue tierMobility = Evaluate("Tier mobility (changes / decade)", perDecade, 8d, 20d);

            double rho = ComputePersistence(result);
            MetricValue persistence = Evaluate("Persistence (Spearman rho, yr5 vs yr" + result.Years + ")", rho, 0.3d, 0.7d);

            // New in Phase 3b — direct scarcity signal, since a contested chart is what makes
            // every metric above mean anything.
            double top10Rate = state.Releases.Count > 0
                ? state.Releases.Count(r => r.WeeksInTop10 > 0) / (double)state.Releases.Count * 100.0
                : 0d;
            MetricValue top10RateMetric = Evaluate("Top-10 rate (% of releases)", top10Rate, 5d, 15d);

            double numberOneRate = state.Releases.Count > 0
                ? state.Releases.Count(r => r.PeakPosition == 1) / (double)state.Releases.Count * 100.0
                : 0d;
            MetricValue numberOneRateMetric = Evaluate("#1 rate (% of releases)", numberOneRate, 0.5d, 3d);

            List<double> activeFandomSizes = state.Groups.Where(g => g.IsActive).Select(g => (double)g.Fandom.Size).ToList();
            double p90 = BalanceMetrics.Percentile(activeFandomSizes, 0.90);
            double p10 = BalanceMetrics.Percentile(activeFandomSizes, 0.10);
            double fandomSpread = p10 > 0d ? p90 / p10 : (p90 > 0d ? double.PositiveInfinity : 0d);
            MetricValue fandomSpreadMetric = Evaluate("Fandom spread (p90/p10)", fandomSpread, 20d, double.PositiveInfinity);

            return new MetricReport
            {
                NumberOneConcentration = numberOne,
                HitLongevity = longevity,
                QualityToPeak = qualityToPeak,
                QualityToLongevity = qualityToLongevity,
                TierMobility = tierMobility,
                Persistence = persistence,
                Top10Rate = top10RateMetric,
                NumberOneRate = numberOneRateMetric,
                FandomSpread = fandomSpreadMetric
            };
        }

        private static double ComputePersistence(SimulationResult result)
        {
            if (!result.SnapshotsByYear.TryGetValue(5, out Dictionary<int, GroupSnapshot> atYear5)) return 0d;
            if (!result.SnapshotsByYear.TryGetValue(result.Years, out Dictionary<int, GroupSnapshot> atFinal)) return 0d;

            List<int> groupIds = atYear5.Keys.Intersect(atFinal.Keys).ToList();
            List<double> sizesYear5 = groupIds.Select(id => (double)atYear5[id].FandomSize).ToList();
            List<double> sizesFinal = groupIds.Select(id => (double)atFinal[id].FandomSize).ToList();

            return BalanceMetrics.SpearmanRho(sizesYear5, sizesFinal);
        }

        // -----------------------------------------------------------------------------------
        // Reporting
        // -----------------------------------------------------------------------------------

        private static void PrintReport(SimulationResult result, MetricReport report)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== KpopManager Balance Report ===");
            sb.AppendLine("Seed " + result.Seed + "   Years " + result.Years +
                "   ConfigHash " + result.Config.ComputeConfigHash() +
                "   (" + result.ElapsedMs.ToString("F0", CultureInfo.InvariantCulture) + " ms, " +
                result.State.Releases.Count + " releases, " + result.State.Groups.Count(g => g.IsActive) + " active groups, " +
                result.State.Groups.Count + " groups ever)");
            sb.AppendLine();

            foreach (MetricValue m in report.All())
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-42} value={1,10:F3}   target=[{2,6:F2}, {3,6}]   {4}{5}",
                    m.Name, m.Value, m.Low, FormatBound(m.High),
                    m.Verdict, m.IsProvisional ? "   (PROVISIONAL — band needs revalidating against a contested chart)" : ""));
            }

            Debug.Log(sb.ToString());
        }

        private static void PrintOccupancySummary(SimulationResult result)
        {
            List<OccupancyRow> rows = result.Occupancy;
            if (rows.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Chart Occupancy Summary — seed " + result.Seed + " ===");
            AppendStat(sb, "ActiveGroups", rows.Select(r => (double)r.ActiveGroups));
            AppendStat(sb, "ActiveReleases", rows.Select(r => (double)r.ActiveReleases));
            AppendStat(sb, "ChartedReleases", rows.Select(r => (double)r.ChartedReleases));
            AppendStat(sb, "Top10Entries", rows.Select(r => (double)r.Top10Entries));
            AppendStat(sb, "PointsAtPos1", rows.Select(r => (double)r.PointsAtPos1));
            AppendStat(sb, "PointsAtPos10", rows.Select(r => (double)r.PointsAtPos10));
            AppendStat(sb, "PointsAtPos100", rows.Select(r => (double)r.PointsAtPos100));

            Debug.Log(sb.ToString());
        }

        private static void AppendStat(StringBuilder sb, string label, IEnumerable<double> values)
        {
            List<double> list = values.ToList();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-16} mean={1,8:F2}   min={2,8:F2}   max={3,8:F2}",
                label, list.Average(), list.Min(), list.Max()));
        }

        private static void PrintPromoSpendPercentiles(SimulationResult result)
        {
            List<double> spends = result.State.Releases.Select(r => (double)r.PromoSpend).ToList();
            if (spends.Count == 0) return;

            double p5 = BalanceMetrics.Percentile(spends, 0.05);
            double p50 = BalanceMetrics.Percentile(spends, 0.50);
            double p95 = BalanceMetrics.Percentile(spends, 0.95);

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "PromoSpend percentiles — seed {0}: p5={1:F0}  p50={2:F0}  p95={3:F0}", result.Seed, p5, p50, p95));
        }

        private static void PrintSweepSummary(List<(ulong seed, MetricReport report)> all)
        {
            List<MetricValue[]> perSeed = all.Select(entry => entry.report.All().ToArray()).ToList();
            int metricCount = perSeed.Count > 0 ? perSeed[0].Length : 0;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Balance Sweep Summary — " + all.Count + " seeds: " +
                string.Join(", ", all.Select(e => e.seed.ToString())) + " ===");
            sb.AppendLine();

            for (int mi = 0; mi < metricCount; mi++)
            {
                string name = perSeed[0][mi].Name;
                double low = perSeed[0][mi].Low;
                double high = perSeed[0][mi].High;

                List<double> values = perSeed.Select(m => m[mi].Value).Where(v => !double.IsInfinity(v)).ToList();
                double mean = values.Count > 0 ? values.Average() : 0d;
                double min = values.Count > 0 ? values.Min() : 0d;
                double max = values.Count > 0 ? values.Max() : 0d;
                int passCount = perSeed.Select(m => m[mi]).Count(v => v.Value >= low && v.Value <= high);

                string verdict = passCount == perSeed.Count
                    ? "PASS (all " + perSeed.Count + " seeds)"
                    : passCount + "/" + perSeed.Count + " seeds pass";

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-42} mean={1,10:F3}   range=[{2,10:F3}, {3,10:F3}]   target=[{4,6:F2}, {5,6}]   {6}",
                    name, mean, min, max, low, FormatBound(high), verdict));
            }

            Debug.Log(sb.ToString());
        }

        // -----------------------------------------------------------------------------------
        // CSV export
        // -----------------------------------------------------------------------------------

        private static string SimOutputDirectory()
        {
            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)!.FullName;
            string dir = Path.Combine(projectRoot, "SimOutput");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string Timestamp()
        {
            // Editor-only tooling, not Core — this doesn't touch the simulation or any determinism
            // guarantee; it only names an output file.
            return DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        }

        private static void WriteReleasesCsv(SimulationResult result)
        {
            GameState state = result.State;
            string fileName = "releases_s" + result.Seed + "_cfg" + result.Config.ComputeConfigHash() + "_" + Timestamp() + ".csv";
            string path = Path.Combine(SimOutputDirectory(), fileName);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ReleaseId,GroupId,GroupName,GroupTierAtRelease,ReleaseYear,ReleaseWeek,TrackQuality,PromoSpend,FandomSizeAtRelease,PeakPosition,WeeksCharted,WeeksInTop10,TotalPoints");

            for (int i = 0; i < state.Releases.Count; i++)
            {
                Release r = state.Releases[i];
                Group g = state.GetGroup(r.GroupId);
                Track t = state.GetTrack(r.TitleTrackId);

                sb.AppendLine(string.Join(",",
                    r.Id.ToString(CultureInfo.InvariantCulture),
                    r.GroupId.ToString(CultureInfo.InvariantCulture),
                    CsvField(g?.Name ?? ""),
                    r.GroupTierAtRelease.ToString(),
                    r.ReleaseDate.Year.ToString(CultureInfo.InvariantCulture),
                    r.ReleaseDate.Week.ToString(CultureInfo.InvariantCulture),
                    (t?.Quality ?? 0f).ToString("F2", CultureInfo.InvariantCulture),
                    r.PromoSpend.ToString("F2", CultureInfo.InvariantCulture),
                    r.FandomSizeAtRelease.ToString(CultureInfo.InvariantCulture),
                    r.PeakPosition.ToString(CultureInfo.InvariantCulture),
                    r.WeeksCharted.ToString(CultureInfo.InvariantCulture),
                    r.WeeksInTop10.ToString(CultureInfo.InvariantCulture),
                    r.TotalPoints.ToString("F2", CultureInfo.InvariantCulture)));
            }

            File.WriteAllText(path, sb.ToString());
            Debug.Log("[BalanceRunner] Wrote " + path);
        }

        private static void WriteChartHistoryCsv(SimulationResult result)
        {
            GameState state = result.State;
            string fileName = "chart_history_s" + result.Seed + "_cfg" + result.Config.ComputeConfigHash() + "_" + Timestamp() + ".csv";
            string path = Path.Combine(SimOutputDirectory(), fileName);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ReleaseId,GroupId,GroupName,WeeksSinceRelease,Year,Week,Position,Points");

            for (int i = 0; i < state.Releases.Count; i++)
            {
                Release r = state.Releases[i];
                Group g = state.GetGroup(r.GroupId);

                for (int w = 0; w < r.WeeklyPositions.Count; w++)
                {
                    SimDate d = r.ReleaseDate.AdvanceWeeks(w);
                    sb.AppendLine(string.Join(",",
                        r.Id.ToString(CultureInfo.InvariantCulture),
                        r.GroupId.ToString(CultureInfo.InvariantCulture),
                        CsvField(g?.Name ?? ""),
                        w.ToString(CultureInfo.InvariantCulture),
                        d.Year.ToString(CultureInfo.InvariantCulture),
                        d.Week.ToString(CultureInfo.InvariantCulture),
                        r.WeeklyPositions[w].ToString(CultureInfo.InvariantCulture),
                        r.WeeklyPoints[w].ToString("F3", CultureInfo.InvariantCulture)));
                }
            }

            File.WriteAllText(path, sb.ToString());
            Debug.Log("[BalanceRunner] Wrote " + path);
        }

        private static void WriteChartOccupancyCsv(SimulationResult result)
        {
            string fileName = "chart_occupancy_s" + result.Seed + "_cfg" + result.Config.ComputeConfigHash() + "_" + Timestamp() + ".csv";
            string path = Path.Combine(SimOutputDirectory(), fileName);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Year,Week,ActiveGroups,ActiveReleases,ChartedReleases,Top10Entries,PointsAtPos1,PointsAtPos10,PointsAtPos100");

            foreach (OccupancyRow row in result.Occupancy)
            {
                sb.AppendLine(string.Join(",",
                    row.Year.ToString(CultureInfo.InvariantCulture),
                    row.Week.ToString(CultureInfo.InvariantCulture),
                    row.ActiveGroups.ToString(CultureInfo.InvariantCulture),
                    row.ActiveReleases.ToString(CultureInfo.InvariantCulture),
                    row.ChartedReleases.ToString(CultureInfo.InvariantCulture),
                    row.Top10Entries.ToString(CultureInfo.InvariantCulture),
                    row.PointsAtPos1.ToString("F3", CultureInfo.InvariantCulture),
                    row.PointsAtPos10.ToString("F3", CultureInfo.InvariantCulture),
                    row.PointsAtPos100.ToString("F3", CultureInfo.InvariantCulture)));
            }

            File.WriteAllText(path, sb.ToString());
            Debug.Log("[BalanceRunner] Wrote " + path);
        }

        private static string FormatBound(double value)
        {
            return double.IsPositiveInfinity(value) ? "inf" : value.ToString("F2", CultureInfo.InvariantCulture);
        }

        private static string CsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
