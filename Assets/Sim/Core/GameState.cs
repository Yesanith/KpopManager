using System.Collections.Generic;
using KpopManager.Core.Systems.Chart;
using Newtonsoft.Json;

namespace KpopManager.Core
{
    // The entire mutable world, as a plain data graph.
    //
    // No system references, no delegates, no events, no circular parent pointers. Entities
    // reference each other by int Id rather than by object reference, so that the whole graph
    // serialises cleanly with Newtonsoft in Phase 9.
    //
    // Systems read and write this and nothing else. If a system needs to remember something
    // between ticks, that something belongs here, not in a field on the system.
    //
    // Storage pattern, applied consistently for every entity type: an ordered List<T> is the
    // authoritative collection — iterate it, and only it, whenever order could affect a sim
    // outcome — plus a Dictionary<int, T> lookup index rebuilt from that list, used only for
    // by-id access. The dictionary is never iterated for a sim outcome; hash order is not
    // guaranteed stable and would silently break determinism.
    public class GameState
    {
        // Advanced by SimEngine after every system has ticked.
        public SimDate Date { get; set; }

        // Every random draw in the sim comes from here.
        public SimRandom Random { get; set; }

        public SimLog Log { get; set; }

        // Kept so a save can report and reproduce it.
        public ulong Seed { get; set; }

        // Id allocator shared by every entity type — people, groups and centers all draw from
        // this one counter, so ids are unique across the whole game, not just within a type.
        public int NextEntityId { get; set; }

        // The company the player's center belongs to.
        public Company Company { get; set; }

        // Every tunable number behind the chart sim, scheduler, tier mobility and the Phase 3
        // fandom stand-in. Hangs off the state so it serialises with a save and so a balance run
        // can swap it wholesale.
        public ChartConfig ChartConfig { get; set; } = new ChartConfig();

        // ---- Entities: authoritative ordered lists -----------------------------------------
        public List<Person> People { get; set; } = new List<Person>();
        public List<Group> Groups { get; set; } = new List<Group>();
        public List<ProductionCenter> Centers { get; set; } = new List<ProductionCenter>();
        public List<Track> Tracks { get; set; } = new List<Track>();
        public List<Release> Releases { get; set; } = new List<Release>();

        // ---- Lookup indices — rebuilt from the lists above, never iterated for sim outcomes ----
        [JsonIgnore] private Dictionary<int, Person> _peopleById = new Dictionary<int, Person>();
        [JsonIgnore] private Dictionary<int, Group> _groupsById = new Dictionary<int, Group>();
        [JsonIgnore] private Dictionary<int, ProductionCenter> _centersById = new Dictionary<int, ProductionCenter>();
        [JsonIgnore] private Dictionary<int, Track> _tracksById = new Dictionary<int, Track>();
        [JsonIgnore] private Dictionary<int, Release> _releasesById = new Dictionary<int, Release>();

        // Loaded content — name banks and the like. Not part of the save: it is reloaded from
        // disk by the Editor (or Phase 7's runtime loader) each time a run starts, independently
        // of whatever save is restored.
        [JsonIgnore] public WorldData WorldData { get; set; }

        public int AllocateEntityId()
        {
            int id = NextEntityId;
            NextEntityId++;
            return id;
        }

        public void AddPerson(Person person)
        {
            People.Add(person);
            _peopleById[person.Id] = person;
        }

        public void AddGroup(Group group)
        {
            Groups.Add(group);
            _groupsById[group.Id] = group;
        }

        public void AddCenter(ProductionCenter center)
        {
            Centers.Add(center);
            _centersById[center.Id] = center;
        }

        public void AddTrack(Track track)
        {
            Tracks.Add(track);
            _tracksById[track.Id] = track;
        }

        public void AddRelease(Release release)
        {
            Releases.Add(release);
            _releasesById[release.Id] = release;
        }

        public Person GetPerson(int id) => _peopleById.TryGetValue(id, out Person person) ? person : null;

        public Group GetGroup(int id) => _groupsById.TryGetValue(id, out Group group) ? group : null;

        public ProductionCenter GetCenter(int id) => _centersById.TryGetValue(id, out ProductionCenter center) ? center : null;

        public Track GetTrack(int id) => _tracksById.TryGetValue(id, out Track track) ? track : null;

        public Release GetRelease(int id) => _releasesById.TryGetValue(id, out Release release) ? release : null;

        // Rebuilds every lookup index from the authoritative lists. Call after construction and,
        // from Phase 9 onward, after loading a save — the lists serialise, the indices don't.
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

            _tracksById = new Dictionary<int, Track>(Tracks.Count);
            for (int i = 0; i < Tracks.Count; i++)
            {
                _tracksById[Tracks[i].Id] = Tracks[i];
            }

            _releasesById = new Dictionary<int, Release>(Releases.Count);
            for (int i = 0; i < Releases.Count; i++)
            {
                _releasesById[Releases[i].Id] = Releases[i];
            }
        }
    }
}
