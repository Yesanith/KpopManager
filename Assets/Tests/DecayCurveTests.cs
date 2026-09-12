using KpopManager.Core.Systems.Chart;
using NUnit.Framework;

namespace KpopManager.Tests
{
    [TestFixture]
    public class DecayCurveTests
    {
        [Test]
        public void Evaluate_IsExactlyOneAtWeekZero()
        {
            ChartConfig config = new ChartConfig();
            Assert.That(DecayCurve.Evaluate(0, 50f, config), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void Evaluate_IsMonotonicallyDecreasing()
        {
            ChartConfig config = new ChartConfig();
            float previous = DecayCurve.Evaluate(0, 60f, config);

            for (int week = 1; week <= 52; week++)
            {
                float current = DecayCurve.Evaluate(week, 60f, config);
                Assert.That(current, Is.LessThan(previous), "not strictly decreasing at week " + week);
                previous = current;
            }
        }

        [Test]
        public void HigherQuality_DecaysStrictlySlowerAtEveryWeek()
        {
            ChartConfig config = new ChartConfig();

            for (int week = 1; week <= 52; week++)
            {
                float low = DecayCurve.Evaluate(week, 10f, config);
                float high = DecayCurve.Evaluate(week, 90f, config);
                Assert.That(high, Is.GreaterThan(low), "quality 90 should decay slower than quality 10 at week " + week);
            }
        }

        [Test]
        public void StaysPositive_OverAnyRealisticChartingLifetime()
        {
            // In real numbers exp(-k*weeks) never reaches zero. In `float`, it legitimately
            // underflows to exactly 0.0 once k*weeks gets large enough (e.g. quality 0, k=DecayKMax,
            // around week 200+) — that's correct IEEE-754 behaviour, not a bug, and it never
            // matters in practice: ChartSystem retires a release from the chart floor long before
            // its points could imply a week count anywhere near that. 100 weeks (two years still
            // "charting" at zero quality) is already far beyond any realistic lifetime and stays
            // comfortably inside float's non-underflowing range for every configured quality.
            ChartConfig config = new ChartConfig();

            for (int week = 0; week <= 100; week += 5)
            {
                Assert.That(DecayCurve.Evaluate(week, 0f, config), Is.GreaterThan(0f), "week " + week);
                Assert.That(DecayCurve.Evaluate(week, 100f, config), Is.GreaterThan(0f), "week " + week);
            }
        }

        [Test]
        public void KFor_MatchesTheDocumentedFormula()
        {
            ChartConfig config = new ChartConfig();
            float k = DecayCurve.KFor(70f, config);
            float expected = config.DecayKMax - (70f / 100f) * (config.DecayKMax - config.DecayKMin);

            Assert.That(k, Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void KFor_ClampsQualityOutsideZeroToHundred()
        {
            ChartConfig config = new ChartConfig();
            Assert.That(DecayCurve.KFor(-50f, config), Is.EqualTo(DecayCurve.KFor(0f, config)));
            Assert.That(DecayCurve.KFor(500f, config), Is.EqualTo(DecayCurve.KFor(100f, config)));
        }
    }
}
