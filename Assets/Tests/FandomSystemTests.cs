using KpopManager.Core;
using KpopManager.Core.Systems.Chart;
using KpopManager.Core.Systems.Fandom;
using NUnit.Framework;

namespace KpopManager.Tests
{
    [TestFixture]
    public class FandomSystemTests
    {
        private static GameState BuildStateWithOneChartingGroup(long fandomSize, float pointsThisWeek)
        {
            GameState state = new GameState { ChartConfig = new ChartConfig(), Log = new SimLog(), Date = new SimDate(10, 1) };

            Group group = new Group { Id = 1, Name = "G", IsActive = true, Fandom = new Fandom { Size = fandomSize } };
            state.AddGroup(group);

            Track track = new Track { Id = 1, Quality = 50f };
            state.AddTrack(track);

            Release release = new Release { Id = 1, GroupId = group.Id, TitleTrackId = track.Id, ReleaseDate = state.Date, IsCharting = true };
            release.WeeklyPositions.Add(1);
            release.WeeklyPoints.Add(pointsThisWeek);
            state.AddRelease(release);
            group.ReleaseIds.Add(release.Id);

            return state;
        }

        [Test]
        public void Growth_IsLargerInAbsoluteTermsForABiggerExistingFandom()
        {
            // Phase 3b fix 1: this is the whole point of the fix — additive growth made every
            // group gain the same absolute fans regardless of history, which is why fandoms
            // converged instead of compounding.
            GameState small = BuildStateWithOneChartingGroup(10_000, 40f);
            GameState big = BuildStateWithOneChartingGroup(500_000, 40f);

            long beforeSmall = small.Groups[0].Fandom.Size;
            long beforeBig = big.Groups[0].Fandom.Size;

            new FandomSystem().Tick(small);
            new FandomSystem().Tick(big);

            long growthSmall = small.Groups[0].Fandom.Size - beforeSmall;
            long growthBig = big.Groups[0].Fandom.Size - beforeBig;

            Assert.That(growthBig, Is.GreaterThan(growthSmall),
                "a 500k-fan group should gain more absolute fans than a 10k-fan one for the same chart points");
        }

        [Test]
        public void Growth_LetsAZeroFandomGroupGrowFromNothing()
        {
            GameState state = BuildStateWithOneChartingGroup(0, 30f);
            new FandomSystem().Tick(state);

            Assert.That(state.Groups[0].Fandom.Size, Is.GreaterThan(0),
                "the bootstrap term should let a brand-new group grow even at zero starting fandom");
        }

        [Test]
        public void Decay_HasNoFloor_AndCanShrinkTowardZero()
        {
            GameState state = new GameState { ChartConfig = new ChartConfig(), Log = new SimLog(), Date = SimDate.Start };
            Group group = new Group { Id = 1, Name = "G", IsActive = true, Fandom = new Fandom { Size = 1000 } };
            state.AddGroup(group);

            // No charting release at all -> inactive every week. Retention is 0.998/week (a ~6.6
            // year half-life), so this needs a genuinely long stretch of inactivity, not a handful
            // of weeks, to show a real reduction.
            int weeks = state.ChartConfig.FandomInactivityGraceWeeks + 1000;
            for (int i = 0; i < weeks; i++)
            {
                state.Date = SimDate.Start.AdvanceWeeks(i);
                new FandomSystem().Tick(state);
            }

            Assert.That(group.Fandom.Size, Is.LessThan(500),
                "a group inactive for " + weeks + " weeks should have shrunk by at least half, not levelled off at a floor");
        }
    }
}
