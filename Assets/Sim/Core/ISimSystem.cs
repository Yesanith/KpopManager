namespace KpopManager.Core
{
    // One step of the weekly tick.
    //
    // A system is stateless. Everything it needs to remember lives on the GameState it is
    // handed, because only that survives a save. A system that grows a private field has broken
    // save/load, and the determinism test will not catch it.
    //
    // Systems must not call the UI, must not read wall-clock time, and must draw randomness only
    // from GameState.Random.
    public interface ISimSystem
    {
        // Shown in harness timings and used in log messages.
        string Name { get; }

        // Called once per tick, in tick order.
        void Tick(GameState state);
    }
}
