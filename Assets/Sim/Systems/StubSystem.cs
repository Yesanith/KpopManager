namespace KpopManager.Core.Systems
{
    // A registered tick step with no behaviour yet.
    //
    // DESIGN: chose one parameterised placeholder over twelve empty classes. Twelve empty classes
    // would occupy the names the real systems want (ChartSystem, FandomSystem, and so on) and each
    // would have to be deleted at the moment its replacement is written. With this, graduating a
    // step is a one-line edit in TickOrder: swap new StubSystem("Chart simulation", 3) for
    // new ChartSystem(). Revisit if a stub ever needs to hold placeholder behaviour rather than
    // none.
    public sealed class StubSystem : ISimSystem
    {
        // name: the step's name, matching the tick order table in ARCHITECTURE.md.
        // duePhase: the phase that will replace this stub. Documentation only.
        public StubSystem(string name, int duePhase)
        {
            Name = name;
            DuePhase = duePhase;
        }

        public string Name { get; }

        public int DuePhase { get; }

        // Does nothing, on purpose. Must not log and must not draw from GameState.Random — a stub
        // that consumed a random number would shift every later draw, and replacing it with real
        // behaviour would silently invalidate every seed balanced against it.
        public void Tick(GameState state)
        {
        }
    }
}
