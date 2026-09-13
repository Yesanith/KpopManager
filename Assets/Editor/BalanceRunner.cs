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
            ResetTop10Tracking();

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
            public int ActiveGroups, ActiveReleases, ChartedReleases, DistinctTop10EntrantsThisWeek;
            public float PointsAtPos1, PointsAtPos10, PointsAtPos100;
        }

        // Which release ids sat in the top 10 last time CaptureOccupancy ran — module-level state
        // purely for computing DistinctTop10EntrantsThisWeek (fix 2c); reset per RunOne via
        // ResetTop10Tracking so two consecutive runs in the same sweep never bleed into each other.
        private static HashSet<int> _previousTop10ReleaseIds = new HashSet<int>();

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

            int activeReleases = 0, chartedReleases = 0;
            float pos1 = 0f, pos10 = 0f, pos100 = 0f;
            HashSet<int> currentTop10ReleaseIds = new HashSet<int>();

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
                if (position <= 10) currentTop10ReleaseIds.Add(r.Id);
                if (position == 1) pos1 = points;
                if (position == 10) pos10 = points;
                if (position == 100) pos100 = points;
            }

            // Fix 2c: the old Top10Entries always read exactly 10.00 (the top 10 has ten slots by
            // definition, so it measured nothing). This instead counts releases in the top 10 this
            // week that weren't there last week — a real churn signal.
            int distinctEntrants = 0;
            foreach (int id in currentTop10ReleaseIds)
            {
                if (!_previousTop10ReleaseIds.Contains(id)) distinctEntrants++;
            }
            _previousTop10ReleaseIds = currentTop10ReleaseIds;

            return new OccupancyRow
            {
                Year = simulatedWeek.Year, Week = simulatedWeek.Week,
                ActiveGroups = activeGroups, ActiveReleases = activeReleases,
                ChartedReleases = chartedReleases, DistinctTop10EntrantsThisWeek = distinctEntrants,
                PointsAtPos1 = pos1, PointsAtPos10 = pos10, PointsAtPos100 = pos100
            };
        }

        private static void ResetTop10Tracking()
        {
            _previousTop10ReleaseIds = new HashSet<int>();
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

            // Fix 2d: a reported-only number with no pass/fail band (releases per year, group
            // lifespan, crossover rate, weeks-charted mean/p95). Low/High are meaningless for these
            // and must not be scanned into a sweep's pass count.
            public bool IsDiagnostic;
        }

        private sealed class MetricReport
        {
            public MetricValue NumberOneConcentration;
            public MetricValue TopDecileNumberOneShare;

            // Fix 2a: the population is bimodal by design (a typical comeback collapses in weeks; a
            // crossover hit runs for years), so a single mean hides exactly the shape that matters.
            // Iteration 4: mean promoted from an unbanded diagnostic to a real band (6-11) now that
            // iteration 3's flicker fix made it measurable against something other than noise; the
            // median/p95 bands were also revised (iteration 3's were guesses that turned out wrong
            // in the opposite direction — see BALANCE.md). LongevitySkew is new: a plain
            // (mean-median)/mean check, because a roughly-symmetric distribution (iteration 3's
            // failure mode) is exactly the thing none of median/p95/mean alone catch on their own.
            public MetricValue HitLongevityMedian;
            public MetricValue HitLongevityP95;
            public MetricValue HitLongevityMean;
            public MetricValue LongevitySkew;

            // Iteration 4: what we actually care about — how many distinct songs a year ever crack
            // the top 10 — derived from an ESTIMATED entrant count (see BALANCE.md), not measured,
            // so the band itself is provisional.
            public MetricValue DistinctTop10EntrantsPerYear;

            public MetricValue QualityToPeak;
            public MetricValue QualityToLongevity;

            // Fix 2b: the raw count doesn't account for a growing/shrinking active population: 227.5
            // changes/decade across 114.6 active groups is 1.97/group, not the alarming absolute
            // number it looks like. Per-group carries the target; raw stays as a diagnostic.
            public MetricValue TierMobilityRaw;
            public MetricValue TierMobilityPerGroup;

            public MetricValue Persistence;
            public MetricValue Top10Rate;
            public MetricValue NumberOneRate;
            public MetricValue FandomSpread;

            // Fix 2d diagnostics — report only, no target bands.
            public MetricValue ReleasesPerYear;
            public MetricValue GroupLifespanMean;
            public MetricValue GroupLifespanP10;
            public MetricValue GroupLifespanP50;
            public MetricValue GroupLifespanP90;
            public MetricValue CrossoverRatePct;
            public MetricValue MeanWeeksCharted;
            public MetricValue P95WeeksCharted;

            // Phase 3b iteration 3 fix 1/4 diagnostics.
            public MetricValue CompetitionModifierMean;
            public MetricValue CompetitionModifierP5;
            public MetricValue CompetitionModifierP95;
            public MetricValue ChartReentryCount;

            public IEnumerable<MetricValue> All()
            {
                yield return NumberOneConcentration;
                yield return TopDecileNumberOneShare;
                yield return HitLongevityMedian;
                yield return HitLongevityP95;
                yield return HitLongevityMean;
                yield return LongevitySkew;
                yield return DistinctTop10EntrantsPerYear;
                yield return QualityToPeak;
                yield return QualityToLongevity;
                yield return TierMobilityRaw;
                yield return TierMobilityPerGroup;
                yield return Persistence;
                yield return Top10Rate;
                yield return NumberOneRate;
                yield return FandomSpread;
                yield return ReleasesPerYear;
                yield return GroupLifespanMean;
                yield return GroupLifespanP10;
                yield return GroupLifespanP50;
                yield return GroupLifespanP90;
                yield return CrossoverRatePct;
                yield return MeanWeeksCharted;
                yield return P95WeeksCharted;
                yield return CompetitionModifierMean;
                yield return CompetitionModifierP5;
                yield return CompetitionModifierP95;
                yield return ChartReentryCount;
            }
        }

        private static MetricValue Evaluate(string name, double value, double low, double high, bool provisional = false)
        {
            string verdict = value < low ? "LOW" : (value > high ? "HIGH" : "PASS");
            return new MetricValue { Name = name, Value = value, Low = low, High = high, Verdict = verdict, IsProvisional = provisional };
        }

        private static MetricValue Diagnostic(string name, double value)
        {
            return new MetricValue { Name = name, Value = value, Verdict = "INFO", IsDiagnostic = true };
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
            // Phase 3b iteration 3 fix 3: 0.45-0.70 was calibrated against a 21-group world where
            // most groups had a realistic shot at #1. At ~187 active groups and ~30 distinct #1
            // songs/year, the overwhelming majority of groups will never see #1 regardless of how
            // healthy the sim is — Gini over that population is forced up near 0.9 by population
            // size alone, which is also what the real industry looks like. Revised band: 0.82-0.94.
            double gini = BalanceMetrics.Gini(numberOneCounts.Values.Select(v => (double)v).ToList());
            MetricValue numberOne = Evaluate("#1 concentration (Gini)", gini, 0.82, 0.94);

            // Fix 3b: a concentration measure the long tail (the hundreds of groups that will
            // never see #1) can't dominate the way it dominates Gini — what fraction of every #1
            // week landed with the top 10% of groups BY #1-WEEK COUNT. Answers "is the winners'
            // circle itself concentrated," which is the thing actually worth measuring.
            List<double> numberOneCountsDesc = numberOneCounts.Values.Select(v => (double)v).OrderByDescending(v => v).ToList();
            int topDecileCount = Math.Max(1, (int)Math.Ceiling(numberOneCountsDesc.Count * 0.10));
            double topDecileSum = numberOneCountsDesc.Take(topDecileCount).Sum();
            double totalNumberOneWeeks = numberOneCountsDesc.Sum();
            double topDecileShare = totalNumberOneWeeks > 0d ? topDecileSum / totalNumberOneWeeks : 0d;
            MetricValue topDecileShareMetric = Evaluate("Top-decile #1 share", topDecileShare, 0.55, 0.80);

            // Fix 2a: report the distribution, not a mean, since the two-curve model deliberately
            // produces a bimodal population (fast-collapsing fandom-driven hits vs. multi-year
            // crossover long-runners).
            List<Release> hits = state.Releases.Where(r => r.WeeksInTop10 > 0).ToList();
            List<double> top10WeeksForHits = hits.Select(r => (double)r.WeeksInTop10).ToList();
            double medianLongevity = BalanceMetrics.Percentile(top10WeeksForHits, 0.5);
            double p95Longevity = BalanceMetrics.Percentile(top10WeeksForHits, 0.95);
            double meanLongevity = top10WeeksForHits.Count > 0 ? top10WeeksForHits.Average() : 0d;

            // Phase 3b iteration 4: bands revised again — iteration 3's 2-5/25-60 were guesses that
            // turned out wrong in the opposite direction (measured median ~27.7 once flicker was
            // fixed). These are derived from an ESTIMATED 50-80 distinct-top10-entrants/year figure
            // run through the 520 slot-weeks identity, not measured — see BALANCE.md.
            MetricValue longevityMedian = Evaluate("Hit longevity median (weeks in top 10)", medianLongevity, 3d, 8d);
            MetricValue longevityP95 = Evaluate("Hit longevity p95 (weeks in top 10)", p95Longevity, 30d, 55d);
            MetricValue longevityMean = Evaluate("Hit longevity mean (weeks in top 10)", meanLongevity, 6d, 11d);

            // Iteration 4: a plain skew check. Iteration 3 produced a roughly SYMMETRIC distribution
            // (mean 25.9, median 27.7 — median slightly ABOVE mean) even though the two-curve model
            // exists specifically to produce a long-tailed, two-population shape. Nothing else in
            // this report would have caught that on its own; median/p95 both looked internally
            // consistent with each other, just both wrong.
            double longevitySkew = meanLongevity > 0d ? (meanLongevity - medianLongevity) / meanLongevity : 0d;
            MetricValue longevitySkewMetric = Evaluate("Longevity skew ((mean-median)/mean)", longevitySkew, 0.25d, double.PositiveInfinity);

            // Distinct top-10 entrants/year: releases whose FIRST top-10 week falls in a given
            // calendar year, summed across the run and divided by run length — not the same
            // population as "hits" above (that's every release that ever reached top 10, counted
            // once regardless of when); this measures entrant RATE, which is what the 8-13% Top-10
            // rate target and the identity check actually reason about.
            int distinctEntrants = 0;
            for (int i = 0; i < state.Releases.Count; i++)
            {
                Release r = state.Releases[i];
                for (int w = 0; w < r.WeeklyPositions.Count; w++)
                {
                    if (r.WeeklyPositions[w] > 0 && r.WeeklyPositions[w] <= 10)
                    {
                        distinctEntrants++;
                        break;
                    }
                }
            }
            double distinctEntrantsPerYear = result.Years > 0 ? distinctEntrants / (double)result.Years : 0d;
            MetricValue distinctEntrantsMetric = Evaluate("Distinct top-10 entrants / year", distinctEntrantsPerYear, 50d, 80d);

            List<Release> charted = state.Releases.Where(r => r.PeakPosition > 0).ToList();
            List<double> qualities = charted.Select(r => (double)(state.GetTrack(r.TitleTrackId)?.Quality ?? 0f)).ToList();
            List<double> peaks = charted.Select(r => (double)r.PeakPosition).ToList();
            List<double> top10Weeks = charted.Select(r => (double)r.WeeksInTop10).ToList();

            double rPeak = BalanceMetrics.PearsonR(qualities, peaks);
            MetricValue qualityToPeak = Evaluate("Quality -> Peak (Pearson r)", rPeak, -0.40d, -0.15d);

            double rLongevity = BalanceMetrics.PearsonR(qualities, top10Weeks);
            MetricValue qualityToLongevity = Evaluate("Quality -> Longevity (Pearson r)", rLongevity, 0.55d, 1.0d);

            // Fix 2b: raw count doesn't account for population size, so it's kept only as a
            // diagnostic; per-active-group is what carries a real target.
            int tierChangeCount = state.Log.Entries.Count(e => e.Category == LogCategory.Group && e.Severity == LogSeverity.Notable);
            double perDecadeRaw = result.Years > 0 ? tierChangeCount / (result.Years / 10.0) : 0d;
            double meanActiveGroups = result.Occupancy.Count > 0 ? result.Occupancy.Average(o => (double)o.ActiveGroups) : 0d;
            double perGroupPerDecade = meanActiveGroups > 0 ? perDecadeRaw / meanActiveGroups : 0d;

            MetricValue tierMobilityRaw = Diagnostic("Tier mobility raw (changes / decade)", perDecadeRaw);
            MetricValue tierMobilityPerGroup = Evaluate("Tier mobility per active group (/ decade)", perGroupPerDecade, 0.4d, 1.5d);

            double rho = ComputePersistence(result);
            MetricValue persistence = Evaluate("Persistence (Spearman rho, yr5 vs yr" + result.Years + ")", rho, 0.3d, 0.7d);

            // Phase 3b iteration 4: revised again, 4-10% -> 8-13%. Iteration 3's fix 2 (retirement)
            // overshot the other way — measured ~3.2%, implying only ~20 distinct songs/year ever
            // touch the top 10 against a real Melon-scale estimate of 50-80. Derived from that
            // estimate run through the 520 slot-weeks identity, not measured — see BALANCE.md.
            double top10Rate = state.Releases.Count > 0
                ? state.Releases.Count(r => r.WeeksInTop10 > 0) / (double)state.Releases.Count * 100.0
                : 0d;
            MetricValue top10RateMetric = Evaluate("Top-10 rate (% of releases)", top10Rate, 8d, 13d);

            double numberOneRate = state.Releases.Count > 0
                ? state.Releases.Count(r => r.PeakPosition == 1) / (double)state.Releases.Count * 100.0
                : 0d;
            MetricValue numberOneRateMetric = Evaluate("#1 rate (% of releases)", numberOneRate, 0.5d, 3d);

            List<double> activeFandomSizes = state.Groups.Where(g => g.IsActive).Select(g => (double)g.Fandom.Size).ToList();
            double p90 = BalanceMetrics.Percentile(activeFandomSizes, 0.90);
            double p10 = BalanceMetrics.Percentile(activeFandomSizes, 0.10);
            double fandomSpread = p10 > 0d ? p90 / p10 : (p90 > 0d ? double.PositiveInfinity : 0d);
            MetricValue fandomSpreadMetric = Evaluate("Fandom spread (p90/p10)", fandomSpread, 20d, double.PositiveInfinity);

            // Fix 2d diagnostics.
            double releasesPerYear = result.Years > 0 ? state.Releases.Count / (double)result.Years : 0d;
            MetricValue releasesPerYearMetric = Diagnostic("Releases per year", releasesPerYear);

            List<double> lifespanYears = ComputeWorldGroupLifespansYears(state);
            MetricValue lifespanMean = Diagnostic("Group lifespan mean (years)", lifespanYears.Count > 0 ? lifespanYears.Average() : 0d);
            MetricValue lifespanP10 = Diagnostic("Group lifespan p10 (years)", BalanceMetrics.Percentile(lifespanYears, 0.10));
            MetricValue lifespanP50 = Diagnostic("Group lifespan p50 (years)", BalanceMetrics.Percentile(lifespanYears, 0.50));
            MetricValue lifespanP90 = Diagnostic("Group lifespan p90 (years)", BalanceMetrics.Percentile(lifespanYears, 0.90));

            double crossoverRate = state.Releases.Count > 0
                ? state.Releases.Count(r => r.CrossoverMultiplier > 1f) / (double)state.Releases.Count * 100.0
                : 0d;
            MetricValue crossoverRateMetric = Diagnostic("Crossover rate (% of releases)", crossoverRate);

            List<double> weeksChartedAll = state.Releases.Select(r => (double)r.WeeksCharted).ToList();
            MetricValue meanWeeksChartedMetric = Diagnostic("Weeks charted mean (any position)", weeksChartedAll.Count > 0 ? weeksChartedAll.Average() : 0d);
            MetricValue p95WeeksChartedMetric = Diagnostic("Weeks charted p95 (any position)", BalanceMetrics.Percentile(weeksChartedAll, 0.95));

            // Phase 3b iteration 3 fix 1 diagnostics: the real, stored-per-week competition
            // modifier distribution (Release.WeeklyCompetitionModifiers), not a reconstruction.
            List<double> allModifiers = new List<double>();
            for (int i = 0; i < state.Releases.Count; i++)
            {
                List<float> mods = state.Releases[i].WeeklyCompetitionModifiers;
                for (int w = 0; w < mods.Count; w++) allModifiers.Add(mods[w]);
            }
            MetricValue competitionModifierMean = Diagnostic("Competition modifier mean", allModifiers.Count > 0 ? allModifiers.Average() : 0d);
            MetricValue competitionModifierP5 = Diagnostic("Competition modifier p5", BalanceMetrics.Percentile(allModifiers, 0.05));
            MetricValue competitionModifierP95 = Diagnostic("Competition modifier p95", BalanceMetrics.Percentile(allModifiers, 0.95));

            // Fix 4 diagnostic: a genuine re-entry is any 0 -> nonzero transition in
            // WeeklyPositions after the release had already charted at least once before.
            int reentryCount = 0;
            for (int i = 0; i < state.Releases.Count; i++)
            {
                List<int> positions = state.Releases[i].WeeklyPositions;
                bool hasChartedBefore = false;
                for (int w = 0; w < positions.Count; w++)
                {
                    if (positions[w] > 0)
                    {
                        if (w > 0 && positions[w - 1] == 0 && hasChartedBefore) reentryCount++;
                        hasChartedBefore = true;
                    }
                }
            }
            MetricValue chartReentryCountMetric = Diagnostic("Chart re-entries (count)", reentryCount);

            return new MetricReport
            {
                NumberOneConcentration = numberOne,
                TopDecileNumberOneShare = topDecileShareMetric,
                HitLongevityMedian = longevityMedian,
                HitLongevityP95 = longevityP95,
                HitLongevityMean = longevityMean,
                LongevitySkew = longevitySkewMetric,
                DistinctTop10EntrantsPerYear = distinctEntrantsMetric,
                QualityToPeak = qualityToPeak,
                QualityToLongevity = qualityToLongevity,
                TierMobilityRaw = tierMobilityRaw,
                TierMobilityPerGroup = tierMobilityPerGroup,
                Persistence = persistence,
                Top10Rate = top10RateMetric,
                NumberOneRate = numberOneRateMetric,
                FandomSpread = fandomSpreadMetric,
                ReleasesPerYear = releasesPerYearMetric,
                GroupLifespanMean = lifespanMean,
                GroupLifespanP10 = lifespanP10,
                GroupLifespanP50 = lifespanP50,
                GroupLifespanP90 = lifespanP90,
                CrossoverRatePct = crossoverRateMetric,
                MeanWeeksCharted = meanWeeksChartedMetric,
                P95WeeksCharted = p95WeeksChartedMetric,
                CompetitionModifierMean = competitionModifierMean,
                CompetitionModifierP5 = competitionModifierP5,
                CompetitionModifierP95 = competitionModifierP95,
                ChartReentryCount = chartReentryCountMetric
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

        // Fix 3 diagnostic: lifespan in years for every world group that ever existed (the churn
        // population — the player's own 3 centers never disband and would only dilute this with a
        // flat "ran the whole 50 years"). Still-active groups are right-censored at the
        // simulation's final date rather than dropped, so a healthy population (which mostly
        // hasn't disbanded yet) doesn't read as artificially short-lived.
        private static List<double> ComputeWorldGroupLifespansYears(GameState state)
        {
            List<double> lifespans = new List<double>();
            if (state.Company == null) return lifespans;

            SimDate finalDate = state.Date;

            for (int i = 0; i < state.Groups.Count; i++)
            {
                Group group = state.Groups[i];
                if (state.Company.CenterIds.Contains(group.CenterId)) continue; // player's own company

                SimDate endDate = group.DisbandDate ?? finalDate;
                int weeks = SimDate.WeeksBetween(group.DebutDate, endDate);
                lifespans.Add(weeks / (double)SimDate.WeeksPerYear);
            }

            return lifespans;
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
                if (m.IsDiagnostic)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-42} value={1,10:F3}   (diagnostic, no target)", m.Name, m.Value));
                    continue;
                }

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
            AppendStat(sb, "DistinctTop10Entrants", rows.Select(r => (double)r.DistinctTop10EntrantsThisWeek));
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
                bool isDiagnostic = perSeed[0][mi].IsDiagnostic;

                List<double> values = perSeed.Select(m => m[mi].Value).Where(v => !double.IsInfinity(v)).ToList();
                double mean = values.Count > 0 ? values.Average() : 0d;
                double min = values.Count > 0 ? values.Min() : 0d;
                double max = values.Count > 0 ? values.Max() : 0d;

                if (isDiagnostic)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-42} mean={1,10:F3}   range=[{2,10:F3}, {3,10:F3}]   (diagnostic, no target)",
                        name, mean, min, max));
                    continue;
                }

                double low = perSeed[0][mi].Low;
                double high = perSeed[0][mi].High;
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
            sb.AppendLine("ReleaseId,GroupId,GroupName,GroupTierAtRelease,ReleaseYear,ReleaseWeek,TrackQuality,PromoSpend,FandomSizeAtRelease,FandomPull,PublicAppeal,CrossoverMultiplier,PeakPosition,WeeksCharted,WeeksInTop10,TotalPoints");

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
                    r.FandomPull.ToString("F3", CultureInfo.InvariantCulture),
                    r.PublicAppeal.ToString("F3", CultureInfo.InvariantCulture),
                    r.CrossoverMultiplier.ToString("F3", CultureInfo.InvariantCulture),
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
            sb.AppendLine("Year,Week,ActiveGroups,ActiveReleases,ChartedReleases,DistinctTop10EntrantsThisWeek,PointsAtPos1,PointsAtPos10,PointsAtPos100");

            foreach (OccupancyRow row in result.Occupancy)
            {
                sb.AppendLine(string.Join(",",
                    row.Year.ToString(CultureInfo.InvariantCulture),
                    row.Week.ToString(CultureInfo.InvariantCulture),
                    row.ActiveGroups.ToString(CultureInfo.InvariantCulture),
                    row.ActiveReleases.ToString(CultureInfo.InvariantCulture),
                    row.ChartedReleases.ToString(CultureInfo.InvariantCulture),
                    row.DistinctTop10EntrantsThisWeek.ToString(CultureInfo.InvariantCulture),
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
