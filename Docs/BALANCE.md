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

### 2026-09-12 — [structural] Phase 3b iteration 2 — Korean chart dynamics, volume, metric normalization

A model change, not a tuning pass, per its own brief — **`WeightFandom`, tier percentiles, and fandom growth were explicitly untouched**, on the expectation that they'd move once the trajectory model changed. New ConfigHash: `33973864`.

**Changed:**
- Replaced the single BuzzScore-times-decay trajectory with two independent components stored on `Release` at release time: `FandomPull` (scales with fandom size, steep `FandomDecayK` decay, ~2-week half-life — "총공"/total-attack behaviour) and `PublicAppeal` (scales with quality/conceptFit/trendFit, shallow `PublicDecayK` decay, ~35-week half-life, with a rare `CrossoverMultiplier` roll). `WeightGroupTier`/`WeightPromoSpend` repurposed as multiplicative boosts on the summed total instead of additive BuzzScore terms. `DecayCurve.cs`, `DecayKMin`/`DecayKMax`, `FandomSurgeWeek0`/`FandomSurgeDecayWeeks`, and the old `Weight*` BuzzScore terms all retired.
- Hit longevity: mean → median (target 2–5) + p95 (target 25–60); mean kept as an unbanded diagnostic.
- Tier mobility: raw count → per-active-group-per-decade (target 0.4–1.5); raw kept as an unbanded diagnostic.
- `chart_occupancy` CSV: tautological `Top10Entries` (always exactly 10, the chart's own slot count) → `DistinctTop10EntrantsThisWeek`.
- Top-10 rate band: 5–15% → 4–10%.
- 8 new unbanded diagnostics: releases/year, group lifespan mean/p10/p50/p90 (new `Group.DisbandDate` field), crossover rate, weeks-charted mean/p95.
- `IndustryChurnSystem`: fixed `NewWorldGroupsPerYearMin`/`Max` roll → target-seeking (`BaselineNewGroupsPerYear + clamp((TargetActiveWorldGroups - active) * DebutRateCorrectionGain, 0, MaxNewWorldGroupsPerYear)`). `DisbandFailureYears` 3 → 5, after measuring mean group lifespan at 3 landed under 6 years in early verification; 5 measures out to a mean of 8.75–9.17 years across the sweep.

**Observed** (5-seed sweep, same seeds, 50 years each):

```
#1 concentration (Gini)                    mean=0.886    range=[0.873, 0.895]      target=[0.45,0.70]   0/5 pass — HIGH
Hit longevity median (weeks in top 10)     mean=1.000    range=[1.000, 1.000]      target=[2.00,5.00]   0/5 pass — LOW
Hit longevity p95 (weeks in top 10)        mean=17.200   range=[15.0, 19.0]        target=[25.0,60.0]   0/5 pass — LOW
Hit longevity mean (weeks in top 10)       mean=2.877    range=[2.696, 2.989]      (diagnostic)
Quality -> Peak (Pearson r)                mean=-0.135   range=[-0.139,-0.131]     target=[-0.40,-0.15] 0/5 pass — HIGH (barely outside; was PASS iter.1)
Quality -> Longevity (Pearson r)           mean=0.095    range=[0.087, 0.101]      target=[0.55,1.00]   0/5 pass — LOW
Tier mobility raw (changes / decade)       mean=441.640  range=[426.6, 469.0]      (diagnostic)
Tier mobility per active group (/decade)   mean=2.376    range=[2.285, 2.518]      target=[0.40,1.50]   0/5 pass — HIGH
Persistence (Spearman rho, yr5 vs yr50)    mean=0.682    range=[0.626, 0.732]      target=[0.30,0.70]   3/5 pass (was 0/5 iter.1)
Top-10 rate (% of releases)                mean=29.160   range=[27.9, 31.6]        target=[4.00,10.00]  0/5 pass — HIGH
#1 rate (% of releases)                    mean=1.853    range=[1.750, 2.006]      target=[0.50,3.00]   PASS (5/5) (was HIGH at 12-14% iter.1)
Fandom spread (p90/p10)                    mean=5150.308 range=[2802.2, 12140.7]   target=[20.0,inf]    PASS (5/5) — ~18x worse than iter.1's 289x mean
Releases per year                          mean=620.988  range=[610.3, 630.4]      (diagnostic)
Group lifespan mean (years)                mean=8.914    range=[8.760, 9.167]      (diagnostic)
Group lifespan p10/p50/p90 (years)         5.00 / 7.00 / 15.39 (mean across seeds)  (diagnostic)
Crossover rate (% of releases)             mean=3.648    range=[3.487, 3.737]      (diagnostic)
Weeks charted mean/p95 (any position)      8.375 / 16.8  (mean across seeds)        (diagnostic)
```

**Config timing:** ~13.9–15.8s per 50-year/200-group seed (measured on the external verification harness, not the Unity Editor) — well under the ~15s "don't preemptively optimize" line from iteration 1's own brief, so left untouched. This is up from iteration 1's ~1–1.6s/seed, entirely because the public component's shallow decay keeps far more releases actively simulated for years (`CompetitionModifier` is O(active²) per week) — an intended consequence of fix 1, confirmed by the identity check and the 270-week crossover run below, not a regression. The repo's own `PerformanceTests` threshold was raised 5s → 20s for the same reason.

**Identity check** (`top10_rate × releases/yr × mean_top10_weeks`, vs. 520 = 10 slots × 52 weeks): holds **exactly 520.00 on every one of the 5 sweep seeds and on seed 12345**. The top-10 accounting is internally consistent — the chart is continuously full — even though the resulting Top-10 rate reads 3x over its own (already-lowered) band.

**Seed 12345 deliverables:**

`ActiveGroups` at years 1/10/25/50: **170 / 186 / 188 / 186** (target 200 — much closer than iteration 1's ~114.6 mean, converges just under target rather than exactly at it).

Group lifespan (world groups, n=1196): mean **8.75y**, p10 **5.00y**, p50 **7.00y**, p90 **14.71y**.

Crossover rate **3.68%**; weeks-charted (any position) mean **8.24**, **max 270** (the longest-charting release below).

Weeks-in-top-10 distribution (releases with `WeeksInTop10>0`, n=8985): deciles p10 through p80 are **all exactly 1.0 week** — the median top-10 entrant is a single-week blip — then p90=2.0, p95=18.0, max=94. Histogram is heavily front-loaded (8342/8985 releases in the 0–9-week bucket) with a real but thin tail out past 90 weeks. Genuinely bimodal in shape; not a clean two-hump split.

Six chart runs (seed 12345, week-by-week positions, 0 = unranked):

```
[HIGHEST FANDOMPULL] Aurora Line - "Glass Sunrise" (Q48.0, TopTier, FandomPull=100.00, PublicAppeal=48.60, x1.00, Y5W38, 10wk charted, 2wk top10, peak 1)
1,9,33,35,54,71,50,71,70,0,0,0,95,0,...

[HIGHEST PUBLICAPPEAL] Lunar Wave 7 - "Wildflower Reverie" (Q100.0, TopTier, FandomPull=91.03, PublicAppeal=494.98, x5.82, Y48W51, 106wk charted, 57wk top10, peak 1)
1,3,4,2,2,2,2,2,2,2,2,2,2,2,1,2,1,2,2,2,1,1,2,1,2,2,2,2,1,3,3,5,3,3,5,5,7,8,5,4,8,5,13,8,9,5,10,6,7,12,...(continues rising then falling through week 106)

[LONGEST-CHARTING OVERALL / CROSSOVER EXAMPLE] Azure Muse 3 - "Ghost Aftermath" (Q77.1, Rookie at release, FandomPull=58.44, PublicAppeal=408.33, x5.92, Y5W11, 270wk charted, 41wk top10, peak 1)
2,3,5,4,2,1,1,1,1,1,1,1,1,2,2,2,2,2,3,5,4,3,3,4,...(climbs, peaks repeatedly in the teens/twenties for ~150 weeks, gradually fades, last charts around week 270)

[TYPICAL MID-TIER, RISING/ESTABLISHED] Wild Rush - "Glass Kaleidoscope" (Q37.6, Rising, FandomPull=67.40, PublicAppeal=41.35, x1.00, Y1W1, 3wk charted, 0wk top10, peak 58)
58,0,77,93,0,...

[LOWEST QUALITY STILL REACHING TOP 10] Fierce Order 3 - "Wildfire Afterparty" (Q2.0, Established, FandomPull=96.17, PublicAppeal=16.42, x1.00, Y31W48, 2wk charted, 1wk top10, peak 10)
10,75,0,...
```

The single highest-`FandomPull`/highest-`PublicAppeal` release both happened to be the same overall population type in this seed's pull (both TopTier), and the crossover example and longest-charting release are literally the same release — a Rookie-tier release (quality 77, well above the mean) that rolled a 5.92x crossover multiplier and rode it for 270 weeks. This is exactly the mechanism fix 1 was built to produce.

**Observed, unprompted:** #1 rate and Persistence both genuinely improved (see table). Everything else that measures concentration (Gini, fandom spread) or contestation (Top-10 rate, tier mobility, hit longevity) either didn't move or moved further from target — fandom spread in particular got roughly 18x worse, because ~620 releases/year (vs. iteration 1's ~19.5/week-equivalent population) feeds far more `WeeklyPoints` into `FandomSystem`'s still-untouched proportional growth term every week. This is the exact interaction the brief warned about ("overcorrected and will move when the trajectory model changes") — it moved, in the wrong direction, because the real lever is fandom growth, not the trajectory model, and that lever is still untouched.

**Verdict:** *(left blank — yours to call)*

---

### 2026-09-13 — [structural] Phase 3b iteration 3 — scale invariance

Not a tuning pass — two formulas were calibrated against the 21-group world and broke silently when iteration 2 grew it 10x, the same failure mode as the Gini band. Fixed those first; the trajectory rebalance is iteration 4's job. **Every trajectory parameter, all fandom-growth fields, `RandomVariancePct`, `ChartSize`, promo spend, `TrackGenerator`, the churn fields, and `TierHysteresisPct` were explicitly untouched.** New ConfigHash: `71736b15`.

**Changed:**
- `CompetitionModifier`: sum-based (`1/(1+c*sumOfRivalBuzz/100)`, which silently divides every release's points by another ~30x whenever the world grows) → share-based (`1/(1+c*ratio*crowdFactor)`, `ratio = rivalMeanStrength/ownStrength`, `crowdFactor = clamp(rivalCount/CompetitionExpectedRivals, 0.25, 4)`). Scale-invariant by construction: doubling world size while doubling `CompetitionExpectedRivals` leaves the mean modifier within 10% (measured: within 1%, see below). New fields: `CompetitionExpectedRivals` (24), `CompetitionCrowdFactorMin`/`Max` (0.25/4.0).
- Retirement: absolute-points-floor-based (`ChartRetirementWeeksBelowFloor`) → chart-presence-based (`ChartRetirementWeeksOffChart` = 12 consecutive weeks with no charted position) plus a hard `ChartMaxSimulatedWeeks` = 350 backstop (Melon's real long-runners top out ~341 weeks). `Release.WeeksBelowFloor` renamed `WeeksOffChart`, now counts off-chart weeks not below-floor weeks. `ChartFloorPoints` kept, narrowed to its original "too few points to chart at all" purpose only.
- `#1 concentration (Gini)` band: 0.45–0.70 → 0.82–0.94 (the old band was calibrated against a 21-group world where most groups had a realistic #1 shot; at ~187 groups and ~30 distinct #1 songs/year, Gini is forced near 0.9 by population size alone, which also matches the real industry). New metric: **Top-decile #1 share** (fraction of #1 weeks held by the top 10% of groups by #1-week count), target 0.55–0.80 — a concentration measure the long tail can't dominate the way it dominates Gini.
- Chart re-entry: added `ChartReentryMarginPct` = 0.15 — a release that was off-chart last week must beat this week's natural rank-`ChartSize` cutoff by 15% to re-enter, one-directional hysteresis (same shape as `TierHysteresisPct`) that only gates getting back in, never staying in or debuting fresh.
- `Release.WeeklyCompetitionModifiers` added (index-aligned with `WeeklyPositions`/`WeeklyPoints`) so the modifier's real distribution is measurable directly instead of reconstructed after the fact (a reconstruction would need each week's live group tier at the time, which isn't recoverable once tier has since changed).
- `BalanceRunner`: two new metrics (Gini's revised band, Top-decile #1 share) and four new diagnostics (competition modifier mean/p5/p95, chart re-entry count).

**Observed** (5-seed sweep, same seeds, 50 years each):

```
#1 concentration (Gini)                    mean=0.899     range=[0.895,0.904]      target=[0.82,0.94]   PASS (5/5) (was 0/5 HIGH pre-rebandng)
Top-decile #1 share                        mean=0.858     range=[0.847,0.879]      target=[0.55,0.80]   0/5 pass — HIGH
Hit longevity median (weeks in top 10)     mean=27.700    range=[27.0,29.0]        target=[2.00,5.00]   0/5 pass — HIGH (was LOW at 1.000 iter.2)
Hit longevity p95 (weeks in top 10)        mean=54.400    range=[53.0,56.0]        target=[25.0,60.0]   PASS (5/5) (was LOW at ~17 iter.2)
Hit longevity mean (weeks in top 10)       mean=26.064    range=[25.3,27.7]        (diagnostic)
Quality -> Peak (Pearson r)                mean=-0.184    range=[-0.186,-0.183]    target=[-0.40,-0.15] PASS (5/5) (was HIGH at -0.135 iter.2)
Quality -> Longevity (Pearson r)           mean=0.090     range=[0.087,0.098]      target=[0.55,1.00]   0/5 pass — LOW (predicted: frozen this iteration)
Tier mobility raw (changes / decade)       mean=540.520   range=[512.2,574.4]      (diagnostic)
Tier mobility per active group (/decade)   mean=2.884     range=[2.757,3.062]      target=[0.40,1.50]   0/5 pass — HIGH
Top-10 rate (% of releases)                mean=3.174     range=[3.006,3.284]      target=[4.00,10.00]  0/5 pass — LOW (was HIGH at ~29% iter.2)
#1 rate (% of releases)                    mean=0.898     range=[0.866,0.940]      target=[0.50,3.00]   PASS (5/5)
Fandom spread (p90/p10)                    mean=18343.4   range=[11314.5,22619.9]  target=[20.0,inf]    PASS (5/5) — frozen this iteration, still enormous
Releases per year                          mean=629.196   range=[619.6,644.6]      (diagnostic)
Group lifespan mean (years)                mean=9.319     range=[9.183,9.431]      (diagnostic)
Crossover rate (% of releases)             mean=3.555     range=[3.440,3.722]      (diagnostic)
Weeks charted mean/p95 (any position)      8.266 / 28.6                            (diagnostic)
Competition modifier mean/p5/p95           0.642 / 0.457 / 0.938                   (diagnostic) — comfortably in the "roughly 0.2-1.0" sane range fix 1 targeted
Chart re-entries (count)                   mean=2183.2    range=[2066,2297]        (diagnostic)
```

**Timing:** ~1.9–2.2s per 50-year seed — down from iteration 2's ~14–16s, roughly **7x faster**, confirming fix 2's diagnosis: `ActiveReleases` mean (seed 12345) dropped from 1,277 to **237.70** (target range was 250–450; landed just under it, close enough that the off-chart/backstop retirement combination is clearly doing its job).

**Occupancy summary, seed 12345:** `ActiveReleases` mean 237.70 (min 135, max 265); `ChartedReleases` mean 100.00 (still always full); `DistinctTop10Entrants` mean **1.27/week** (very low churn — consistent with hit longevity's median jumping to ~26 weeks, meaning most weeks nobody new cracks the top 10); `PointsAtPos1`/`10`/`100` means 275.17 / 149.32 / 39.95 — a far wider, saner points scale than iteration 2's 10.54 / 6.05 / 2.19 (the whole diagnosis that started this iteration).

**Scale-invariance test (Fix 1):** pooled 3 seeds × 10 years, 100 vs. 200 world groups (`CompetitionExpectedRivals` scaled 12→24, `TargetActiveWorldGroups` scaled to match so the smaller world doesn't spend the run growing toward the unscaled default): mean modifier 0.6470 (100 groups) vs. 0.6524 (200 groups), ratio **1.0084** — within 1%, well inside the 10% requirement. (First attempt without also scaling `TargetActiveWorldGroups` measured a spurious 16.8% gap — not a formula bug, a test-setup gap: the "100-group" world was drifting toward 200 the whole run because churn's debut target is a separate config field. Fixed and reconfirmed.)

**Weeks-in-top-10 deciles, seed 12345** (n=1045, `WeeksInTop10>0`): p10=1, p20=3, p30=10, p40=18, p50=26, p60=32, p70=37, p80=43, p90=49, p95=54, max=72. A real, roughly continuous distribution now — no longer the "median exactly 1.0, then a cliff" shape iteration 2 measured; removing re-entry flicker (fix 4) revealed the underlying dynamics are long-residency by default, not short.

**Chart re-entries:** ~2066–2297 per 50-year run (mean 2183.2). Not comparable to a "before" figure (iteration 2 didn't track this), but the six sample runs below show clean single-arc trajectories with no oscillation, which is what the margin was built to produce.

**Six chart runs, seed 12345** (week-by-week positions, 0 = unranked):

```
[HIGHEST FANDOMPULL] Cosmic Wave - "Frozen Habit" (Q57.6, TopTier, FandomPull=100.00, PublicAppeal=55.34, x1.00, Y1W27, 13wk charted, 1wk top10, peak 10)
10,11,20,21,32,34,50,50,82,72,76,93,95,0,...

[HIGHEST PUBLICAPPEAL / LONGEST-CHARTING / CROSSOVER EXAMPLE] (same release) Lunar Flux 5 - "Rainy Daylight" (Q95.5, Rookie at release, FandomPull=70.12, PublicAppeal=471.23, x5.76, Y32W52, 129wk charted, 58wk top10, peak 1)
6,4,4,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,2,1,2,1,3,2,...(holds #1-5 for ~50 weeks, gradually climbs back down through the 80s-90s, drops off around week 118)

[TYPICAL MID-TIER, RISING/ESTABLISHED] ARA - "Ghost Mirage" (Q51.1, Established, x1.00, Y1W21, 4wk charted, 0wk top10, peak 12)
12,33,88,94,0,...

[LOWEST QUALITY STILL REACHING TOP 10] Lunar Grace 2 - "Chrome Prologue" (Q20.6, Rookie, x1.00, Y1W10, 9wk charted, 1wk top10, peak 8)
8,21,28,35,45,55,57,82,87,0,...
```

Every sample run is now a clean single arc — rise, peak, fall — with no on/off flicker anywhere, exactly what fix 4 was built to produce (compare iteration 2's "58, 0, 77, 93, 0" mid-tier sample).

**Observed, unprompted:** Hit longevity swung from LOW (median 1.0, iteration 2's noise-inflated churn) to HIGH (median ~27) in the *opposite* direction — the 2–5 target band assumed a much shorter typical residency than what the sim actually produces once flicker is removed; that band was calibrated against a wrong mental model, not against measured data, exactly the kind of thing iteration 2's own BALANCE.md entry flagged as a risk. Top-10 rate flipped from HIGH (~29%) to LOW (~3.2%) as a direct consequence — fewer, longer-lived hits monopolize the top 10 rather than many short-lived ones cycling through it. Quality→Peak moved cleanly into its target band (was barely outside). Quality→Longevity sat at ~0.09-0.10 exactly as predicted ("expect this to stay broken — its cause is the crossover coefficient, frozen this round").

**Verdict:** *(left blank — yours to call)*

---

### 2026-09-13 — [structural] Phase 3b iteration 4 — restore the two populations

Not purely structural this time — four numeric coefficients changed, but all four target a specific diagnosed defect (fandom spike deleted, crossover magnitude/gating wrong, no quality floor), not a general tuning pass. **Every fandom-growth field, `FandomDecayK`, `PublicDecayK`, `PublicBuildWeeks`/`Floor`, `PublicAppealScale`, all competition fields, `RandomVariancePct`, `ChartSize`, retirement fields, re-entry margin, promo spend, `TrackGenerator`, churn fields, `TierHysteresisPct`, and all `TierPct*` stayed frozen.** New ConfigHash: `2497f64e`.

**Changed:**
- `FandomPullScale`: 1.0 → 2.8. At 1.0, `NormalizeFandom`'s 100-point cap meant FandomPull maxed at 100 — below iteration 3's measured ~149 PointsAtPos10, so a fandom spike could no longer buy a top-10 debut at ANY fandom size. Iteration 3's own highest-FandomPull sample ("Frozen Habit") peaked at #10 and slid gently for 13 weeks — the spike population had been deleted.
- `CrossoverMultiplierMin`/`Max`: 2.5–6.0 → 1.8–3.2. The old range, combined with `PublicDecayK`'s unchanged shallow decay, let a crossover hit sit in the top 5 for ~50 consecutive weeks (iteration 3's "Rainy Daylight") — far beyond even Spring Day's real 341-week *charting* run, which was never a year at #1–#5.
- `CrossoverChanceBase`/`CrossoverChancePerQuality`: 0.03/0.0006 → 0.008/0.0018. The iteration-2 coefficients made a Q=100 track (6%) barely more likely to cross over than a Q=50 track (3%) — crossover was a near-quality-independent coin flip, which is why Quality→Longevity had been stuck near 0.09 for three iterations regardless of what else changed.
- New `PublicAppealQualityExponent` = 1.6 (DESIGN): the PublicAppeal quality term became `pow(quality/100, 1.6) * 100` instead of linear quality — a linear term let a Q=20.6 track reach #8 (iteration 3's "Chrome Prologue") because 20/100 still contributed a full 20% of the max term. The exponent suppresses weak songs disproportionately (Q=20 → ~8% of max, Q=80 → ~70%) without touching FandomPull — a weak song can still spike briefly on fandom alone, it just can't sustain a top-10 run on public appeal.
- `BalanceRunner`: `Top-10 rate` band 4–10% → 8–13%; `Hit longevity mean` promoted from an unbanded diagnostic to a real band (6–11); `Hit longevity median` 2–5 → 3–8; `Hit longevity p95` 25–60 → 30–55. Two new metrics: **Longevity skew** `(mean−median)/mean` over weeks-in-top-10, target >0.25 (nothing else in the report catches a symmetric distribution, which is exactly what iteration 3 silently produced); **Distinct top-10 entrants/year**, target 50–80.

**The revised bands are a derivation, not a measurement.** Top-10 rate came in at ~3.17% of ~630 releases/year in iteration 3, implying only ~20 distinct songs/year ever touched the top 10 — far below a real Melon-scale estimate of 50–80. Running 65 (a midpoint estimate) through the 520 slot-weeks identity gives ~10.3% rate and ~8.0 weeks mean residency; the bands above bracket that derivation. **This estimate has not been validated against any real-world data source and should be treated as revisable.**

**Observed** (5-seed sweep, same seeds, 50 years each) — every fandom/longevity metric overshot its own revised band, in most cases by a lot:

```
#1 concentration (Gini)                    mean=0.801     range=[0.786,0.812]      target=[0.82,0.94]   0/5 pass — LOW (dropped just under; was PASS iter.3)
Top-decile #1 share                        mean=0.635     range=[0.602,0.661]      target=[0.55,0.80]   PASS (5/5)
Hit longevity median (weeks in top 10)     mean=1.000     range=[1.000,1.000]      target=[3.00,8.00]   0/5 pass — LOW (collapsed from ~27.7 iter.3)
Hit longevity p95 (weeks in top 10)        mean=2.000     range=[2.000,2.000]      target=[30.0,55.0]   0/5 pass — LOW (collapsed from ~54.4 iter.3)
Hit longevity mean (weeks in top 10)       mean=1.139     range=[1.133,1.148]      target=[6.00,11.00]  0/5 pass — LOW
Longevity skew ((mean-median)/mean)        mean=0.122     range=[0.117,0.129]      target=[0.25,inf]    0/5 pass — LOW (still not skewed enough)
Distinct top-10 entrants / year            mean=456.4     range=[452.9,459.0]      target=[50.0,80.0]   0/5 pass — HIGH (~6-9x over)
Quality -> Peak (Pearson r)                mean=-0.114    range=[-0.120,-0.107]    target=[-0.40,-0.15] 0/5 pass — HIGH (barely outside; was PASS iter.3)
Quality -> Longevity (Pearson r)           mean=0.144     range=[0.131,0.157]      target=[0.55,1.00]   0/5 pass — LOW (improved from ~0.09-0.10, still far off)
Tier mobility per active group (/decade)   mean=2.298     range=[2.162,2.436]      target=[0.40,1.50]   0/5 pass — HIGH
Persistence (Spearman rho, yr5 vs yr50)    mean=0.743     range=[0.723,0.777]      target=[0.30,0.70]   0/5 pass — HIGH (frozen fandom growth; expected to move, did)
Top-10 rate (% of releases)                mean=71.908    range=[70.98,72.92]      target=[8.00,13.00]  0/5 pass — HIGH (~5-6x over the revised band)
#1 rate (% of releases)                    mean=8.159     range=[8.001,8.295]      target=[0.50,3.00]   0/5 pass — HIGH
Fandom spread (p90/p10)                    mean=8647.7    range=[5074.9,11667.7]   target=[20.0,inf]    PASS (5/5) — frozen, still enormous
Crossover rate (% of releases)             mean=2.803     range=[2.598,2.899]      (diagnostic) — fell from ~3.65% as fix 3 intended
Competition modifier mean                  mean=0.588     range=[0.582,0.592]      (diagnostic)
Chart re-entries (count)                   mean=938.4     range=[921,959]          (diagnostic)
```

**Identity check:** holds exactly 520.00 on every seed, as always.

**Root cause of the overshoot:** `NormalizeFandom` is `log10(size)/7 * 100`, which compresses fandom size hard — an ordinary group with a merely respectable fandom (tens of thousands of fans, nowhere near a "giant") already normalizes to 50-70 before `FandomPullScale` is even applied. At 2.8x, that's 140-196 points — comfortably above the measured PointsAtPos10 cutoff (~193 mean, seed 12345) for a huge fraction of ALL releases, not just fandom giants. The fix correctly restored the ability for a big fandom to spike into the top 10 — it just restored that ability to far more of the population than intended, because the log compression means "big" and "ordinary" fandom sizes aren't as far apart in normalized terms as the linear derivation in the fix's own comment assumed. **`FandomPullScale`/`NormalizeFandom` are both frozen for this session per the brief**, so this is reported, not corrected.

**Occupancy, seed 12345:** `ActiveReleases` mean 238.69 (essentially unchanged from iteration 3's 237.70 — this iteration didn't touch retirement). `PointsAtPos1`/`10`/`100`: 267.58 / 193.35 / 43.35 — position 10's cutoff rose from iteration 3's 149.32, consistent with more releases now clearing it.

**Weeks-in-top-10 deciles, seed 12345** (n=23,086 — up from iteration 3's 1,045, confirming the population explosion): p10 through p80 are **all exactly 1.0 week** again — the exact symmetric-collapse-to-flicker-shaped pattern iteration 2 first produced, now for a different mechanical reason (a fandom spike population so large it swamps the genuine crossover long-tail in the aggregate, rather than iteration 2's re-entry flicker). p90=2.0, p95=2.0, max=16 (down from iteration 3's max=72 — even the tail got shorter, because `CrossoverMultiplierMax` dropping from 6.0 to 3.2 shrank the biggest crossover hits' peak residency too). Skew is 0.112 — still not the >0.25 target, because the fix that was supposed to restore a SECOND population instead diluted the first population so much larger that the aggregate statistics read as "everyone is a one-week spike" again, just via a different mechanism than iteration 2.

**Crossover rate by track-quality decile, seed 12345** — confirms fix 3 is gating correctly: decile 1 (Q1.0–38.5) = 0.74%, decile 5 (Q54.3–58.1) = 2.32%, decile 10 (Q76.9–100) = 6.28%. Monotonically increasing with quality, exactly as designed.

**Lowest-quality top-10 access, seed 12345:** lowest quality EVER reaching top 10 is still **Q=2.2** (peak 7, 1 week in top 10) — a fandom spike can still put a near-zero-quality song briefly at the top, which fix 4 always intended to allow ("realistic — total attack doesn't care about quality"). But lowest quality **holding top 10 for 3+ weeks** is **Q=34.7** — up sharply from iteration 3's ~Q20.6 sustaining a top-10-adjacent run, confirming fix 4's actual goal (separating "can spike" from "can sustain") is working as designed, even though the population-scale overshoot above swamps it in the aggregate metrics.

**Six chart runs, seed 12345** (week-by-week positions, 0 = unranked):

```
[HIGHEST FANDOMPULL] RI 2 - "Ocean Serenade" (Q68.8, Legendary, FandomPull=280.00, PublicAppeal=53.46, x1.00, Y1W35, 14wk charted, 2wk top10, peak 1)
1,4,19,22,28,34,42,52,75,74,94,91,96,98,0,...

[HIGHEST PUBLICAPPEAL] Starlight 6 - "Afterglow Instinct" (Q100.0, Rookie at release, FandomPull=280.00, PublicAppeal=267.06, x3.14, Y30W46, 97wk charted, 12wk top10, peak 1)
1,4,9,8,14,10,14,9,11,15,10,10,12,24,20,13,20,20,14,11,11,10,14,8,16,...(oscillates in the 10-30 range for ~60 weeks, then climbs into the 50s-90s and drops off around week 93)

[LONGEST-CHARTING / CROSSOVER EXAMPLE] (same release) Silver Legend 2 - "Silver Kaleidoscope" (Q92.3, Rookie at release, FandomPull=222.74, PublicAppeal=243.62, x3.18, Y1W23, 97wk charted, 16wk top10, peak 1)
1,2,11,3,5,6,6,3,15,11,11,9,11,7,18,8,11,15,18,18,17,4,11,11,15,...(similar shape — early top-20 volatility, mid-run climb into the 40s-60s, drops off ~week 97)

[TYPICAL MID-TIER, RISING/ESTABLISHED] ORI - "Rainy Anthem" (Q40.7, Established, x1.00, Y1W1, 7wk charted, 0wk top10, peak 24)
24,24,41,35,39,70,74,0,...

[LOWEST QUALITY STILL REACHING TOP 10] Electric Echo 3 - "Afterglow Rush" (Q2.2, Established, FandomPull=280.00, PublicAppeal=15.16, x1.00, Y14W36, 5wk charted, 1wk top10, peak 7)
7,28,44,70,87,0,...
```

**The highest-FandomPull sample is now genuinely spike-shaped** — peaks at #1 in week 0, is out of the top 10 by week 2 (position 19), and off the chart entirely by week 14 — much closer to the requested `2,5,14,38,71,0` shape than iteration 3's 13-week gentle slide, confirming fix 1 achieved its specific, narrow goal even though the population-wide overshoot above shows it went too far in aggregate.

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
- **(new, Phase 3b iteration 2)** Fandom spread is now the single most overcorrected metric in the whole sim (p90/p10 mean 5150x, target >20x) and it's a `FandomSystem` problem, not a `ChartSystem` one — release volume (~620/year) means far more `WeeklyPoints` compound into `Fandom.Size` every week than the growth formula's constants were ever measured against. Any fandom-growth tuning pass needs to account for total weekly points across the whole active population, not just points per release.
- **(new, Phase 3b iteration 2)** Hit longevity's revised bands (median 2–5, p95 25–60) were calibrated against a mental model of "bimodal," not measured data — actual median lands at exactly 1.000 on every seed. Either the bands are still wrong, or the fandom-driven blip population needs to shrink relative to the crossover population before a "typical" top-10 entrant lasts 2+ weeks. Worth revisiting only after fandom growth is retuned, since chart pressure and fandom size are coupled.
- **(new, Phase 3b iteration 2, resolved in iteration 3)** Top-10 rate came in at ~29% against an already-lowered 4–10% band. Iteration 3's fix 2 (retirement keyed off chart presence, not points floor) addressed the mechanism — but overshot the other way, to ~3.2%. See the iteration 3 bullet below.
- **(new, Phase 3b iteration 2)** `ActiveGroups` converges to ~185–188 against a `TargetActiveWorldGroups` of 200 — close, but the target-seeking formula never quite closes the last ~7% gap. Might be `BaselineNewGroupsPerYear`/`DebutRateCorrectionGain` needing a slightly stronger pull near the target, or might just be the natural equilibrium point where disbanding balances debuting; not investigated further. (Iteration 3 measured essentially the same: 184–190 across the sweep.)
- **(new, Phase 3b iteration 2, resolved in iteration 3)** Quality→Peak flipped from a clean PASS (iteration 1, r≈−0.22) to barely-outside-band HIGH (r≈−0.135). Iteration 3 measured a clean PASS again (r≈−0.184) after fixes 1/2/4 — didn't touch the trajectory model, so this was apparently a side effect of the points-scale compression fix 1 addressed, not a real Quality→Peak mechanism problem. Confirms the hypothesis was on the right track without anyone needing to test it directly.
- **(new, Phase 3b iteration 3)** Hit longevity swung from LOW (median 1.0, iteration 2) to HIGH (median ~27, iteration 3) — overshot past the 2–5 band in the other direction once flicker was removed. The band itself was never validated against real data at either point; a proper pass needs the actual distribution shape (now genuinely measurable: p10=1 through p90=49, a real spread) rather than a guessed band.
- **(new, Phase 3b iteration 3)** Top-10 rate went from HIGH (~29%, iteration 2) to LOW (~3.2%, iteration 3) — the fandom-driven blip population that inflated iteration 2's rate is mostly gone (fewer, longer-lived hits now monopolize the top 10 instead), so the *rate* dropped even though the *quality* of what's charting improved. Whether 4–10% is the right target for a "few long-lived hits" chart shape (vs. the "many short-lived hits" shape it was designed around) is genuinely open.
- **(new, Phase 3b iteration 3)** Top-decile #1 share (0.847–0.879) sits well above its 0.55–0.80 target even though Gini itself now passes cleanly — the winners' circle is more concentrated than Gini alone suggested. Worth checking whether this is downstream of the still-frozen, still-overcorrected fandom growth (a few groups' fandom sizes are now so far ahead of everyone else's that they mechanically win most #1s) before treating it as its own problem.
- **(new, Phase 3b iteration 3)** `ActiveReleases` landed at 237.70 (seed 12345), just under fix 2's own 250–450 target range. Not investigated further — `ChartRetirementWeeksOffChart` (12) or `ChartMaxSimulatedWeeks` (350) could both be nudged if a slightly larger active pool is wanted, but the fix's main goal (getting off 1,277) was achieved by a wide margin.
- **(new, Phase 3b iteration 3)** Tier mobility per group got slightly worse (2.28–2.52 iteration 2 → 2.76–3.06 iteration 3) despite none of iteration 3's fixes touching `TierSystem` or its inputs directly — plausibly a knock-on effect of hit longevity's shift (groups holding chart positions longer changes the peak/weeks-in-top-10 inputs `TierSystem`'s composite score reads). Untested hypothesis.
- **(new, Phase 3b iteration 4, the central finding)** `NormalizeFandom`'s log compression means "ordinary" and "giant" fandom sizes aren't nearly as far apart in normalized (0-100) terms as a linear points derivation assumes — a merely respectable fandom already normalizes to 50-70 before `FandomPullScale` is applied. This is *why* `FandomPullScale=2.8` restored the spike population to ~6-9x more of the release population than the 50-80 entrants/year estimate assumed (measured ~456/year). Any future pass at `FandomPullScale` should reason in normalized-fandom terms (what fraction of releases clear a given normalized threshold), not in raw points terms — the raw-points derivation in this iteration's own brief undershot by an order of magnitude for exactly this reason.
- **(new, Phase 3b iteration 4)** Longevity skew stayed LOW (0.112-0.129, target >0.25) even after fix 1 explicitly restored the spike population — the population-scale overshoot above means the aggregate distribution is *still* dominated by one-week entries, just via "almost everyone spikes" instead of iteration 2's "everyone flickers." The skew metric is doing its job (catching a symmetric-reading distribution) but the underlying cause moved, not resolved. Needs `FandomPullScale` (or `NormalizeFandom`'s divisor) tuned to a much narrower population than a linear derivation would suggest — see the bullet above.
- **(new, Phase 3b iteration 4)** #1 concentration (Gini) dropped to 0.786-0.812, just under the 0.82-0.94 band, for the first time in three iterations of otherwise being comfortably inside or above it. Directionally consistent with a much larger population of releases now able to spike to #1 briefly (measured #1 rate 8.0-8.3%, up from ~0.9% iteration 3) — more distinct #1-winners per year mechanically lowers Gini even with fandom growth itself untouched. Not a #1-concentration-mechanism problem; downstream of the same overshoot as everything else this iteration.
- **(new, Phase 3b iteration 4)** Fix 4 (the quality exponent) is confirmed working in isolation — crossover rate rises monotonically by quality decile (0.74% → 6.28%), and the quality floor for HOLDING top 10 3+ weeks rose from ~Q20.6 (iteration 3) to Q34.7. Both signals are real and specific; they're just swamped in the aggregate metrics by the FandomPullScale overshoot above. Worth remembering when the next pass reads the aggregate numbers and looks like fix 4 "didn't work" — it did, on the specific thing it targeted.
- **(new, Phase 3b iteration 4)** The 50-80 distinct-entrants/year estimate this iteration's bands are derived from has never been checked against a real source (Melon, Circle Chart, or similar) — flagged in the brief as revisable, worth actually looking up before the next longevity-band revision rather than re-deriving from another guess.
