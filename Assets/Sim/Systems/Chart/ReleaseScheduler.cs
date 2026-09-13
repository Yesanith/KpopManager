using System;

namespace KpopManager.Core.Systems.Chart
{
    // Tick step 3: drives every non-player group's comeback cadence — generates a track, creates a
    // Release, and schedules the group's next one. Also runs IndustryChurnSystem first, so a group
    // disbanded or newly debuted this same week is already reflected before release scheduling runs.
    //
    // The player's own group is never touched here; Phase 4's player-driven comeback cycle owns
    // it. Everything this system needs to remember between ticks — a group's cadence and its next
    // due date — lives on Group itself, seeded once at world generation by GroupGenerator, because
    // this system, like every ISimSystem, is stateless.
    public sealed class ReleaseScheduler : ISimSystem
    {
        public string Name => "Track quality decay / new releases";

        public void Tick(GameState state)
        {
            ChartConfig config = state.ChartConfig;
            SimDate now = state.Date;

            IndustryChurnSystem.Evaluate(state, now);

            for (int i = 0; i < state.Groups.Count; i++)
            {
                Group group = state.Groups[i];
                if (!group.IsActive) continue;
                if (group.NextReleaseDate > now) continue;

                ProductionCenter center = state.GetCenter(group.CenterId);
                if (center != null && center.IsPlayer) continue; // the player's own comeback cycle is Phase 4's job

                Release release = CreateRelease(state, group, center, config, now);
                LogRelease(state, group, release, now);
                ScheduleNext(state, group, center, config, now);
            }
        }

        private static Release CreateRelease(GameState state, Group group, ProductionCenter center, ChartConfig config, SimDate now)
        {
            SimRandom rng = state.Random;

            int composerSkill = ComputeComposerSkill(rng, state, group, center, config, out int composerId);
            int centerTierOrdinal = center != null ? (int)center.Tier : 0;

            Track track = TrackGenerator.Generate(rng, centerTierOrdinal, composerSkill, config, state.WorldData);
            track.Id = state.AllocateEntityId();
            track.ComposerId = composerId;
            state.AddTrack(track);

            // Phase 3b iteration 2: both trajectory components are rolled once, here, and held
            // fixed for the release's whole chart life — see ChartConfig's own comment.
            float crossoverMultiplier = ChartSystem.RollCrossoverMultiplier(rng, track.Quality, config);

            Release release = new Release
            {
                Id = state.AllocateEntityId(),
                GroupId = group.Id,
                TitleTrackId = track.Id,
                ReleaseDate = now,
                Concept = RollConcept(rng),
                PromoSpend = ComputePromoSpend(rng, center, group, config),
                Type = RollReleaseType(rng, config),
                FandomSizeAtRelease = group.Fandom.Size,
                GroupTierAtRelease = group.Tier,
                FandomPull = ChartSystem.ComputeFandomPull(group.Fandom.Size, config),
                PublicAppeal = ChartSystem.ComputePublicAppeal(track.Quality, config) * crossoverMultiplier,
                CrossoverMultiplier = crossoverMultiplier
            };

            state.AddRelease(release);
            group.ReleaseIds.Add(release.Id);

            return release;
        }

        private static int ComputeComposerSkill(
            SimRandom rng, GameState state, Group group, ProductionCenter center, ChartConfig config, out int composerId)
        {
            composerId = Person.NoEntity;

            if (group.MemberIds.Count > 0 && rng.Chance(config.ChanceGroupMemberComposes))
            {
                Person member = state.GetPerson(rng.Pick(group.MemberIds));
                if (member != null)
                {
                    composerId = member.Id;
                    float skill = Math.Max(member.Songwriting, member.Composition);
                    return (int)skill;
                }
            }

            int centerTierOrdinal = center != null ? (int)center.Tier : 0;
            float baseline = config.ExternalComposerBaseSkill + config.ExternalComposerSkillPerCenterTier * centerTierOrdinal;
            return (int)Clamp(baseline, 0f, 100f);
        }

        // Phase 3b fix 2d: multiplicative, not additive. With the old additive formula and every
        // group Legendary (tier system was previously incapable of producing anything else), the
        // spend range was a flat 30k-50k for everyone; fixing tier restores most of the spread on
        // its own, but multiplicative composition on top is what lets a Legendary group at a
        // top-tier center outspend a Rookie at a poor one by an order of magnitude rather than 3x.
        // Internal, not private: exercised directly by ReleaseSchedulerTests so the multiplicative
        // scaling is tested against the real formula.
        internal static float ComputePromoSpend(SimRandom rng, ProductionCenter center, Group group, ChartConfig config)
        {
            int centerTierOrdinal = center != null ? (int)center.Tier : 0;
            int groupTierOrdinal = (int)group.Tier;

            float baseAmount = config.PromoSpendBase
                * Pow(config.PromoSpendCenterTierMult, centerTierOrdinal)
                * Pow(config.PromoSpendGroupTierMult, groupTierOrdinal);

            float jitter = 1f + rng.NextFloat(-config.PromoSpendJitterPct, config.PromoSpendJitterPct);
            float spend = baseAmount * jitter;
            return spend < 0f ? 0f : spend;
        }

        private static float Pow(float baseValue, int exponent)
        {
            float result = 1f;
            for (int i = 0; i < exponent; i++) result *= baseValue;
            return result;
        }

        private static Concept RollConcept(SimRandom rng)
        {
            Array values = Enum.GetValues(typeof(Concept));
            return (Concept)values.GetValue(rng.NextInt(0, values.Length));
        }

        private static ReleaseType RollReleaseType(SimRandom rng, ChartConfig config)
        {
            float total = config.ReleaseTypeSingleWeight + config.ReleaseTypeMiniWeight + config.ReleaseTypeFullWeight;
            if (total <= 0f) return ReleaseType.Single;

            float roll = rng.NextFloat(0f, total);
            if (roll < config.ReleaseTypeSingleWeight) return ReleaseType.Single;
            if (roll < config.ReleaseTypeSingleWeight + config.ReleaseTypeMiniWeight) return ReleaseType.Mini;
            return ReleaseType.Full;
        }

        private static void LogRelease(GameState state, Group group, Release release, SimDate now)
        {
            Track track = state.GetTrack(release.TitleTrackId);
            state.Log.Add(
                now, LogCategory.Chart, LogSeverity.Info,
                group.Name + " released \"" + (track?.Title ?? "?") + "\" (" + release.Type + ", " + release.Concept + ")",
                group.Id);
        }

        // Advances a group's cadence-based next-release date: base cadence in weeks, nudged for
        // the season, nudged again to mildly avoid colliding with a sibling center's
        // already-scheduled release (world groups outside the company don't coordinate at all).
        private static void ScheduleNext(GameState state, Group group, ProductionCenter center, ChartConfig config, SimDate now)
        {
            SimRandom rng = state.Random;

            int cadenceWeeks = Math.Max(1, (int)(group.ReleaseCadenceMonths * config.WeeksPerMonth));
            SimDate candidate = now.AdvanceWeeks(cadenceWeeks);

            candidate = ApplySeasonality(candidate, config);
            candidate = ApplySiblingAvoidance(state, group, center, candidate, config);

            group.NextReleaseDate = candidate;
        }

        // DESIGN: a coarse heuristic, not a real seasonal model — nudges a candidate date a few
        // weeks earlier if it already falls in the spring/autumn windows, or later if it falls in
        // the midsummer window, proportional to the configured boost/penalty strength. Revisit
        // during balancing if release timing needs to read as more than "roughly clustered."
        private static SimDate ApplySeasonality(SimDate candidate, ChartConfig config)
        {
            int week = candidate.Week;
            bool isSpringOrAutumn = (week >= 9 && week <= 21) || (week >= 35 && week <= 47);
            bool isMidsummer = week >= 22 && week <= 30;

            const int maxNudgeWeeks = 4;

            if (isSpringOrAutumn)
            {
                int pull = (int)((config.SeasonalityBoostSpringAutumn - 1f) * maxNudgeWeeks);
                return candidate.AdvanceWeeks(-pull);
            }

            if (isMidsummer)
            {
                int push = (int)((1f - config.SeasonalityPenaltySummer) * maxNudgeWeeks);
                return candidate.AdvanceWeeks(push);
            }

            return candidate;
        }

        private static bool IsCompanySibling(GameState state, ProductionCenter center)
        {
            return center != null && !center.IsPlayer && state.Company != null && state.Company.CenterIds.Contains(center.Id);
        }

        private static SimDate ApplySiblingAvoidance(GameState state, Group group, ProductionCenter center, SimDate candidate, ChartConfig config)
        {
            if (!IsCompanySibling(state, center)) return candidate; // world groups don't coordinate

            for (int i = 0; i < state.Groups.Count; i++)
            {
                Group other = state.Groups[i];
                if (ReferenceEquals(other, group)) continue;

                ProductionCenter otherCenter = state.GetCenter(other.CenterId);
                if (!IsCompanySibling(state, otherCenter)) continue;
                if (otherCenter.Id == center.Id) continue; // same center as us isn't a "rival" sibling

                int gap = Math.Abs(SimDate.WeeksBetween(candidate, other.NextReleaseDate));
                if (gap <= 1)
                {
                    int push = Math.Max(1, (int)Math.Ceiling(config.SiblingCollisionAvoidance * 2f));
                    return candidate.AdvanceWeeks(push);
                }
            }

            return candidate;
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
