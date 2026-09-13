using System.Collections.Generic;
using KpopManager.Core.Systems.Generation;

namespace KpopManager.Core.Systems.Chart
{
    // Phase 3b fix 2b. Nothing previously disbanded a group or debuted a new one, so the industry
    // was a fixed cast for 50 years — a real industry has constant churn at the bottom, and churn
    // is part of what produces chart concentration.
    //
    // DESIGN: scoped to world groups only (home center not in state.Company.CenterIds) — the
    // player's own company (their group plus its 2 siblings) isn't touched by random disbanding.
    // Not its own ISimSystem/tick-order step, for the same reason as TierSystem: DESIGN.md's
    // twelve steps have no slot for it. Invoked from the start of ReleaseScheduler.Tick (step 3,
    // "new releases") so a group disbanded or newly debuted this week is already reflected before
    // release scheduling runs that same tick.
    //
    // Runs once per calendar year (gated on Week == 1) rather than per-group-debut-anniversary,
    // since "how many new groups debut this year" is a single yearly roll, not a per-group one.
    public static class IndustryChurnSystem
    {
        public static void Evaluate(GameState state, SimDate now)
        {
            if (now.Week != 1) return;
            if (state.Company == null) return;

            ChartConfig config = state.ChartConfig;
            SimRandom rng = state.Random;

            List<ProductionCenter> worldCenters = CollectWorldCenters(state);
            if (worldCenters.Count == 0) return;

            EvaluateDisbands(state, worldCenters, config, rng, now);
            EvaluateDebuts(state, worldCenters, config, rng, now);
        }

        private static List<ProductionCenter> CollectWorldCenters(GameState state)
        {
            List<ProductionCenter> worldCenters = new List<ProductionCenter>();
            for (int i = 0; i < state.Centers.Count; i++)
            {
                ProductionCenter center = state.Centers[i];
                if (!state.Company.CenterIds.Contains(center.Id)) worldCenters.Add(center);
            }
            return worldCenters;
        }

        private static bool IsWorldGroup(GameState state, Group group)
        {
            return !state.Company.CenterIds.Contains(group.CenterId);
        }

        private static void EvaluateDisbands(GameState state, List<ProductionCenter> worldCenters, ChartConfig config, SimRandom rng, SimDate now)
        {
            // Iterate a snapshot of the current list, not state.Groups directly — disbanding only
            // flips IsActive, it never removes from the authoritative list, so this is safe either
            // way, but a snapshot keeps the loop obviously correct if that ever changes.
            List<Group> groups = new List<Group>(state.Groups);

            for (int i = 0; i < groups.Count; i++)
            {
                Group group = groups[i];
                if (!group.IsActive) continue;
                if (!IsWorldGroup(state, group)) continue;

                if (group.Tier == GroupTier.Rookie)
                {
                    group.ConsecutiveRookieYears++;
                }
                else
                {
                    group.ConsecutiveRookieYears = 0;
                }

                if (group.ConsecutiveRookieYears >= config.DisbandFailureYears)
                {
                    Disband(state, group, now, "failed to escape Rookie tier");
                    continue;
                }

                if (now >= group.ContractExpiry)
                {
                    if (rng.Chance(config.DisbandChanceAtContractEnd))
                    {
                        Disband(state, group, now, "did not re-sign at contract end");
                    }
                    else
                    {
                        group.ContractExpiry = group.ContractExpiry.AdvanceYears((int)config.DisbandContractYears);
                    }
                }
            }
        }

        private static void Disband(GameState state, Group group, SimDate now, string reason)
        {
            group.IsActive = false;
            group.DisbandDate = now;
            state.Log.Add(now, LogCategory.Rival, LogSeverity.Notable, group.Name + " disbanded (" + reason + ")", group.Id);
        }

        // Phase 3b iteration 2 fix 3: target-seeking, not a fixed roll — the fixed
        // NewWorldGroupsPerYearMin/Max range let disbanding outpace debuting badly (measured mean
        // ActiveGroups 114.6 against a WorldGroupCount target of 200). Deterministic given the
        // active count, not a random roll: BaselineNewGroupsPerYear plus a proportional correction
        // toward TargetActiveWorldGroups, clamped so a big shortfall can't debut an implausible
        // flood in one year.
        private static int CountActiveWorldGroups(GameState state)
        {
            int count = 0;
            for (int i = 0; i < state.Groups.Count; i++)
            {
                Group group = state.Groups[i];
                if (group.IsActive && IsWorldGroup(state, group)) count++;
            }
            return count;
        }

        private static void EvaluateDebuts(GameState state, List<ProductionCenter> worldCenters, ChartConfig config, SimRandom rng, SimDate now)
        {
            int activeWorldGroups = CountActiveWorldGroups(state);
            float shortfall = (config.TargetActiveWorldGroups - activeWorldGroups) * config.DebutRateCorrectionGain;
            float clampedShortfall = shortfall < 0f ? 0f : (shortfall > config.MaxNewWorldGroupsPerYear ? config.MaxNewWorldGroupsPerYear : shortfall);
            int count = config.BaselineNewGroupsPerYear + (int)clampedShortfall;

            for (int i = 0; i < count; i++)
            {
                ProductionCenter home = rng.Pick(worldCenters);
                Group group = GroupGenerator.Generate(state, home.Id, (int)GroupTier.Rookie, now);
                home.GroupIds.Add(group.Id);

                state.Log.Add(now, LogCategory.Rival, LogSeverity.Info, group.Name + " debuted at " + home.Name, group.Id);
            }
        }
    }
}
