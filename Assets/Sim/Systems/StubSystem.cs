namespace KpopManager.Core.Systems
{
    /// <summary>
    /// A registered tick step with no behaviour yet.
    /// </summary>
    /// <remarks>
    /// DESIGN: chose one parameterised placeholder over twelve empty classes. Twelve empty classes
    /// would occupy the names the real systems want (<c>ChartSystem</c>, <c>FandomSystem</c>, and
    /// so on) and each would have to be deleted at the moment its replacement is written. With
    /// this, graduating a step is a one-line edit in <see cref="TickOrder"/>: swap
    /// <c>new StubSystem("Chart simulation", 3)</c> for <c>new ChartSystem()</c>. Revisit if a
    /// stub ever needs to hold placeholder behaviour rather than none.
    /// </remarks>
    public sealed class StubSystem : ISimSystem
    {
        /// <summary>Creates a placeholder for a step that is not implemented yet.</summary>
        /// <param name="name">The step's name, matching the tick order table in ARCHITECTURE.md.</param>
        /// <param name="duePhase">The phase that will replace this stub. Documentation only.</param>
        public StubSystem(string name, int duePhase)
        {
            Name = name;
            DuePhase = duePhase;
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <summary>The phase in which this step gets a real implementation.</summary>
        public int DuePhase { get; }

        /// <summary>
        /// Does nothing, on purpose. It must not log and must not draw from
        /// <see cref="GameState.Random"/> — a stub that consumed a random number would shift every
        /// later draw, and replacing it with real behaviour would silently invalidate every seed
        /// balanced against it.
        /// </summary>
        public void Tick(GameState state)
        {
        }
    }
}
