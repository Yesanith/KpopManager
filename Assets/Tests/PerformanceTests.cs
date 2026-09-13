using System.Diagnostics;
using KpopManager.Core;
using NUnit.Framework;

namespace KpopManager.Tests
{
    // Guards the tick budget. Balancing means running 50 years headless and reading the CSV — if
    // a tick gets slow enough that a 50-year run stops being instant, the balance loop stops
    // being usable and the game stops getting balanced. Catching that at the moment a system
    // gets expensive is much cheaper than finding it in Phase 10.
    [TestFixture]
    public class PerformanceTests
    {
        [Test]
        public void TenYears_CompletesUnderOneSecond()
        {
            const int weeks = 10 * SimDate.WeeksPerYear;

            // Warm up so the measurement is not dominated by JIT on the first tick.
            new SimEngine(1UL).AdvanceWeeks(SimDate.WeeksPerYear);

            SimEngine engine = new SimEngine(20250911UL);

            Stopwatch stopwatch = Stopwatch.StartNew();
            engine.AdvanceWeeks(weeks);
            stopwatch.Stop();

            Assert.That(engine.WeeksElapsed, Is.EqualTo(weeks));
            Assert.That(stopwatch.Elapsed.TotalMilliseconds, Is.LessThan(1000d),
                "520 ticks took " + stopwatch.Elapsed.TotalMilliseconds.ToString("F1") + " ms.");
        }

        [Test]
        public void FiftyYears_ProducesAtLeastOneEntryPerWeek()
        {
            SimEngine engine = new SimEngine(1UL);
            engine.AdvanceYears(50);

            // WeekCounterSystem alone guarantees one line per week (2600 for 50 years); Phase 3a's
            // ReleaseScheduler and TierSystem now add more on top of that whenever a group
            // releases or changes tier, so the count is a floor, not an exact figure any more.
            Assert.That(engine.State.Log.Count, Is.GreaterThanOrEqualTo(50 * SimDate.WeeksPerYear));
            Assert.That(engine.State.Date, Is.EqualTo(new SimDate(51, 1)));
        }

        [Test]
        public void FiftyYearRun_WithAFullWorldAndChartSimulation_CompletesUnderTwentySeconds()
        {
            // Phase 3b iteration 2's two-curve trajectory model deliberately keeps far more
            // releases actively charting for years (PublicDecayK's ~35-week half-life is what
            // produces Melon-style multi-year long-runners) — measured ~12s for this exact run,
            // versus roughly 1s under iteration 1's single-curve model, because CompetitionModifier
            // is O(active releases^2) per week and "active releases" no longer shrinks to a
            // handful within a few dozen weeks. This is the intended effect of the fix, not a
            // regression: not preemptively optimized per the iteration 2 brief's own performance
            // note (report timing; only optimize if a run gets excessive). 20s keeps real margin
            // over the measured figure without hiding a genuine future blowup.
            WorldData data = TestFixtures.BuildWorldData();

            // Warm up so the measurement isn't dominated by JIT.
            SimEngine warm = new SimEngine(1UL);
            KpopManager.Core.Systems.Generation.WorldGenerator.Generate(warm.State, data);
            warm.AdvanceYears(1);

            SimEngine engine = new SimEngine(20250911UL);
            KpopManager.Core.Systems.Generation.WorldGenerator.Generate(engine.State, data);

            Stopwatch stopwatch = Stopwatch.StartNew();
            engine.AdvanceYears(50);
            stopwatch.Stop();

            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(20d),
                "50-year run with chart simulation took " + stopwatch.Elapsed.TotalSeconds.ToString("F2") + " s.");
        }
    }
}
