# KpopManager — Progress

Phase tracker. Tick items as they complete. Full detail lives in the roadmap.

**Current phase:** 3 — chart simulation *(Phase 2 complete, 2026-09-11)*

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
