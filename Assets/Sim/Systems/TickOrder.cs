using System.Collections.Generic;
using KpopManager.Core.Systems.Chart;
using KpopManager.Core.Systems.Fandom;

namespace KpopManager.Core.Systems
{
    /// <summary>
    /// The twelve-step weekly tick order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the spine of the project.</b> It is deliberately the only place the order is
    /// written down. Changing it once systems depend on each other's side effects is painful, so
    /// every step is registered from Phase 1 — as a do-nothing stub where the behaviour does not
    /// exist yet — to lock the sequence in before anything can come to rely on the wrong one.
    /// </para>
    /// <para>
    /// The order matches <c>Docs/ARCHITECTURE.md</c>. If one changes, change the other in the same
    /// session.
    /// </para>
    /// <para>
    /// The order also fixes the sequence of draws from <see cref="GameState.Random"/>, which is
    /// half of what makes a run reproducible. Reordering steps changes every future random number,
    /// so an existing save will not replay after a reorder.
    /// </para>
    /// </remarks>
    public static class TickOrder
    {
        /// <summary>
        /// Builds the standard system list, in tick order. Every new <see cref="SimEngine"/> gets
        /// one of these.
        /// </summary>
        public static List<ISimSystem> BuildDefault()
        {
            return new List<ISimSystem>
            {
                // Phase 1 diagnostic. Not one of the twelve steps — it exists so the harness has
                // something to print while the real systems are stubs. Delete it once Phase 2
                // entities give the log real content.
                new WeekCounterSystem(),

                //             name, the phase that replaces it                     step
                new StubSystem("Training & aging", 5),                     //  1
                new StubSystem("Scheduled activities", 4),                 //  2
                new ReleaseScheduler(),                                    //  3  (Phase 3a)
                new ChartSystem(),                                         //  4  (Phase 3a)
                new StubSystem("Music show results", 4),                   //  5
                new FandomSystem(),                                        //  6  (Phase 3a, stand-in — see FandomSystem's remarks)
                new StubSystem("Fatigue / health / morale", 4),            //  7
                new StubSystem("Random events", 8),                        //  8
                new StubSystem("Rival AI turns", 3),                       //  9
                new StubSystem("News generation", 8),                      // 10
                new StubSystem("Cash settlement", 6),                      // 11
                new StubSystem("Board target check", 6),                   // 12
            };
        }
    }
}
