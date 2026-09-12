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
    /// <summary>
    /// Runs the chart simulation headless for N years and reports whether it's balanced — the
    /// half of Phase 3a that isn't the simulation itself. <b>This tool does not tune anything.</b>
    /// It measures. See <c>Docs/BALANCE.md</c> for where the tuning conversation actually happens.
    /// </summary>
    public static class BalanceRunner
    {
        private const int DefaultYears = 50;
        private const ulong DefaultSeed = 12345UL;

        // DESIGN: five arbitrary, fixed seeds — not tuned, just "different enough to catch a
        // formula that only works by luck on one of them." Kept as a named constant so a sweep is
        // always the same five runs, reproducibly.
        private static readonly ulong[] SweepSeeds = { 11111UL, 22222UL, 33333UL, 44444UL, 55555UL };

        [MenuItem("KpopManager/Run Balance Simulation")]
        public static void RunBalanceSimulation()
        {
            SimulationResult result = RunOne(DefaultSeed, DefaultYears);
            MetricReport report = ComputeMetrics(result);

            PrintReport(result, report);
            WriteReleasesCsv(result);
            WriteChartHistoryCsv(result);
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

            int snapshotYear = Math.Min(5, years);
            Dictionary<int, long> fandomAtYear5 = null;

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int year = 1; year <= years; year++)
            {
                engine.AdvanceYears(1);
                if (year == snapshotYear)
                {
                    fandomAtYear5 = SnapshotFandom(engine.State);
                }
            }
            stopwatch.Stop();

            Dictionary<int, long> fandomAtFinalYear = SnapshotFandom(engine.State);

            return new SimulationResult
            {
                Seed = seed,
                Years = years,
                State = engine.State,
                Config = engine.State.ChartConfig,
                FandomAtYear5 = fandomAtYear5 ?? fandomAtFinalYear,
                FandomAtFinalYear = fandomAtFinalYear,
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }

        private static Dictionary<int, long> SnapshotFandom(GameState state)
        {
            Dictionary<int, long> snapshot = new Dictionary<int, long>(state.Groups.Count);
            for (int i = 0; i < state.Groups.Count; i++)
            {
                snapshot[state.Groups[i].Id] = state.Groups[i].Fandom.Size;
            }
            return snapshot;
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
            public Dictionary<int, long> FandomAtYear5;
            public Dictionary<int, long> FandomAtFinalYear;
            public double ElapsedMs;
        }

        private sealed class MetricValue
        {
            public string Name;
            public double Value;
            public double Low;
            public double High;
            public string Verdict;
        }

        private sealed class MetricReport
        {
            public MetricValue NumberOneConcentration;
            public MetricValue HitLongevity;
            public MetricValue QualityToPeak;
            public MetricValue QualityToLongevity;
            public MetricValue TierMobility;
            public MetricValue Persistence;

            public IEnumerable<MetricValue> All()
            {
                yield return NumberOneConcentration;
                yield return HitLongevity;
                yield return QualityToPeak;
                yield return QualityToLongevity;
                yield return TierMobility;
                yield return Persistence;
            }
        }

        private static MetricValue Evaluate(string name, double value, double low, double high)
        {
            string verdict = value < low ? "LOW" : (value > high ? "HIGH" : "PASS");
            return new MetricValue { Name = name, Value = value, Low = low, High = high, Verdict = verdict };
        }

        private static MetricReport ComputeMetrics(SimulationResult result)
        {
            GameState state = result.State;

            // 1. #1 concentration — Gini over every group's own #1-week count, including groups
            // that never hit #1 (0), since concentration is about the whole population, not just
            // the winners.
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

            // 2. Hit longevity — mean WeeksInTop10 across releases that reached top 10 at all.
            List<Release> hits = state.Releases.Where(r => r.WeeksInTop10 > 0).ToList();
            double meanLongevity = hits.Count > 0 ? hits.Average(r => (double)r.WeeksInTop10) : 0d;
            MetricValue longevity = Evaluate("Hit longevity (mean weeks in top 10)", meanLongevity, 4d, 12d);

            // 3 & 4. Quality correlations, over releases that actually charted at some point.
            List<Release> charted = state.Releases.Where(r => r.PeakPosition > 0).ToList();
            List<double> qualities = charted.Select(r => (double)(state.GetTrack(r.TitleTrackId)?.Quality ?? 0f)).ToList();
            List<double> peaks = charted.Select(r => (double)r.PeakPosition).ToList();
            List<double> top10Weeks = charted.Select(r => (double)r.WeeksInTop10).ToList();

            double rPeak = BalanceMetrics.PearsonR(qualities, peaks);
            MetricValue qualityToPeak = Evaluate("Quality -> Peak (Pearson r)", rPeak, -0.40d, -0.15d);

            double rLongevity = BalanceMetrics.PearsonR(qualities, top10Weeks);
            MetricValue qualityToLongevity = Evaluate("Quality -> Longevity (Pearson r)", rLongevity, 0.55d, 1.0d);

            // 5. Tier mobility — TierSystem only ever logs Category=Group/Severity=Notable on an
            // actual tier change, so counting those entries directly counts tier changes.
            int tierChangeCount = state.Log.Entries.Count(e => e.Category == LogCategory.Group && e.Severity == LogSeverity.Notable);
            double perDecade = result.Years > 0 ? tierChangeCount / (result.Years / 10.0) : 0d;
            MetricValue tierMobility = Evaluate("Tier mobility (changes / decade)", perDecade, 8d, 20d);

            // 6. Persistence — Spearman rank correlation of fandom size at year 5 vs the final year.
            List<int> groupIds = result.FandomAtYear5.Keys.Intersect(result.FandomAtFinalYear.Keys).ToList();
            List<double> sizesYear5 = groupIds.Select(id => (double)result.FandomAtYear5[id]).ToList();
            List<double> sizesFinal = groupIds.Select(id => (double)result.FandomAtFinalYear[id]).ToList();
            double rho = BalanceMetrics.SpearmanRho(sizesYear5, sizesFinal);
            MetricValue persistence = Evaluate("Persistence (Spearman rho, yr5 vs yr" + result.Years + ")", rho, 0.3d, 0.7d);

            return new MetricReport
            {
                NumberOneConcentration = numberOne,
                HitLongevity = longevity,
                QualityToPeak = qualityToPeak,
                QualityToLongevity = qualityToLongevity,
                TierMobility = tierMobility,
                Persistence = persistence
            };
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
                result.State.Releases.Count + " releases, " + result.State.Groups.Count + " groups)");
            sb.AppendLine();

            foreach (MetricValue m in report.All())
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-46} value={1,8:F3}   target=[{2,6:F2}, {3,6:F2}]   {4}",
                    m.Name, m.Value, m.Low, m.High, m.Verdict));
            }

            Debug.Log(sb.ToString());
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

                List<double> values = perSeed.Select(m => m[mi].Value).ToList();
                double mean = values.Average();
                double min = values.Min();
                double max = values.Max();
                int passCount = values.Count(v => v >= low && v <= high);

                string verdict = passCount == values.Count
                    ? "PASS (all " + values.Count + " seeds)"
                    : passCount + "/" + values.Count + " seeds pass";

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-46} mean={1,8:F3}   range=[{2,8:F3}, {3,8:F3}]   target=[{4,6:F2}, {5,6:F2}]   {6}",
                    name, mean, min, max, low, high, verdict));
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
            // Editor-only tooling, not Core — DateTime.Now here doesn't touch the simulation and
            // isn't part of any determinism guarantee; it only names an output file.
            return DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        }

        private static void WriteReleasesCsv(SimulationResult result)
        {
            GameState state = result.State;
            string fileName = "releases_s" + result.Seed + "_cfg" + result.Config.ComputeConfigHash() + "_" + Timestamp() + ".csv";
            string path = Path.Combine(SimOutputDirectory(), fileName);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ReleaseId,GroupId,GroupName,GroupTier,ReleaseYear,ReleaseWeek,TrackQuality,PromoSpend,FandomSizeAtRelease,PeakPosition,WeeksCharted,WeeksInTop10,TotalPoints");

            for (int i = 0; i < state.Releases.Count; i++)
            {
                Release r = state.Releases[i];
                Group g = state.GetGroup(r.GroupId);
                Track t = state.GetTrack(r.TitleTrackId);

                sb.AppendLine(string.Join(",",
                    r.Id.ToString(CultureInfo.InvariantCulture),
                    r.GroupId.ToString(CultureInfo.InvariantCulture),
                    CsvField(g?.Name ?? ""),
                    g?.Tier.ToString() ?? "",
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

        private static string CsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
