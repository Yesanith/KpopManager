using System.Collections.Generic;
using KpopManager.Core;
using NUnit.Framework;

namespace KpopManager.Tests
{
    // The most important tests in the project. Determinism is the property everything else rests
    // on: balancing means comparing two runs, and save/load means resuming one. A single stray
    // DateTime.Now, System.Random, or dictionary-ordered iteration breaks it permanently and
    // silently. Run these every session.
    [TestFixture]
    public class DeterminismTests
    {
        private const int TickCount = 1000;

        private static List<string> RunAndSerialize(ulong seed, int ticks)
        {
            SimEngine engine = new SimEngine(seed);
            engine.AdvanceWeeks(ticks);

            IReadOnlyList<SimLogEntry> entries = engine.State.Log.Entries;
            List<string> serialized = new List<string>(entries.Count);

            for (int i = 0; i < entries.Count; i++)
            {
                serialized.Add(entries[i].ToStableString());
            }

            return serialized;
        }

        [Test]
        public void SameSeed_ProducesIdenticalLog()
        {
            List<string> a = RunAndSerialize(20250911UL, TickCount);
            List<string> b = RunAndSerialize(20250911UL, TickCount);

            Assert.That(a.Count, Is.GreaterThan(0), "The run produced no log output, so this test proves nothing.");
            Assert.That(b.Count, Is.EqualTo(a.Count), "Two runs of the same seed produced different numbers of entries.");

            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b[i], Is.EqualTo(a[i]), "Divergence at log entry " + i + ".");
            }
        }

        [Test]
        public void SameSeed_LeavesRandomInIdenticalState()
        {
            SimEngine a = new SimEngine(777UL);
            SimEngine b = new SimEngine(777UL);

            a.AdvanceWeeks(TickCount);
            b.AdvanceWeeks(TickCount);

            // Matching logs would not catch a system that drew a random number without logging
            // anything. The generator state would.
            Assert.That(b.State.Random.State, Is.EqualTo(a.State.Random.State));
            Assert.That(b.State.Random.Inc, Is.EqualTo(a.State.Random.Inc));
            Assert.That(b.State.Date, Is.EqualTo(a.State.Date));
            Assert.That(b.State.NextEntityId, Is.EqualTo(a.State.NextEntityId));
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentRandomStreams()
        {
            // Guards the seeding path. If a bug made the seed a no-op, the determinism test above
            // would still pass while the game silently played the same run every time.
            SimRandom a = new SimRandom(1UL);
            SimRandom b = new SimRandom(2UL);

            bool anyDifference = false;
            for (int i = 0; i < 64; i++)
            {
                if (a.NextUInt() != b.NextUInt())
                {
                    anyDifference = true;
                    break;
                }
            }

            Assert.That(anyDifference, Is.True, "Two different seeds produced the same first 64 draws.");
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentEngineState()
        {
            SimEngine a = new SimEngine(1UL);
            SimEngine b = new SimEngine(2UL);

            a.AdvanceWeeks(TickCount);
            b.AdvanceWeeks(TickCount);

            Assert.That(b.State.Random.State, Is.Not.EqualTo(a.State.Random.State),
                "Two different seeds left the generator in the same state after " + TickCount + " ticks.");
        }

        [Test]
        public void Tick_AdvancesDateByExactlyOneWeek()
        {
            SimEngine engine = new SimEngine(5UL);

            Assert.That(engine.State.Date, Is.EqualTo(SimDate.Start));

            engine.AdvanceWeek();

            Assert.That(engine.State.Date, Is.EqualTo(new SimDate(1, 2)));
            Assert.That(engine.WeeksElapsed, Is.EqualTo(1));
        }

        [Test]
        public void AdvanceYears_AdvancesFiftyTwoWeeksPerYear()
        {
            SimEngine engine = new SimEngine(5UL);
            engine.AdvanceYears(3);

            Assert.That(engine.WeeksElapsed, Is.EqualTo(156));
            Assert.That(engine.State.Date, Is.EqualTo(new SimDate(4, 1)));
        }

        [Test]
        public void TickOrder_RegistersAllTwelveSteps()
        {
            SimEngine engine = new SimEngine(1UL);

            // Twelve design steps plus the Phase 1 week counter. When the counter is deleted in
            // Phase 2, this expectation drops to 12.
            Assert.That(engine.Systems.Count, Is.EqualTo(13),
                "The tick order no longer matches the twelve steps in ARCHITECTURE.md.");

            List<string> expected = new List<string>
            {
                "Week counter (Phase 1 diagnostic)",
                "Training & aging",
                "Scheduled activities",
                "Track quality decay / new releases",
                "Chart simulation",
                "Music show results",
                "Fandom update",
                "Fatigue / health / morale",
                "Random events",
                "Rival AI turns",
                "News generation",
                "Cash settlement",
                "Board target check"
            };

            for (int i = 0; i < expected.Count; i++)
            {
                Assert.That(engine.Systems[i].Name, Is.EqualTo(expected[i]), "Tick step " + i + " is out of order.");
            }
        }
    }
}
