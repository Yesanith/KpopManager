using System;
using System.Collections.Generic;
using KpopManager.Core.Systems;

namespace KpopManager.Core
{
    // Owns a GameState and drives it forward one week at a time.
    //
    // A tick runs every registered ISimSystem in order and then advances GameState.Date. Systems
    // therefore all observe the same date during a tick, which is what makes "this happened in
    // Y3 W17" unambiguous.
    //
    // The engine itself is not part of the save. It is rebuilt around a restored GameState, which
    // is why no simulation data may live on it.
    public sealed class SimEngine
    {
        private readonly List<ISimSystem> _systems;

        // Builds a new run from a seed, with the standard tick order registered.
        public SimEngine(ulong seed)
            : this(seed, TickOrder.BuildDefault())
        {
        }

        // Builds a new run from a seed with an explicit system list. Used by tests that want to
        // isolate one system; normal play always uses TickOrder.BuildDefault.
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

        // The world. Read it, render it, never mutate it from outside the sim.
        public GameState State { get; }

        // The date this run began, kept for elapsed-time readouts. Not part of the save.
        public SimDate StartDate { get; }

        public IReadOnlyList<ISimSystem> Systems => _systems;

        public int WeeksElapsed => SimDate.WeeksBetween(StartDate, State.Date);

        // Every system in order, then the clock.
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

        // Throws ArgumentOutOfRangeException if weeks is negative.
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

        public void AdvanceYears(int years)
        {
            AdvanceWeeks(years * SimDate.WeeksPerYear);
        }
    }
}
