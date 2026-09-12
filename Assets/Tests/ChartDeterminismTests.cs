using System.Collections.Generic;
using KpopManager.Core;
using KpopManager.Core.Systems.Generation;
using Newtonsoft.Json;
using NUnit.Framework;

namespace KpopManager.Tests
{
    /// <summary>Phase 3a's extension of Phase 1's determinism guarantee: with a full chart
    /// simulation now running every week, a 50-year history must still be byte-identical for the
    /// same seed.</summary>
    [TestFixture]
    public class ChartDeterminismTests
    {
        private static GameState RunFiftyYears(ulong seed)
        {
            WorldData data = TestFixtures.BuildWorldData();
            SimEngine engine = new SimEngine(seed);
            WorldGenerator.Generate(engine.State, data);
            engine.AdvanceYears(50);
            return engine.State;
        }

        [Test]
        public void SameSeed_FiftyYears_ProducesIdenticalChartHistories()
        {
            GameState a = RunFiftyYears(42UL);
            GameState b = RunFiftyYears(42UL);

            Assert.That(a.Releases.Count, Is.GreaterThan(0), "the run produced no releases, so this test proves nothing");

            string snapshotA = SnapshotReleases(a);
            string snapshotB = SnapshotReleases(b);

            Assert.That(snapshotB, Is.EqualTo(snapshotA));
        }

        [Test]
        public void SameSeed_FiftyYears_ProducesIdenticalLog()
        {
            GameState a = RunFiftyYears(99UL);
            GameState b = RunFiftyYears(99UL);

            List<string> logA = StableLog(a);
            List<string> logB = StableLog(b);

            Assert.That(logA.Count, Is.GreaterThan(0));
            Assert.That(logB, Is.EqualTo(logA));
        }

        [Test]
        public void DifferentSeed_FiftyYears_ProducesADifferentChartHistory()
        {
            GameState a = RunFiftyYears(1UL);
            GameState b = RunFiftyYears(2UL);

            Assert.That(SnapshotReleases(a), Is.Not.EqualTo(SnapshotReleases(b)));
        }

        private static string SnapshotReleases(GameState state)
        {
            return JsonConvert.SerializeObject(state.Releases) + JsonConvert.SerializeObject(state.Tracks);
        }

        private static List<string> StableLog(GameState state)
        {
            List<string> result = new List<string>(state.Log.Entries.Count);
            foreach (SimLogEntry entry in state.Log.Entries)
            {
                result.Add(entry.ToStableString());
            }
            return result;
        }
    }
}
