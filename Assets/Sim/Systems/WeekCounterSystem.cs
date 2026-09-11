namespace KpopManager.Core.Systems
{
    /// <summary>
    /// Logs one line per week so that Phase 1 has something visible to tick.
    /// </summary>
    /// <remarks>
    /// Temporary. This is the only system with real behaviour until Phase 2, and it exists so the
    /// harness log view, the filters and the determinism test all have real data to work against.
    /// Remove it once generated entities produce their own output.
    /// </remarks>
    public sealed class WeekCounterSystem : ISimSystem
    {
        /// <inheritdoc />
        public string Name => "Week counter (Phase 1 diagnostic)";

        /// <inheritdoc />
        public void Tick(GameState state)
        {
            state.Log.Add(
                state.Date,
                LogCategory.System,
                LogSeverity.Info,
                "Week ticked: " + state.Date);
        }
    }
}
