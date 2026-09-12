using System.Globalization;
using System.Reflection;
using System.Text;

namespace KpopManager.Core.Systems.Chart
{
    /// <summary>
    /// Every tunable number behind the chart simulation, the release scheduler, tier mobility,
    /// track generation, and the Phase 3 fandom stand-in — in one place, on purpose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>If a balance session has to grep the codebase for a number, this class has failed.</b>
    /// Plain public fields, not properties — this is a bag of knobs meant to be read and written
    /// in bulk (by a balance sweep, eventually by a save file), not an object with behaviour.
    /// No constants live in <c>ChartSystem</c>, <c>ReleaseScheduler</c>, <c>TierSystem</c>,
    /// <c>FandomSystem</c>, <c>TrackGenerator</c> or <c>DecayCurve</c> — every one of them reads
    /// from an instance of this class instead.
    /// </para>
    /// <para>
    /// The starting values below are <b>deliberately untuned</b> — Phase 3a's job is to build the
    /// instruments that measure whether they're wrong, not to make them right. See
    /// <c>Docs/BALANCE.md</c> for the tuning log once that session starts.
    /// </para>
    /// <para>
    /// Hangs off <see cref="GameState.ChartConfig"/> so it serialises with a save (Phase 9) and so
    /// a balance run can swap the whole thing out between seeds.
    /// </para>
    /// </remarks>
    public sealed class ChartConfig
    {
        // -------------------------------------------------------------------------------
        // BuzzScore term weights — should sum to 1.0, but nothing enforces or requires it;
        // ChartSystem just scales whatever BuzzScore comes out. DESIGN: revisit every weight
        // during balancing — these are placeholders straight from the Phase 3a brief.
        // -------------------------------------------------------------------------------
        public float WeightTrackQuality = 0.30f;
        public float WeightConceptFit = 0.10f;
        public float WeightTrendFit = 0.10f;
        public float WeightGroupTier = 0.15f;
        public float WeightPromoSpend = 0.15f;
        public float WeightFandom = 0.20f;

        // -------------------------------------------------------------------------------
        // Decay: k = DecayKMax - (quality / 100) * (DecayKMax - DecayKMin). Higher k = faster
        // collapse. See DecayCurve.
        // -------------------------------------------------------------------------------
        public float DecayKMin = 0.08f;
        public float DecayKMax = 0.45f;

        // -------------------------------------------------------------------------------
        // Week-one fandom surge: the fandom term's weight is multiplied by this at week 0,
        // decaying linearly to 1.0 over FandomSurgeDecayWeeks.
        // -------------------------------------------------------------------------------
        public float FandomSurgeWeek0 = 2.20f;
        public float FandomSurgeDecayWeeks = 4f;

        // -------------------------------------------------------------------------------
        // Competition: modifier = 1 / (1 + c * sumOfRivalBuzzThisWindow / 100). "Rival" here
        // means any other release whose ReleaseDate falls within CompetitionWindowWeeks of this
        // release's own ReleaseDate — a debut-timing collision, per DESIGN.md's "drop against a
        // major group's comeback and you get buried."
        // -------------------------------------------------------------------------------
        public float CompetitionStrength = 0.35f;
        public int CompetitionWindowWeeks = 2;

        // -------------------------------------------------------------------------------
        // Variance
        // -------------------------------------------------------------------------------
        public float RandomVariancePct = 0.12f;

        // -------------------------------------------------------------------------------
        // Fandom normalization: log10(size) / FandomLogDivisor, clamped 0-100. Shared by the
        // chart formula's fandom term and by TierSystem's fandom score component.
        // -------------------------------------------------------------------------------
        public float FandomLogDivisor = 7f;

        // -------------------------------------------------------------------------------
        // Chart
        // -------------------------------------------------------------------------------
        public int ChartSize = 100;
        public float ChartFloorPoints = 0.5f;

        /// <summary>Consecutive weeks a release must sit below <see cref="ChartFloorPoints"/>
        /// before <c>ChartSystem</c> stops actively simulating it, so a 50-year run doesn't keep
        /// recomputing thousands of long-dead releases every week.</summary>
        public int ChartRetirementWeeksBelowFloor = 4;

        // -------------------------------------------------------------------------------
        // Track generation (TrackGenerator)
        // -------------------------------------------------------------------------------
        public float TrackQualityMeanBase = 35f;
        public float TrackQualityMeanPerTier = 10f;
        public float TrackQualityComposerWeight = 0.25f;
        public float TrackQualityStdDev = 14f;

        // -------------------------------------------------------------------------------
        // Release scheduler (ReleaseScheduler) — cadence, in months, interpolated by group tier:
        // Rookie gets CadenceMinMonths (frequent), Legendary gets CadenceMaxMonths (rare), per
        // DESIGN.md's "established acts release less and bigger" real-industry pattern.
        // -------------------------------------------------------------------------------
        public float CadenceMinMonths = 4f;
        public float CadenceMaxMonths = 8f;
        public float CadenceJitterMonths = 1f;

        /// <summary>Multiplies release probability in spring/autumn windows (comeback seasons cluster lightly).</summary>
        public float SeasonalityBoostSpringAutumn = 1.15f;

        /// <summary>Multiplies release probability in the midsummer window.</summary>
        public float SeasonalityPenaltySummer = 0.85f;

        /// <summary>How strongly sibling centers (same company as the player) nudge their release
        /// date away from a same-week collision with another sibling center's release. 0 = no
        /// avoidance, 1 = always push by a full week per colliding sibling.</summary>
        public float SiblingCollisionAvoidance = 0.6f;

        public float PromoSpendBaseByCenterTier = 8000f;
        public float PromoSpendPerGroupTier = 6000f;
        public float PromoSpendJitterPct = 0.25f;

        /// <summary>Divisor for normalising promo spend into the BuzzScore's 0–100 term — see
        /// <c>ChartSystem.NormalizePromo</c>.</summary>
        public float PromoSpendNormalizeDivisor = 40000f;

        /// <summary>Calendar-months-to-weeks conversion, shared by <c>GroupGenerator</c> (initial
        /// cadence/next-release seeding) and <c>ReleaseScheduler</c> (advancing to the next release)
        /// so the two never drift out of sync with each other.</summary>
        public float WeeksPerMonth = 4.345f;

        /// <summary>Chance an AI release credits an actual group member as composer (using their
        /// own Songwriting/Composition) instead of an external, unmodelled one.</summary>
        public float ChanceGroupMemberComposes = 0.25f;

        public float ExternalComposerBaseSkill = 55f;
        public float ExternalComposerSkillPerCenterTier = 8f;

        // DESIGN: doesn't feed any formula yet (ReleaseType has no mechanical weight this phase)
        // but it's still a distribution parameter, so it lives here rather than as an inline magic
        // number in ReleaseScheduler.
        public float ReleaseTypeSingleWeight = 0.6f;
        public float ReleaseTypeMiniWeight = 0.3f;
        public float ReleaseTypeFullWeight = 0.1f;

        // -------------------------------------------------------------------------------
        // Tier system (TierSystem) — not its own tick-order step; invoked from FandomSystem's
        // tick, since it needs this week's fresh chart and fandom results and DESIGN.md's twelve
        // steps have no slot named for it. See ARCHITECTURE.md.
        // -------------------------------------------------------------------------------
        public int TierWindowYears = 3;
        public int TierEvaluationIntervalWeeks = 13;

        public float TierScoreWeightPeak = 0.45f;
        public float TierScoreWeightWeeksInTop10 = 0.25f;
        public float TierScoreWeightFandom = 0.30f;

        /// <summary>Ascending score thresholds a group must clear to sit at least at each tier.
        /// Rookie is the floor and needs no threshold.</summary>
        public float TierThresholdRising = 20f;
        public float TierThresholdEstablished = 40f;
        public float TierThresholdTopTier = 62f;
        public float TierThresholdLegendary = 82f;

        /// <summary>Weeks in top 10 within the window needed to max out that score component
        /// (30 weeks over a 3-year window ≈ one strong hit's worth of top-10 weeks per year).</summary>
        public float TierWeeksInTop10NormalizerWeeks = 30f;

        // -------------------------------------------------------------------------------
        // Fandom stand-in (FandomSystem) — Phase 3 placeholder. Real FandomSystem is Phase 5;
        // this only grows Size from chart performance and decays it during inactivity, so charts
        // have something to snowball or fade against. Sentiment/PublicAwareness stay static.
        // -------------------------------------------------------------------------------
        public float FandomGrowthPerPoint = 45f;

        /// <summary>The bigger a group's existing fandom, the less the same chart points add to
        /// it — <c>growth *= 1 / (1 + Size / FandomGrowthSaturationSize)</c>. A Rookie group's
        /// fandom (tens of thousands) grows close to linearly; a Legendary one's (millions)
        /// visibly dampens.</summary>
        public float FandomGrowthSaturationSize = 2_000_000f;

        public float FandomDecayRateInactive = 0.998f;
        public int FandomInactivityGraceWeeks = 8;

        /// <summary>Computes a stable hash of every field's current value, so two CSV exports can
        /// be compared for "were these actually the same config." Deterministic FNV-1a over the
        /// concatenated field values — not <see cref="object.GetHashCode"/>, which .NET does not
        /// guarantee is stable across runtime versions.</summary>
        public string ComputeConfigHash()
        {
            StringBuilder sb = new StringBuilder();

            FieldInfo[] fields = typeof(ChartConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
            // Reflection order isn't guaranteed, so sort by name — the hash must not depend on
            // however the runtime happens to enumerate the type's fields.
            System.Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));

            for (int i = 0; i < fields.Length; i++)
            {
                object value = fields[i].GetValue(this);
                sb.Append(fields[i].Name).Append('=');
                sb.Append(System.Convert.ToString(value, CultureInfo.InvariantCulture));
                sb.Append(';');
            }

            return Fnv1aHex(sb.ToString());
        }

        private static string Fnv1aHex(string text)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;

            uint hash = offsetBasis;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= prime;
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
