using System.Globalization;
using System.Reflection;
using System.Text;

namespace KpopManager.Core.Systems.Chart
{
    // Every tunable number behind the chart simulation, the release scheduler, tier mobility,
    // track generation, and the Phase 3 fandom stand-in — in one place, on purpose.
    //
    // If a balance session has to grep the codebase for a number, this class has failed. Plain
    // public fields, not properties — this is a bag of knobs meant to be read and written in
    // bulk (by a balance sweep, eventually by a save file), not an object with behaviour. No
    // constants live in ChartSystem, ReleaseScheduler, TierSystem, FandomSystem, TrackGenerator
    // or DecayCurve — every one of them reads from an instance of this class instead.
    //
    // The starting values below are deliberately untuned. See Docs/BALANCE.md for the tuning log.
    //
    // Hangs off GameState.ChartConfig so it serialises with a save (Phase 9) and so a balance run
    // can swap the whole thing out between seeds.
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

        // Consecutive weeks a release must sit below ChartFloorPoints before ChartSystem stops
        // actively simulating it, so a 50-year run doesn't keep recomputing thousands of
        // long-dead releases every week.
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

        // Multiplies release probability in spring/autumn windows (comeback seasons cluster lightly).
        public float SeasonalityBoostSpringAutumn = 1.15f;

        // Multiplies release probability in the midsummer window.
        public float SeasonalityPenaltySummer = 0.85f;

        // How strongly sibling centers (same company as the player) nudge their release date
        // away from a same-week collision with another sibling center's release. 0 = no
        // avoidance, 1 = always push by a full week per colliding sibling.
        public float SiblingCollisionAvoidance = 0.6f;

        // Phase 3b fix 2d: multiplicative, not additive — spend = Base * CenterMult^centerOrdinal
        // * GroupMult^groupOrdinal * jitter, so a Legendary group at a top-tier center outspends a
        // Rookie at a poor one by an order of magnitude, not 3x.
        public float PromoSpendBase = 4000f;
        public float PromoSpendCenterTierMult = 1.9f;
        public float PromoSpendGroupTierMult = 2.1f;
        public float PromoSpendJitterPct = 0.25f;

        // DESIGN: a first-pass estimate, not a measured value — set so a Legendary group at the
        // (currently near-universal, since almost every non-player center is CenterTier.Established)
        // realistic top end of spend normalises near 100. Phase 3b's balance-sweep report includes
        // the actual p5/p50/p95 PromoSpend so this can be set from real data next session instead.
        public float PromoSpendNormalizeDivisor = 1600f;

        // Calendar-months-to-weeks conversion, shared by GroupGenerator (initial cadence/
        // next-release seeding) and ReleaseScheduler (advancing to the next release) so the two
        // never drift out of sync with each other.
        public float WeeksPerMonth = 4.345f;

        // Chance an AI release credits an actual group member as composer (using their own
        // Songwriting/Composition) instead of an external, unmodelled one.
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

        // Weeks in top 10 within the window needed to max out that score component (30 weeks
        // over a 3-year window ~= one strong hit's worth of top-10 weeks per year).
        public float TierWeeksInTop10NormalizerWeeks = 30f;

        // Phase 3b fix 2c: percentile ranking against the cohort of every currently-active group,
        // not absolute score thresholds — an absolute threshold breaks the moment the chart's scale
        // shifts (with the uncontested pre-fix chart, a deliberately mediocre group still scored
        // 91.7, past the old TierThresholdLegendary of 82; nothing could be anything but Legendary).
        // Top-down shares, should sum to 1.0; the remainder below all four is Rookie.
        public float TierPctLegendary = 0.02f;
        public float TierPctTopTier = 0.08f;
        public float TierPctEstablished = 0.20f;
        public float TierPctRising = 0.30f;

        // Percentile margin a group must clear beyond a boundary before the tier actually
        // changes, so a group sitting right on a threshold doesn't oscillate every evaluation.
        public float TierHysteresisPct = 0.02f;

        // -------------------------------------------------------------------------------
        // Fandom stand-in (FandomSystem) — Phase 3 placeholder. Real FandomSystem is Phase 5;
        // this only grows Size from chart performance and decays it during inactivity, so charts
        // have something to snowball or fade against. Sentiment/PublicAwareness stay static.
        // -------------------------------------------------------------------------------
        // Phase 3b fix 1: growth must scale with existing size or nothing compounds. The original
        // additive-growth-vs-multiplicative-decay pairing converged every group toward the same
        // fixed point regardless of history (measured max/min fandom ratio, seed 11111: 613x at
        // year 1 collapsing to 3.1x by year 50) — the opposite of DESIGN.md's "rewards accumulated
        // success." Growth is now a hybrid: a small absolute bootstrap (lets a brand-new group grow
        // from nothing) plus a proportional term (the one that makes fandom actually compound).
        // Absolute fans gained per chart point, independent of current size.
        public float FandomBootstrapPerPoint = 12f;

        // Proportional growth: fans gained per chart point as a fraction of current size. This
        // is what makes fandom compound.
        public float FandomGrowthRatePerPoint = 0.0012f;

        // Size at which proportional growth is damped to half. A brake against runaway, not a
        // leveller — DESIGN: must stay well above the fandom sizes the game actually reaches, or
        // it reintroduces the convergence this fix removes.
        public float FandomGrowthDampingSize = 25_000_000f;

        // Renamed from FandomDecayRateInactive: 0.998 is a retention factor (98.8% of fans stay
        // each inactive week), not a decay rate — the old name read as "decay 99.8% per week,"
        // the opposite of what the field does.
        public float FandomRetentionRateInactive = 0.998f;
        public int FandomInactivityGraceWeeks = 8;

        // -------------------------------------------------------------------------------
        // World scale (WorldGenerator) — Phase 3b fix 2a. Mean active releases per week was 19.5
        // against ChartSize=100 with the original hardcoded 15-group world: the chart was never a
        // fifth full, so "hit longevity 4-12" and the quality correlations were passing against no
        // real scarcity. 200 is a starting estimate, not a spec — see Docs/PROGRESS.md for the
        // measured result and whether it needs another pass.
        // -------------------------------------------------------------------------------
        public int WorldGroupCount = 200;

        // Pyramid shares by tier, Rookie first through Legendary last. Should sum to 1.0; the
        // remainder (if any) falls to Rookie.
        public float WorldTierShareRookie = 0.46f;
        public float WorldTierShareRising = 0.28f;
        public float WorldTierShareEstablished = 0.17f;
        public float WorldTierShareTopTier = 0.07f;
        public float WorldTierShareLegendary = 0.02f;

        public int WorldGroupDebutMaxYearsAgo = 15;

        // -------------------------------------------------------------------------------
        // Industry churn (Phase 3b fix 2b) — nothing previously disbanded a group or debuted a new
        // one, so the "industry" was a fixed cast for 50 years. Scoped to world groups only (not
        // the player's own company's 3 centers) — see IndustryChurnSystem's own DESIGN note.
        // -------------------------------------------------------------------------------
        public int NewWorldGroupsPerYearMin = 8;
        public int NewWorldGroupsPerYearMax = 24;

        // Matches DESIGN.md's seven-year contract wall.
        public float DisbandContractYears = 7f;

        // Consecutive years stuck at Rookie tier before a group disbands regardless of contract timing.
        public int DisbandFailureYears = 3;

        public float DisbandChanceAtContractEnd = 0.55f;

        // Computes a stable hash of every field's current value, so two CSV exports can be
        // compared for "were these actually the same config." Deterministic FNV-1a over the
        // concatenated field values — not object.GetHashCode, which .NET does not guarantee is
        // stable across runtime versions.
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
