namespace KpopManager.Core
{
    /// <summary>Musical genre. Flat and small on purpose — this is a generation/flavour tag for
    /// Phase 3a, not yet wired into trend or concept mechanics (Phase 4 owns that).</summary>
    public enum Genre
    {
        Pop,
        HipHop,
        RnB,
        EDM,
        Ballad,
        Rock,
        Trot
    }

    /// <summary>
    /// One song. <see cref="Quality"/> is the hidden truth the whole chart formula is built on —
    /// never shown to the player directly (Phase 5's scouting-fog philosophy extends to tracks:
    /// A&amp;R gives a read, not a number).
    /// </summary>
    public sealed class Track
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public Genre Genre { get; set; }

        /// <summary>0–100, hidden. Everything in <c>ChartSystem</c> ultimately answers to this.</summary>
        public float Quality { get; set; }

        /// <summary>The composing <see cref="Person"/>'s id, or <see cref="Person.NoEntity"/> for
        /// an external songwriter not modelled as a person.</summary>
        public int ComposerId { get; set; } = Person.NoEntity;

        public bool IsTitleTrack { get; set; }
    }
}
