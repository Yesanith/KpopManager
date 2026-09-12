using System.Collections.Generic;
using KpopManager.Core;
using KpopManager.Core.Systems.Chart;
using KpopManager.Core.Systems.Generation;
using NUnit.Framework;

namespace KpopManager.Tests
{
    [TestFixture]
    public class ChartSystemTests
    {
        private static GameState BuildTickedState(ulong seed, int weeks)
        {
            WorldData data = TestFixtures.BuildWorldData();
            SimEngine engine = new SimEngine(seed);
            WorldGenerator.Generate(engine.State, data);
            engine.AdvanceWeeks(weeks);
            return engine.State;
        }

        [Test]
        public void Chart_NeverHasDuplicateOrSkippedPositions_AndNeverExceedsChartSize()
        {
            GameState state = BuildTickedState(1UL, 200);
            ChartConfig config = state.ChartConfig;

            // Reconstruct, per calendar week, every release's charted position that week — history
            // is stored per-release indexed by weeks-since-release, not per calendar week, so this
            // rebuilds the actual weekly chart the way a player would see it.
            Dictionary<int, List<int>> positionsByCalendarWeek = new Dictionary<int, List<int>>();

            for (int i = 0; i < state.Releases.Count; i++)
            {
                Release release = state.Releases[i];
                for (int w = 0; w < release.WeeklyPositions.Count; w++)
                {
                    int position = release.WeeklyPositions[w];
                    if (position <= 0) continue;

                    int key = release.ReleaseDate.AdvanceWeeks(w).TotalWeeks;
                    if (!positionsByCalendarWeek.TryGetValue(key, out List<int> list))
                    {
                        list = new List<int>();
                        positionsByCalendarWeek[key] = list;
                    }
                    list.Add(position);
                }
            }

            Assert.That(positionsByCalendarWeek.Count, Is.GreaterThan(0), "expected at least some charted weeks across a 200-week run");

            foreach (KeyValuePair<int, List<int>> entry in positionsByCalendarWeek)
            {
                List<int> positions = entry.Value;
                positions.Sort();

                for (int i = 1; i < positions.Count; i++)
                {
                    Assert.That(positions[i], Is.Not.EqualTo(positions[i - 1]), "duplicate chart position, calendar week " + entry.Key);
                }

                for (int i = 0; i < positions.Count; i++)
                {
                    Assert.That(positions[i], Is.EqualTo(i + 1), "chart position sequence has a gap, calendar week " + entry.Key);
                }

                Assert.That(positions.Count, Is.LessThanOrEqualTo(config.ChartSize));
            }
        }

        [Test]
        public void CompetitionModifier_IsAlwaysInZeroToOneRange()
        {
            ChartConfig config = new ChartConfig();
            SimDate baseDate = new SimDate(5, 1);

            List<Release> releases = new List<Release>();
            for (int i = 0; i < 15; i++)
            {
                releases.Add(new Release { Id = i, GroupId = i, ReleaseDate = baseDate.AdvanceWeeks(i % 3) });
            }

            float[] rawBuzz = new float[releases.Count];
            for (int i = 0; i < rawBuzz.Length; i++) rawBuzz[i] = 20f + i * 7.3f;

            for (int i = 0; i < releases.Count; i++)
            {
                float modifier = ChartSystem.CompetitionModifier(releases, rawBuzz, i, config);
                Assert.That(modifier, Is.GreaterThan(0f), "release " + i);
                Assert.That(modifier, Is.LessThanOrEqualTo(1f), "release " + i);
            }
        }

        [Test]
        public void CompetitionModifier_WeakensWithMoreColliderReleases()
        {
            ChartConfig config = new ChartConfig();
            SimDate date = new SimDate(5, 1);

            // A release with no collisions at all.
            List<Release> alone = new List<Release> { new Release { Id = 0, ReleaseDate = date } };
            float aloneModifier = ChartSystem.CompetitionModifier(alone, new[] { 40f }, 0, config);

            // The same release, now with four rivals releasing the same week.
            List<Release> crowded = new List<Release>
            {
                new Release { Id = 0, ReleaseDate = date },
                new Release { Id = 1, ReleaseDate = date },
                new Release { Id = 2, ReleaseDate = date },
                new Release { Id = 3, ReleaseDate = date },
                new Release { Id = 4, ReleaseDate = date },
            };
            float[] crowdedBuzz = { 40f, 40f, 40f, 40f, 40f };
            float crowdedModifier = ChartSystem.CompetitionModifier(crowded, crowdedBuzz, 0, config);

            Assert.That(crowdedModifier, Is.LessThan(aloneModifier));
        }

        [Test]
        public void RawBuzz_WithZeroFandomAndZeroPromo_IsStillValidAndNonNegative()
        {
            GameState state = new GameState { ChartConfig = new ChartConfig() };
            Group group = new Group { Id = 1, Tier = GroupTier.Rookie, Fandom = new Fandom { Size = 0 } };
            state.AddGroup(group);

            Track track = new Track { Id = 1, Quality = 40f };
            state.AddTrack(track);

            Release release = new Release { Id = 1, GroupId = group.Id, TitleTrackId = track.Id, ReleaseDate = SimDate.Start, PromoSpend = 0f };

            float buzz = ChartSystem.ComputeRawBuzz(state, release, 0, state.ChartConfig);

            Assert.That(buzz, Is.GreaterThanOrEqualTo(0f));
            Assert.That(float.IsNaN(buzz), Is.False);
            Assert.That(float.IsInfinity(buzz), Is.False);
        }

        [Test]
        public void BuzzScoreWeights_SummingToSomethingOtherThanOne_StillProducesAValidScore()
        {
            GameState state = new GameState { ChartConfig = new ChartConfig() };
            state.ChartConfig.WeightTrackQuality = 5f;
            state.ChartConfig.WeightConceptFit = 3f;
            state.ChartConfig.WeightTrendFit = 2f;
            state.ChartConfig.WeightGroupTier = 4f;
            state.ChartConfig.WeightPromoSpend = 1f;
            state.ChartConfig.WeightFandom = 6f; // sums to 21, nowhere near 1.0

            Group group = new Group { Id = 1, Tier = GroupTier.Established, Fandom = new Fandom { Size = 500_000 } };
            state.AddGroup(group);
            Track track = new Track { Id = 1, Quality = 65f };
            state.AddTrack(track);
            Release release = new Release { Id = 1, GroupId = group.Id, TitleTrackId = track.Id, ReleaseDate = SimDate.Start, PromoSpend = 10000f };

            float buzz = ChartSystem.ComputeRawBuzz(state, release, 0, state.ChartConfig);

            Assert.That(buzz, Is.GreaterThan(0f));
            Assert.That(float.IsNaN(buzz), Is.False);
            Assert.That(float.IsInfinity(buzz), Is.False);
        }

        [Test]
        public void FiftyYearRun_CompletesWithoutError_AndProducesReleases()
        {
            GameState state = BuildTickedState(2024UL, 52 * 50);
            Assert.That(state.Releases.Count, Is.GreaterThan(0));
        }
    }
}
