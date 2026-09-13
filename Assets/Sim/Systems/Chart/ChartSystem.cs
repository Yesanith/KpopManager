using System;
using System.Collections.Generic;

namespace KpopManager.Core.Systems.Chart
{
    // Tick step 4: computes every actively-charting release's weekly points from its two
    // trajectory components, applies competition and variance, ranks the result, and appends one
    // week of history to each release.
    //
    // Phase 3b iteration 2 replaced the single BuzzScore-times-decay formula with two superimposed
    // components stored on Release at release time (see ChartConfig's own comment for why):
    //   - FandomPull: a steep-decay term scaled by fandom size at release. "Total attack" — buys a
    //     big week one, then fades fast (half-life ~2 weeks).
    //   - PublicAppeal: a shallow-decay term scaled by track quality (plus a rare crossover
    //     multiplier), deliberately independent of fandom size. Buys the multi-year long tail.
    //
    // Five passes per tick, in this order, because each depends on the previous being complete
    // for every release before it can run:
    //   1. Weeks-since-release and an undecayed "raw strength" per release (both components at
    //      full amplitude, no decay/competition/variance yet) — used only to feed the competition
    //      comparison, the same role ComputeRawBuzz played before the iteration 2 rewrite.
    //   2. Competition modifier per release — iteration 3 fix 1 made this share-based (a release's
    //      strength vs. the MEAN of its rivals', times how crowded the window is relative to
    //      normal) instead of sum-based, so it no longer scales with world size. See
    //      CompetitionModifier's own comment.
    //   3. Actual weekly points: both components decayed by their own independent curve, summed,
    //      then promo/competition/variance applied.
    //   4. Rank everyone by weekly points. Iteration 3 fix 4 adds one-directional hysteresis here:
    //      a release re-entering the chart after being off it must beat this week's natural
    //      rank-ChartSize cutoff by ChartReentryMarginPct, or it stays at position 0 — otherwise
    //      random variance alone was enough to flicker a release on and off the chart weekly.
    //   5. Record history and retire anything that's been off-chart too long or hit the hard
    //      simulated-weeks backstop — iteration 3 fix 2, see RecordWeek.
    //
    // conceptFit and trendFit are flat 50 this phase — Phase 4 owns both; the term is wired in
    // now so the shape doesn't change later, only the two functions computing them.
    public sealed class ChartSystem : ISimSystem
    {
        public string Name => "Chart simulation";

        public void Tick(GameState state)
        {
            ChartConfig config = state.ChartConfig;
            SimDate now = state.Date;

            List<Release> active = CollectActiveReleases(state);
            if (active.Count == 0) return;

            int[] weeksSince = new int[active.Count];
            float[] rawStrength = new float[active.Count];
            for (int i = 0; i < active.Count; i++)
            {
                weeksSince[i] = SimDate.WeeksBetween(active[i].ReleaseDate, now);
                rawStrength[i] = ComputeUndecayedStrength(state, active[i], config);
            }

            float[] weeklyPoints = new float[active.Count];
            float[] competitionModifiers = new float[active.Count];
            for (int i = 0; i < active.Count; i++)
            {
                competitionModifiers[i] = CompetitionModifier(active, rawStrength, i, config);
                weeklyPoints[i] = ComputeWeeklyPoints(state, active[i], weeksSince[i], competitionModifiers[i], config, state.Random);
            }

            int[] order = SortIndicesDescending(weeklyPoints, active.Count);

            // Phase 3b iteration 3 fix 4: the reference cutoff a re-entrant must beat by
            // ChartReentryMarginPct — computed from the NATURAL (non-margin) ranking up front, so
            // every release's margin check has a stable reference regardless of how many other
            // releases end up denied re-entry this same week.
            float reentryCutoff = ComputeReentryCutoffPoints(weeklyPoints, order, config);

            int rank = 0;
            for (int idx = 0; idx < order.Length; idx++)
            {
                int i = order[idx];
                Release release = active[i];
                float points = weeklyPoints[i];

                bool clearsFloor = points >= config.ChartFloorPoints;
                bool chartFull = rank >= config.ChartSize;
                bool deniedReentry = clearsFloor && !chartFull && RequiresReentryMargin(release) &&
                    points < reentryCutoff * (1f + config.ChartReentryMarginPct);

                int position;
                if (!clearsFloor || chartFull || deniedReentry)
                {
                    // A denied re-entrant doesn't consume a rank slot — the next release in
                    // descending order (already accounting for this one's absence) takes it
                    // instead, so the position sequence stays contiguous with no gaps.
                    position = 0;
                }
                else
                {
                    rank++;
                    position = rank;
                }

                RecordWeek(release, position, points, competitionModifiers[i], config);
            }
        }

        // The points value a release needs to make the chart under plain ranking (ignoring the
        // re-entry margin) — the ChartSize-th release's points, or the smallest-points release
        // that still clears ChartFloorPoints if fewer than ChartSize releases do. This is "this
        // week's rank-100 cutoff" that ChartConfig.ChartReentryMarginPct's comment refers to.
        //
        // Internal, not private: exercised directly by ChartSystemTests.
        internal static float ComputeReentryCutoffPoints(float[] weeklyPoints, int[] order, ChartConfig config)
        {
            int naturalRank = 0;
            float cutoff = config.ChartFloorPoints;

            for (int idx = 0; idx < order.Length; idx++)
            {
                float points = weeklyPoints[order[idx]];
                if (points < config.ChartFloorPoints) break; // sorted descending; nothing further clears the floor

                naturalRank++;
                cutoff = points;
                if (naturalRank >= config.ChartSize) break;
            }

            return cutoff;
        }

        // True only for a genuine re-entrant — a release with prior chart history whose most
        // recent recorded week was off-chart. False for a brand-new release's debut week (nothing
        // to "re"-enter) and false for a release that's already charting (no margin needed to stay
        // put — this is one-directional hysteresis, the same shape as TierSystem's
        // TierHysteresisPct: harder to get back in than to stay in).
        //
        // Internal, not private: exercised directly by ChartSystemTests.
        internal static bool RequiresReentryMargin(Release release)
        {
            int recordedWeeks = release.WeeklyPositions.Count;
            if (recordedWeeks == 0) return false;
            return release.WeeklyPositions[recordedWeeks - 1] == 0;
        }

        private static List<Release> CollectActiveReleases(GameState state)
        {
            List<Release> active = new List<Release>();
            for (int i = 0; i < state.Releases.Count; i++)
            {
                if (state.Releases[i].IsCharting) active.Add(state.Releases[i]);
            }
            return active;
        }

        // FandomPull at release time: NormalizeFandom(fandomSize) * FandomPullScale. Called once
        // by ReleaseScheduler when a release is created; the result is stored on Release, not
        // recomputed weekly, so a release's week-one spike potential is fixed at the moment fans
        // actually bought (matching real "총공" behaviour, which doesn't re-roll later).
        //
        // Internal, not private: exercised directly by ChartSystemTests.
        internal static float ComputeFandomPull(long fandomSizeAtRelease, ChartConfig config)
        {
            return NormalizeFandom(fandomSizeAtRelease, config) * config.FandomPullScale;
        }

        // PublicAppeal at release time: a weighted sum of a quality term plus conceptFit/trendFit,
        // deliberately NOT a function of fandom size — this is what carries a release's long-tail
        // potential. conceptFit and trendFit are flat 50 this phase; see the class-level comment.
        //
        // Phase 3b iteration 4 fix 4 (DESIGN): the quality term is pow(quality/100,
        // PublicAppealQualityExponent) * 100, not quality itself. A linear term let a Q=20.6 track
        // reach #8 (iteration 3's "Chrome Prologue") because 20/100 quality still contributed a
        // full 20% of the maximum quality term — enough, combined with a fandom spike, to hold a
        // top-10 spot. The exponent (>1) suppresses weak songs disproportionately (Q=20 -> ~8% of
        // max, Q=80 -> ~70%) without touching FandomPull at all: a weak song's fandom can still buy
        // it a brief spike (realistic — total attack doesn't care about quality), it just can't
        // SUSTAIN a top-10 run on public appeal the way a genuinely good song can.
        //
        // Internal, not private: exercised directly by ChartSystemTests.
        internal static float ComputePublicAppeal(float quality, ChartConfig config)
        {
            const float conceptFit = 50f;
            const float trendFit = 50f;

            float clampedQuality = quality < 0f ? 0f : (quality > 100f ? 100f : quality);
            float qualityTerm = (float)Math.Pow(clampedQuality / 100f, config.PublicAppealQualityExponent) * 100f;

            float appeal =
                (config.PublicAppealQualityWeight * qualityTerm +
                 config.PublicAppealConceptWeight * conceptFit +
                 config.PublicAppealTrendWeight * trendFit) * config.PublicAppealScale;

            return appeal < 0f ? 0f : appeal;
        }

        // Rolls whether this release catches on with the general public, and by how much.
        // Rare by design (CrossoverChanceBase + a small per-quality-point bump) — see ChartConfig's
        // DESIGN note. Returns 1.0 (no-op multiplier) if the roll fails.
        //
        // Internal, not private: exercised directly by ChartSystemTests.
        internal static float RollCrossoverMultiplier(SimRandom rng, float quality, ChartConfig config)
        {
            float qualityAboveFifty = quality > 50f ? quality - 50f : 0f;
            float chance = config.CrossoverChanceBase + config.CrossoverChancePerQuality * qualityAboveFifty;

            if (!rng.Chance(chance)) return 1f;
            return rng.NextFloat(config.CrossoverMultiplierMin, config.CrossoverMultiplierMax);
        }

        // Undecayed strength (both components at full amplitude, tier/promo boosts applied, no
        // competition/variance) — fed only into the competition-window sum below, the same role
        // the old ComputeRawBuzz played. Tier and promo read the group's CURRENT state (tier can
        // change, e.g. a promotion, over a long-charting release's life); FandomPull/PublicAppeal
        // themselves are the release-time-fixed values already stored on Release.
        //
        // Internal, not private: exercised directly by ChartSystemTests.
        internal static float ComputeUndecayedStrength(GameState state, Release release, ChartConfig config)
        {
            Group group = state.GetGroup(release.GroupId);
            (float tierBoost, float promoBoost) = ComputeBoosts(state, release, group, config);

            float total = (release.FandomPull * tierBoost + release.PublicAppeal) * promoBoost;
            return total < 0f ? 0f : total;
        }

        // Actual weekly points: each component decayed by its own independent curve (see
        // ChartConfig), summed, then tier/promo boosts, competition, and variance applied.
        //
        // Internal, not private: exercised directly by ChartSystemTests.
        internal static float ComputeWeeklyPoints(
            GameState state, Release release, int weeksSince, float competitionModifier, ChartConfig config, SimRandom rng)
        {
            Group group = state.GetGroup(release.GroupId);
            (float tierBoost, float promoBoost) = ComputeBoosts(state, release, group, config);

            float fandomComponent = release.FandomPull * tierBoost * (float)Math.Exp(-config.FandomDecayK * weeksSince);
            float publicComponent = release.PublicAppeal * BuildCurve(weeksSince, config) * (float)Math.Exp(-config.PublicDecayK * weeksSince);

            float variance = 1f + rng.NextFloat(-config.RandomVariancePct, config.RandomVariancePct);

            float points = (fandomComponent + publicComponent) * promoBoost * competitionModifier * variance;
            return points < 0f ? 0f : points;
        }

        // DESIGN: promo boosts the whole total (initial push and ongoing visibility both);
        // tier only boosts the fandom side (an established act's existing reach buys a bigger
        // opening, not a longer tail). See ChartConfig's own DESIGN note.
        private static (float tierBoost, float promoBoost) ComputeBoosts(GameState state, Release release, Group group, ChartConfig config)
        {
            float tierScore = TierScore(group?.Tier ?? GroupTier.Rookie);
            float promoTerm = NormalizePromo(release.PromoSpend, config);

            float tierBoost = 1f + config.WeightGroupTier * (tierScore / 100f);
            float promoBoost = 1f + config.WeightPromoSpend * (promoTerm / 100f);

            return (tierBoost, promoBoost);
        }

        // The public component's ramp-in: PublicBuildFloor at week 0, linearly up to 1.0 by
        // PublicBuildWeeks, then held at 1.0 forever after (the shallow PublicDecayK is what
        // eventually brings a long-runner back down, not this curve). See ChartConfig's DESIGN
        // note on why linear, not a smoothstep or logistic shape.
        //
        // Internal, not private: exercised directly by ChartSystemTests.
        internal static float BuildCurve(int weeksSinceRelease, ChartConfig config)
        {
            if (config.PublicBuildWeeks <= 0) return 1f;
            if (weeksSinceRelease <= 0) return config.PublicBuildFloor;
            if (weeksSinceRelease >= config.PublicBuildWeeks) return 1f;

            float t = (float)weeksSinceRelease / config.PublicBuildWeeks;
            return config.PublicBuildFloor + (1f - config.PublicBuildFloor) * t;
        }

        // DESIGN (Phase 3b iteration 3 fix 1): share-based, not sum-based. The original formula
        // compared a release against the ABSOLUTE SUM of every rival's raw strength in its window
        // — that sum scales with however many releases happen to be active, so growing the world
        // (iteration 2) silently divided every release's points by another ~30x on top of it. This
        // instead compares against the rival group's MEAN strength (population-size-independent on
        // its own) times a crowdFactor capturing "how crowded is this week, relative to the normal
        // rival count" — so the modifier stays put as long as CompetitionExpectedRivals scales
        // with world size along with everything else. Exact form:
        //
        //   ratio       = rivalMeanStrength / ownStrength
        //   crowdFactor = clamp(rivalCount / CompetitionExpectedRivals, CrowdFactorMin, CrowdFactorMax)
        //   modifier    = 1 / (1 + CompetitionStrength * ratio * crowdFactor)
        //
        // Still rewards/punishes timing exactly as before (DESIGN.md's "drop against a major
        // group's comeback and you get buried") — a weak release amid strong rivals still gets a
        // low modifier via `ratio`; what changed is that the effect no longer compounds with raw
        // population size, only with how unusually crowded THIS window is relative to normal.
        //
        // Internal, not private: exercised directly by ChartSystemTests, including the
        // scale-invariance property this rewrite exists to guarantee.
        internal static float CompetitionModifier(List<Release> active, float[] rawStrength, int index, ChartConfig config)
        {
            Release release = active[index];
            float ownStrength = rawStrength[index];

            float rivalSum = 0f;
            int rivalCount = 0;

            for (int j = 0; j < active.Count; j++)
            {
                if (j == index) continue;

                int gap = Math.Abs(SimDate.WeeksBetween(release.ReleaseDate, active[j].ReleaseDate));
                if (gap <= config.CompetitionWindowWeeks)
                {
                    rivalSum += rawStrength[j];
                    rivalCount++;
                }
            }

            // No rivals, or nothing of our own to be threatened: no penalty either way — a
            // zero-strength release's actual points will be ~0 regardless of the modifier.
            if (rivalCount == 0 || ownStrength <= 0f) return 1f;
            if (config.CompetitionExpectedRivals <= 0) return 1f; // guard against a pathological config

            float rivalMeanStrength = rivalSum / rivalCount;
            float ratio = rivalMeanStrength / ownStrength;

            float crowdFactor = rivalCount / (float)config.CompetitionExpectedRivals;
            crowdFactor = crowdFactor < config.CompetitionCrowdFactorMin ? config.CompetitionCrowdFactorMin
                : (crowdFactor > config.CompetitionCrowdFactorMax ? config.CompetitionCrowdFactorMax : crowdFactor);

            float denominator = 1f + config.CompetitionStrength * ratio * crowdFactor;
            if (denominator <= 0f) return 1f; // guard against a pathological (e.g. negative) config

            float modifier = 1f / denominator;
            return modifier > 1f ? 1f : modifier;
        }

        // Rookie..Legendary mapped linearly onto 0-100.
        private static float TierScore(GroupTier tier)
        {
            return (int)tier / 4f * 100f;
        }

        private static float NormalizePromo(float promoSpend, ChartConfig config)
        {
            if (promoSpend <= 0f || config.PromoSpendNormalizeDivisor <= 0f) return 0f;

            float normalized = 100f * promoSpend / config.PromoSpendNormalizeDivisor;
            return Clamp01To100(normalized);
        }

        // Internal, not private: TierSystem reuses this exact formula for its own fandom score
        // component so the two never drift out of sync with each other.
        internal static float NormalizeFandom(long fandomSize, ChartConfig config)
        {
            if (fandomSize <= 0 || config.FandomLogDivisor <= 0f) return 0f;

            double logSize = Math.Log10(fandomSize);
            float normalized = (float)(logSize / config.FandomLogDivisor) * 100f;
            return Clamp01To100(normalized);
        }

        // Phase 3b iteration 3 fix 2: retirement now keys off consecutive weeks OFF CHART
        // (position == 0), not an absolute points floor — see ChartConfig's comment on
        // ChartRetirementWeeksOffChart for why the floor stopped being a meaningful cutoff. A
        // separate hard backstop (ChartMaxSimulatedWeeks) retires anything that's been simulated
        // for an implausibly long time regardless of chart presence.
        private static void RecordWeek(Release release, int position, float points, float competitionModifier, ChartConfig config)
        {
            release.WeeklyPositions.Add(position);
            release.WeeklyPoints.Add(points);
            release.WeeklyCompetitionModifiers.Add(competitionModifier);
            release.TotalPoints += points;

            if (position > 0)
            {
                release.WeeksCharted++;
                if (release.PeakPosition == 0 || position < release.PeakPosition) release.PeakPosition = position;
                if (position <= 10) release.WeeksInTop10++;
                release.WeeksOffChart = 0;
            }
            else
            {
                release.WeeksOffChart++;
            }

            bool offChartTooLong = release.WeeksOffChart >= config.ChartRetirementWeeksOffChart;
            bool hitMaxSimulatedWeeks = release.WeeklyPositions.Count >= config.ChartMaxSimulatedWeeks;
            if (offChartTooLong || hitMaxSimulatedWeeks)
            {
                release.IsCharting = false;
            }
        }

        // A full deterministic ordering (points descending, ties broken by index) so the result
        // never depends on whichever sort algorithm Array.Sort happens to use — with no ties left
        // in the comparer, algorithm stability is irrelevant.
        private static int[] SortIndicesDescending(float[] points, int count)
        {
            int[] indices = new int[count];
            for (int i = 0; i < count; i++) indices[i] = i;

            Array.Sort(indices, (a, b) =>
            {
                int cmp = points[b].CompareTo(points[a]);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            return indices;
        }

        private static float Clamp01To100(float value)
        {
            return value < 0f ? 0f : (value > 100f ? 100f : value);
        }
    }
}
