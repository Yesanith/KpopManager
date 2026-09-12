using System.Collections.Generic;

namespace KpopManager.Core
{
    // Where someone is in their career.
    public enum PersonStatus
    {
        Trainee,
        Active,
        Enlisted,
        Departed
    }

    // A role within a group. Leader and Maknae are group-role tags, not performance archetypes —
    // a person always carries exactly one performance archetype (the rest of this enum) plus,
    // for at most two members per group, one of these two tags.
    public enum Position
    {
        MainVocal,
        LeadVocal,
        MainRapper,
        LeadRapper,
        MainDancer,
        LeadDancer,
        Visual,
        Leader,
        Maknae,
        AllRounder
    }

    // Mechanical, not flavour: per DESIGN.md, nationality gates language training, overseas
    // market access and a set of visa/homesickness events layered on in later phases.
    public enum Nationality
    {
        Korean,
        Japanese,
        Chinese,
        Thai,
        American,
        Other
    }

    // DESIGN: added even though the Phase 2 field list didn't ask for it, because two requested
    // pieces only make sense with it: the gender tag on the given-name content files, and
    // DESIGN.md's "male idols enlist by around 28" military-service hook (Phase 8+). Girl groups
    // vs boy groups vs co-ed is still DESIGN.md's open question — GroupGenerator currently rolls
    // a random gender per group rather than deciding it. No non-binary modelling; out of scope
    // for this game.
    public enum Gender
    {
        Female,
        Male
    }

    // One human being: a trainee and an idol are the same class, distinguished only by Status.
    // Every 0-100 field is a float so training (Phase 5) can apply fractional growth without
    // rounding noise.
    public sealed class Person
    {
        // Value for GroupId or CenterId when there isn't one.
        public const int NoEntity = -1;

        // ---- Identity ----------------------------------------------------------------------
        public int Id { get; set; }
        public string GivenName { get; set; }
        public string FamilyName { get; set; }

        // Null or empty when the person performs under their given name.
        public string StageName { get; set; }

        public Gender Gender { get; set; }
        public Nationality Nationality { get; set; }
        public int BirthYear { get; set; }
        public PersonStatus Status { get; set; }

        // ---- Performance (0-100) -------------------------------------------------------------
        public float Vocal { get; set; }
        public float Rap { get; set; }
        public float Dance { get; set; }
        public float StagePresence { get; set; }

        // ---- Star (0-100) ---------------------------------------------------------------------
        public float Visual { get; set; }
        public float Charisma { get; set; }
        public float Variety { get; set; }
        public float FanConnection { get; set; }

        // ---- Creative (0-100) -------------------------------------------------------------
        public float Songwriting { get; set; }
        public float Composition { get; set; }
        public float Choreography { get; set; }

        // ---- Hidden (0-100) — never shown to the player as a number until scouting narrows it ----
        public float WorkEthic { get; set; }
        public float MentalResilience { get; set; }
        public float Ambition { get; set; }
        public float Professionalism { get; set; }

        // The soft cap on growth. Generated truthfully in Phase 2; Phase 5's scouting fog is what
        // actually hides it from the player.
        public float Potential { get; set; }

        // ---- Dynamic (0-100) — fluctuates during play -------------------------------------
        public float Morale { get; set; }
        public float Fatigue { get; set; }
        public float Health { get; set; }
        public float InGroupPopularity { get; set; }

        // ---- Membership ---------------------------------------------------------------------
        public int GroupId { get; set; } = NoEntity;
        public int CenterId { get; set; } = NoEntity;

        // Usually 1-2: one performance archetype, plus optionally Leader or Maknae.
        public List<Position> Positions { get; set; } = new List<Position>();

        // ---- Trainee ----------------------------------------------------------------------
        public int YearsTraining { get; set; }
        public SimDate JoinedDate { get; set; }

        // Proficiency (0-100) keyed by nationality, standing in for "language" per DESIGN.md's
        // treatment of language as mechanical. DESIGN: a Dictionary is fine here — every read is
        // a lookup by a specific known nationality, never an iteration over all keys, so it
        // cannot introduce hash-order nondeterminism the way iterating one would.
        public Dictionary<Nationality, int> LanguageProficiency { get; set; } = new Dictionary<Nationality, int>();

        // Age in whole years as of now.
        public int Age(SimDate now) => now.Year - BirthYear;

        // Stage name if set, otherwise given name — what the UI shows by default.
        public string DisplayName => string.IsNullOrEmpty(StageName) ? GivenName : StageName;

        // Korean-order full legal name: family name first.
        public string FullName => string.IsNullOrEmpty(FamilyName) ? GivenName : FamilyName + " " + GivenName;

        // The highest of the eleven attributes training can raise — Performance ∪ Star ∪
        // Creative. Potential must never fall below this: a person cannot have already exceeded
        // their own ceiling.
        public float CoreAttributeMax()
        {
            float max = Vocal;
            if (Rap > max) max = Rap;
            if (Dance > max) max = Dance;
            if (StagePresence > max) max = StagePresence;
            if (Visual > max) max = Visual;
            if (Charisma > max) max = Charisma;
            if (Variety > max) max = Variety;
            if (FanConnection > max) max = FanConnection;
            if (Songwriting > max) max = Songwriting;
            if (Composition > max) max = Composition;
            if (Choreography > max) max = Choreography;
            return max;
        }
    }
}
