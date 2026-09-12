# KpopManager — Progress

Phase tracker. Tick items as they complete. Full detail lives in the roadmap.

**Current phase:** 3b — the actual chart balance pass *(Phase 3a — simulation + tooling — complete, 2026-09-11)*

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
