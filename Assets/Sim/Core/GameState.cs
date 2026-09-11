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
        /// Id allocator for the entities Phase 2 introduces. Lives on the state rather than in a
        /// static counter so that ids stay stable across save/load and across two engines built
        /// from the same seed.
        /// </summary>
        public int NextEntityId { get; set; }

        /// <summary>Allocates and returns the next unused entity id.</summary>
        public int AllocateEntityId()
        {
            int id = NextEntityId;
            NextEntityId++;
            return id;
        }
    }
}
