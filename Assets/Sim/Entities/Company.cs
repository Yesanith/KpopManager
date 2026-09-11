using System.Collections.Generic;

namespace KpopManager.Core
{
    /// <summary>
    /// Empty shape for now — Phase 6's <c>BoardSystem</c> defines the real fields (target type,
    /// threshold, deadline). A concrete class rather than <c>object</c> so <see cref="Board.Targets"/>
    /// stays strongly typed and forward-compatible with Newtonsoft.
    /// </summary>
    public sealed class BoardTarget
    {
    }

    /// <summary>The board above the player: sets targets, tolerates a run of misses, can fire them.
    /// Placeholder shape — behaviour belongs to Phase 6.</summary>
    public sealed class Board
    {
        /// <summary>How many missed years the board tolerates before firing the player. Difficulty setting; unset until Phase 6.</summary>
        public float Patience { get; set; }

        public List<BoardTarget> Targets { get; set; } = new List<BoardTarget>();
    }

    /// <summary>
    /// The company the player's center belongs to, per DESIGN.md's data model: one company owns
    /// <see cref="CenterIds"/> — the player's center plus the two sibling centers in the same
    /// building. Groups from other companies entirely (the wider "industry") are not part of this
    /// company; they live in <see cref="GameState.Centers"/> under their own, separate centers.
    /// </summary>
    public sealed class Company
    {
        public string Name { get; set; }
        public List<int> CenterIds { get; set; } = new List<int>();
        public Board Board { get; set; } = new Board();
    }
}
