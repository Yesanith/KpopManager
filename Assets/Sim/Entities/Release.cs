using System.Collections.Generic;

namespace KpopManager.Core
{
    // How big a release is. Flavour and promo-spend scaling for now; Phase 4 gives it real
    // mechanical weight (comeback phases, budget sliders).
    public enum ReleaseType
    {
        Single,
        Mini,
        Full
    }

    // Placeholder set matching DESIGN.md's own list verbatim. Phase 4 owns concepts properly
    // (fatigue, whiplash, fit-to-song); this phase only needs something for ChartSystem's
    // conceptFit term to point at, and that term is a flat 50 until Phase 4 exists anyway.
    public enum Concept
    {
        Cute,
        GirlCrush,
        Dark,
        Retro,
        Summer,
        Ballad,
        Experimental
    }

    // One comeback: a title track charted over time. Everything ChartSystem computes each week
    // is appended here, so a release's full history survives long after it drops off the chart.
    public sealed class Release
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public int TitleTrackId { get; set; }
        public SimDate ReleaseDate { get; set; }
        public Concept Concept { get; set; }
        public float PromoSpend { get; set; }
        public ReleaseType Type { get; set; }

        // The group's Fandom.Size at the moment this released — a fixed snapshot for analysis,
        // since the live value keeps moving after release.
        public long FandomSizeAtRelease { get; set; }

        // The group's GroupTier at the moment this released — same reasoning as
        // FandomSizeAtRelease: the live value moves, so an export that reads it later (as
        // BalanceRunner's CSV did) reports today's tier for a release from twenty years ago.
        // Added in Phase 3b after that bug was caught in the CSV output.
        public GroupTier GroupTierAtRelease { get; set; }

        // ---- Chart history --------------------------------------------------------------
        // Index 0 = release week. 0 at an index means unranked that week.
        public List<int> WeeklyPositions { get; set; } = new List<int>();

        public List<float> WeeklyPoints { get; set; } = new List<float>();
        public int PeakPosition { get; set; }
        public int WeeksInTop10 { get; set; }
        public int WeeksCharted { get; set; }

        // Sum of every WeeklyPoints entry ever recorded — a release's lifetime "area under the
        // curve," used by the CSV export.
        public float TotalPoints { get; set; }

        // Stays 0 this phase; Phase 4's MusicShowSystem fills it in.
        public int MusicShowWins { get; set; }

        // Whether ChartSystem still actively simulates this release. Not part of the Phase 3a
        // brief's field list — added so a 50-year run doesn't keep recomputing an ever-growing
        // pile of releases that flatlined to nothing years ago. Set false once WeeksBelowFloor
        // passes ChartConfig.ChartRetirementWeeksBelowFloor.
        public bool IsCharting { get; set; } = true;

        // Consecutive recent weeks this release's points have sat below the chart floor. Resets
        // to 0 the moment it charts again. Drives retirement via IsCharting.
        public int WeeksBelowFloor { get; set; }
    }
}
