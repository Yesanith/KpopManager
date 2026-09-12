using System.Linq;
using KpopManager.Core;
using KpopManager.Core.Systems.Chart;
using KpopManager.Core.Systems.Generation;
using NUnit.Framework;

namespace KpopManager.Tests
{
    [TestFixture]
    public class TierSystemTests
    {
        [Test]
        public void DominantChartHistory_PromotesAGroup_AndLogsIt()
        {
            GameState state = new GameState { ChartConfig = new ChartConfig(), Log = new SimLog() };

            SimDate debut = new SimDate(1, 1);
            Group group = new Group
            {
                Id = 1, Name = "TestGroup", Tier = GroupTier.Rookie, DebutDate = debut, IsActive = true,
                Fandom = new Fandom { Size = 5_000_000 }
            };
            state.AddGroup(group);

            // 26 weeks at #1 — evaluation interval defaults to 13, so 26 weeks since debut is due.
            Release release = new Release { Id = 1, GroupId = group.Id, ReleaseDate = debut };
            for (int w = 0; w < 26; w++) release.WeeklyPositions.Add(1);
            state.AddRelease(release);
            group.ReleaseIds.Add(release.Id);

            SimDate now = debut.AdvanceWeeks(26);
            TierSystem.Evaluate(state, now);

            Assert.That(group.Tier, Is.Not.EqualTo(GroupTier.Rookie), "26 weeks at #1 with a huge fandom should promote a group");

            bool logged = state.Log.Entries.Any(e =>
                e.Category == LogCategory.Group && e.Severity == LogSeverity.Notable && e.RelatedEntityId == group.Id);
            Assert.That(logged, Is.True, "a tier change must be logged to SimLog");
        }

        [Test]
        public void WeakestGroupInARealCohort_NeverPromotesFromRookie()
        {
            // A single-group "cohort" is trivially always the top of itself under percentile
            // ranking, so this needs a real population to mean anything — that's the whole point
            // of Phase 3b fix 2c.
            GameState state = new GameState { ChartConfig = new ChartConfig(), Log = new SimLog() };
            SimDate debut = new SimDate(1, 1);

            Group weak = new Group
            {
                Id = 1, Name = "NeverCharted", Tier = GroupTier.Rookie, DebutDate = debut, IsActive = true,
                Fandom = new Fandom { Size = 100 }
            };
            state.AddGroup(weak);

            for (int i = 0; i < 9; i++)
            {
                Group strong = new Group
                {
                    Id = 10 + i, Name = "Strong" + i, Tier = GroupTier.Established, DebutDate = debut, IsActive = true,
                    Fandom = new Fandom { Size = 3_000_000 }
                };
                Release release = new Release { Id = 100 + i, GroupId = strong.Id, ReleaseDate = debut };
                for (int w = 0; w < 26; w++) release.WeeklyPositions.Add(1 + i);
                state.AddGroup(strong);
                state.AddRelease(release);
                strong.ReleaseIds.Add(release.Id);
            }

            TierSystem.Evaluate(state, debut.AdvanceWeeks(26));

            Assert.That(weak.Tier, Is.EqualTo(GroupTier.Rookie), "the worst-scoring group in a 10-group cohort should not be promoted");
        }

        [Test]
        public void NotYetDue_LeavesTierUnchangedEvenWithAStrongHistory()
        {
            GameState state = new GameState { ChartConfig = new ChartConfig(), Log = new SimLog() };

            SimDate debut = new SimDate(1, 1);
            Group group = new Group
            {
                Id = 1, Name = "TestGroup", Tier = GroupTier.Rookie, DebutDate = debut, IsActive = true,
                Fandom = new Fandom { Size = 5_000_000 }
            };
            state.AddGroup(group);

            Release release = new Release { Id = 1, GroupId = group.Id, ReleaseDate = debut };
            for (int w = 0; w < 5; w++) release.WeeklyPositions.Add(1);
            state.AddRelease(release);
            group.ReleaseIds.Add(release.Id);

            // 5 weeks since debut; default interval is 13, so this isn't a due week.
            TierSystem.Evaluate(state, debut.AdvanceWeeks(5));

            Assert.That(group.Tier, Is.EqualTo(GroupTier.Rookie));
        }

        [Test]
        public void LargeCohortWithVariedScores_ProducesAllFiveTiers_NotEveryoneLegendary()
        {
            // The exact bug Phase 3b fix 2c targets: with the pre-fix absolute thresholds, a
            // deliberately mediocre group scored 91.7 against a Legendary threshold of 82 — every
            // group in the actual sim came out Legendary. This builds 100 groups spanning a wide
            // score range and checks the result actually spreads across tiers.
            GameState state = new GameState { ChartConfig = new ChartConfig(), Log = new SimLog() };
            SimDate debut = new SimDate(1, 1);
            SimRandom rng = new SimRandom(99UL);

            for (int i = 0; i < 100; i++)
            {
                Group group = new Group { Id = i, Name = "G" + i, Tier = GroupTier.Rookie, DebutDate = debut, IsActive = true };

                // Spread peak position and fandom widely across the population, deterministically.
                int peak = 1 + i; // 1..100
                long fandom = (long)System.Math.Pow(10, 3 + i / 20.0); // ~1k up to ~10M

                group.Fandom = new Fandom { Size = fandom };

                Release release = new Release { Id = 1000 + i, GroupId = group.Id, ReleaseDate = debut };
                for (int w = 0; w < 26; w++) release.WeeklyPositions.Add(peak);

                state.AddGroup(group);
                state.AddRelease(release);
                group.ReleaseIds.Add(release.Id);
            }

            TierSystem.Evaluate(state, debut.AdvanceWeeks(26));

            var byTier = state.Groups.GroupBy(g => g.Tier).ToDictionary(g => g.Key, g => g.Count());

            int legendaryCount = byTier.TryGetValue(GroupTier.Legendary, out int lc) ? lc : 0;

            Assert.That(byTier.Keys.Count, Is.GreaterThan(1), "a 100-group spread should not collapse onto a single tier");
            Assert.That(legendaryCount, Is.LessThan(state.Groups.Count), "not every group in a varied cohort should be Legendary");
        }

        [Test]
        public void FiftyYearRun_LogsAtLeastOneTierChange()
        {
            WorldData data = TestFixtures.BuildWorldData();
            SimEngine engine = new SimEngine(7UL);
            WorldGenerator.Generate(engine.State, data);
            engine.AdvanceYears(50);

            bool anyTierChange = engine.State.Log.Entries.Any(e =>
                e.Category == LogCategory.Group && e.Severity == LogSeverity.Notable);

            Assert.That(anyTierChange, Is.True, "a 50-year run across dozens of groups should produce at least one tier change");
        }
    }
}
