using System.Collections.Generic;
using KpopManager.Core.Systems.Chart;

namespace KpopManager.Core.Systems.Fandom
{
    /// <summary>
    /// Tick step 6. <b>A Phase 3 stand-in, not the real FandomSystem</b> (that's Phase 5) — the
    /// minimum needed so charts have something to snowball or decay against: <see cref="Group.Fandom"/>'s
    /// <c>Size</c> grows from this week's chart points (with diminishing returns at scale) and
    /// decays slowly during inactivity. <c>Sentiment</c> and <c>PublicAwareness</c> stay static
    /// until Phase 5.
    /// </summary>
    /// <remarks>
    /// Also invokes <see cref="TierSystem.Evaluate"/> at the end of its tick — tier mobility isn't
    /// one of DESIGN.md's twelve named steps, and this is the point in the week where this week's
    /// chart results (step 4) and this week's fandom update are both already fresh. See
    /// ARCHITECTURE.md.
    /// </remarks>
    public sealed class FandomSystem : ISimSystem
    {
        public string Name => "Fandom update";

        public void Tick(GameState state)
        {
            ChartConfig config = state.ChartConfig;
            SimDate now = state.Date;

            // Built fresh every tick by iterating state.Releases (a List, deterministic order),
            // and only ever read back by a specific group id afterward — never iterated for an
            // outcome, so the dictionary itself introduces no hash-order hazard.
            Dictionary<int, float> pointsThisWeekByGroup = SumThisWeeksPointsByGroup(state);

            for (int i = 0; i < state.Groups.Count; i++)
            {
                Group group = state.Groups[i];
                if (!group.IsActive) continue;

                bool chartedThisWeek = pointsThisWeekByGroup.TryGetValue(group.Id, out float points) && points > 0f;

                if (chartedThisWeek)
                {
                    ApplyGrowth(group, points, config);
                    group.WeeksSinceLastCharted = 0;
                }
                else
                {
                    group.WeeksSinceLastCharted++;
                    if (group.WeeksSinceLastCharted > config.FandomInactivityGraceWeeks)
                    {
                        ApplyDecay(group, config);
                    }
                }
            }

            TierSystem.Evaluate(state, now);
        }

        private static Dictionary<int, float> SumThisWeeksPointsByGroup(GameState state)
        {
            Dictionary<int, float> sums = new Dictionary<int, float>();

            for (int i = 0; i < state.Releases.Count; i++)
            {
                Release release = state.Releases[i];
                if (!release.IsCharting) continue;
                if (release.WeeklyPoints.Count == 0) continue;

                // ChartSystem (step 4) always runs earlier in the same tick, so the last entry in
                // a still-charting release's history is this very week's.
                float pointsThisWeek = release.WeeklyPoints[release.WeeklyPoints.Count - 1];
                if (pointsThisWeek <= 0f) continue;

                sums[release.GroupId] = sums.TryGetValue(release.GroupId, out float existing)
                    ? existing + pointsThisWeek
                    : pointsThisWeek;
            }

            return sums;
        }

        private static void ApplyGrowth(Group group, float pointsThisWeek, ChartConfig config)
        {
            float rawGrowth = config.FandomGrowthPerPoint * pointsThisWeek;

            float saturation = config.FandomGrowthSaturationSize > 0f
                ? 1f / (1f + group.Fandom.Size / config.FandomGrowthSaturationSize)
                : 1f;

            long growth = (long)(rawGrowth * saturation);
            if (growth > 0) group.Fandom.Size += growth;
        }

        private static void ApplyDecay(Group group, ChartConfig config)
        {
            if (group.Fandom.Size <= 0) return;

            long decayed = (long)(group.Fandom.Size * config.FandomDecayRateInactive);
            group.Fandom.Size = decayed < 0 ? 0 : decayed;
        }
    }
}
