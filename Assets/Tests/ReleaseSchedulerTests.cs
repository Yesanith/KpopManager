using KpopManager.Core;
using KpopManager.Core.Systems.Chart;
using NUnit.Framework;

namespace KpopManager.Tests
{
    [TestFixture]
    public class ReleaseSchedulerTests
    {
        [Test]
        public void PromoSpend_ScalesMultiplicativelyWithBothTiers()
        {
            ChartConfig config = new ChartConfig();
            SimRandom rng = new SimRandom(1UL);

            ProductionCenter lowCenter = new ProductionCenter { Tier = CenterTier.Junior };
            ProductionCenter highCenter = new ProductionCenter { Tier = CenterTier.Flagship };

            Group rookie = new Group { Tier = GroupTier.Rookie };
            Group legendary = new Group { Tier = GroupTier.Legendary };

            // No jitter to compare cleanly: jitter is +-25%, so average many draws instead of
            // asserting on a single noisy sample.
            double lowLowTotal = 0, highHighTotal = 0;
            const int n = 200;
            for (int i = 0; i < n; i++)
            {
                lowLowTotal += ReleaseScheduler.ComputePromoSpend(rng, lowCenter, rookie, config);
                highHighTotal += ReleaseScheduler.ComputePromoSpend(rng, highCenter, legendary, config);
            }

            double lowLowMean = lowLowTotal / n;
            double highHighMean = highHighTotal / n;

            // Expected ratio ignoring jitter: CenterMult^(2) * GroupMult^(4) (Flagship ordinal 2 vs
            // Junior ordinal 0; Legendary ordinal 4 vs Rookie ordinal 0).
            double expectedRatio = System.Math.Pow(config.PromoSpendCenterTierMult, 2) * System.Math.Pow(config.PromoSpendGroupTierMult, 4);
            double actualRatio = highHighMean / lowLowMean;

            Assert.That(actualRatio, Is.EqualTo(expectedRatio).Within(expectedRatio * 0.05),
                "a Legendary group at a Flagship center should outspend a Rookie at a Junior center by roughly " + expectedRatio.ToString("F1") + "x");
            Assert.That(actualRatio, Is.GreaterThan(5d), "the whole point of fix 2d was an order-of-magnitude spread, not 3x");
        }

        [Test]
        public void PromoSpend_IsNeverNegative()
        {
            ChartConfig config = new ChartConfig();
            SimRandom rng = new SimRandom(2UL);
            ProductionCenter center = new ProductionCenter { Tier = CenterTier.Junior };
            Group group = new Group { Tier = GroupTier.Rookie };

            for (int i = 0; i < 1000; i++)
            {
                Assert.That(ReleaseScheduler.ComputePromoSpend(rng, center, group, config), Is.GreaterThanOrEqualTo(0f));
            }
        }
    }
}
