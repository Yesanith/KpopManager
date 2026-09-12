using System.Collections.Generic;

namespace KpopManager.Core
{
    // Empty shape for now — Phase 6's BoardSystem defines the real fields (target type,
    // threshold, deadline). A concrete class rather than object so Board.Targets stays strongly
    // typed and forward-compatible with Newtonsoft.
    public sealed class BoardTarget
    {
    }

    // The board above the player: sets targets, tolerates a run of misses, can fire them.
    // Placeholder shape — behaviour belongs to Phase 6.
    public sealed class Board
    {
        // How many missed years the board tolerates before firing the player. Difficulty
        // setting; unset until Phase 6.
        public float Patience { get; set; }

        public List<BoardTarget> Targets { get; set; } = new List<BoardTarget>();
    }

    // The company the player's center belongs to, per DESIGN.md's data model: one company owns
    // CenterIds — the player's center plus the two sibling centers in the same building. Groups
    // from other companies entirely (the wider "industry") are not part of this company; they
    // live in GameState.Centers under their own, separate centers.
    public sealed class Company
    {
        public string Name { get; set; }
        public List<int> CenterIds { get; set; } = new List<int>();
        public Board Board { get; set; } = new Board();
    }
}
