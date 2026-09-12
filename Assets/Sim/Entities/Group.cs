using System.Collections.Generic;

namespace KpopManager.Core
{
    /// <summary>Where a group sits in the industry. Shifts generation means; has no other meaning yet.</summary>
    public enum GroupTier
    {
        Rookie,
        Rising,
        Established,
        TopTier,
        Legendary
    }

    /// <summary>
    /// Three separate numbers, per DESIGN.md — collapsing them into one popularity score loses the
    /// strategic axis the whole game is built around (a group can have huge sales and no public
    /// awareness, or the reverse).
    /// </summary>
    public sealed class Fandom
    {
        /// <summary>Album sales, concert capacity, week-1 chart position — the "how many" number.</summary>
        public long Size { get; set; }

        /// <summary>0–100. Loyalty, forgiveness, willingness to spend.</summary>
        public float Sentiment { get; set; }

        /// <summary>0–100. Fame with the general public, independent of the fandom itself.</summary>
        public float PublicAwareness { get; set; }
    }

    /// <summary>A K-pop group. Members reference it by <see cref="Person.GroupId"/>; it references
    /// them back by ordered id, never by object reference.</summary>
    public sealed class Group
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int CenterId { get; set; }

        /// <summary>Authoritative and ordered — iterate this, never rebuild membership from a scan of People.</summary>
        public List<int> MemberIds { get; set; } = new List<int>();

        public SimDate DebutDate { get; set; }

        /// <summary>Always exactly <see cref="DebutDate"/> plus 7 years, per DESIGN.md's seven-year wall.</summary>
        public SimDate ContractExpiry { get; set; }

        public GroupTier Tier { get; set; }
        public bool IsActive { get; set; }
        public Fandom Fandom { get; set; } = new Fandom();
        public List<int> ReleaseIds { get; set; } = new List<int>();

        /// <summary>DESIGN: single-gender, matching the real-world convention and mirroring <see cref="Core.Gender"/>
        /// on <see cref="Person"/>. Co-ed groups are a DESIGN.md open question, not modelled yet.</summary>
        public Gender Gender { get; set; }

        // ---- Phase 3a: ReleaseScheduler state ----------------------------------------------
        // Per CLAUDE.md's rule that a stateless system's memory lives on GameState, not on the
        // system itself — ReleaseScheduler is a stateless ISimSystem, so the two things it needs
        // to remember between ticks for each group live here.

        /// <summary>This group's own comeback cadence in months, rolled once at world generation
        /// from a tier-derived mean (see <c>ChartConfig.CadenceMinMonths</c>/<c>MaxMonths</c>).
        /// Not player-adjustable — Phase 4 gives the player's own group a real comeback-timing
        /// decision; this is the AI's fixed rhythm.</summary>
        public float ReleaseCadenceMonths { get; set; }

        /// <summary>The next date <see cref="Systems.Chart.ReleaseScheduler"/> should release this
        /// group's next single, seeded at world generation so groups don't all debut in the same
        /// week, then pushed forward by <see cref="ReleaseCadenceMonths"/> (plus jitter) after
        /// every release.</summary>
        public SimDate NextReleaseDate { get; set; }

        /// <summary>
        /// Consecutive weeks since this group last had a release actively charting — the Phase 3
        /// <c>FandomSystem</c> stand-in's memory of "has this group gone quiet," since it's a
        /// stateless <see cref="ISimSystem"/> and this can't live on it. Resets to 0 the moment a
        /// release of theirs charts again; drives <see cref="ChartConfig.FandomInactivityGraceWeeks"/>
        /// and decay.
        /// </summary>
        public int WeeksSinceLastCharted { get; set; }
    }
}
