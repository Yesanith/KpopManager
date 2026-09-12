using System.Collections.Generic;

namespace KpopManager.Core.Systems.Chart
{
    // Re-derives a group's GroupTier from a rolling window of recent chart performance and
    // current fandom size, so tier mobility is real in both directions.
    //
    // Not registered as its own tick-order step — DESIGN.md's twelve steps have no slot named for
    // it, and this is a static, stateless function rather than an ISimSystem. Fandom.FandomSystem
    // calls Evaluate at the end of its own tick (step 6), since that's the point in the week where
    // this week's chart results (step 4, already run) and this week's fandom update are both
    // fresh. See ARCHITECTURE.md.
    //
    // Only re-evaluates a group once every ChartConfig.TierEvaluationIntervalWeeks weeks (gated on
    // weeks-since-debut so each group's own cadence is deterministic and doesn't depend on
    // iteration order) — both to avoid weekly tier flicker and to avoid rescanning a group's
    // entire release history every single week for no reason.
    //
    // Phase 3b fix 2c: tier is now percentile rank against the cohort of every currently-active
    // group, not an absolute score threshold — absolute thresholds broke the moment the chart's
    // scale changed (a deliberately mediocre group still scored 91.7 against the old
    // TierThresholdLegendary of 82; every group came out Legendary). Every active group's
    // composite score is computed once per week, up front, before checking which groups are due —
    // DESIGN: this is the "consistent cohort" choice called out in the Phase 3b brief. A group's
    // percentile is ranked against everyone's score *at this same moment*, not re-derived from
    // whatever week each other group happened to last be due; the alternative (rank only against
    // whoever else is due this same week) would make the cohort size and composition arbitrary and
    // dependent on debut-date scheduling coincidence.
    public static class TierSystem
    {
        public static void Evaluate(GameState state, SimDate now)
        {
            ChartConfig config = state.ChartConfig;

            List<Group> activeGroups = new List<Group>();
            for (int i = 0; i < state.Groups.Count; i++)
            {
                if (state.Groups[i].IsActive) activeGroups.Add(state.Groups[i]);
            }
            if (activeGroups.Count == 0) return;

            float[] scores = new float[activeGroups.Count];
            for (int i = 0; i < activeGroups.Count; i++)
            {
                scores[i] = ComputeCompositeScore(state, activeGroups[i], now, config);
            }

            for (int i = 0; i < activeGroups.Count; i++)
            {
                Group group = activeGroups[i];
                if (!IsDue(group, now, config)) continue;

                float percentileFromTop = PercentileFromTop(scores, i);
                GroupTier newTier = MapPercentileToTierWithHysteresis(percentileFromTop, group.Tier, config);

                if (newTier != group.Tier)
                {
                    LogTierChange(state, group, newTier, now);
                    group.Tier = newTier;
                }
            }
        }

        private static bool IsDue(Group group, SimDate now, ChartConfig config)
        {
            if (config.TierEvaluationIntervalWeeks <= 0) return true;

            int weeksSinceDebut = SimDate.WeeksBetween(group.DebutDate, now);
            if (weeksSinceDebut < 0) return false;

            return weeksSinceDebut % config.TierEvaluationIntervalWeeks == 0;
        }

        private static float ComputeCompositeScore(GameState state, Group group, SimDate now, ChartConfig config)
        {
            SimDate windowStart = now.AdvanceWeeks(-config.TierWindowYears * SimDate.WeeksPerYear);

            int bestPeakInWindow = int.MaxValue;
            int weeksInTop10InWindow = 0;

            for (int i = 0; i < group.ReleaseIds.Count; i++)
            {
                Release release = state.GetRelease(group.ReleaseIds[i]);
                if (release == null) continue;

                for (int week = 0; week < release.WeeklyPositions.Count; week++)
                {
                    SimDate weekDate = release.ReleaseDate.AdvanceWeeks(week);
                    if (weekDate < windowStart || weekDate > now) continue;

                    int position = release.WeeklyPositions[week];
                    if (position <= 0) continue;

                    if (position < bestPeakInWindow) bestPeakInWindow = position;
                    if (position <= 10) weeksInTop10InWindow++;
                }
            }

            float peakScore = bestPeakInWindow == int.MaxValue ? 0f : Clamp(101 - bestPeakInWindow, 0f, 100f);

            float weeksInTop10Score = config.TierWeeksInTop10NormalizerWeeks > 0f
                ? Clamp(weeksInTop10InWindow / config.TierWeeksInTop10NormalizerWeeks * 100f, 0f, 100f)
                : 0f;

            float fandomScore = ChartSystem.NormalizeFandom(group.Fandom.Size, config);

            return config.TierScoreWeightPeak * peakScore +
                   config.TierScoreWeightWeeksInTop10 * weeksInTop10Score +
                   config.TierScoreWeightFandom * fandomScore;
        }

        // Fraction of every OTHER active group whose score is strictly higher — 0.0 means nobody
        // scored higher (you're #1), close to 1.0 means almost everyone scored higher.
        private static float PercentileFromTop(float[] scores, int index)
        {
            float mine = scores[index];
            int better = 0;
            for (int j = 0; j < scores.Length; j++)
            {
                if (j != index && scores[j] > mine) better++;
            }
            return (float)better / scores.Length;
        }

        private static GroupTier MapPercentileToTier(float percentileFromTop, ChartConfig config)
        {
            if (percentileFromTop < config.TierPctLegendary) return GroupTier.Legendary;

            float b2 = config.TierPctLegendary + config.TierPctTopTier;
            if (percentileFromTop < b2) return GroupTier.TopTier;

            float b3 = b2 + config.TierPctEstablished;
            if (percentileFromTop < b3) return GroupTier.Established;

            float b4 = b3 + config.TierPctRising;
            if (percentileFromTop < b4) return GroupTier.Rising;

            return GroupTier.Rookie;
        }

        // Dead-band hysteresis: nudge the percentile toward "worse" before confirming a promotion,
        // or toward "better" before confirming a demotion. If the nudged value still crosses in the
        // same direction as the naive check, commit the move; otherwise the group stays where it
        // was. This is what stops a group sitting exactly on a boundary from flipping every
        // evaluation.
        //
        // Only applied to a single-tier move. A jump of two or more tiers in one evaluation is a
        // decisive real change (e.g. a Rookie's release rockets straight to #1), not boundary
        // flicker, and shouldn't be second-guessed by a small margin. This also sidesteps a real
        // bug the config's own starting values would otherwise hit: TierHysteresisPct and
        // TierPctLegendary are both 0.02, so a group sitting at the exact top of its cohort
        // (percentile 0.0) would have its promotion check land exactly on the Legendary boundary
        // after the nudge and get rejected every time — silently making Legendary unreachable by
        // promotion from anywhere below it.
        private static GroupTier MapPercentileToTierWithHysteresis(float percentileFromTop, GroupTier currentTier, ChartConfig config)
        {
            GroupTier naive = MapPercentileToTier(percentileFromTop, config);
            if (naive == currentTier) return currentTier;

            int tierGap = System.Math.Abs((int)naive - (int)currentTier);
            if (tierGap > 1) return naive;

            bool promoting = naive > currentTier;
            float adjusted = promoting ? percentileFromTop + config.TierHysteresisPct : percentileFromTop - config.TierHysteresisPct;
            GroupTier confirmed = MapPercentileToTier(adjusted, config);

            return confirmed == naive ? naive : currentTier;
        }

        private static void LogTierChange(GameState state, Group group, GroupTier newTier, SimDate now)
        {
            bool promoted = newTier > group.Tier;
            state.Log.Add(
                now, LogCategory.Group, LogSeverity.Notable,
                group.Name + (promoted ? " promoted from " : " demoted from ") + group.Tier + " to " + newTier,
                group.Id);
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
