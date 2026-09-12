namespace KpopManager.Core.Systems
{
    // Logs one line per week so that Phase 1 has something visible to tick.
    //
    // Temporary. This is the only system with real behaviour until Phase 2, and it exists so the
    // harness log view, the filters and the determinism test all have real data to work against.
    // Remove it once generated entities produce their own output.
    public sealed class WeekCounterSystem : ISimSystem
    {
        public string Name => "Week counter (Phase 1 diagnostic)";

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
