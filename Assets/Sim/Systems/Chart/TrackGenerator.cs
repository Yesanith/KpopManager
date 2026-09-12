using System;

namespace KpopManager.Core.Systems.Chart
{
    /// <summary>Builds one <see cref="Track"/>: a quality roll plus a generated title. The caller
    /// assigns <see cref="Track.Id"/> after generation, matching <c>PersonGenerator</c>'s pattern.</summary>
    public static class TrackGenerator
    {
        /// <summary>Generates a track with default config and no title word bank (falls back to a
        /// synthetic title). This is the literal Phase 3a signature; <see cref="ReleaseScheduler"/>
        /// uses the fuller overload below so titles and tuning actually come from the real config.</summary>
        public static Track Generate(SimRandom rng, int centerTier, int composerSkill)
        {
            return Generate(rng, centerTier, composerSkill, new ChartConfig(), null);
        }

        /// <summary>Generates a track using real config and content.</summary>
        /// <param name="centerTier">0–4, mirroring <see cref="GroupTier"/>'s ordinal.</param>
        /// <param name="composerSkill">0–100. The composing person's skill, or a flat baseline for
        /// an external songwriter (<see cref="Track.ComposerId"/> stays <see cref="Person.NoEntity"/>).</param>
        public static Track Generate(SimRandom rng, int centerTier, int composerSkill, ChartConfig config, WorldData worldData)
        {
            int clampedTier = centerTier < 0 ? 0 : (centerTier > 4 ? 4 : centerTier);
            float clampedSkill = composerSkill < 0 ? 0 : (composerSkill > 100 ? 100 : composerSkill);

            float mean = config.TrackQualityMeanBase
                + config.TrackQualityMeanPerTier * clampedTier
                + config.TrackQualityComposerWeight * clampedSkill;

            float quality = rng.NextGaussian(mean, config.TrackQualityStdDev);
            quality = quality < 0f ? 0f : (quality > 100f ? 100f : quality);

            Track track = new Track
            {
                Title = GenerateTitle(rng, worldData),
                Genre = (Genre)rng.NextInt(0, Enum.GetValues(typeof(Genre)).Length),
                Quality = quality,
                IsTitleTrack = true
            };

            return track;
        }

        private static string GenerateTitle(SimRandom rng, WorldData worldData)
        {
            if (worldData == null || worldData.TrackTitlePrefixes.Count == 0 || worldData.TrackTitleSuffixes.Count == 0)
            {
                // No content loaded — e.g. a unit test. A clearly-synthetic fallback rather than
                // throwing, matching PersonGenerator's WorldData-less path.
                return "Untitled " + rng.NextInt(1000, 9999);
            }

            string prefix = rng.Pick(worldData.TrackTitlePrefixes);
            string suffix = rng.Pick(worldData.TrackTitleSuffixes);
            return prefix + " " + suffix;
        }
    }
}
