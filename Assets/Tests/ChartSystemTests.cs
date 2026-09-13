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
        public void UndecayedStrength_WithZeroFandomAndZeroPromo_IsStillValidAndNonNegative()
        {
            GameState state = new GameState { ChartConfig = new ChartConfig() };
            Group group = new Group { Id = 1, Tier = GroupTier.Rookie, Fandom = new Fandom { Size = 0 } };
            state.AddGroup(group);

            Release release = new Release
            {
                Id = 1, GroupId = group.Id, ReleaseDate = SimDate.Start, PromoSpend = 0f,
                FandomPull = ChartSystem.ComputeFandomPull(0L, state.ChartConfig),
                PublicAppeal = ChartSystem.ComputePublicAppeal(40f, state.ChartConfig)
            };

            float strength = ChartSystem.ComputeUndecayedStrength(state, release, state.ChartConfig);

            Assert.That(strength, Is.GreaterThanOrEqualTo(0f));
            Assert.That(float.IsNaN(strength), Is.False);
            Assert.That(float.IsInfinity(strength), Is.False);
        }

        [Test]
        public void PublicAppealWeights_SummingToSomethingOtherThanOne_StillProducesAValidScore()
        {
            GameState state = new GameState { ChartConfig = new ChartConfig() };
            state.ChartConfig.PublicAppealQualityWeight = 5f;
            state.ChartConfig.PublicAppealConceptWeight = 3f;
            state.ChartConfig.PublicAppealTrendWeight = 2f; // sums to 10, nowhere near 1.0
            state.ChartConfig.WeightGroupTier = 4f;
            state.ChartConfig.WeightPromoSpend = 1f;

            Group group = new Group { Id = 1, Tier = GroupTier.Established, Fandom = new Fandom { Size = 500_000 } };
            state.AddGroup(group);

            Release release = new Release
            {
                Id = 1, GroupId = group.Id, ReleaseDate = SimDate.Start, PromoSpend = 10000f,
                FandomPull = ChartSystem.ComputeFandomPull(group.Fandom.Size, state.ChartConfig),
                PublicAppeal = ChartSystem.ComputePublicAppeal(65f, state.ChartConfig)
            };

            float strength = ChartSystem.ComputeUndecayedStrength(state, release, state.ChartConfig);

            Assert.That(strength, Is.GreaterThan(0f));
            Assert.That(float.IsNaN(strength), Is.False);
            Assert.That(float.IsInfinity(strength), Is.False);
        }

        [Test]
        public void BuildCurve_RisesFromFloorToOneOverPublicBuildWeeks_ThenHolds()
        {
            ChartConfig config = new ChartConfig();

            Assert.That(ChartSystem.BuildCurve(0, config), Is.EqualTo(config.PublicBuildFloor).Within(0.0001f));
            Assert.That(ChartSystem.BuildCurve(config.PublicBuildWeeks, config), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(ChartSystem.BuildCurve(config.PublicBuildWeeks + 50, config), Is.EqualTo(1f).Within(0.0001f));

            float previous = ChartSystem.BuildCurve(0, config);
            for (int week = 1; week <= config.PublicBuildWeeks; week++)
            {
                float current = ChartSystem.BuildCurve(week, config);
                Assert.That(current, Is.GreaterThanOrEqualTo(previous), "build curve should not fall while ramping");
                previous = current;
            }
        }

        [Test]
        public void RollCrossoverMultiplier_IsEitherExactlyOneOrWithinTheConfiguredRange()
        {
            ChartConfig config = new ChartConfig();
            SimRandom rng = new SimRandom(42UL);

            for (int i = 0; i < 500; i++)
            {
                float multiplier = ChartSystem.RollCrossoverMultiplier(rng, 70f, config);
                bool noCrossover = multiplier == 1f;
                bool inRange = multiplier >= config.CrossoverMultiplierMin && multiplier <= config.CrossoverMultiplierMax;

                Assert.That(noCrossover || inRange, Is.True, "multiplier " + multiplier + " at iteration " + i);
            }
        }

        [Test]
        public void FandomComponent_DecaysFasterThanPublicComponent()
        {
            // The whole point of the two-curve model: fandom buys the opening, public appeal buys
            // the tail. A release with only fandom pull should fall off a cliff; a release with
            // only public appeal should still be charting respectably at the same week.
            ChartConfig config = new ChartConfig();
            GameState state = new GameState { ChartConfig = config };
            Group group = new Group { Id = 1, Tier = GroupTier.Rookie };
            state.AddGroup(group);
            SimRandom rng = new SimRandom(1UL);

            Release fandomOnly = new Release { Id = 1, GroupId = group.Id, ReleaseDate = SimDate.Start, FandomPull = 80f, PublicAppeal = 0f };
            Release publicOnly = new Release { Id = 2, GroupId = group.Id, ReleaseDate = SimDate.Start, FandomPull = 0f, PublicAppeal = 80f };

            const int week = 20;
            float fandomPoints = ChartSystem.ComputeWeeklyPoints(state, fandomOnly, week, 1f, config, rng);
            float publicPoints = ChartSystem.ComputeWeeklyPoints(state, publicOnly, week, 1f, config, rng);

            Assert.That(publicPoints, Is.GreaterThan(fandomPoints), "at week " + week + ", public-appeal-only should outlast fandom-only");
        }

        [Test]
        public void FiftyYearRun_CompletesWithoutError_AndProducesReleases()
        {
            GameState state = BuildTickedState(2024UL, 52 * 50);
            Assert.That(state.Releases.Count, Is.GreaterThan(0));
        }

        // -----------------------------------------------------------------------------------
        // Phase 3b iteration 3 fix 1: share-based competition, scale-invariant in world size.
        // -----------------------------------------------------------------------------------

        [Test]
        public void CompetitionModifier_IsScaleInvariant_WhenExpectedRivalsScalesWithWorldSize()
        {
            // Pooled across 3 seeds per world size, not 1 — a single 10-year/520-week sample is
            // noisy enough on its own (measured ~17% apart on seed 777 alone, outside the 10%
            // band by chance) to make a single-seed comparison unreliable, the same reason
            // BalanceRunner's own sweep uses 5 seeds rather than judging a formula on one run.
            static double PooledMeanModifierAcrossTenYears(int worldGroupCount, int expectedRivals, ulong[] seeds)
            {
                List<double> modifiers = new List<double>();

                for (int s = 0; s < seeds.Length; s++)
                {
                    WorldData data = TestFixtures.BuildWorldData();
                    SimEngine engine = new SimEngine(seeds[s]);
                    engine.State.ChartConfig.WorldGroupCount = worldGroupCount;
                    engine.State.ChartConfig.CompetitionExpectedRivals = expectedRivals;
                    // IndustryChurnSystem targets TargetActiveWorldGroups independently of
                    // WorldGroupCount — without scaling it too, the "100-group" world would spend
                    // the whole 10 years growing toward the unscaled default (200), never actually
                    // testing a stable 100-group population.
                    engine.State.ChartConfig.TargetActiveWorldGroups = worldGroupCount;
                    WorldGenerator.Generate(engine.State, data);
                    engine.AdvanceYears(10);

                    for (int i = 0; i < engine.State.Releases.Count; i++)
                    {
                        List<float> mods = engine.State.Releases[i].WeeklyCompetitionModifiers;
                        for (int w = 0; w < mods.Count; w++) modifiers.Add(mods[w]);
                    }
                }

                Assert.That(modifiers.Count, Is.GreaterThan(0), "the runs produced no chart history, so this proves nothing");
                double sum = 0d;
                for (int i = 0; i < modifiers.Count; i++) sum += modifiers[i];
                return sum / modifiers.Count;
            }

            ulong[] seeds = { 101UL, 202UL, 303UL };
            double meanAt100 = PooledMeanModifierAcrossTenYears(100, 12, seeds);
            double meanAt200 = PooledMeanModifierAcrossTenYears(200, 24, seeds);

            double ratio = meanAt200 / meanAt100;
            Assert.That(ratio, Is.InRange(0.9, 1.1),
                "doubling world size with CompetitionExpectedRivals scaled proportionally should leave the mean " +
                "modifier within 10%: " + meanAt100.ToString("F4") + " (100 groups) vs " + meanAt200.ToString("F4") + " (200 groups)");
        }

        [Test]
        public void CompetitionModifier_DoesNotCollapseTowardZero_UnderAMeasuredRealisticCrowd()
        {
            // Iteration 2 measured ~60 rivals in a +/-2-week window at realistic release volume.
            // The old sum-based formula drove the modifier to ~0.01 under exactly this load; the
            // share-based replacement must not.
            ChartConfig config = new ChartConfig();
            SimDate date = new SimDate(5, 1);

            List<Release> active = new List<Release>();
            float[] rawStrength = new float[61];
            for (int i = 0; i < 60; i++)
            {
                active.Add(new Release { Id = i, ReleaseDate = date });
                rawStrength[i] = 40f;
            }
            active.Add(new Release { Id = 60, ReleaseDate = date });
            rawStrength[60] = 40f; // the release under test, comparable strength to its rivals

            float modifier = ChartSystem.CompetitionModifier(active, rawStrength, 60, config);

            Assert.That(modifier, Is.GreaterThanOrEqualTo(0.15f),
                "a comparable-strength release amid a realistic crowd should not collapse toward zero the way the old sum-based formula did");
        }

        [Test]
        public void CompetitionModifier_NoRivals_IsExactlyOne()
        {
            ChartConfig config = new ChartConfig();
            List<Release> alone = new List<Release> { new Release { Id = 0, ReleaseDate = SimDate.Start } };

            float modifier = ChartSystem.CompetitionModifier(alone, new[] { 40f }, 0, config);

            Assert.That(modifier, Is.EqualTo(1f));
        }

        // -----------------------------------------------------------------------------------
        // Phase 3b iteration 3 fix 2: retirement keys off chart presence, plus a hard backstop.
        // -----------------------------------------------------------------------------------

        [Test]
        public void Release_Retires_AfterConsecutiveWeeksOffChart()
        {
            ChartConfig config = new ChartConfig();
            GameState state = new GameState { ChartConfig = config, Random = new SimRandom(1UL), Date = SimDate.Start, Log = new SimLog() };
            Group group = new Group { Id = 1, Tier = GroupTier.Rookie, IsActive = true };
            state.AddGroup(group);

            // Zero components: never clears ChartFloorPoints, so every week is off-chart.
            Release release = new Release { Id = 1, GroupId = group.Id, ReleaseDate = state.Date, FandomPull = 0f, PublicAppeal = 0f };
            state.AddRelease(release);

            ChartSystem system = new ChartSystem();
            for (int week = 0; week < config.ChartRetirementWeeksOffChart; week++)
            {
                Assert.That(release.IsCharting, Is.True, "should still be simulated before the retirement threshold, week " + week);
                system.Tick(state);
                state.Date = state.Date.AdvanceWeeks(1);
            }

            Assert.That(release.IsCharting, Is.False, "should retire once WeeksOffChart reaches ChartRetirementWeeksOffChart");
            Assert.That(release.WeeksOffChart, Is.EqualTo(config.ChartRetirementWeeksOffChart));
        }

        [Test]
        public void Release_Retires_AtChartMaxSimulatedWeeks_EvenWhileStillCharting()
        {
            ChartConfig config = new ChartConfig();
            GameState state = new GameState { ChartConfig = config, Random = new SimRandom(1UL), Date = SimDate.Start, Log = new SimLog() };
            Group group = new Group { Id = 1, Tier = GroupTier.Rookie, IsActive = true };
            state.AddGroup(group);

            // High enough PublicAppeal that PublicDecayK's shallow decay keeps it above
            // ChartFloorPoints for the entire backstop window on its own (no rivals to compete
            // against), so the backstop — not the off-chart rule — is what actually retires it.
            Release release = new Release { Id = 1, GroupId = group.Id, ReleaseDate = state.Date, FandomPull = 0f, PublicAppeal = 2000f };
            state.AddRelease(release);

            ChartSystem system = new ChartSystem();
            for (int week = 0; week < config.ChartMaxSimulatedWeeks; week++)
            {
                system.Tick(state);
                state.Date = state.Date.AdvanceWeeks(1);
            }

            Assert.That(release.IsCharting, Is.False, "should hit the hard simulated-weeks backstop regardless of chart presence");
            Assert.That(release.WeeklyPositions.Count, Is.EqualTo(config.ChartMaxSimulatedWeeks));
        }

        // -----------------------------------------------------------------------------------
        // Phase 3b iteration 3 fix 4: re-entry margin, one-directional hysteresis.
        // -----------------------------------------------------------------------------------

        [Test]
        public void RequiresReentryMargin_TrueOnlyForAGenuineReentrant()
        {
            Release neverCharted = new Release();
            Assert.That(ChartSystem.RequiresReentryMargin(neverCharted), Is.False, "a first-ever week is not a re-entry");

            Release currentlyCharting = new Release();
            currentlyCharting.WeeklyPositions.Add(5);
            Assert.That(ChartSystem.RequiresReentryMargin(currentlyCharting), Is.False, "already charting needs no margin to stay put");

            Release reentering = new Release();
            reentering.WeeklyPositions.Add(5);
            reentering.WeeklyPositions.Add(0);
            Assert.That(ChartSystem.RequiresReentryMargin(reentering), Is.True, "fell off last week — a genuine re-entry attempt");
        }

        [Test]
        public void ComputeReentryCutoffPoints_IsTheChartSizeThRankedPointsValue()
        {
            ChartConfig config = new ChartConfig { ChartSize = 2, ChartFloorPoints = 0.5f };
            float[] points = { 10f, 5f, 3f, 1f };
            int[] order = { 0, 1, 2, 3 };

            float cutoff = ChartSystem.ComputeReentryCutoffPoints(points, order, config);

            Assert.That(cutoff, Is.EqualTo(5f), "with ChartSize=2, the cutoff is rank 2's points");
        }

        [Test]
        public void ComputeReentryCutoffPoints_FallsBackToTheLowestFloorClearingValue_WhenFewerThanChartSizeQualify()
        {
            ChartConfig config = new ChartConfig { ChartSize = 10, ChartFloorPoints = 0.5f };
            float[] points = { 10f, 5f, 0.2f }; // only the first two clear the floor
            int[] order = { 0, 1, 2 };

            float cutoff = ChartSystem.ComputeReentryCutoffPoints(points, order, config);

            Assert.That(cutoff, Is.EqualTo(5f));
        }
    }
}
