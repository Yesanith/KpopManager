using System.Linq;
using KpopManager.Core;
using KpopManager.Core.Systems.Chart;
using NUnit.Framework;

namespace KpopManager.Tests
{
    [TestFixture]
    public class IndustryChurnSystemTests
    {
        private static GameState BuildStateWithOneWorldCenter(WorldData data, out ProductionCenter playerCenter, out ProductionCenter worldCenter)
        {
            GameState state = new GameState { ChartConfig = new ChartConfig(), Log = new SimLog(), Random = new SimRandom(1UL), Date = SimDate.Start, WorldData = data };

            playerCenter = new ProductionCenter { Id = 1, Name = "Player", IsPlayer = true };
            state.AddCenter(playerCenter);

            worldCenter = new ProductionCenter { Id = 2, Name = "World Co" };
            state.AddCenter(worldCenter);

            state.Company = new Company { Name = "Co", CenterIds = { playerCenter.Id } };

            return state;
        }

        [Test]
        public void OnlyRunsOnceAYear_AtWeekOne()
        {
            WorldData data = TestFixtures.BuildWorldData();
            GameState state = BuildStateWithOneWorldCenter(data, out _, out _);

            IndustryChurnSystem.Evaluate(state, new SimDate(1, 15));
            Assert.That(state.Groups.Count, Is.EqualTo(0), "should not debut anything outside week 1");

            IndustryChurnSystem.Evaluate(state, new SimDate(1, 1));
            Assert.That(state.Groups.Count, Is.GreaterThan(0), "should debut new groups at week 1");
        }

        [Test]
        public void NewDebuts_LandInTheConfiguredRange()
        {
            WorldData data = TestFixtures.BuildWorldData();
            GameState state = BuildStateWithOneWorldCenter(data, out _, out _);

            IndustryChurnSystem.Evaluate(state, new SimDate(1, 1));

            Assert.That(state.Groups.Count, Is.InRange(state.ChartConfig.NewWorldGroupsPerYearMin, state.ChartConfig.NewWorldGroupsPerYearMax));
            Assert.That(state.Groups.All(g => g.Tier == GroupTier.Rookie), Is.True, "new debuts should always start Rookie");
        }

        [Test]
        public void SustainedRookieFailure_DisbandsAWorldGroup()
        {
            WorldData data = TestFixtures.BuildWorldData();
            GameState state = BuildStateWithOneWorldCenter(data, out _, out ProductionCenter worldCenter);

            Group group = new Group { Id = 100, Name = "Failing Group", CenterId = worldCenter.Id, Tier = GroupTier.Rookie, IsActive = true, DebutDate = SimDate.Start, ContractExpiry = SimDate.Start.AdvanceYears(7) };
            state.AddGroup(group);
            worldCenter.GroupIds.Add(group.Id);

            for (int year = 1; year <= state.ChartConfig.DisbandFailureYears; year++)
            {
                IndustryChurnSystem.Evaluate(state, new SimDate(year, 1));
            }

            Assert.That(group.IsActive, Is.False, "a group stuck at Rookie for DisbandFailureYears should disband");
        }

        [Test]
        public void PlayerCompanyGroups_AreNeverTouchedByChurn()
        {
            WorldData data = TestFixtures.BuildWorldData();
            GameState state = BuildStateWithOneWorldCenter(data, out ProductionCenter playerCenter, out _);

            Group companyGroup = new Group { Id = 100, Name = "Player's Group", CenterId = playerCenter.Id, Tier = GroupTier.Rookie, IsActive = true, DebutDate = SimDate.Start, ContractExpiry = SimDate.Start.AdvanceYears(7) };
            state.AddGroup(companyGroup);
            playerCenter.GroupIds.Add(companyGroup.Id);

            for (int year = 1; year <= 10; year++)
            {
                IndustryChurnSystem.Evaluate(state, new SimDate(year, 1));
            }

            Assert.That(companyGroup.IsActive, Is.True, "churn must never disband a group belonging to the player's own company");
        }

        [Test]
        public void RecoveringFromRookie_ResetsTheFailureCounter()
        {
            WorldData data = TestFixtures.BuildWorldData();
            GameState state = BuildStateWithOneWorldCenter(data, out _, out ProductionCenter worldCenter);

            Group group = new Group { Id = 100, Name = "Recovering Group", CenterId = worldCenter.Id, Tier = GroupTier.Rookie, IsActive = true, DebutDate = SimDate.Start, ContractExpiry = SimDate.Start.AdvanceYears(7) };
            state.AddGroup(group);
            worldCenter.GroupIds.Add(group.Id);

            IndustryChurnSystem.Evaluate(state, new SimDate(1, 1));
            group.Tier = GroupTier.Rising; // escaped Rookie before failing
            IndustryChurnSystem.Evaluate(state, new SimDate(2, 1));
            group.Tier = GroupTier.Rookie; // fell back
            IndustryChurnSystem.Evaluate(state, new SimDate(3, 1));

            Assert.That(group.ConsecutiveRookieYears, Is.EqualTo(1), "a year spent above Rookie should reset the counter");
            Assert.That(group.IsActive, Is.True);
        }
    }
}
