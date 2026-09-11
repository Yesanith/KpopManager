using System.Collections.Generic;

namespace KpopManager.Core
{
    /// <summary>
    /// A production center's own progression tier, per DESIGN.md's "long arc" (Junior →
    /// Established → Flagship → Spin-off label). Distinct from <see cref="GroupTier"/>, which
    /// tracks an individual group's standing, not the center that runs it.
    /// </summary>
    public enum CenterTier
    {
        Junior,
        Established,
        Flagship,
        SpinOff
    }

    /// <summary>
    /// One production center. The player runs exactly one (<see cref="IsPlayer"/>); everyone
    /// else's centers are simulated the same way so the data model already supports the multi-group,
    /// multi-center future without the MVP building any of it.
    /// </summary>
    public sealed class ProductionCenter
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsPlayer { get; set; }
        public float Budget { get; set; }

        /// <summary>0–100. Spent on requests to HQ (DESIGN.md's "what you have to ask for" list); rises with results.</summary>
        public float Goodwill { get; set; }

        public List<int> GroupIds { get; set; } = new List<int>();
        public List<int> TraineeIds { get; set; } = new List<int>();
        public CenterTier Tier { get; set; }
    }
}
