# KpopManager — Progress

Phase tracker. Tick items as they complete. Full detail lives in the roadmap.

**Current phase:** 3b — the actual chart balance pass *(Phase 3a — simulation + tooling — complete, 2026-09-11; iteration 1 — structural fixes — complete, 2026-09-12; iteration 2 — Korean chart dynamics — complete, 2026-09-12; iteration 3 — scale invariance — complete, 2026-09-13; iteration 4 — restore the two populations — complete, 2026-09-13; tuning not started)*

---

## Phase 0 — Project setup

- [x] Unity 6 LTS project created, 2D (Core) template
- [x] Api Compatibility Level → .NET Standard 2.1
- [x] Resolution: windowed, 1600×900, resizable
- [x] Enter Play Mode Options on, Reload Domain + Reload Scene unchecked
- [x] Newtonsoft Json installed (`com.unity.nuget.newtonsoft-json` 3.2.2)
- [x] Folder structure created
- [x] `KpopManager.Core` asmdef — **No Engine References ✓**
- [x] `KpopManager.Editor` asmdef — Editor only
- [x] `KpopManager.Tests` asmdef — Editor only, TestRunner refs
- [x] `KpopManager.Unity` asmdef
- [x] **Isolation verified** — `using UnityEngine;` in `Assets/Sim/` fails to compile (proven)
- [x] Test Runner setup verified — 77 tests pass (can confirm in Test Runner window)
- [x] `.gitignore` (Unity template + `/SimOutput/`)
- [x] `CLAUDE.md` in repo root
- [x] Docs created: DESIGN, ARCHITECTURE, PROGRESS, BALANCE
- [x] First commit

---

## Phase 1 — Sim core

- [x] `SimRandom` (PCG32, hand-rolled, state exposed for serialization)
- [x] `SimDate` (week-based, 52/year, no DateTime)
- [x] `SimLog` + `SimLogEntry`
- [x] `GameState` (minimal)
- [x] `ISimSystem`
- [x] `SimEngine` with twelve tick steps registered as stubs
- [x] `WeekCounterSystem` (first real system)
- [x] `HarnessWindow` — seed, tick, run-N-years, filtered log view, timing
- [x] Test: determinism (same seed → identical output)
- [x] Test: divergence (different seed → different output)
- [x] Test: SimDate rollover and comparisons
- [x] Test: SimRandom distribution
- [x] Test: SimRandom state round-trip
- [x] Test: 520 ticks under 1000ms
- [x] ARCHITECTURE.md updated

---

## Phase 2 — Entities and generation

- [x] `Person` (single class, `Status` enum: Trainee / Active / Enlisted / Departed)
- [x] Attribute block (Performance, Star, Creative, Hidden, dynamic state)
- [x] `Group`
- [x] `ProductionCenter`
- [x] `Company`
- [x] Name bank JSON (given, family, stage, group names) — plus `foreign-names.json` (Japanese/Chinese/Thai)
- [x] `PersonGenerator` with correlated attributes
- [x] `WorldGenerator` — player center, 2 rivals, ~15 world groups, trainee pool
- [x] Harness: **Generate World** button printing readable tables (World tab: Centers → Groups → Members, Trainees table, Industry table, Person Detail panel)
- [x] Tests: range validity, distribution sanity, seed reproducibility (77 tests, all passing — see below)
- [x] **Read the generated world. Do these look like plausible groups?** — confirmed in harness (plausible distribution, player roster & industry table verified)

**Also done, not on the original checklist:**
- [x] Newtonsoft.Json added to Core (`GUID:` asmdef reference — a plain managed library, not an engine reference)
- [x] Storage pattern (ordered `List<T>` + `Dictionary<int,T>` index, `Add`/`Get`/`RebuildIndices`) applied to `GameState.People`/`Groups`/`Centers`
- [x] `GroupGenerator` retries/disambiguates group names so two companies never end up with an identical name in one world
- [x] Bugfix: Leader/Maknae could collide onto the same person (silently dropping the Maknae tag) — fixed, tested

---

## Phase 3 — Chart simulation ★ critical

### Phase 3a — simulation + analysis tooling *(complete, 2026-09-11)*

Structure and instruments only, per the Phase 3a brief — **nothing was tuned.** All starting values in `ChartConfig` are exactly what the brief specified, untouched.

- [x] `Release` (+ `Track`, both new entities under `Entities/`)
- [x] `ChartConfig` — every tunable number, one class, plain public fields, `ComputeConfigHash()`
- [x] `ChartSystem` (BuzzScore → weekly points → top 100)
- [x] `DecayCurve` (quality-dependent k, pure function)
- [x] `CompetitionModifier` (built into `ChartSystem`, exposed `internal` for direct testing)
- [x] `TrackGenerator` (+ `track-titles.json` word bank, 2500 combinations)
- [x] AI release scheduler (`ReleaseScheduler`) — cadence, seasonality, sibling avoidance
- [x] `TierSystem` — rolling-window promotion/demotion, logged to `SimLog`
- [x] Phase 3 `FandomSystem` stand-in — `Size` grows/decays; `Sentiment`/`PublicAwareness` untouched
- [x] Chart history per release (`WeeklyPositions`/`WeeklyPoints`, retired once stale)
- [x] Menu item: **Run Balance Simulation** → 6 metrics printed + CSV to `/SimOutput/`
- [x] Menu item: **Run Balance Sweep** → same 6 metrics across 5 seeds, mean/range/pass-count
- [x] Tests: determinism (50yr chart history), weight-sum robustness, decay monotonicity, zero-fandom validity, chart position invariants, competition modifier range, tier-change logging, 50yr-under-5s (all passing — 97 tests total, see below)
- [ ] BALANCE.md updated throughout — **intentionally not started; no tuning happened this session**
- [ ] **Gate: a 50-year history reads like a plausible industry.** — Not evaluated against this gate yet: that judgment belongs to the actual balancing session (Phase 3b), which is what the sweep output below exists to inform.

**Full 5-seed sweep output, and the 3 longest-charting releases from seed 12345, are pasted in the Phase 3a chat report** (not duplicated here — see the balance tooling section of `ARCHITECTURE.md` for what the six metrics mean). Headline: everything passes on all 5 seeds except **#1 concentration**, which is low on all 5 (Gini 0.26–0.45 against a 0.45–0.70 target) — the first thing to try tuning next session.

### Phase 3b — the actual balance pass *(iteration 1 — structural fixes — complete, 2026-09-12; tuning not started)*

Iteration 1 was structural, not tuning, per its own brief: two root causes (additive fandom growth converging every group toward the same size; a world too small to contest a 100-slot chart) plus their downstream symptoms (tier always Legendary, promo spend always flat). **No `Weight*`, decay constant, competition constant, or fandom-surge value was touched.**

- [x] Fix 1 — `FandomSystem`: additive/saturating growth → proportional + bootstrap, damped only well above the sizes the sim reaches
- [x] Fix 2a — `WorldGenerator`: 15 groups / 5 companies → `ChartConfig.WorldGroupCount` (200) / 25 companies, tier-share pyramid
- [x] Fix 2b — new `IndustryChurnSystem`: yearly disbands + debuts for world groups (player's own company untouched)
- [x] Fix 2c — `TierSystem`: absolute thresholds → percentile rank against the active cohort, with dead-band hysteresis
- [x] Fix 2d — `ReleaseScheduler.ComputePromoSpend`: additive → multiplicative
- [x] Tooling: `Release.GroupTierAtRelease` (a real CSV-export bug — was reading the group's *current* tier), `chart_occupancy_*.csv`, 3 new metrics (Top-10 rate, #1 rate, Fandom spread), Hit longevity marked provisional
- [x] Bug caught in verification, fixed: `TierHysteresisPct`/`TierPctLegendary` both `0.02` made Legendary unreachable by promotion — see `ARCHITECTURE.md`
- [x] Tests: 11 new (fandom growth compounding/bootstrap/no-floor-decay, churn scope/timing/counters, percentile tier distribution, multiplicative promo spend) — 108 total, all passing
- [x] Determinism still holds (same seed → same 50-year chart history; the RNG draw sequence shifted, as expected any time Core code changes, but the property itself is intact)
- [ ] Balance: distribution of #1s is a power law with upsets — **measured, not fixed: now overcorrected.** Gini ~0.90 (target 0.45–0.70) — swung from too-flat to too-extreme.
- [ ] Balance: average top-10 longevity lands 4–12 weeks — **measured: ~1.4 weeks, way low**, and the band itself is now flagged provisional (Phase 3a's baseline was passing against an uncontested chart, so 4–12 was never validated against a correctly-contested one either)
- [ ] Balance: quality correlates weakly with peak, strongly with longevity — Peak still passes (r ≈ −0.22); Longevity now fails low (r ≈ 0.36 against >0.55)
- [ ] Balance: fandom persists without total lock-in — **measured, not fixed: now overcorrected.** Persistence rho ~0.87 (target 0.3–0.7) and fandom spread p90/p10 ~289x (target >20, but nowhere near "just cleared") — this reads as lock-in, the exact failure mode DESIGN.md warns against, just arrived at from the opposite direction.
- [ ] BALANCE.md updated throughout — done for this iteration; the *next* iteration (actual tuning against these numbers) hasn't started
- [ ] **Gate: a 50-year history reads like a plausible industry. Do not proceed otherwise.** — Not cleared. Every concentration/lock-in metric and every scarcity/churn metric is now off in the *opposite* direction from Phase 3a's baseline. Full sweep output, 4 sample chart runs, fandom-ratio and tier-distribution snapshots, and PromoSpend percentiles are pasted in the Phase 3b chat report (not duplicated here) and summarized in `Docs/BALANCE.md`.

### Phase 3b — the actual balance pass *(iteration 2 — Korean chart dynamics, volume, metric normalization — complete, 2026-09-12; tuning not started)*

A model change, not a tuning pass, per its own brief: replaced the single BuzzScore-times-decay trajectory with two independent components (fandom buys the opening, public appeal buys the years), fixed three measurement problems in the metrics themselves, and replaced the fixed debut-rate roll with a target-seeking one. **`WeightFandom`, tier percentiles, and fandom growth were explicitly out of scope** — the brief expected (correctly) that they'd move once the trajectory model changed, without being touched.

- [x] Fix 1 — two-curve trajectory model: `Release.FandomPull`/`PublicAppeal`/`CrossoverMultiplier` (rolled once at release), independent `FandomDecayK`/`PublicDecayK` decay rates, linear `BuildCurve` ramp-in for the public component, `WeightGroupTier`/`WeightPromoSpend` repurposed as multiplicative boosts on the summed total. `DecayCurve.cs` retired (its one job — quality-interpolated `k` — no longer exists).
- [x] Fix 2a — Hit longevity: mean replaced with median (target 2–5) + p95 (target 25–60); mean kept as an unbanded diagnostic
- [x] Fix 2b — Tier mobility: per-active-group-per-decade (target 0.4–1.5) added; raw count kept as an unbanded diagnostic
- [x] Fix 2c — `chart_occupancy` CSV: tautological `Top10Entries` (always exactly 10) replaced with `DistinctTop10EntrantsThisWeek`
- [x] Fix 2d — 8 new unbanded diagnostics: releases/year, group lifespan mean/p10/p50/p90 (new `Group.DisbandDate` field), crossover rate, weeks-charted mean/p95
- [x] Fix 2e — Top-10 rate band lowered from 5–15% to 4–10%
- [x] Fix 3 — `IndustryChurnSystem`: fixed `NewWorldGroupsPerYearMin/Max` roll replaced with target-seeking debuts (`BaselineNewGroupsPerYear + clamp((TargetActiveWorldGroups - active) * DebutRateCorrectionGain, 0, MaxNewWorldGroupsPerYear)`); `DisbandFailureYears` raised 3 → 5 after the measured mean group lifespan (8.75–9.17 years across the sweep) confirmed 3 was too aggressive
- [x] `Docs/DESIGN.md` chart section rewritten for the two-curve model; music-show multi-factor note recorded under Open questions for Phase 4 (not implemented)
- [x] Tests: `DecayCurveTests.cs` removed (6 tests, tested a retired class); 2 `ChartSystemTests` rewritten + 4 new (undecayed-strength validity, build-curve shape, crossover-roll range, fandom-vs-public decay-rate ordering); `IndustryChurnSystemTests` gained a target-seeking-formula test and a taper-off-near-target test — 106 total, all passing
- [x] Performance test threshold raised 5s → 20s (measured ~12–16s for a full 50-year/200-group run) — an intended consequence of the public component's shallow decay keeping far more releases actively simulated for years, not a regression; not optimized per the brief's own "don't preemptively optimize" note
- [x] Determinism still holds (same seed → same 50-year chart history)
- [ ] Balance: distribution of #1s is a power law with upsets — **still measured, not fixed.** Gini 0.873–0.895 (target 0.45–0.70), essentially unchanged from iteration 1 — the population size (1000+ groups, mostly with zero #1 weeks) dominates this metric more than the trajectory model does.
- [ ] Balance: average top-10 longevity lands 2–5 weeks median, 25–60 weeks p95 — **measured: median exactly 1.000 on every seed (LOW), p95 15–19 (LOW).** Genuinely bimodal (deciles p10–p80 all read 1.0 week; p95/max show a real long tail out to 94 weeks) but the central tendency undershoots even the revised band.
- [ ] Balance: quality correlates weakly with peak, strongly with longevity — **both got worse.** Quality→Peak −0.131 to −0.139 (target −0.40 to −0.15, now HIGH — barely outside, was PASS in iteration 1); Quality→Longevity 0.087–0.101 (target >0.55, still LOW, further from target than iteration 1's ~0.36).
- [ ] Balance: fandom persists without total lock-in — **Persistence genuinely improved** (rho 0.626–0.732, 3/5 seeds now PASS against 0/5 in iteration 1) but **fandom spread got roughly 18x worse** (p90/p10 mean 5150x, range 2802x–12140x, against iteration 1's 289x) — release-volume growth interacting with `FandomSystem`'s untouched proportional growth term. #1 rate now PASSES cleanly (1.75–2.0%, was HIGH at 12–14%).
- [x] Validated: a crossover release can chart 200+ weeks without being culled (measured 270 consecutive weeks, seed 12345) — the core goal of fix 1.
- [x] Validated: the 520 slot-weeks identity (`top10_rate × releases/yr × mean_top10_weeks`) holds exactly on every sweep seed — the top-10 accounting is internally consistent even though Top-10 rate itself reads 3x over its band (28–32% against 4–10%).
- [x] BALANCE.md updated throughout — done for this iteration; tuning still hasn't started
- [ ] **Gate: a 50-year history reads like a plausible industry. Do not proceed otherwise.** — Still not cleared. Full sweep output, identity check, 6 sample chart runs, group-lifespan and weeks-in-top-10 distributions are in `Docs/BALANCE.md`'s iteration 2 entry.

### Phase 3b — the actual balance pass *(iteration 3 — scale invariance — complete, 2026-09-13; tuning not started)*

Not a tuning pass, per its own brief: two formulas were calibrated against the 21-group world and broke silently when iteration 2 grew it 10x — the same failure mode as the Gini band. Fixed those, plus one boundary-flicker bug. **Every trajectory parameter, all fandom-growth fields, `RandomVariancePct`, `ChartSize`, promo spend, `TrackGenerator`, the churn fields, and `TierHysteresisPct` were explicitly untouched** — the trajectory rebalance is iteration 4's job.

- [x] Fix 1 — `CompetitionModifier`: sum-based (scales with world size) → share-based (`ratio = rivalMeanStrength/ownStrength`, `crowdFactor = clamp(rivalCount/CompetitionExpectedRivals, 0.25, 4)`). New `ChartConfig` fields: `CompetitionExpectedRivals`, `CompetitionCrowdFactorMin`/`Max`.
- [x] Fix 2 — Retirement: absolute-points-floor-based (`ChartRetirementWeeksBelowFloor`) → chart-presence-based (`ChartRetirementWeeksOffChart` = 12) plus a hard `ChartMaxSimulatedWeeks` = 350 backstop. `Release.WeeksBelowFloor` renamed `WeeksOffChart`.
- [x] Fix 3a — `#1 concentration (Gini)` band: 0.45–0.70 → 0.82–0.94 (the old band assumed a 21-group world where most groups had a #1 shot; at ~187 groups, Gini is forced near 0.9 by population size alone).
- [x] Fix 3b — New metric: Top-decile #1 share (fraction of #1 weeks held by the top 10% of groups by #1-week count), target 0.55–0.80 — a concentration measure the long tail can't dominate the way it dominates Gini.
- [x] Fix 4 — Chart re-entry margin: `ChartReentryMarginPct` = 0.15 — a release off-chart last week must beat this week's natural rank-`ChartSize` cutoff by 15% to re-enter (one-directional hysteresis, same shape as `TierHysteresisPct`). New `Release.WeeklyCompetitionModifiers` list makes the real per-week modifier distribution measurable directly.
- [x] `BalanceRunner`: Gini's revised band, Top-decile #1 share metric, competition modifier mean/p5/p95 diagnostics, chart re-entry count diagnostic.
- [x] Tests: `ChartSystemTests` gained 10 new tests — competition scale-invariance (pooled 3 seeds × 10 years, 100 vs. 200 world groups), competition bounded-range, off-chart retirement, max-simulated-weeks backstop, re-entry-margin helper unit tests — 114 total, all passing. One genuine bug caught in the scale-invariance test itself during verification (not production code): the test wasn't also scaling `TargetActiveWorldGroups` to match `WorldGroupCount`, so the "100-group" world spent the whole 10-year run drifting toward the unscaled default of 200, producing a spurious ~17% gap; fixed by scaling both together, reconfirmed at 1.0084 (within 1%).
- [x] Determinism still holds (same seed → same 50-year chart history)
- [x] Performance: measured ~1.9–2.2s per 50-year/200-group seed (down from iteration 2's ~14–16s, ~7x faster) — `ActiveReleases` mean dropped 1,277 → 237.70, confirming fix 2's diagnosis. `PerformanceTests`' 20s threshold (raised in iteration 2) now has wide margin; left unchanged.
- [ ] Balance: **Hit longevity swung from LOW to HIGH** — median jumped from 1.000 (iteration 2, flicker-inflated) to ~27 (target 2–5) once flicker was removed; p95 landed cleanly in-band (54.4 against 25–60, was LOW at ~17). The band was never validated against real data at either point.
- [ ] Balance: **Top-10 rate flipped from HIGH (~29%) to LOW (~3.2%)** — fewer, longer-lived hits now monopolize the top 10 instead of many short-lived ones cycling through it.
- [x] Balance: **Gini now passes cleanly** (0.895–0.904 against the revised 0.82–0.94) but **Top-decile #1 share reads HIGH** (0.847–0.879 against 0.55–0.80) — the winners' circle is more concentrated than Gini alone showed, plausibly downstream of fandom spread's still-frozen overcorrection.
- [x] Balance: **Quality→Peak moved cleanly into its target band** (−0.184 mean, was barely-outside HIGH in iteration 2) without the trajectory model being touched — a side effect of fix 1's points-scale fix, not a Quality→Peak mechanism change.
- [x] Balance: **Quality→Longevity stayed broken exactly as predicted** (~0.09–0.10 against >0.55) — its brief explicitly called this: "its cause is the crossover coefficient, frozen this round."
- [x] Balance: **Fandom spread, tier mobility, and Persistence are unaffected/frozen this iteration** as intended — still enormous (11,314x–22,620x), still overshooting (2.76–3.06/decade against 0.4–1.5), not recomputed this pass respectively.
- [x] Scale-invariance validated: 100 vs. 200 world groups, `CompetitionExpectedRivals` scaled 12→24, mean competition modifier ratio 1.0084 — within 1%, well inside the 10% requirement.
- [x] BALANCE.md updated throughout — done for this iteration; tuning still hasn't started
- [ ] **Gate: a 50-year history reads like a plausible industry. Do not proceed otherwise.** — Still not cleared. Full sweep output, occupancy summary, scale-invariance check, weeks-in-top-10 deciles, and 6 sample chart runs (now clean single arcs, no flicker) are in `Docs/BALANCE.md`'s iteration 3 entry.

### Phase 3b — the actual balance pass *(iteration 4 — restore the two populations — complete, 2026-09-13; tuning not started)*

Not purely structural — four coefficients changed — but each targets a specific diagnosed defect (fandom spike deleted by iteration 3, crossover magnitude/gating wrong since iteration 2) rather than a general tuning pass. **All fandom-growth fields, `FandomDecayK`, `PublicDecayK`, `PublicBuildWeeks`/`Floor`, `PublicAppealScale`, all competition fields, `RandomVariancePct`, `ChartSize`, retirement fields, re-entry margin, promo spend, `TrackGenerator`, churn fields, `TierHysteresisPct`, and all `TierPct*` stayed frozen.**

- [x] Fix 1 — `FandomPullScale`: 1.0 → 2.8. At 1.0, `NormalizeFandom`'s 100-point cap meant FandomPull maxed at 100 — below iteration 3's measured ~149-point position-10 cutoff, deleting the fandom-spike population entirely (iteration 3's "Frozen Habit" peaked at #10 and slid gently for 13 weeks instead of spiking).
- [x] Fix 2 — `CrossoverMultiplierMin`/`Max`: 2.5–6.0 → 1.8–3.2. The old range let a crossover hit sit in the top 5 for ~50 consecutive weeks (iteration 3's "Rainy Daylight") — far beyond a real long *chart* tail, which `PublicDecayK` alone already produces without needing an oversized multiplier.
- [x] Fix 3 — `CrossoverChanceBase`/`CrossoverChancePerQuality`: 0.03/0.0006 → 0.008/0.0018. Old coefficients made crossover a near-quality-independent coin flip (Q=100 got 6%, Q=50 got 3%) — the reason `Quality -> Longevity` had been stuck near 0.09 for three iterations.
- [x] Fix 4 (DESIGN) — New `PublicAppealQualityExponent` = 1.6: `ComputePublicAppeal`'s quality term became `pow(quality/100, 1.6) * 100`, not linear — a linear term let a Q=20.6 track reach #8 (iteration 3's "Chrome Prologue").
- [x] `BalanceRunner`: Top-10 rate band 4–10% → 8–13%; Hit longevity mean promoted from diagnostic to a real band (6–11); Hit longevity median 2–5 → 3–8; Hit longevity p95 25–60 → 30–55. Two new metrics: Longevity skew `(mean-median)/mean`, target >0.25; Distinct top-10 entrants/year, target 50–80.
- [x] **Revised bands are an explicit derivation, not a measurement** — from an estimated 50-80 distinct-top10-entrants/year (a real-Melon-scale guess) run through the 520 slot-weeks identity. Recorded as revisable in `Docs/BALANCE.md`.
- [x] Determinism still holds (same seed → same 50-year chart history)
- [x] Tests: 114/114 still passing (no new tests required — iteration 4 only changed `ChartConfig` values and `ComputePublicAppeal`'s formula, both already covered by existing `ChartSystemTests`)
- [x] **Every fix confirmed working correctly in isolation**: highest-FandomPull sample is now genuinely spike-shaped (peak #1, out of top 10 by week 2, `1,4,19,22,...`); crossover rate rises monotonically by quality decile (0.74%→6.28%); quality floor for holding top 10 3+ weeks rose from ~Q20.6 to Q34.7.
- [ ] Balance: **all four fixes together overshot the derived bands by roughly an order of magnitude.** Distinct-top-10-entrants/year measured ~456 against the 50-80 target (~6-9x over); Top-10 rate ~72% against 8-13% (~5-6x over); Hit longevity collapsed back to median 1.0/mean ~1.14 (target 3-8/6-11); Longevity skew stayed LOW (~0.12 against >0.25) — the spike population came back, just far larger than the linear-points estimate assumed, because `NormalizeFandom`'s log10 compression puts "ordinary" and "giant" fandom sizes much closer together in normalized terms than raw points suggest.
- [x] Balance: **Quality -> Longevity improved** (0.09-0.10 → ~0.14, still far below the >0.55 target) as fix 3 intended, though not enough to pass.
- [x] Balance: **Gini, Persistence, tier mobility, fandom spread all moved as side effects of the same overshoot**, not from any direct change to their own mechanisms — reported in `Docs/BALANCE.md`, not investigated further this session.
- [x] BALANCE.md updated throughout — done for this iteration; tuning still hasn't started
- [ ] **Gate: a 50-year history reads like a plausible industry. Do not proceed otherwise.** — Still not cleared. Full sweep output, occupancy, crossover-rate-by-quality-decile, weeks-in-top-10 deciles, and 6 sample chart runs (now genuinely spike-shaped for the fandom sample) are in `Docs/BALANCE.md`'s iteration 4 entry.

---

## Phase 4 — Comeback cycle

- [ ] `Concept` definitions (JSON)
- [ ] `TrendSystem` (taste drifts, lifecycles)
- [ ] `ConceptFatigue` (repeat decay, whiplash penalty)
- [ ] `TrackOffer` (hidden quality, perceived range by A&R skill)
- [ ] `ComebackPlan`
- [ ] `ComebackSystem` (four phases)
- [ ] `MusicShowSystem`
- [ ] `ActivitySystem` (variety, fan signs, radio)
- [ ] Harness: Comeback tab
- [ ] Tests: zero-budget resolves, fatigue bounds, concept fatigue recovery
- [ ] **Gate: run 20 comebacks. Does concept + budget feel like a real decision?**

---

## Phase 5 — Trainees, debut, fandom

- [ ] `TrainingSystem` (growth curves, age slowdown, potential cap)
- [ ] `ScoutingFog` (ranges narrow, potential never numeric)
- [ ] `EvaluationSystem` (monthly ranked report)
- [ ] `IntakeSystem` (4 channels)
- [ ] `DebutSystem` (lineup, positions, initial fandom seeding)
- [ ] `FandomSystem` (Size / Sentiment / PublicAwareness)
- [ ] Trainee attrition
- [ ] **Gate: 7-year career where choices visibly change trajectory**

---

## Phase 6 — Board, economy, full loop ★ vertical slice

- [ ] `EconomySystem`
- [ ] `BoardSystem` (targets, review, warnings, firing)
- [ ] `GoodwillSystem` (requests to HQ)
- [ ] `SharedServicesSystem` (contested pools, rival bidding)
- [ ] Game over / new game
- [ ] **Gate: full career playable in the harness, and it is fun. Make or break.**

---

## Phase 7 — UI Toolkit layer

- [ ] `GameController` MonoBehaviour
- [ ] Design system first — `Common.uss` (palette, type scale, spacing, table/panel/stat-bar classes)
- [ ] Home
- [ ] Roster (group → member)
- [ ] Charts (ListView + position history graph)
- [ ] Comeback (most complex screen)
- [ ] Trainees
- [ ] Calendar
- [ ] Inbox
- [ ] Finances / Requests / Staff
- [ ] Multi-pane desktop layout, 1280×720 minimum
- [ ] **Gate: every harness action doable in the real UI**

---

## Phase 8 — Events and content

- [ ] Event schema (JSON)
- [ ] `EventSystem` (weighted selection, cooldowns)
- [ ] ~40 events written
- [ ] News feed templating

---

## Phase 9 — Save/load

- [ ] Newtonsoft serialization of `GameState`
- [ ] `SimRandom` state serialized
- [ ] Save format version field
- [ ] Autosave + manual slots

---

## Phase 10 — Balance and polish

- [ ] Difficulty tiers (board patience, starting budget)
- [ ] First-run guidance
- [ ] Keyboard shortcuts
- [ ] Settings, resolution handling
- [ ] Full balance pass
