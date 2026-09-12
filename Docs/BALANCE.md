# KpopManager — Balance Log

Every tuning change gets a line. What you changed, what happened, whether you kept it.

This exists because balancing is a long sequence of small experiments, and without a log you will make the same change twice, three months apart, having forgotten why you reverted it the first time.

---

## How to use this

1. Change **one** coefficient.
2. Run **KpopManager → Run Balance Simulation** (or **Run Balance Sweep** for all 5 seeds at once).
3. Open `/SimOutput/*.csv`.
4. Record the result below.
5. Repeat.

Always change one thing at a time. Two changes at once and you learn nothing from the result.

---

## Target metrics

The numbers a healthy sim should produce. Check these every balance run.

| Metric | Target | Why |
|---|---|---|
| Distribution of #1 songs | Power law with upsets | One group always winning is broken; uniform random is also broken |
| Average weeks in top 10 (for a hit) | 4–12 | Below 4 and nothing feels earned |
| `TrackQuality` vs `PeakPosition` | Weak correlation | Fandom should dominate week one — that's realistic |
| `TrackQuality` vs `WeeksInTop10` | Strong correlation | Quality must matter *somewhere*, or the player has no lever |
| Top group in year 5 vs year 50 | Some persistence, not lock-in | Total lock-in is a dead game |
| Groups reaching top tier per decade | 2–4 | Mobility has to exist |

---

## Log

Format:

```
### YYYY-MM-DD — [system] short description
**Changed:** coefficient X from A to B
**Expected:** ...
**Observed:** ...
**Verdict:** kept / reverted / partial
```

---

### 2026-09-12 — [baseline] Phase 3a untuned starting values, sweep confirmed for real in the Editor

**Changed:** nothing — this is the baseline. Confirms `BalanceRunner` works end-to-end in the actual Unity Editor (not just external verification), for the record.

**Observed:** ConfigHash `f5863184`. 5/6 metrics pass on every seed with zero tuning:

```
#1 concentration (Gini)                   mean=0.322   range=[0.257, 0.385]   target=[0.45, 0.70]   0/5 pass
Hit longevity (mean weeks in top 10)      mean=10.107  range=[8.901, 10.717]  target=[4.00, 12.00]  PASS (5/5)
Quality -> Peak (Pearson r)               mean=-0.356  range=[-0.383, -0.312] target=[-0.40, -0.15] PASS (5/5)
Quality -> Longevity (Pearson r)          mean=0.723   range=[0.714, 0.743]   target=[0.55, 1.00]   PASS (5/5)
Tier mobility (changes / decade)          mean=9.360   range=[9.000, 10.000]  target=[8.00, 20.00]  PASS (5/5)
Persistence (Spearman rho, yr5 vs yr50)   mean=0.407   range=[0.220, 0.556]   target=[0.30, 0.70]   4/5 pass
```

**Verdict:** kept, as a baseline. Investigation into *why* #1 concentration failed (and whether the other five passed for the right reasons) led directly into the next entry.

---

### 2026-09-12 — [structural] Phase 3b iteration 1 — fandom growth, world scale, churn, percentile tiers, promo spend

Structural fixes only, per the Phase 3b brief — **no BuzzScore weight, decay constant, competition constant, or fandom-surge value was touched.** New ConfigHash: `35e92816`.

**Changed:**
- `FandomSystem.ApplyGrowth`: additive (`FandomGrowthPerPoint * points`, saturating) → hybrid proportional + bootstrap (`(Size * FandomGrowthRatePerPoint * points + FandomBootstrapPerPoint * points) * damping`). `FandomDecayRateInactive` renamed `FandomRetentionRateInactive` (same value, correct name).
- `WorldGenerator`: world group count 15 → `ChartConfig.WorldGroupCount` (200), tier-share-driven pyramid, 25 companies (was 5), debut stagger up to 15 years (was 10).
- New `IndustryChurnSystem`: yearly disbands (3 consecutive Rookie years, or a coin-flip at each 7-year contract mark) and debuts (8–24 Rookies/year) for world groups only.
- `TierSystem`: absolute score thresholds → percentile rank against the full active-group cohort, with dead-band hysteresis (only applied to single-tier moves — see the bug note below).
- `ReleaseScheduler.ComputePromoSpend`: additive → multiplicative (`Base * CenterMult^ordinal * GroupMult^ordinal`). `PromoSpendNormalizeDivisor` 40000 → 1600 (a first-pass estimate; see the reported p5/p50/p95 below).
- `Release.GroupTierAtRelease` added — `BalanceRunner`'s CSV was reading the group's *current* tier for every historical row, so a release from year 3 was reported at whatever tier the group happened to be at export time. Same class of bug `FandomSizeAtRelease` already existed to prevent, just missed for tier.
- `BalanceRunner`: three new metrics (Top-10 rate, #1 rate, Fandom spread p90/p10), a `chart_occupancy_*.csv` export, Hit longevity marked provisional, ticks week-by-week instead of year-by-year so occupancy can be sampled weekly.

**Bug caught during verification, fixed before measuring:** `TierHysteresisPct` and `TierPctLegendary` are both `0.02` in the given starting values — numerically identical. A group sitting at the exact top of its cohort (percentile 0.0) had its promotion check land exactly on the Legendary boundary after the hysteresis nudge and get rejected, every time — silently making Legendary unreachable by promotion from below. Fixed by only applying hysteresis to single-tier moves; a jump of two or more tiers is a decisive change and shouldn't be second-guessed by a small margin. `TierSystemTests.LargeCohortWithVariedScores_ProducesAllFiveTiers_NotEveryoneLegendary` guards this.

**Observed** (5-seed sweep, same seeds, 50 years each; full per-seed output and every other requested data pull is in the Phase 3b chat report, not duplicated here):

```
#1 concentration (Gini)                   mean=0.896    range=[0.881, 0.903]     target=[0.45, 0.70]  0/5 pass — HIGH
Hit longevity (mean weeks in top 10)      mean=1.409    range=[1.346, 1.498]     target=[4.00, 12.00] 0/5 pass — LOW  (provisional)
Quality -> Peak (Pearson r)               mean=-0.219   range=[-0.233, -0.205]   target=[-0.40,-0.15] PASS (5/5)
Quality -> Longevity (Pearson r)          mean=0.365    range=[0.356, 0.379]     target=[0.55, 1.00]  0/5 pass — LOW
Tier mobility (changes / decade)          mean=233.480  range=[213.8, 257.0]     target=[8.00, 20.00] 0/5 pass — HIGH (~12x over)
Persistence (Spearman rho, yr5 vs yr50)   mean=0.869    range=[0.850, 0.890]     target=[0.30, 0.70]  0/5 pass — HIGH
Top-10 rate (% of releases)               mean=92.108   range=[90.5, 93.4]       target=[5.00, 15.00] 0/5 pass — HIGH
#1 rate (% of releases)                   mean=12.930   range=[12.1, 13.9]       target=[0.50, 3.00]  0/5 pass — HIGH
Fandom spread (p90/p10)                   mean=289.134  range=[102.8, 474.0]     target=[20.00, inf]  PASS (5/5) — but at the extreme end, not just "cleared"
```

Only *Quality → Peak* still passes cleanly. Every metric that measures concentration/lock-in (Gini, Persistence, Fandom spread) swung from **too flat** to **too extreme**; every metric that measures scarcity/churn (Top-10 rate, #1 rate, Hit longevity, Tier mobility) swung from **too uncontested** to **way overcontested**. Nothing crashed; every number is internally consistent with the mechanism that produced it — this reads as real overcorrection from the fixes as specified, not a bug:

- Proportional fandom growth is a rich-get-richer mechanic. At the sizes this sim actually reaches (p90 ≈ 4.9M fans by year 50), `FandomGrowthDampingSize` (25M) never meaningfully engages — nothing brakes the compounding. That's most of #1 concentration, Persistence, and Fandom spread's overshoot in one mechanism.
- World growth (200 → ~1,000 groups ever, ~110 active at any time via churn) plus ~140 simultaneously-active releases against `ChartSize=100` made the chart *more* contested than intended, not correctly contested — Hit longevity collapsed and Top-10/#1 rates stayed high (a different mechanism: the week-0 fandom surge, untouched this session, gives almost every release one brief top-10 moment on debut regardless of population size).
- Tier mobility's ~12x overshoot looks like an interaction the brief didn't anticipate: percentile rank is sensitive to cohort *composition*, and churn changes that composition constantly (8–24 new Rookies/year reshuffles everyone else's relative percentile even when their own score hasn't moved).

**Verdict:** *(left blank — yours to call)*

---

## Reverted changes

Things tried and rejected. Keep them here so you don't retry them in six months.

*(empty)*

---

## Open balance questions

- What should the decay constant `k` range be across the quality spectrum?
- Should `CompetitionModifier` be symmetric, or should the bigger release suppress the smaller one disproportionately?
- Does fandom need an age-decay term, or does concept fatigue alone prevent snowballing?
- How much should promotion spend matter relative to track quality? Too high and money wins; too low and the budget split stops being a decision.
- **(new, Phase 3b)** `FandomGrowthDampingSize` (25M) never actually engages at the sizes the sim reaches (p90 ≈ 4.9M by year 50). Either lower the damping size substantially, or accept that damping isn't the lever and something else (decay rate, a hard cap, sentiment-gated growth from Phase 5) needs to do the braking.
- **(new, Phase 3b)** Is `ChartSize=100` still right against a ~110–140-active-release world, or does a bigger world need a bigger chart (or the reverse — a smaller world)? `WorldGroupCount`/`NewWorldGroupsPerYearMin`/`Max` and `ChartSize` are coupled and were only adjusted on one side of that coupling this session.
- **(new, Phase 3b)** Does `TierEvaluationIntervalWeeks` (13) need to lengthen now that percentile rank reshuffles with every batch of new churn debuts, independent of any single group's own performance changing?
- **(new, Phase 3b)** `PromoSpendNormalizeDivisor` was set to 1600 as a first-pass estimate. Measured p5/p50/p95 PromoSpend (seed 11111): 6,203 / 15,984 / 74,856 — revisit the divisor against this real distribution, not the pre-fix guess.
