using System.Collections.Generic;

namespace KpopManager.Core
{
    /// <summary>How big a release is. Flavour and promo-spend scaling for now; Phase 4 gives it
    /// real mechanical weight (comeback phases, budget sliders).</summary>
    public enum ReleaseType
    {
        Single,
        Mini,
        Full
    }

    /// <summary>
    /// Placeholder set matching DESIGN.md's own list verbatim. Phase 4 owns concepts properly
    /// (fatigue, whiplash, fit-to-song); this phase only needs something for <c>ChartSystem</c>'s
    /// <c>conceptFit</c> term to point at, and that term is a flat 50 until Phase 4 exists anyway.
    /// </summary>
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

    /// <summary>
    /// One comeback: a title track charted over time. Everything <c>ChartSystem</c> computes each
    /// week is appended here, so a release's full history survives long after it drops off the
    /// chart.
    /// </summary>
    public sealed class Release
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public int TitleTrackId { get; set; }
        public SimDate ReleaseDate { get; set; }
        public Concept Concept { get; set; }
        public float PromoSpend { get; set; }
        public ReleaseType Type { get; set; }

        /// <summary>The group's <see cref="Fandom.Size"/> at the moment this released — a fixed
        /// snapshot for analysis, since the live value keeps moving after release.</summary>
        public long FandomSizeAtRelease { get; set; }

        // ---- Chart history --------------------------------------------------------------
        /// <summary>Index 0 = release week. 0 at an index means unranked that week.</summary>
        public List<int> WeeklyPositions { get; set; } = new List<int>();

        public List<float> WeeklyPoints { get; set; } = new List<float>();
        public int PeakPosition { get; set; }
        public int WeeksInTop10 { get; set; }
        public int WeeksCharted { get; set; }

        /// <summary>Sum of every <see cref="WeeklyPoints"/> entry ever recorded — a release's
        /// lifetime "area under the curve," used by the CSV export.</summary>
        public float TotalPoints { get; set; }

        /// <summary>Stays 0 this phase; Phase 4's <c>MusicShowSystem</c> fills it in.</summary>
        public int MusicShowWins { get; set; }

        /// <summary>
        /// Whether <see cref="ChartSystem"/> still actively simulates this release. Not part of
        /// the Phase 3a brief's field list — added so a 50-year run doesn't keep recomputing an
        /// ever-growing pile of releases that flatlined to nothing years ago. Set false once
        /// <see cref="WeeksBelowFloor"/> passes <c>ChartConfig.ChartRetirementWeeksBelowFloor</c>.
        /// </summary>
        public bool IsCharting { get; set; } = true;

        /// <summary>Consecutive recent weeks this release's points have sat below the chart floor.
        /// Resets to 0 the moment it charts again. Drives retirement via <see cref="IsCharting"/>.</summary>
        public int WeeksBelowFloor { get; set; }
    }
}
