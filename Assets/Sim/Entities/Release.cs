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

        // ---- Two-curve trajectory (Phase 3b iteration 2) ---------------------------------
        // Both rolled once at release time and held fixed for the release's whole chart life —
        // see ChartConfig's own comment for why these are two independent components rather than
        // one BuzzScore. Kept on Release (not recomputed weekly) so a CSV export can inspect what
        // kind of release this was without re-deriving it from the group's state at release time.
        public float FandomPull { get; set; }
        public float PublicAppeal { get; set; }

        // 1.0 if this release didn't roll a crossover; > 1.0 (CrossoverMultiplierMin..Max) if it
        // did. Stored rather than just applied invisibly into PublicAppeal so BalanceRunner can
        // report a real crossover rate instead of inferring it from the appeal value.
        public float CrossoverMultiplier { get; set; } = 1f;

        // ---- Chart history --------------------------------------------------------------
        // Index 0 = release week. 0 at an index means unranked that week.
        public List<int> WeeklyPositions { get; set; } = new List<int>();

        public List<float> WeeklyPoints { get; set; } = new List<float>();

        // Phase 3b iteration 3: the competition modifier actually applied that week, index-aligned
        // with WeeklyPositions/WeeklyPoints. Added so BalanceRunner can report a real mean/p5/p95
        // of the modifier across a run instead of reconstructing it after the fact — a
        // reconstruction would need each week's LIVE group tier at the time, which isn't otherwise
        // recoverable once the group's tier has since changed.
        public List<float> WeeklyCompetitionModifiers { get; set; } = new List<float>();

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
        // pile of releases that flatlined to nothing years ago. Set false once WeeksOffChart
        // passes ChartConfig.ChartRetirementWeeksOffChart, or once the release has been simulated
        // for ChartConfig.ChartMaxSimulatedWeeks regardless (Phase 3b iteration 3 fix 2).
        public bool IsCharting { get; set; } = true;

        // Consecutive recent weeks this release held no charted position (position 0 — not merely
        // low points; see ChartConfig's iteration 3 fix 2 comment for why retirement moved off an
        // absolute points floor). Resets to 0 the moment it charts again. Drives retirement
        // alongside ChartConfig.ChartMaxSimulatedWeeks.
        public int WeeksOffChart { get; set; }
    }
}
