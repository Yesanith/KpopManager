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
    // constants live in ChartSystem, ReleaseScheduler, TierSystem, FandomSystem, or TrackGenerator
    // — every one of them reads from an instance of this class instead.
    //
    // The starting values below are deliberately untuned. See Docs/BALANCE.md for the tuning log.
    //
    // Hangs off GameState.ChartConfig so it serialises with a save (Phase 9) and so a balance run
    // can swap the whole thing out between seeds.
    public sealed class ChartConfig
    {
        // -------------------------------------------------------------------------------
        // Phase 3b iteration 2, fix 1: two-curve trajectory model, replacing the single
        // BuzzScore-times-decay formula. Korean digital charts don't behave like the Hot 100 —
        // fandom buying ("총공"/"total attack") front-loads a release's first weeks regardless of
        // the song, while what produces multi-year longevity (BTS's "Spring Day": 341 charting
        // weeks) is general-public crossover appeal, which is deliberately NOT scaled by fandom
        // size. One release, two superimposed components with independent decay rates — see
        // ChartSystem.ComputeFandomPull / ComputePublicAppeal / BuildCurve.
        //
        // FandomPull = NormalizeFandom(size) * FandomPullScale — week-one spike potential.
        //
        // Phase 3b iteration 4 fix 1: raised 1.0 -> 2.8. At FandomPullScale=1.0, NormalizeFandom's
        // 100-point cap meant FandomPull maxed out at 100 — well below iteration 3's measured
        // PointsAtPos10 (~149), so a fandom spike could no longer buy a top-10 debut at ANY fandom
        // size. That deleted the "total attack" spike population the two-curve model exists to
        // produce (iteration 3's own highest-FandomPull sample peaked at #10 and slid gently for 13
        // weeks — an ordinary song, not a spike). At 2.8, a maxed-out FandomPull (280 points) clears
        // a realistic position-10 cutoff and then FandomDecayK's unchanged 2-week half-life brings
        // it back down in ~3 weeks, restoring the authentic shape. Do not change FandomDecayK to
        // compensate — the steep half-life IS the mechanism; only the amplitude was wrong.
        // -------------------------------------------------------------------------------
        public float FandomPullScale = 2.8f;

        // PublicAppeal = (qualityTerm*w1 + conceptFit*w2 + trendFit*w3) * PublicAppealScale —
        // long-tail potential. Deliberately not a function of fandom size at all.
        public float PublicAppealQualityWeight = 0.70f;
        public float PublicAppealConceptWeight = 0.15f;
        public float PublicAppealTrendWeight = 0.15f;
        public float PublicAppealScale = 1.0f;

        // DESIGN (Phase 3b iteration 4 fix 4): the quality term feeding PublicAppeal is
        // pow(quality/100, PublicAppealQualityExponent) * 100, not quality itself — a plain linear
        // term let a Q=20.6 track reach #8 (iteration 3's "Chrome Prologue"), because 20/100 quality
        // still contributed a full 20% of the max quality term. An exponent > 1 suppresses weak
        // songs disproportionately (Q=20 -> ~8% of max quality contribution, Q=80 -> ~70%) while
        // leaving strong songs close to linear, without touching FandomPull — a weak song's fandom
        // can still buy it a brief spike (realistic), it just can't SUSTAIN a top-10 run on public
        // appeal alone the way a genuinely good song can.
        public float PublicAppealQualityExponent = 1.6f;

        // DESIGN: crossover is rare and mostly not under the player's control — a small fraction
        // of releases get a multiplier on PublicAppeal, representing a song that catches on with
        // the general public. This is what produces Spring Day-shaped outliers. Rolled once at
        // release from state.Random; the multiplier (1.0 if it didn't roll) is stored on the
        // release so it's inspectable in the CSV.
        //
        // Phase 3b iteration 4 fix 3: the chance coefficients were miscalibrated since iteration 2
        // — CrossoverChancePerQuality=0.0006 meant a Q=100 track (6%) was barely more likely to
        // cross over than a Q=50 track (3%), making crossover close to a quality-independent coin
        // flip. Since crossover is what drives most of the sim's longevity, Quality -> Longevity
        // stayed stuck near 0.09 for three iterations no matter what else changed. Raised the
        // per-quality-point term ~3x and lowered the base ~4x so a Q=50 track crosses over rarely
        // (0.8%) and a Q=100 track meaningfully more often (9.8%) — crossover chance now actually
        // gates on quality instead of being decorative.
        public float CrossoverChanceBase = 0.008f;
        public float CrossoverChancePerQuality = 0.0018f; // added per point of quality above 50

        // Phase 3b iteration 4 fix 2: lowered from 2.5-6.0 to 1.8-3.2. Combined with PublicDecayK's
        // unchanged shallow decay, the old range let a crossover hit sit in the top 5 for roughly
        // fifty consecutive weeks (iteration 3's "Rainy Daylight") — far beyond even Spring Day's
        // real 341-week *charting* run, which was never a year at #1-#5. The goal is a long CHART
        // tail (a crossover hit sitting in the top 50-100 for one to three years, which
        // PublicDecayK's shallow rate still produces on its own) without the same release
        // monopolizing the top 10 for a year. Only the ceiling on the multiplier's magnitude
        // changed; its persistence is entirely PublicDecayK's job, and that stays frozen.
        public float CrossoverMultiplierMin = 1.8f;
        public float CrossoverMultiplierMax = 3.2f;

        // Fandom component: steep. Half-life ~2 weeks (ln2/0.35) — fans buy immediately, then stop.
        public float FandomDecayK = 0.35f;

        // Public component: very shallow. Half-life ~35 weeks (ln2/0.02), so a crossover hit can
        // chart for years the way Melon long-runners do.
        public float PublicDecayK = 0.020f;

        // DESIGN: linear ramp, not a smoothstep or logistic curve — the public component starts at
        // PublicBuildFloor and rises to full strength by PublicBuildWeeks because word of mouth
        // takes time to build, then holds at 1.0 forever after (the shallow PublicDecayK is what
        // eventually brings it down, not the build curve). Revisit the shape during balancing if a
        // gentler ramp-in reads better.
        public int PublicBuildWeeks = 6;
        public float PublicBuildFloor = 0.25f;

        // DESIGN: promo and tier are modifiers on the summed (fandom + public) total, not additive
        // BuzzScore terms — promo plausibly boosts both a song's initial push and its ongoing
        // visibility, so it multiplies the total; tier plausibly only buys a bigger opening (a
        // Legendary group's existing reach), so it's folded into the fandom side. See
        // ChartSystem.ComputeWeeklyPoints for exactly where each applies.
        public float WeightGroupTier = 0.15f;
        public float WeightPromoSpend = 0.15f;

        // -------------------------------------------------------------------------------
        // Competition. "Rival" means any other release whose ReleaseDate falls within
        // CompetitionWindowWeeks of this release's own ReleaseDate — a debut-timing collision,
        // per DESIGN.md's "drop against a major group's comeback and you get buried."
        //
        // Phase 3b iteration 3 fix 1: the original modifier (1 / (1 + c * sumOfRivalBuzz / 100))
        // compared a release against the ABSOLUTE SUM of rival buzz in its window. That sum scales
        // with world size — growing the world 10x (iteration 2's fix 3) silently divided every
        // release's points by another ~30x on top of it, because a +/-2-week window went from a
        // handful of rivals to ~60. Share-based instead: compare against the rival group's MEAN
        // buzz (population-size-independent) times a "how crowded is this week, relative to
        // normal" factor, so the modifier is invariant to world size as long as
        // CompetitionExpectedRivals is scaled proportionally with it. See
        // ChartSystem.CompetitionModifier for the exact formula and
        // ChartSystemTests.CompetitionModifier_IsScaleInvariant... for the property this
        // guarantees.
        // -------------------------------------------------------------------------------
        public float CompetitionStrength = 0.35f;
        public int CompetitionWindowWeeks = 2;

        // DESIGN: the "normal" rival count for a +/-CompetitionWindowWeeks window, calibrated
        // against WorldGroupCount=200's release volume (~621/year measured in iteration 2). Scale
        // this proportionally with WorldGroupCount if the world size changes again, or the
        // crowdedness factor below silently drifts the same way the old sum-based formula did.
        public int CompetitionExpectedRivals = 24;

        // Bounds on how much an unusually quiet or crowded week can move the modifier, so a
        // release still gets buried by real competition (DESIGN.md's "drop against a major
        // group's comeback") without the effect scaling unboundedly with population.
        public float CompetitionCrowdFactorMin = 0.25f;
        public float CompetitionCrowdFactorMax = 4.0f;

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

        // Too few points to chart at all, regardless of anything else. Separate purpose from
        // retirement below (iteration 3 fix 2) — this is "not enough to rank," not "simulate this
        // release any further."
        public float ChartFloorPoints = 0.5f;

        // Phase 3b iteration 3 fix 2: retirement now keys off chart PRESENCE (consecutive weeks
        // with no charted position), not an absolute points floor — the floor no longer bears any
        // relation to what it actually takes to chart once competition and world size moved
        // (iteration 2 measured 1,277 mean ActiveReleases/week because releases sat well above
        // ChartFloorPoints=0.5 for ~200 weeks at PublicDecayK's shallow decay rate while still
        // failing to CHART at all against everyone else). 12 weeks is deliberately generous so a
        // slow-building crossover release can climb back in — "Ghost Aftermath" (iteration 2)
        // charted 270 weeks total and must remain possible.
        public int ChartRetirementWeeksOffChart = 12;

        // Hard ceiling on how long any release is simulated, as a backstop independent of the
        // off-chart rule above. Melon's real long-runners top out around 341 weeks (BTS's "Spring
        // Day"), so nothing here should meaningfully exceed that.
        public int ChartMaxSimulatedWeeks = 350;

        // Phase 3b iteration 3 fix 4: a release that was off-chart last week must beat this
        // week's rank-100 cutoff by this margin to re-enter, rather than by a single point — a
        // release sitting right at the ChartSize cutoff was measured oscillating on random
        // variance alone ("58, 0, 77, 93, 0" — a song doing nothing, not a real comeback). Applies
        // only to re-entry, never to a release already charting or debuting for the first time —
        // the same one-directional-hysteresis shape as TierSystem's TierHysteresisPct.
        public float ChartReentryMarginPct = 0.15f;

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
        //
        // Phase 3b iteration 2 fix 3: the fixed NewWorldGroupsPerYearMin/Max roll let disbanding
        // outpace debuting badly — ActiveGroups settled at a measured mean of 114.6 against a
        // WorldGroupCount target of 200. Replaced with a target-seeking rate: each year debuts
        // BaselineNewGroupsPerYear + clamp((TargetActiveWorldGroups - activeWorldGroups) *
        // DebutRateCorrectionGain, 0, MaxNewWorldGroupsPerYear) Rookie groups, so the population
        // is pulled back toward its target instead of drifting wherever disbanding happens to land.
        // -------------------------------------------------------------------------------
        public int TargetActiveWorldGroups = 200;
        public float DebutRateCorrectionGain = 0.35f;
        public int MaxNewWorldGroupsPerYear = 40;
        public int BaselineNewGroupsPerYear = 6;

        // Matches DESIGN.md's seven-year contract wall.
        public float DisbandContractYears = 7f;

        // Consecutive years stuck at Rookie tier before a group disbands regardless of contract
        // timing. Iteration 1's value of 3 produced a measured mean group lifespan under six years
        // (see BALANCE.md) — too aggressive for a group to get a fair run, so this is now higher;
        // see the balance-sweep note for the exact before/after.
        public int DisbandFailureYears = 5;

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
