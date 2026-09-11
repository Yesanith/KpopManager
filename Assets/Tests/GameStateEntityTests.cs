using KpopManager.Core;
using NUnit.Framework;

namespace KpopManager.Tests
{
    /// <summary>Covers the storage pattern every entity type shares: an authoritative ordered
    /// list plus a lookup index that must survive <see cref="GameState.RebuildIndices"/>.</summary>
    [TestFixture]
    public class GameStateEntityTests
    {
        [Test]
        public void AddPerson_IsRetrievableByGetPerson()
        {
            GameState state = new GameState();
            Person person = new Person { Id = 5, GivenName = "Test" };

            state.AddPerson(person);

            Assert.That(state.People, Has.Count.EqualTo(1));
            Assert.That(state.GetPerson(5), Is.SameAs(person));
        }

        [Test]
        public void AddGroup_IsRetrievableByGetGroup()
        {
            GameState state = new GameState();
            Group group = new Group { Id = 7, Name = "Test Group" };

            state.AddGroup(group);

            Assert.That(state.GetGroup(7), Is.SameAs(group));
        }

        [Test]
        public void AddCenter_IsRetrievableByGetCenter()
        {
            GameState state = new GameState();
            ProductionCenter center = new ProductionCenter { Id = 3, Name = "Test Center" };

            state.AddCenter(center);

            Assert.That(state.GetCenter(3), Is.SameAs(center));
        }

        [Test]
        public void GetPerson_ReturnsNullForAnUnknownId()
        {
            GameState state = new GameState();
            Assert.That(state.GetPerson(999), Is.Null);
        }

        [Test]
        public void RebuildIndices_RestoresLookupFromTheAuthoritativeLists()
        {
            GameState state = new GameState();
            state.People.Add(new Person { Id = 1, GivenName = "A" });
            state.People.Add(new Person { Id = 2, GivenName = "B" });
            state.Groups.Add(new Group { Id = 10, Name = "G" });
            state.Centers.Add(new ProductionCenter { Id = 20, Name = "C" });

            // Simulating a fresh load: the lists are populated (as they would be by
            // deserialization) but the indices are not — RebuildIndices is what Phase 9 calls.
            Assert.That(state.GetPerson(1), Is.Null, "index shouldn't be populated until RebuildIndices runs");

            state.RebuildIndices();

            Assert.That(state.GetPerson(1)?.GivenName, Is.EqualTo("A"));
            Assert.That(state.GetPerson(2)?.GivenName, Is.EqualTo("B"));
            Assert.That(state.GetGroup(10)?.Name, Is.EqualTo("G"));
            Assert.That(state.GetCenter(20)?.Name, Is.EqualTo("C"));
        }

        [Test]
        public void AllocateEntityId_IsMonotonicAndStartsAtNextEntityId()
        {
            GameState state = new GameState { NextEntityId = 5 };

            Assert.That(state.AllocateEntityId(), Is.EqualTo(5));
            Assert.That(state.AllocateEntityId(), Is.EqualTo(6));
            Assert.That(state.NextEntityId, Is.EqualTo(7));
        }
    }
}
