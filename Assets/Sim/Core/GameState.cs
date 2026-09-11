using System.Collections.Generic;
using Newtonsoft.Json;

namespace KpopManager.Core
{
    /// <summary>
    /// The entire mutable world, as a plain data graph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No system references, no delegates, no events, no circular parent pointers. Entities
    /// reference each other by <c>int Id</c> rather than by object reference, so that the whole
    /// graph serialises cleanly with Newtonsoft in Phase 9.
    /// </para>
    /// <para>
    /// Systems read and write this and nothing else. If a system needs to remember something
    /// between ticks, that something belongs here, not in a field on the system.
    /// </para>
    /// <para>
    /// <b>Storage pattern, applied consistently for every entity type:</b> an ordered
    /// <c>List&lt;T&gt;</c> is the authoritative collection — iterate it, and only it, whenever
    /// order could affect a sim outcome — plus a <c>Dictionary&lt;int, T&gt;</c> lookup index
    /// rebuilt from that list, used only for by-id access. The dictionary is never iterated for a
    /// sim outcome; hash order is not guaranteed stable and would silently break determinism.
    /// </para>
    /// </remarks>
    public class GameState
    {
        /// <summary>The current week. Advanced by <see cref="SimEngine"/> after every system has ticked.</summary>
        public SimDate Date { get; set; }

        /// <summary>The one and only generator. Every random draw in the sim comes from here.</summary>
        public SimRandom Random { get; set; }

        /// <summary>Everything the simulation has said so far.</summary>
        public SimLog Log { get; set; }

        /// <summary>The seed this run was created from. Kept so a save can report and reproduce it.</summary>
        public ulong Seed { get; set; }

        /// <summary>
        /// Id allocator shared by every entity type — people, groups and centers all draw from
        /// this one counter, so ids are unique across the whole game, not just within a type.
        /// </summary>
        public int NextEntityId { get; set; }

        /// <summary>The company the player's center belongs to.</summary>
        public Company Company { get; set; }

        // ---- Entities: authoritative ordered lists -----------------------------------------
        public List<Person> People { get; set; } = new List<Person>();
        public List<Group> Groups { get; set; } = new List<Group>();
        public List<ProductionCenter> Centers { get; set; } = new List<ProductionCenter>();

        // ---- Lookup indices — rebuilt from the lists above, never iterated for sim outcomes ----
        [JsonIgnore] private Dictionary<int, Person> _peopleById = new Dictionary<int, Person>();
        [JsonIgnore] private Dictionary<int, Group> _groupsById = new Dictionary<int, Group>();
        [JsonIgnore] private Dictionary<int, ProductionCenter> _centersById = new Dictionary<int, ProductionCenter>();

        /// <summary>
        /// Loaded content — name banks and the like. Not part of the save: it is reloaded from
        /// disk by the Editor (or Phase 7's runtime loader) each time a run starts, independently
        /// of whatever save is restored.
        /// </summary>
        [JsonIgnore] public WorldData WorldData { get; set; }

        /// <summary>Allocates and returns the next unused entity id.</summary>
        public int AllocateEntityId()
        {
            int id = NextEntityId;
            NextEntityId++;
            return id;
        }

        /// <summary>Adds a person to both the authoritative list and the lookup index.</summary>
        public void AddPerson(Person person)
        {
            People.Add(person);
            _peopleById[person.Id] = person;
        }

        /// <summary>Adds a group to both the authoritative list and the lookup index.</summary>
        public void AddGroup(Group group)
        {
            Groups.Add(group);
            _groupsById[group.Id] = group;
        }

        /// <summary>Adds a center to both the authoritative list and the lookup index.</summary>
        public void AddCenter(ProductionCenter center)
        {
            Centers.Add(center);
            _centersById[center.Id] = center;
        }

        /// <summary>Looks up a person by id, or null if there isn't one.</summary>
        public Person GetPerson(int id) => _peopleById.TryGetValue(id, out Person person) ? person : null;

        /// <summary>Looks up a group by id, or null if there isn't one.</summary>
        public Group GetGroup(int id) => _groupsById.TryGetValue(id, out Group group) ? group : null;

        /// <summary>Looks up a center by id, or null if there isn't one.</summary>
        public ProductionCenter GetCenter(int id) => _centersById.TryGetValue(id, out ProductionCenter center) ? center : null;

        /// <summary>
        /// Rebuilds every lookup index from the authoritative lists. Call after construction and,
        /// from Phase 9 onward, after loading a save — the lists serialise, the indices don't.
        /// </summary>
        public void RebuildIndices()
        {
            _peopleById = new Dictionary<int, Person>(People.Count);
            for (int i = 0; i < People.Count; i++)
            {
                _peopleById[People[i].Id] = People[i];
            }

            _groupsById = new Dictionary<int, Group>(Groups.Count);
            for (int i = 0; i < Groups.Count; i++)
            {
                _groupsById[Groups[i].Id] = Groups[i];
            }

            _centersById = new Dictionary<int, ProductionCenter>(Centers.Count);
            for (int i = 0; i < Centers.Count; i++)
            {
                _centersById[Centers[i].Id] = Centers[i];
            }
        }
    }
}
