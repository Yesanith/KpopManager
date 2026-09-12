namespace KpopManager.Core
{
    // Musical genre. Flat and small on purpose — this is a generation/flavour tag for Phase 3a,
    // not yet wired into trend or concept mechanics (Phase 4 owns that).
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

    // One song. Quality is the hidden truth the whole chart formula is built on — never shown to
    // the player directly (Phase 5's scouting-fog philosophy extends to tracks: A&R gives a read,
    // not a number).
    public sealed class Track
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public Genre Genre { get; set; }

        // 0-100, hidden. Everything in ChartSystem ultimately answers to this.
        public float Quality { get; set; }

        // The composing Person's id, or Person.NoEntity for an external songwriter not modelled
        // as a person.
        public int ComposerId { get; set; } = Person.NoEntity;

        public bool IsTitleTrack { get; set; }
    }
}
