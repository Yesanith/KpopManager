using System.Diagnostics;
using KpopManager.Core;
using NUnit.Framework;

namespace KpopManager.Tests
{
    /// <summary>
    /// Guards the tick budget.
    /// </summary>
    /// <remarks>
    /// Balancing means running 50 years headless and reading the CSV. If a tick gets slow enough
    /// that a 50-year run stops being instant, the balance loop stops being usable and the game
    /// stops getting balanced. Catching that at the moment a system gets expensive is much cheaper
    /// than finding it in Phase 10.
    /// </remarks>
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
        public void FiftyYears_ProducesTheExpectedNumberOfEntries()
        {
            SimEngine engine = new SimEngine(1UL);
            engine.AdvanceYears(50);

            // One line per week from WeekCounterSystem, and nothing else yet. When Phase 2 adds
            // real output this expectation changes; that is the point at which the week counter
            // should be deleted.
            Assert.That(engine.State.Log.Count, Is.EqualTo(50 * SimDate.WeeksPerYear));
            Assert.That(engine.State.Date, Is.EqualTo(new SimDate(51, 1)));
        }
    }
}
