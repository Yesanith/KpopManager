using System;

namespace KpopManager.Core.Systems.Chart
{
    /// <summary>
    /// Re-derives a group's <see cref="GroupTier"/> from a rolling window of recent chart
    /// performance and current fandom size, so tier mobility is real in both directions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not registered as its own tick-order step — DESIGN.md's twelve steps have no slot named for
    /// it, and this is a static, stateless pure-ish function rather than an <see cref="ISimSystem"/>.
    /// <see cref="Fandom.FandomSystem"/> calls <see cref="Evaluate"/> at the end of its own tick
    /// (step 6), since that's the point in the week where this week's chart results (step 4,
    /// already run) and this week's fandom update are both fresh. See ARCHITECTURE.md.
    /// </para>
    /// <para>
    /// Only re-evaluates a group once every <see cref="ChartConfig.TierEvaluationIntervalWeeks"/>
    /// weeks (gated on weeks-since-debut so each group's own cadence is deterministic and doesn't
    /// depend on iteration order) — both to avoid weekly tier flicker and to avoid rescanning a
    /// group's entire release history every single week for no reason.
    /// </para>
    /// </remarks>
    public static class TierSystem
    {
        public static void Evaluate(GameState state, SimDate now)
        {
            ChartConfig config = state.ChartConfig;

            for (int i = 0; i < state.Groups.Count; i++)
            {
                Group group = state.Groups[i];
                if (!group.IsActive) continue;
                if (!IsDue(group, now, config)) continue;

                GroupTier newTier = ComputeTier(state, group, now, config);
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

        private static GroupTier ComputeTier(GameState state, Group group, SimDate now, ChartConfig config)
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

            float compositeScore =
                config.TierScoreWeightPeak * peakScore +
                config.TierScoreWeightWeeksInTop10 * weeksInTop10Score +
                config.TierScoreWeightFandom * fandomScore;

            if (compositeScore >= config.TierThresholdLegendary) return GroupTier.Legendary;
            if (compositeScore >= config.TierThresholdTopTier) return GroupTier.TopTier;
            if (compositeScore >= config.TierThresholdEstablished) return GroupTier.Established;
            if (compositeScore >= config.TierThresholdRising) return GroupTier.Rising;
            return GroupTier.Rookie;
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
