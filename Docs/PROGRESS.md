# KpopManager — Progress

Phase tracker. Tick items as they complete. Full detail lives in the roadmap.

**Current phase:** 2 — entities and generation *(Phase 1 complete, 2026-09-11)*

---

## Phase 0 — Project setup

- [x] Unity 6 LTS project created, 2D (Core) template
- [x] Api Compatibility Level → .NET Standard 2.1
- [ ] Resolution: windowed, 1600×900, resizable — *currently 1920×1080, fullscreen, non-resizable*
- [ ] Enter Play Mode Options on, Reload Domain + Reload Scene unchecked — *not set*
- [ ] Newtonsoft Json installed — *not in `Packages/manifest.json`; Phase 9 needs it*
- [x] Folder structure created
- [x] `KpopManager.Core` asmdef — **No Engine References ✓**
- [x] `KpopManager.Editor` asmdef — Editor only
- [x] `KpopManager.Tests` asmdef — Editor only, TestRunner refs
- [x] `KpopManager.Unity` asmdef
- [ ] **Isolation verified** — `using UnityEngine;` in `Assets/Sim/` fails to compile — *asmdef flag set, not yet proven by hand*
- [ ] Test Runner shows one green test — *45 tests pass outside Unity; confirm in the Test Runner window*
- [ ] `.gitignore` (Unity template + `/SimOutput/`) — *Unity template present, `/SimOutput/` missing*
- [ ] `CLAUDE.md` in repo root — *lives at `.claude/CLAUDE.md`*
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

- [ ] `Person` (single class, `Status` enum: Trainee / Active / Enlisted / Departed)
- [ ] Attribute block (Performance, Star, Creative, Hidden, dynamic state)
- [ ] `Group`
- [ ] `ProductionCenter`
- [ ] `Company`
- [ ] Name bank JSON (given, family, stage, group names)
- [ ] `PersonGenerator` with correlated attributes
- [ ] `WorldGenerator` — player center, 2 rivals, ~15 world groups, trainee pool
- [ ] Harness: **Generate World** button printing readable tables
- [ ] Tests: range validity, distribution sanity, seed reproducibility
- [ ] **Read the generated world. Do these look like plausible groups?**

---

## Phase 3 — Chart simulation ★ critical

- [ ] `Release`
- [ ] `ChartSystem` (BuzzScore → weekly points → top 100)
- [ ] `DecayCurve` (quality-dependent k)
- [ ] `CompetitionModifier`
- [ ] AI release scheduler
- [ ] Chart history per release
- [ ] Menu item: **Run 50-Year Simulation** → CSV to `/SimOutput/`
- [ ] Balance: distribution of #1s is a power law with upsets
- [ ] Balance: average top-10 longevity lands 4–12 weeks
- [ ] Balance: quality correlates weakly with peak, strongly with longevity
- [ ] Balance: fandom persists without total lock-in
- [ ] BALANCE.md updated throughout
- [ ] **Gate: a 50-year history reads like a plausible industry. Do not proceed otherwise.**

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
