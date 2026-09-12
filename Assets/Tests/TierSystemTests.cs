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
        public void NoChartHistory_NeverPromotesFromRookie()
        {
            GameState state = new GameState { ChartConfig = new ChartConfig(), Log = new SimLog() };

            Group group = new Group
            {
                Id = 1, Name = "NeverCharted", Tier = GroupTier.Rookie, DebutDate = SimDate.Start, IsActive = true,
                Fandom = new Fandom { Size = 100 }
            };
            state.AddGroup(group);

            TierSystem.Evaluate(state, SimDate.Start.AdvanceWeeks(26));

            Assert.That(group.Tier, Is.EqualTo(GroupTier.Rookie));
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
