using System;
using System.Collections.Generic;

namespace KpopManager.Core.Systems.Chart
{
    // Tick step 4: computes every actively-charting release's BuzzScore, applies competition and
    // decay, ranks the result, and appends one week of history to each release.
    //
    // Three passes per tick, in this order, because each depends on the previous being complete
    // for every release before it can run:
    //   1. Raw BuzzScore per release (quality/fit/tier/promo/fandom terms — no competition, no
    //      decay, no variance yet).
    //   2. Competition modifier per release, which needs every other release's raw BuzzScore to
    //      sum against, then decay and variance applied to produce this week's WeeklyPoints.
    //   3. Rank everyone by WeeklyPoints, assign positions, and record history.
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
            float[] rawBuzz = new float[active.Count];
            for (int i = 0; i < active.Count; i++)
            {
                weeksSince[i] = SimDate.WeeksBetween(active[i].ReleaseDate, now);
                rawBuzz[i] = ComputeRawBuzz(state, active[i], weeksSince[i], config);
            }

            float[] weeklyPoints = new float[active.Count];
            for (int i = 0; i < active.Count; i++)
            {
                float competitionModifier = CompetitionModifier(active, rawBuzz, i, config);

                Track track = state.GetTrack(active[i].TitleTrackId);
                float quality = track?.Quality ?? 0f;
                float decay = DecayCurve.Evaluate(weeksSince[i], quality, config);

                float variance = 1f + state.Random.NextFloat(-config.RandomVariancePct, config.RandomVariancePct);

                float points = rawBuzz[i] * competitionModifier * decay * variance;
                weeklyPoints[i] = points < 0f ? 0f : points;
            }

            int[] order = SortIndicesDescending(weeklyPoints, active.Count);

            int rank = 0;
            for (int idx = 0; idx < order.Length; idx++)
            {
                int i = order[idx];
                Release release = active[i];
                float points = weeklyPoints[i];
                bool belowFloor = points < config.ChartFloorPoints;

                int position;
                if (belowFloor || rank >= config.ChartSize)
                {
                    position = 0;
                }
                else
                {
                    rank++;
                    position = rank;
                }

                RecordWeek(release, position, points, config, belowFloor);
            }
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

        // Internal, not private: exercised directly by ChartSystemTests so the "zero fandom and
        // zero promo still produces a valid, non-negative score" invariant is tested against the
        // real formula, not a re-derivation of it.
        internal static float ComputeRawBuzz(GameState state, Release release, int weeksSince, ChartConfig config)
        {
            Track track = state.GetTrack(release.TitleTrackId);
            float quality = track?.Quality ?? 0f;

            // Phase 4 owns both of these properly (trend lifecycles, concept-to-song fit). Flat 50
            // keeps the term's contribution neutral without deleting it from the formula.
            const float conceptFit = 50f;
            const float trendFit = 50f;

            Group group = state.GetGroup(release.GroupId);
            float tierScore = TierScore(group?.Tier ?? GroupTier.Rookie);
            float promoTerm = NormalizePromo(release.PromoSpend, config);

            long fandomSize = group?.Fandom.Size ?? 0L;
            float fandomTerm = NormalizeFandom(fandomSize, config) * SurgeMultiplier(weeksSince, config);

            float buzz =
                config.WeightTrackQuality * quality +
                config.WeightConceptFit * conceptFit +
                config.WeightTrendFit * trendFit +
                config.WeightGroupTier * tierScore +
                config.WeightPromoSpend * promoTerm +
                config.WeightFandom * fandomTerm;

            return buzz < 0f ? 0f : buzz;
        }

        // Sums every OTHER release's raw BuzzScore whose own ReleaseDate falls within
        // ChartConfig.CompetitionWindowWeeks of this release's — a debut-timing collision, per
        // DESIGN.md's "drop against a major group's comeback and you get buried" — then applies
        // 1 / (1 + c * sumOfRivalBuzz / 100).
        //
        // Internal, not private: exercised directly by ChartSystemTests so the "(0,1] always"
        // invariant is tested against the real formula.
        internal static float CompetitionModifier(List<Release> active, float[] rawBuzz, int index, ChartConfig config)
        {
            Release release = active[index];
            float rivalSum = 0f;

            for (int j = 0; j < active.Count; j++)
            {
                if (j == index) continue;

                int gap = Math.Abs(SimDate.WeeksBetween(release.ReleaseDate, active[j].ReleaseDate));
                if (gap <= config.CompetitionWindowWeeks)
                {
                    rivalSum += rawBuzz[j];
                }
            }

            float denominator = 1f + config.CompetitionStrength * rivalSum / 100f;
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

        // Fandom's surge multiplier: ChartConfig.FandomSurgeWeek0 at week 0, decaying linearly
        // to 1.0 by ChartConfig.FandomSurgeDecayWeeks.
        private static float SurgeMultiplier(int weeksSince, ChartConfig config)
        {
            if (weeksSince <= 0) return config.FandomSurgeWeek0;
            if (config.FandomSurgeDecayWeeks <= 0f || weeksSince >= config.FandomSurgeDecayWeeks) return 1f;

            float t = weeksSince / config.FandomSurgeDecayWeeks;
            return config.FandomSurgeWeek0 + (1f - config.FandomSurgeWeek0) * t;
        }

        private static void RecordWeek(Release release, int position, float points, ChartConfig config, bool belowFloor)
        {
            release.WeeklyPositions.Add(position);
            release.WeeklyPoints.Add(points);
            release.TotalPoints += points;

            if (position > 0)
            {
                release.WeeksCharted++;
                if (release.PeakPosition == 0 || position < release.PeakPosition) release.PeakPosition = position;
                if (position <= 10) release.WeeksInTop10++;
            }

            if (belowFloor)
            {
                release.WeeksBelowFloor++;
                if (release.WeeksBelowFloor >= config.ChartRetirementWeeksBelowFloor)
                {
                    release.IsCharting = false;
                }
            }
            else
            {
                release.WeeksBelowFloor = 0;
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
