using System;

namespace KpopManager.Core.Systems.Chart
{
    // The shape a release's chart points follow over time, independent of everything else in
    // ChartSystem. A pure function — no state, easy to unit test, easy to plot.
    //
    // DESIGN: a pure exponential is almost certainly the wrong final shape. Real songs sometimes
    // climb for 2-3 weeks before peaking as word of mouth builds — an exponential can only ever
    // fall from week 0. A gamma-shaped curve (rise then fall) is the likely replacement once
    // balancing gets here. The interface — weeksSinceRelease, quality, config in, one multiplier
    // out — is deliberately stable so that swap is a one-file change with no ripple into
    // ChartSystem.
    public static class DecayCurve
    {
        // Returns the decay multiplier for a release at weeksSinceRelease, given its quality
        // (0-100, higher decays more slowly). 1.0 at week 0, strictly decreasing thereafter,
        // asymptoting toward 0. In real numbers it never reaches 0; in float it legitimately
        // underflows to exactly 0.0 once k * weeksSinceRelease gets large enough (roughly 200+
        // weeks at low quality) — harmless, since ChartSystem retires a release from the chart
        // floor long before its lifetime could reach that.
        public static float Evaluate(int weeksSinceRelease, float quality, ChartConfig config)
        {
            float clampedQuality = quality < 0f ? 0f : (quality > 100f ? 100f : quality);
            float weeks = weeksSinceRelease < 0 ? 0 : weeksSinceRelease;

            float k = KFor(clampedQuality, config);
            return (float)Math.Exp(-k * weeks);
        }

        // The decay constant for a given quality: DecayKMax - (quality/100) * (DecayKMax -
        // DecayKMin). High quality -> low k -> slow decay -> longevity. Exposed separately so
        // tests and the balance tooling can plot it without re-deriving it from Evaluate.
        public static float KFor(float quality, ChartConfig config)
        {
            float clampedQuality = quality < 0f ? 0f : (quality > 100f ? 100f : quality);
            return config.DecayKMax - (clampedQuality / 100f) * (config.DecayKMax - config.DecayKMin);
        }
    }
}
