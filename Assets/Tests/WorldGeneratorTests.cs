using System.Collections.Generic;
using System.Linq;
using KpopManager.Core;
using KpopManager.Core.Systems.Generation;
using Newtonsoft.Json;
using NUnit.Framework;

namespace KpopManager.Tests
{
    [TestFixture]
    public class WorldGeneratorTests
    {
        [Test]
        public void SameSeed_ProducesAnIdenticalWorld()
        {
            WorldData data = TestFixtures.BuildWorldData();

            GameState a = TestFixtures.BuildState(555UL, data);
            WorldGenerator.Generate(a, data);

            GameState b = TestFixtures.BuildState(555UL, data);
            WorldGenerator.Generate(b, data);

            Assert.That(Snapshot(a), Is.Not.Empty);
            Assert.That(Snapshot(b), Is.EqualTo(Snapshot(a)), "two worlds built from the same seed should serialize identically");
        }

        [Test]
        public void DifferentSeed_ProducesADifferentWorld()
        {
            WorldData data = TestFixtures.BuildWorldData();

            GameState a = TestFixtures.BuildState(555UL, data);
            WorldGenerator.Generate(a, data);

            GameState b = TestFixtures.BuildState(556UL, data);
            WorldGenerator.Generate(b, data);

            Assert.That(Snapshot(a), Is.Not.EqualTo(Snapshot(b)));
        }

        [Test]
        public void Ids_AreUniqueAcrossEveryEntityType()
        {
            WorldData data = TestFixtures.BuildWorldData();
            GameState state = TestFixtures.BuildState(999UL, data);
            WorldGenerator.Generate(state, data);

            HashSet<int> ids = new HashSet<int>();
            int total = 0;

            foreach (Person p in state.People) { total++; ids.Add(p.Id); }
            foreach (Group g in state.Groups) { total++; ids.Add(g.Id); }
            foreach (ProductionCenter c in state.Centers) { total++; ids.Add(c.Id); }

            Assert.That(ids.Count, Is.EqualTo(total), "every entity, across every type, should have a unique id");
        }

        [Test]
        public void ExactlyOneCenterIsThePlayers()
        {
            WorldData data = TestFixtures.BuildWorldData();
            GameState state = TestFixtures.BuildState(1UL, data);
            WorldGenerator.Generate(state, data);

            Assert.That(state.Centers.Count(c => c.IsPlayer), Is.EqualTo(1));
        }

        [Test]
        public void PlayerCenter_HasExactlyTwelveTrainees()
        {
            WorldData data = TestFixtures.BuildWorldData();
            GameState state = TestFixtures.BuildState(1UL, data);
            WorldGenerator.Generate(state, data);

            ProductionCenter player = state.Centers.First(c => c.IsPlayer);
            Assert.That(player.TraineeIds.Count, Is.EqualTo(12));

            foreach (int id in player.TraineeIds)
            {
                Person trainee = state.GetPerson(id);
                Assert.That(trainee.Status, Is.EqualTo(PersonStatus.Trainee));
                Assert.That(trainee.Age(state.Date), Is.InRange(15, 19));
            }
        }

        [Test]
        public void PlayerCenter_HasExactlyOneGroup_DebutedWithinTheLastTwoYears()
        {
            WorldData data = TestFixtures.BuildWorldData();
            GameState state = TestFixtures.BuildState(1UL, data);
            WorldGenerator.Generate(state, data);

            ProductionCenter player = state.Centers.First(c => c.IsPlayer);
            Assert.That(player.GroupIds.Count, Is.EqualTo(1));

            Group group = state.GetGroup(player.GroupIds[0]);
            int weeksSinceDebut = SimDate.WeeksBetween(group.DebutDate, state.Date);
            Assert.That(weeksSinceDebut, Is.InRange(52, 104));
            Assert.That(group.Tier, Is.EqualTo(GroupTier.Rookie).Or.EqualTo(GroupTier.Rising));
        }

        [Test]
        public void GeneratesRoughlyFifteenGroupsOutsideTheCompany()
        {
            WorldData data = TestFixtures.BuildWorldData();
            GameState state = TestFixtures.BuildState(1UL, data);
            WorldGenerator.Generate(state, data);

            HashSet<int> companyCenterIds = new HashSet<int>(state.Company.CenterIds);
            int outsideGroupCount = state.Groups.Count(g => !companyCenterIds.Contains(g.CenterId));

            Assert.That(outsideGroupCount, Is.EqualTo(15));
        }

        [Test]
        public void CompanyHasExactlyThreeCenters_PlayerPlusTwoSiblings()
        {
            WorldData data = TestFixtures.BuildWorldData();
            GameState state = TestFixtures.BuildState(1UL, data);
            WorldGenerator.Generate(state, data);

            Assert.That(state.Company.CenterIds.Count, Is.EqualTo(3));
        }

        private static string Snapshot(GameState state)
        {
            return JsonConvert.SerializeObject(state.People)
                 + JsonConvert.SerializeObject(state.Groups)
                 + JsonConvert.SerializeObject(state.Centers);
        }
    }
}
