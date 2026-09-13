using System.Collections.Generic;

namespace KpopManager.Core
{
    // Where a group sits in the industry. Shifts generation means; has no other meaning yet.
    public enum GroupTier
    {
        Rookie,
        Rising,
        Established,
        TopTier,
        Legendary
    }

    // Three separate numbers, per DESIGN.md — collapsing them into one popularity score loses the
    // strategic axis the whole game is built around (a group can have huge sales and no public
    // awareness, or the reverse).
    public sealed class Fandom
    {
        // Album sales, concert capacity, week-1 chart position — the "how many" number.
        public long Size { get; set; }

        // 0-100. Loyalty, forgiveness, willingness to spend.
        public float Sentiment { get; set; }

        // 0-100. Fame with the general public, independent of the fandom itself.
        public float PublicAwareness { get; set; }
    }

    // A K-pop group. Members reference it by Person.GroupId; it references them back by ordered
    // id, never by object reference.
    public sealed class Group
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int CenterId { get; set; }

        // Authoritative and ordered — iterate this, never rebuild membership from a scan of People.
        public List<int> MemberIds { get; set; } = new List<int>();

        public SimDate DebutDate { get; set; }

        // Always exactly DebutDate plus 7 years, per DESIGN.md's seven-year wall.
        public SimDate ContractExpiry { get; set; }

        public GroupTier Tier { get; set; }
        public bool IsActive { get; set; }

        // Null while the group is still active. Set once, by IndustryChurnSystem.Disband, so
        // BalanceRunner can report a real mean-lifespan distribution instead of only "still going
        // or not" — a group's age at disband is otherwise lost the moment IsActive flips to false.
        public SimDate? DisbandDate { get; set; }
        public Fandom Fandom { get; set; } = new Fandom();
        public List<int> ReleaseIds { get; set; } = new List<int>();

        // DESIGN: single-gender, matching the real-world convention and mirroring Person.Gender.
        // Co-ed groups are a DESIGN.md open question, not modelled yet.
        public Gender Gender { get; set; }

        // ---- Phase 3a: ReleaseScheduler state ----------------------------------------------
        // Per CLAUDE.md's rule that a stateless system's memory lives on GameState, not on the
        // system itself — ReleaseScheduler is a stateless ISimSystem, so the two things it needs
        // to remember between ticks for each group live here.

        // This group's own comeback cadence in months, rolled once at world generation from a
        // tier-derived mean (see ChartConfig.CadenceMinMonths/MaxMonths). Not player-adjustable —
        // Phase 4 gives the player's own group a real comeback-timing decision; this is the AI's
        // fixed rhythm.
        public float ReleaseCadenceMonths { get; set; }

        // The next date ReleaseScheduler should release this group's next single, seeded at world
        // generation so groups don't all debut in the same week, then pushed forward by
        // ReleaseCadenceMonths (plus jitter) after every release.
        public SimDate NextReleaseDate { get; set; }

        // Consecutive weeks since this group last had a release actively charting — the Phase 3
        // FandomSystem stand-in's memory of "has this group gone quiet," since it's a stateless
        // ISimSystem and this can't live on it. Resets to 0 the moment a release of theirs charts
        // again; drives ChartConfig.FandomInactivityGraceWeeks and decay.
        public int WeeksSinceLastCharted { get; set; }

        // Consecutive years this group has sat at GroupTier.Rookie — IndustryChurnSystem's memory
        // of a failing group, for the same "stateless system, state lives on the entity" reason as
        // the fields above. Resets to 0 the moment the group is anything but Rookie.
        public int ConsecutiveRookieYears { get; set; }
    }
}
