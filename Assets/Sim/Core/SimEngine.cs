using System;
using System.Collections.Generic;
using KpopManager.Core.Systems;

namespace KpopManager.Core
{
    /// <summary>
    /// Owns a <see cref="GameState"/> and drives it forward one week at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tick runs every registered <see cref="ISimSystem"/> in order and then advances
    /// <see cref="GameState.Date"/>. Systems therefore all observe the same date during a tick,
    /// which is what makes "this happened in Y3 W17" unambiguous.
    /// </para>
    /// <para>
    /// The engine itself is not part of the save. It is rebuilt around a restored
    /// <see cref="GameState"/>, which is why no simulation data may live on it.
    /// </para>
    /// </remarks>
    public sealed class SimEngine
    {
        private readonly List<ISimSystem> _systems;

        /// <summary>Builds a new run from a seed, with the standard tick order registered.</summary>
        public SimEngine(ulong seed)
            : this(seed, TickOrder.BuildDefault())
        {
        }

        /// <summary>
        /// Builds a new run from a seed with an explicit system list. Used by tests that want to
        /// isolate one system; normal play always uses <see cref="TickOrder.BuildDefault"/>.
        /// </summary>
        public SimEngine(ulong seed, IEnumerable<ISimSystem> systems)
        {
            if (systems == null) throw new ArgumentNullException(nameof(systems));

            _systems = new List<ISimSystem>(systems);

            State = new GameState
            {
                Date = SimDate.Start,
                Seed = seed,
                Random = new SimRandom(seed),
                Log = new SimLog(),
                NextEntityId = 1
            };

            StartDate = State.Date;
        }

        /// <summary>The world. Read it, render it, never mutate it from outside the sim.</summary>
        public GameState State { get; }

        /// <summary>The date this run began, kept for elapsed-time readouts. Not part of the save.</summary>
        public SimDate StartDate { get; }

        /// <summary>The registered systems, in tick order.</summary>
        public IReadOnlyList<ISimSystem> Systems => _systems;

        /// <summary>Weeks simulated since the run began.</summary>
        public int WeeksElapsed => SimDate.WeeksBetween(StartDate, State.Date);

        /// <summary>Runs one week: every system in order, then the clock.</summary>
        public void AdvanceWeek()
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].Tick(State);
            }

            // The date advances last, so a system that logs during its tick stamps the week it
            // actually ran in rather than the one about to begin.
            State.Date = State.Date.AdvanceWeeks(1);
        }

        /// <summary>Runs <paramref name="weeks"/> consecutive weeks.</summary>
        /// <exception cref="ArgumentOutOfRangeException">If <paramref name="weeks"/> is negative.</exception>
        public void AdvanceWeeks(int weeks)
        {
            if (weeks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weeks), weeks, "Time does not run backwards.");
            }

            for (int i = 0; i < weeks; i++)
            {
                AdvanceWeek();
            }
        }

        /// <summary>Runs <paramref name="years"/> years, at <see cref="SimDate.WeeksPerYear"/> weeks each.</summary>
        public void AdvanceYears(int years)
        {
            AdvanceWeeks(years * SimDate.WeeksPerYear);
        }
    }
}
