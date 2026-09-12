using System.Collections.Generic;
using KpopManager.Core.Systems.Chart;

namespace KpopManager.Core.Systems.Fandom
{
    // Tick step 6. A Phase 3 stand-in, not the real FandomSystem (that's Phase 5) — the minimum
    // needed so charts have something to snowball or decay against: Group.Fandom's Size grows
    // from this week's chart points and decays slowly during inactivity. Sentiment and
    // PublicAwareness stay static until Phase 5.
    //
    // Also invokes TierSystem.Evaluate at the end of its tick — tier mobility isn't one of
    // DESIGN.md's twelve named steps, and this is the point in the week where this week's chart
    // results (step 4) and this week's fandom update are both already fresh. See ARCHITECTURE.md.
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
            // Phase 3b fix 1: proportional term is what makes fandom compound (bigger fandom, more
            // fans gained per point); bootstrap is a small flat amount so a brand-new group with
            // near-zero fandom isn't stuck growing by ~nothing. Damping only kicks in well above
            // any size the game currently reaches — it's a runaway brake, not a leveller.
            float proportional = group.Fandom.Size * config.FandomGrowthRatePerPoint * pointsThisWeek;
            float bootstrap = config.FandomBootstrapPerPoint * pointsThisWeek;

            float damping = config.FandomGrowthDampingSize > 0f
                ? 1f / (1f + group.Fandom.Size / config.FandomGrowthDampingSize)
                : 1f;

            long growth = (long)((proportional + bootstrap) * damping);
            if (growth > 0) group.Fandom.Size += growth;
        }

        private static void ApplyDecay(Group group, ChartConfig config)
        {
            if (group.Fandom.Size <= 0) return;

            // No floor here on purpose: a small, inactive group shrinking toward nothing is
            // correct — it isn't "leveled" back up to some minimum.
            long retained = (long)(group.Fandom.Size * config.FandomRetentionRateInactive);
            group.Fandom.Size = retained < 0 ? 0 : retained;
        }
    }
}
