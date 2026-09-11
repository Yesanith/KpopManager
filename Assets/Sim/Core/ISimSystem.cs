namespace KpopManager.Core
{
    /// <summary>
    /// One step of the weekly tick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A system is stateless. Everything it needs to remember lives on the <see cref="GameState"/>
    /// it is handed, because only that survives a save. A system that grows a private field has
    /// broken save/load, and the determinism test will not catch it.
    /// </para>
    /// <para>
    /// Systems must not call the UI, must not read wall-clock time, and must draw randomness only
    /// from <see cref="GameState.Random"/>.
    /// </para>
    /// </remarks>
    public interface ISimSystem
    {
        /// <summary>Human-readable name, shown in harness timings and used in log messages.</summary>
        string Name { get; }

        /// <summary>Runs this system for the current week. Called once per tick, in tick order.</summary>
        void Tick(GameState state);
    }
}
