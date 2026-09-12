# KpopManager — Architecture

Technical reference. **Updated with every structural change** — new system, new entity, changed tick order.

Last updated: *(Phase 3a — chart simulation and analysis tooling)*

---

## Assemblies

| Assembly | Path | Purpose | References |
|---|---|---|---|
| `KpopManager.Core` | `Assets/Sim/` | Pure simulation. **No Engine References ✓** | `Newtonsoft.Json` |
| `KpopManager.Editor` | `Assets/Editor/` | Harness window, batch sim, CSV export, content loading. Editor-only. | Core, `Newtonsoft.Json` |
| `KpopManager.Tests` | `Assets/Tests/` | EditMode tests (NUnit). Editor-only. | Core, TestRunner, `Newtonsoft.Json` |
| `KpopManager.Unity` | `Assets/Scripts/` | Runtime Unity glue, UI controllers. Empty until Phase 7. | Core |

Dependency direction is strictly one-way: **Editor / Tests / Unity → Core**. Core references only the plain managed `Newtonsoft.Json` assembly (via a `GUID:` asmdef reference, added in Phase 2) — a plain managed library isn't an engine reference, so it doesn't trip the No Engine References flag. File I/O is still forbidden in Core; only `KpopManager.Editor` touches disk.

---

## Core invariants

1. Core cannot reference `UnityEngine` — enforced by the asmdef's No Engine References flag. It can reference plain managed libraries (`Newtonsoft.Json`, added Phase 2), just not engine or file-I/O ones.
2. All randomness flows through the single `SimRandom` instance on `GameState`.
3. Time is `SimDate { Year, Week }` with 52 weeks per year. No `DateTime` anywhere.
4. `GameState` is a plain serializable data graph. Entities reference each other by `int Id`, never by object reference.
5. The sim never calls the UI. The UI polls the sim.
6. **Storage pattern, applied to every entity type (Phase 2):** an authoritative ordered `List<T>` plus a `Dictionary<int, T>` lookup index rebuilt from it. Iterate the list when order could affect a sim outcome; the dictionary is for by-id lookup only — see the Determinism section.

---

## Core structure

```
Assets/Sim/
├── Core/                     — engine, state, primitives (Phase 1)
├── Entities/                 — Person, Group, ProductionCenter, Company (Phase 2); Track, Release (Phase 3a)
├── Systems/
│   ├── Generation/           — PersonGenerator, GroupGenerator, WorldGenerator (Phase 2)
│   ├── Chart/                — ChartConfig, ChartSystem, DecayCurve, TrackGenerator,
│   │                           ReleaseScheduler, TierSystem (Phase 3a)
│   └── Fandom/               — FandomSystem: Phase 3 stand-in (Phase 3a)
├── Events/                   — (pending Phase 8)
├── Data/                     — WorldData, WorldDataLoader: the shape of loaded content (Phase 2/3a)
└── AssemblyInfo.cs           — [InternalsVisibleTo("KpopManager.Tests")] (Phase 3a)

Assets/SimData/                — the content JSON itself (not C#, not under Assets/Sim/)
Assets/Editor/
├── Generation/ContentLoader.cs  — reads Assets/SimData/*.json, hands Core a JSON string
├── BalanceRunner.cs             — headless N-year runs, 6 balance metrics, CSV export (Phase 3a)
└── BalanceMetrics.cs            — Gini / Pearson r / Spearman rho, dependency-free (Phase 3a)
```

### Core/
| Type | Purpose |
|---|---|
| `SimRandom` | PCG32 (XSH RR 64/32), hand-rolled. Unbiased `NextInt` by rejection sampling, `NextFloat`, `NextGaussian` (Marsaglia polar, spare cached), `Chance`, `Pick`, `Shuffle`. Verified against the reference PCG32 vectors for seed 42 / sequence 54. |
| `SimRandomState` | Serialisable snapshot: `State`, `Inc`, `HasGaussianSpare`, `GaussianSpare`. `GetState()` / `SetState()` round-trip exactly. The spare is part of the state — omitting it desynchronises every later Gaussian across a save. |
| `SimDate` | Immutable `readonly struct`. `Year` + `Week` (1–52), full operator set, `IComparable` / `IEquatable`. `TotalWeeks` is the ordinal every comparison derives from; `FromTotalWeeks` floors correctly for negatives. `ToString()` → `"Y3 W17"`. |
| `SimLogEntry` | Immutable `readonly struct`: date, category, severity, message, `RelatedEntityId` (`NoEntity` = -1). `ToStableString()` is the by-value form the determinism test compares. |
| `LogCategory` / `LogSeverity` | `System, Chart, Group, Person, Finance, Board, Event, Rival` · `Debug, Info, Notable, Major`. |
| `SimLog` | Append-only `List<SimLogEntry>`, always sorted by date because entries arrive in tick order. `Add`, `Entries`, `GetForWeek`, `Clear`. The sim's only output channel. |
| `GameState` | Plain data graph: `Date`, `Random`, `Log`, `Seed`, `NextEntityId`, plus `AllocateEntityId()`. No system references, no events. |
| `ISimSystem` | `string Name` + `void Tick(GameState)`. Systems are stateless; anything remembered between ticks lives on `GameState`. |
| `SimEngine` | Owns a `GameState` and an ordered `List<ISimSystem>`. `AdvanceWeek` / `AdvanceWeeks` / `AdvanceYears`, plus `StartDate` and `WeeksElapsed` for readouts. Not part of the save — it is rebuilt around a restored `GameState`. |

### Entities/
| Type | Purpose |
|---|---|
| `PersonStatus`, `Position`, `Nationality`, `Gender` | Person enums. `Position` includes `Leader`/`Maknae` as group-role tags, not performance archetypes — see `GroupGenerator`. `Gender` wasn't in the original field list but was added: it's what makes the given-name gender tag and DESIGN.md's "male idols enlist by around 28" hook meaningful. Girl/boy/co-ed is still an open DESIGN.md question; nothing here decides it. |
| `Person` | Trainees and idols are the same class, distinguished only by `Status`. Identity, Performance/Star/Creative (0–100 floats), Hidden (incl. `Potential`, generated truthfully — Phase 5's scouting fog is what hides it, not generation), Dynamic state, membership (`GroupId`/`CenterId`, `-1` = none), `Positions` list, trainee fields, and `LanguageProficiency` (a `Dictionary<Nationality,int>` — safe because it's only ever looked up by a known key, never iterated). `Age(now)`, `DisplayName`, `FullName`, `CoreAttributeMax()` are computed helpers. |
| `GroupTier` | Rookie/Rising/Established/TopTier/Legendary. Shifts generation means only. |
| `Fandom` | `Size` (long), `Sentiment` (float 0–100), `PublicAwareness` (float 0–100) — three separate numbers per DESIGN.md; never collapse them. |
| `Group` | `MemberIds` (ordered, authoritative), `DebutDate`, `ContractExpiry` (always `DebutDate.AdvanceYears(7)`), `Tier`, `Fandom`, `Gender`. |
| `CenterTier` | Junior/Established/Flagship/SpinOff — DESIGN.md's "long arc" for the center itself; distinct from `GroupTier`. |
| `ProductionCenter` | `IsPlayer`, `Budget`, `Goodwill`, `GroupIds`, `TraineeIds`, `Tier`. |
| `BoardTarget`, `Board` | Empty placeholder shapes — Phase 6's `BoardSystem` gives them real fields. A concrete class rather than `object` so `Board.Targets` stays strongly typed. |
| `Company` | `CenterIds` — the player's center plus its two siblings *in the same company*, per DESIGN.md's data model. Groups from other companies (the wider "industry") are **not** part of this list; they sit in `GameState.Centers` under their own separate centers. |
| `Genre` | Flavour tag on `Track` (Pop/HipHop/RnB/EDM/Ballad/Rock/Trot). Not wired into any formula yet — generation only. |
| `Track` | `Quality` (0–100, hidden — the truth the whole chart formula answers to), `ComposerId` (`Person.NoEntity` for an external, unmodelled composer), `Genre`, `IsTitleTrack`. |
| `ReleaseType`, `Concept` | `ReleaseType`: Single/Mini/Full, flavour + promo scaling only so far. `Concept`: DESIGN.md's placeholder list verbatim (Cute/GirlCrush/Dark/Retro/Summer/Ballad/Experimental) — Phase 4 owns concept mechanics; this phase's `conceptFit` term is a flat 50 regardless of which concept is picked. |
| `Release` | One comeback's full history: `WeeklyPositions`/`WeeklyPoints` (index = weeks since release, 0 = unranked), `PeakPosition`, `WeeksInTop10`, `WeeksCharted`, `TotalPoints`, `FandomSizeAtRelease` and `GroupTierAtRelease` (fixed snapshots, since the live values keep moving — the tier snapshot was missing in Phase 3a, caught as a real CSV-export bug in Phase 3b). `MusicShowWins` stays 0 until Phase 4. `IsCharting`/`WeeksBelowFloor` (not in the original brief — added so `ChartSystem` can retire a release from active simulation once it's been off-chart for `ChartConfig.ChartRetirementWeeksBelowFloor` consecutive weeks, bounding the per-tick cost of a 50-year run instead of recomputing thousands of long-dead releases forever). |
| `Group` (Phase 3a/3b additions) | `ReleaseCadenceMonths` and `NextReleaseDate` — `ReleaseScheduler`'s memory of this group's comeback rhythm, seeded once by `GroupGenerator` at world-gen time (a stateless `ISimSystem` can't remember it itself). `WeeksSinceLastCharted` — `FandomSystem`'s memory of how long a group has gone quiet, for decay's inactivity grace period. `ConsecutiveRookieYears` (Phase 3b) — `IndustryChurnSystem`'s memory of a failing group, for the same reason. |

### Data/
| Type | Purpose |
|---|---|
| `NameEntry` | `{ Name, Gender }` — one given name. `Gender` carries a custom `GenderJsonConverter` so content JSON can use the compact `"F"`/`"M"` tag instead of spelling out `"Female"`/`"Male"` on ~750 entries. |
| `WeightedName` | `{ Name, Weight }` — one surname with a relative (not normalised) frequency weight. |
| `WorldData` | All loaded content as explicit named lists (not a dictionary — small fixed count, per CLAUDE.md's convention): `KoreanGivenNames`, `KoreanFamilyNames`, `StageNames`, `GroupNames`, `JapaneseGivenNames`, `ChineseGivenNames`, `ThaiGivenNames`, `TrackTitlePrefixes`/`TrackTitleSuffixes` (Phase 3a). `GetForeignGivenNames(nationality)` returns the right list or null. |
| `WorldDataLoader` | `static WorldData FromJson(string json)` — the only door between raw text and `WorldData`. Takes a string, never a path, so Core stays file-I/O-free. |

### Systems/Generation/
| Type | Purpose |
|---|---|
| `PersonGenerator` | `Generate(rng, birthYear, tier, status, now)` — the literal Phase 2 signature — plus a fuller overload taking an optional `Gender?`, `Position?` and `WorldData` that `GroupGenerator` uses to pin both. Archetype first (one of 8 `PrimaryArchetypes`, excluding Leader/Maknae), then attributes correlated to it via per-archetype bonus/variance tables. Every numeric constant is a `// DESIGN:` comment — none of this is balanced yet. Works with `worldData: null` (falls back to a synthetic name), which is what makes the pure-attribute tests possible without any content file. |
| `GroupGenerator` | Rolls a member count (4–9, weighted 5–7), builds a non-duplicated archetype list (the four unique primaries — MainVocal/MainRapper/MainDancer/Visual — always present once; Lead*/AllRounder fill the rest and may repeat), generates each member, tags exactly one Leader and one Maknae (**always two different people** — see the Phase 2 bugfix below), names the group (retried against every existing group name in the world so two companies never end up with an identical group name), and seeds `Fandom` from a tier-keyed mean. Registers everything it creates directly onto `GameState` as it goes. |
| `WorldGenerator` | `Generate(state, data)` — one deterministic pass: `Company` + 3 centers (player + 2 siblings), the player's one Rookie/Rising group (debuted 1–2 years ago, then deliberately depressed below its tier roll — DESIGN.md's "you get hired" framing), 12 mixed-quality trainees, 2–3 groups per sibling center, and (Phase 3b: grown from 15) `ChartConfig.WorldGroupCount` groups tier-pyramided by `WorldTierShare*` across 25 placeholder outside companies (was 5) with debuts staggered up to `WorldGroupDebutMaxYearsAgo` years back (15, was 10). Call once, right after building a fresh `GameState`, before any ticking. |

### Systems/Chart/ — every tunable number lives in ChartConfig

**`ChartConfig`** is the single class every chart, scheduler, tier, and fandom-stand-in number lives in — plain public fields (not properties: this is a bag of knobs meant to be read/written in bulk), no constants scattered through logic, no magic numbers inline anywhere in `Systems/Chart/` or `Systems/Fandom/`. It hangs off `GameState.ChartConfig` so it serialises with a save and a balance run can swap it wholesale. `ComputeConfigHash()` reflects over every public field (sorted by name so reflection's unspecified enumeration order can't affect the hash), concatenates, and runs a hand-written deterministic FNV-1a — not `object.GetHashCode()`, which .NET doesn't guarantee stable across runtime versions. Every CSV `BalanceRunner` writes records this hash, so two exports can be checked for "were these actually the same config."

All starting values are **deliberately untuned** — see `Docs/BALANCE.md` for the running log and the current known-bad results (Phase 3b's structural fixes measurably overcorrected several metrics from too-flat to too-extreme; that's the next tuning session's starting point, not something either phase tried to fix).

| Type | Tick step | Purpose |
|---|---|---|
| `DecayCurve` | *(pure function, no step)* | `Evaluate(weeksSinceRelease, quality, config)`: `exp(-k·weeks)`, `k` interpolated from quality between `DecayKMin`/`DecayKMax`. 1.0 at week 0, strictly decreasing, higher quality decays strictly slower at every week. **DESIGN flag:** a pure exponential can only ever fall from week 0 — real songs sometimes climb 2–3 weeks first as word of mouth builds. A gamma-shaped curve is the likely replacement; the interface (`weeksSinceRelease`, `quality`, config in, one multiplier out) is stable so that swap stays a one-file change. In `float`, an extreme input (very low quality, ~200+ weeks) legitimately underflows to exactly 0.0 — harmless, since `ChartSystem` retires a release from the floor long before then. |
| `TrackGenerator` | *(pure-ish, no step)* | `Generate(rng, centerTier, composerSkill)` — the literal signature — plus a fuller overload taking `config`/`worldData`. Quality is `NextGaussian` around a tier- and composer-skill-driven mean. Titles: `"{Prefix} {Suffix}"` from `WorldData`'s word bank (2500 combinations from 50×50 words — see Content loading). Falls back to a synthetic `"Untitled ####"` title with no `WorldData`, matching `PersonGenerator`'s no-content path. |
| `ChartSystem` | 4 — Chart simulation | Three passes per tick over every `Release` with `IsCharting == true`: (1) raw BuzzScore per release (quality/fit/tier/promo/fandom terms, no competition/decay/variance yet); (2) competition modifier (sums every *other* release's raw BuzzScore whose own `ReleaseDate` falls within `CompetitionWindowWeeks` of this one's — a debut-timing collision) × `DecayCurve` × random variance → `WeeklyPoints`; (3) rank everyone by `WeeklyPoints` with a fully deterministic comparer (points descending, ties broken by id — so sort-algorithm stability can never matter), assign positions 1..N, drop below `ChartFloorPoints` or past `ChartSize`, append one week of history. `conceptFit`/`trendFit` are flat 50 this phase — Phase 4 owns both; the term is wired in now so the shape doesn't change later. A release retires (`IsCharting = false`) after `ChartRetirementWeeksBelowFloor` consecutive weeks below floor, so a 50-year run doesn't keep recomputing thousands of long-dead releases every week. `ComputeRawBuzz`/`CompetitionModifier`/`NormalizeFandom` are `internal`, not `private` — `[InternalsVisibleTo("KpopManager.Tests")]` lets tests exercise the real formula instead of re-deriving it. |
| `ReleaseScheduler` | 3 — Track quality decay / new releases | Runs `IndustryChurnSystem.Evaluate` first, then drives every **non-player** group whose `NextReleaseDate` is due: generates a `Track` (composer is a real group member ~25% of the time, else an external baseline scaled by center tier), creates a `Release` — snapshotting both `FandomSizeAtRelease` and `GroupTierAtRelease` (Phase 3b: the tier snapshot was missing, so `BalanceRunner`'s CSV reported a release's *current* tier rather than the tier it actually released at) — then schedules the next one: cadence in weeks (tier-interpolated, Rookie fastest/Legendary slowest), nudged a few weeks for season and, for the player's own company only, to mildly avoid a sibling center's same-week release. `ComputePromoSpend` is `internal` for the same testability reason as `ChartSystem`'s formula pieces. **Phase 3b fix 2d:** promo spend is now multiplicative (`PromoSpendBase * PromoSpendCenterTierMult^centerOrdinal * PromoSpendGroupTierMult^groupOrdinal * jitter`), not additive — additive, combined with tier being effectively always Legendary pre-fix-2c, meant every AI release spent 30k–50k regardless of who they were. The player's own group is never touched here — Phase 4's job. |
| `IndustryChurnSystem` | *(not a tick-order step — invoked from `ReleaseScheduler`)* | **Phase 3b fix 2b.** Nothing previously disbanded a group or debuted a new one, so the industry was a fixed cast for 50 years. Runs once a year (gated on `now.Week == 1`), scoped to **world groups only** (home center not in `Company.CenterIds` — the player's own company is never touched): disbands a group after `DisbandFailureYears` consecutive years at Rookie, or rolls `DisbandChanceAtContractEnd` at each `DisbandContractYears`-year contract mark (re-signing extends `ContractExpiry` by another cycle); debuts `NewWorldGroupsPerYearMin`–`Max` new Rookie groups, homed at a random existing world center. Disbanding only ever sets `IsActive = false` — never removed from `GameState.Groups`. Logs to `LogCategory.Rival` (not `Group`/`Notable`, which `TierSystem` already owns exclusively — using the same combination would have polluted `BalanceRunner`'s tier-mobility metric, which counts log entries by that exact filter). |
| `TierSystem` | *(not a tick-order step — see below)* | `Evaluate(state, now)`: computes every active group's composite score (peak/weeks-in-top-10/fandom, reconstructed from `Release.WeeklyPositions` history within a rolling `TierWindowYears`-year window) once per week, up front — then, for each group that's individually *due* (`TierEvaluationIntervalWeeks`-gated on weeks-since-debut), ranks its score as a percentile against that whole cohort and maps the percentile to a tier. **Phase 3b fix 2c:** replaced absolute score thresholds, which broke the moment the chart's scale changed — the pre-fix baseline had every group scoring above the old `TierThresholdLegendary` of 82 (a deliberately mediocre group still scored 91.7), so every group in the actual sim came out Legendary regardless of relative standing. Percentile bands (`TierPct*`, top-down, e.g. top 2% = Legendary) with dead-band hysteresis (`TierHysteresisPct`) applied only to single-tier moves — a real bug was caught here during verification: with the given starting values, `TierHysteresisPct` and `TierPctLegendary` are both exactly `0.02`, so a group at the literal top of its cohort had its promotion-into-Legendary check land exactly on that boundary after the hysteresis nudge and get rejected, every time; restricting hysteresis to single-tier moves (a 2+-tier jump is decisive and shouldn't be second-guessed by a small margin) fixes it. Logs every actual change (`LogCategory.Group`, `LogSeverity.Notable`) — nothing else writes that exact combination, which is what lets `BalanceRunner` count tier changes by counting log entries. |

**Why `TierSystem` isn't its own tick-order step:** DESIGN.md's twelve steps have no slot named for tier mobility, and reordering or extending that fixed spine is exactly the kind of large structural change CLAUDE.md says to report and confirm before making. `TierSystem` is a static, stateless function instead — `FandomSystem` calls `TierSystem.Evaluate` at the very end of its own tick (step 6), which is the point in the week where this week's chart results (step 4, already run) and this week's fandom update are both fresh. `IndustryChurnSystem` follows the identical reasoning, invoked from `ReleaseScheduler` (step 3) instead.

### Systems/Fandom/ — the Phase 3 stand-in

| Type | Tick step | Purpose |
|---|---|---|
| `FandomSystem` | 6 — Fandom update | **Not the real FandomSystem — that's Phase 5.** The minimum needed so charts have something to snowball or decay against: sums each group's `WeeklyPoints` earned *this* calendar week (the last entry in every still-charting release's history, since `ChartSystem` always runs earlier in the same tick) and grows `Fandom.Size`. **Phase 3b fix 1** replaced the growth formula entirely: the original (`growth = FandomGrowthPerPoint * points`, saturating at scale) was purely additive against multiplicative decay, which converges every group toward the same fixed point regardless of history — measured max/min fandom ratio (seed 11111) collapsed from 613x at year 1 to 3.1x by year 50, the opposite of "rewards accumulated success." Growth is now `(Size * FandomGrowthRatePerPoint * points + FandomBootstrapPerPoint * points) * damping` — a proportional term (what makes it compound) plus a small flat bootstrap (so a brand-new group isn't stuck growing by ~nothing), damped only well above the sizes the game currently reaches. A group with no charting release this week increments `WeeksSinceLastCharted`; past `FandomInactivityGraceWeeks`, `Size` decays by `FandomRetentionRateInactive` each week (renamed from `FandomDecayRateInactive` — 0.998 is a retention factor, not a decay rate; the old name read backwards) with no floor, on purpose — a small inactive group shrinking toward nothing is correct, not a bug to guard against. `Sentiment` and `PublicAwareness` stay untouched until Phase 5. Calls `TierSystem.Evaluate` at the end of its own tick — see above. |

**Phase 3b's own overcorrection, for the record:** proportional growth fixed the flat-convergence problem but turned out to be a rich-get-richer mechanic strong enough that `FandomGrowthDampingSize` (25M) never meaningfully engages at the sizes the sim actually reaches (p90 ≈ 4.9M by year 50) — nothing brakes the compounding. Measured p90/p10 fandom ratio at year 50 (seed 11111): **474x** against a >20x target — cleared the floor, landed far past any reasonable ceiling. This is reported, not fixed, per this session's scope; see `Docs/BALANCE.md`.

### Systems/ (tick-order stubs, remaining)
| Type | Tick step | Purpose |
|---|---|---|
| `TickOrder` | — | The single place the twelve-step order is written down. `BuildDefault()` returns the list every `SimEngine` is constructed with. Mirrors the table below; change both together. |
| `StubSystem` | 1, 2, 5, 7–12 | A named, registered step with no behaviour. Must never log and never draw from `GameState.Random` — a stub that consumed a draw would shift every later number, invalidating any seed balanced against it. Carries a `DuePhase` for documentation. |
| `WeekCounterSystem` | *(prepended)* | Phase 1 diagnostic. Logs one `Info` line per week so the harness and the determinism test have real data. |

**Graduating a stub:** write the real system in its own file under `Systems/`, then swap one line in `TickOrder.BuildDefault()` — this is exactly what Phase 3a did for steps 3, 4, and 6 (`new StubSystem("Chart simulation", 3)` became `new ChartSystem()`, etc.).

### A Phase 2 bugfix worth remembering

`GroupGenerator`'s Leader/Maknae tagging originally picked each independently, then skipped the Maknae tag entirely if the two picks happened to be the same person — which is possible (nothing stops the youngest member from also being the best leader-score candidate) and did happen in a 200-group randomized test, producing a group with **no** Maknae. Fixed by excluding the leader from the maknae search, so the maknae falls back to the next-youngest instead. `GroupGeneratorTests.EveryGroup_HasExactlyOneLeaderAndOneMaknae` guards this.

---

## Tick order

One tick = one week. `SimEngine` runs registered `ISimSystem`s in this exact order, then advances `GameState.Date`.

**This order is the spine of the project.** Changing it after systems depend on each other's side effects is painful. Stubs are registered from Phase 1 so the sequence is locked before behaviour exists.

It also fixes the sequence of draws from `GameState.Random`, which is half of what makes a run reproducible. **Reordering steps changes every future random number**, so an existing save will not replay after a reorder.

Written down in exactly one place in code: `Systems/TickOrder.BuildDefault()`. The strings below are the literal `ISimSystem.Name` values, asserted in order by `DeterminismTests.TickOrder_RegistersAllTwelveSteps`.

| # | System | Phase | Status |
|---|---|---|---|
| 0 | *Week counter* — Phase 1 diagnostic, prepended | 1 | **real** |
| 1 | Training & aging | 5 | stub |
| 2 | Scheduled activities | 4 | stub |
| 3 | Track quality decay / new releases | 3 | **real** — `ReleaseScheduler` |
| 4 | **Chart simulation** | 3 | **real** — `ChartSystem` |
| 5 | Music show results | 4 | stub |
| 6 | Fandom update | 5 | **real** (Phase 3 stand-in) — `FandomSystem`, also runs `TierSystem` |
| 7 | Fatigue / health / morale | 4 | stub |
| 8 | Random events | 8 | stub |
| 9 | Rival AI turns | 3 | stub |
| 10 | News generation | 8 | stub |
| 11 | Cash settlement | 6 | stub |
| 12 | Board target check | 6 | stub |

Note step 6's "Phase" column still says 5 — that's DESIGN.md's *real* FandomSystem (Size/Sentiment/PublicAwareness, the full mechanic). Phase 3a's `FandomSystem` only grows/decays `Size`; it's occupying the slot early because charts need *something* to snowball against, not because Phase 5 moved.

---

## Data model

*(Fill in as built. Keep this diagram current — it's the fastest way to re-orient after time away.)*

```
GameState
 ├── Date          SimDate            // current week; SimEngine advances it after every system ticks
 ├── Random        SimRandom          // the one generator; every draw in the sim comes from here
 ├── Log           SimLog             // append-only, sorted by date, the sim's only output channel
 ├── Seed          ulong              // the run is reproducible from this alone
 ├── NextEntityId  int                // id allocator shared by every entity type
 ├── Company       Company            // CenterIds = player's center + its 2 siblings, per DESIGN.md
 ├── ChartConfig   ChartConfig        // every chart/scheduler/tier/fandom tunable, in one place (Phase 3a)
 ├── People        List<Person>       // authoritative, ordered — iterate this
 ├── Groups        List<Group>        // authoritative, ordered — iterate this
 ├── Centers       List<ProductionCenter>  // authoritative, ordered — iterate this
 ├── Tracks        List<Track>        // authoritative, ordered — iterate this (Phase 3a)
 ├── Releases      List<Release>      // authoritative, ordered — iterate this (Phase 3a)
 ├── [index] _peopleById, _groupsById, _centersById, _tracksById, _releasesById
 │             // Dictionary<int,T>, [JsonIgnore], rebuilt by RebuildIndices()
 └── WorldData     WorldData          // [JsonIgnore] — loaded content, reloaded from disk each run, not saved

Company
 └── CenterIds: List<int>            // this company's centers only — NOT the wider industry
      Board { Patience, Targets: List<BoardTarget> }   // placeholder shape, Phase 6

ProductionCenter (in GameState.Centers, looked up by id)
 ├── GroupIds: List<int>
 └── TraineeIds: List<int>

Group (in GameState.Groups, looked up by id)
 ├── MemberIds: List<int>            // ordered — Positions[0] on each member is their primary archetype
 ├── ReleaseIds: List<int>           // ordered (Phase 3a)
 ├── ReleaseCadenceMonths, NextReleaseDate, WeeksSinceLastCharted   // ReleaseScheduler/FandomSystem's memory (Phase 3a)
 └── Fandom { Size: long, Sentiment: float, PublicAwareness: float }

Person (in GameState.People, looked up by id)
 ├── GroupId, CenterId: int          // -1 = none
 ├── Positions: List<Position>       // usually 1–2: archetype, optionally + Leader or Maknae
 └── LanguageProficiency: Dictionary<Nationality,int>   // lookup-only, never iterated for a sim outcome

Track (in GameState.Tracks, looked up by id) — Phase 3a
 └── Quality: float                  // 0–100, hidden; everything in ChartSystem answers to this

Release (in GameState.Releases, looked up by id) — Phase 3a
 ├── WeeklyPositions: List<int>      // index = weeks since release; 0 = unranked that week
 ├── WeeklyPoints: List<float>
 └── IsCharting, WeeksBelowFloor     // ChartSystem's retirement bookkeeping
```

---

## Serialization

Newtonsoft Json (`com.unity.nuget.newtonsoft-json`). Not `System.Text.Json` — Unity support is awkward.

Save format carries a version field from day one. `SimRandom`'s internal state must be serialized or determinism breaks across a save/load boundary.

`SimRandom` is already ready for this: `GetState()` / `SetState()` exchange a `SimRandomState` carrying `State`, `Inc`, `HasGaussianSpare` and `GaussianSpare`. **All four matter.** Dropping the cached Gaussian spare desynchronises every Gaussian drawn after a load, which is the kind of bug that only shows up as "my balanced save plays differently now". `SimRandomStateRoundTrip` tests cover both the integer and the Gaussian path.

Save location: `Application.persistentDataPath`. *(Phase 9.)*

---

## Determinism

Same seed + same inputs = identical history, permanently. Guarded by the determinism test in `KpopManager.Tests`, which must be run every session.

Known hazards: `System.Random`, `UnityEngine.Random`, `DateTime.Now`, `Guid.NewGuid()`, hash-order iteration over `Dictionary` or `HashSet`, and any use of floating-point accumulation order that varies by platform.

> When iterating collections in a way that affects sim outcomes, iterate a `List` in a defined order — never a `Dictionary` or `HashSet`.

This is now load-bearing, not hypothetical: every entity type (`GameState.People`/`Groups`/`Centers`) keeps a `Dictionary<int,T>` lookup index alongside its authoritative `List<T>`. `RebuildIndices()` populates the dictionaries *by iterating the lists*, which is fine (the list order is what's authoritative); nothing anywhere iterates a dictionary's keys or values to decide a sim outcome. `Person.LanguageProficiency` is a `Dictionary<Nationality,int>` for the same reason it's safe: it's read only by a specific known key, never enumerated.

Two more, added in Phase 1:

- **A stub that draws.** `StubSystem.Tick` is empty on purpose. If a placeholder ever consumed a random number, every later draw would shift, and replacing it with real behaviour would silently invalidate every seed balanced against it.
- **`Math.Log` in `NextGaussian`.** `Math.Sqrt` is IEEE-754 exact, but .NET does not guarantee `Math.Log` is bit-identical across runtime versions or CPUs. PC-only on one machine, this is fine. If determinism ever has to hold across machines, replace it with a fixed polynomial approximation — flagged as a `// DESIGN:` comment at the call site.

One more, added in Phase 2:

- **Generation order is part of the seed contract.** `WorldGenerator.Generate` draws from `GameState.Random` in a fixed sequence (company → player group → trainees → rival groups → world groups), and each `GroupGenerator.Generate` call draws member count → archetypes → each member → leader/maknae → name → fandom, in that order. Reordering any of this changes every draw after the reorder point, exactly like reordering the tick order does. `WorldGeneratorTests.SameSeed_ProducesAnIdenticalWorld` guards it. This is also why the Harness's **Generate World** button always rebuilds the engine from the seed first (`NewGame()`) rather than generating into whatever state the window happens to be in — ticking before generating would have been a different, equally valid, but *different* deterministic sequence.

Two more, added in Phase 3a:

- **A fully deterministic chart ranking, even with ties.** `ChartSystem` ranks releases with a comparer that never actually ties — points descending, then release id ascending — so the outcome cannot depend on whichever sort algorithm `Array.Sort` happens to use internally. A comparer that leaves real ties on the table (e.g. comparing only on points, with two releases at the exact same value) would hand the outcome to sort-algorithm stability, which .NET doesn't guarantee.
- **`ChartConfig.ComputeConfigHash()` sorts its reflected fields by name first.** `Type.GetFields()`'s enumeration order isn't documented or guaranteed stable, so hashing fields in *reflection* order would make the same config potentially hash differently across runtime versions — exactly the same category of hazard as `Dictionary` iteration order, just for a type's own members instead of a collection's.

---

## Editor harness

`KpopManager/Harness` opens `HarnessWindow`, the only interface to the game until Phase 7. UI Toolkit (`CreateGUI` + `rootVisualElement`), not IMGUI — the log needs a virtualised `ListView` to stay responsive after a 50-year run, and it is practice for the API Phase 7 uses. Two tabs, toggled by a pair of buttons that show/hide two pre-built content roots (no `Toolbar`/`TabView` control — two buttons was simpler and just as clear):

**Log tab** (Phase 1, unchanged): Seed field · New Game · Tick Week · Tick Year · Run N Years · date and elapsed-week readout · per-category filter toggles · minimum-severity dropdown · Clear Log · a collapsed Tick Order list · a timing readout in ms and ms/week for the last batch.

**World tab** (Phase 2): a **Generate World** button, then a left/right split (two `ScrollView`s in a row — not `TwoPaneSplitView`, to keep the API surface small) —
- **Left:** one collapsible `Foldout` per center (Centers → Groups → Members), each holding a nested Trainees table (player center only) and one nested Foldout per group holding a member table, plus an **Industry** section listing every group in the game sorted by tier then fandom size.
- **Right:** the **Person Detail** panel — click any person row (or a group row, for a lighter summary) to populate it. Trainee/member tables show only the *visible* attribute categories (Performance/Star/Creative, 11 columns) per DESIGN.md's own taxonomy; Hidden stats, including `Potential`, are reserved for this detail panel.

Tables are hand-built rows of `Label`s inside a plain (non-virtualised) `VisualElement`, not `MultiColumnListView` or `TreeView` — entity counts here are a few hundred at most, nowhere near the tens-of-thousands the Log tab's `ListView` has to survive, so virtualisation isn't a functional requirement and the simpler, lower-API-risk primitives (`Foldout`, `ScrollView`, `ClickEvent`) were enough.

**Domain reload.** `GameState` is not a Unity-serialisable type, so the engine cannot survive a reload directly. Instead the window keeps `[SerializeField]` copies of the facts that define a run — the seed, whether a world was generated, and the number of weeks simulated — and rebuilds by replaying: construct from the seed, regenerate the world if `_hasWorld`, then re-tick to where it was. Determinism makes the replay exact, and even a 50-year replay plus a full world generation costs a couple of milliseconds, so it is imperceptible. One wrinkle: a cleared log comes back after a reload, because the replay regenerates it.

**Generate World always calls New Game first.** Pressing it rebuilds the engine from the current seed before generating, rather than generating into whatever state the window happens to be in. This is what makes "same seed → identical world" hold regardless of how much ticking happened before the button was pressed.

---

## Balance tooling (Phase 3a)

`KpopManager/Run Balance Simulation` and `KpopManager/Run Balance Sweep` (`BalanceRunner`, `Assets/Editor/`) — headless analysis, no window, output straight to the Console and to `<project root>/SimOutput/` (sibling of `Assets/`, already in `.gitignore`).

Both tick **week-by-week** (not `AdvanceYears(1)` in a loop, as Phase 3a had it) so `chart_occupancy` can sample once per simulated week, and so year-boundary snapshots (`Fandom.Size` + `GroupTier` per group, used by Persistence and the fandom-ratio/tier-distribution diagnostics) land on exactly the right week regardless of how many years are requested.

**Run Balance Simulation:** builds a world from seed 12345, runs 50 years, prints nine metrics with a PASS/HIGH/LOW verdict against a target band (plus PromoSpend p5/p50/p95 and an occupancy summary), and writes three CSVs stamped with the seed, `ChartConfig.ComputeConfigHash()`, and a timestamp:
- `releases_s{seed}_cfg{hash}_{timestamp}.csv` — one row per release (`ReleaseId, GroupId, GroupName, GroupTierAtRelease, ReleaseYear, ReleaseWeek, TrackQuality, PromoSpend, FandomSizeAtRelease, PeakPosition, WeeksCharted, WeeksInTop10, TotalPoints`). Phase 3b renamed the tier column from `GroupTier` to `GroupTierAtRelease` after catching that it was reading the group's *current* tier for every historical row.
- `chart_history_s{seed}_cfg{hash}_{timestamp}.csv` — one row per release per week, for plotting decay shapes.
- `chart_occupancy_s{seed}_cfg{hash}_{timestamp}.csv` (Phase 3b) — one row per simulated week (`Year, Week, ActiveGroups, ActiveReleases, ChartedReleases, Top10Entries, PointsAtPos1, PointsAtPos10, PointsAtPos100`), so contestation is visible directly instead of inferred from the release-level CSVs.

**Run Balance Sweep:** the same run across five fixed seeds (`11111, 22222, 33333, 44444, 55555` — not tuned, just different enough to catch a formula that only passes by luck on one seed), then a summary: mean, min–max range, and how many of the five seeds actually clear each metric's target band.

**The nine metrics** (`BalanceMetrics`: dependency-free `Gini`, `PearsonR`, `SpearmanRho`, `Percentile`):

| Metric | Computation | Target |
|---|---|---|
| #1 concentration | Gini over every group's own #1-chart-week count (including groups that never hit #1) | 0.45–0.70 |
| Hit longevity | Mean `WeeksInTop10` across releases that reached top 10 at all — **marked provisional in the printed report** (see below) | 4–12 |
| Quality → Peak | Pearson r, `Track.Quality` vs `Release.PeakPosition` | −0.40 to −0.15 |
| Quality → Longevity | Pearson r, `Track.Quality` vs `WeeksInTop10` | > 0.55 |
| Tier mobility | Count of `LogCategory.Group`/`LogSeverity.Notable` entries (only `TierSystem` writes that combination) per decade | 8–20 |
| Persistence | Spearman rho of every group's `Fandom.Size` at year 5 vs the final year | 0.3–0.7 |
| Top-10 rate (Phase 3b) | % of all releases that ever reached top 10 | 5–15% |
| #1 rate (Phase 3b) | % of all releases that ever reached #1 | 0.5–3% |
| Fandom spread (Phase 3b) | p90/p10 ratio of active groups' `Fandom.Size` at the run's final year | > 20 |

**Hit longevity is provisional** because Phase 3a's baseline (mean 20.9 weeks charted, 99.8% of releases reaching top 10) was passing against a chart with no real scarcity — the metric wasn't measuring anything. Phase 3b grew the world specifically to fix that, and the band may need revalidating once the chart is *correctly* (not over-) contested.

**This tool does not tune anything** — Phase 3a's job was building the instruments; Phase 3b's was fixing two structural root causes (see `Systems/Chart/` and `Systems/Fandom/` above) and extending the instruments to actually measure contestation directly. Neither phase touched the numbers to make them look good. See `Docs/BALANCE.md` for the full sweep output from both phases, including Phase 3b's measured overcorrection.

---

## Content loading (Phase 2)

Core has no file I/O, so it cannot read `Assets/SimData/*.json` itself. The split:

1. `KpopManager.Editor.Generation.ContentLoader.LoadWorldData()` reads the five files under `Assets/SimData/` off disk, parses each with `Newtonsoft.Json.Linq` (`JObject`/`JArray`), and merges them into one JSON object shaped exactly like `WorldData` (each source file's shape already matches one — or for `foreign-names.json`, three — `WorldData` properties directly, so this is a plain merge, no reshaping).
2. That combined string goes to `KpopManager.Core.WorldDataLoader.FromJson(string json)` — Core's only entry point for content, taking a string and never a path.

Content files (`Assets/SimData/`, not under `Assets/Sim/` — they aren't C# and don't need an asmdef folder):

| File | Shape | Count |
|---|---|---|
| `korean-given-names.json` | `[{ name, gender }]` | 200 |
| `korean-family-names.json` | `[{ name, weight }]` | 61, weighted so Kim/Lee/Park dominate (~21.5/14.7/8.4, approximating real census frequency) |
| `stage-names.json` | `[string]`, single-word | 109 |
| `group-names.json` | `[string]` | 150 |
| `foreign-names.json` | `{ japanese: [...], chinese: [...], thai: [...] }`, each `[{ name, gender }]` | ~40 each |
| `track-titles.json` (Phase 3a) | `{ prefixes: [string], suffixes: [string] }` | 50 × 50 = 2500 combinations |

American/Other nationalities (~5% of the population combined) aren't covered by a content file — `PersonGenerator` falls back to a small embedded name list for those two rather than asking for two more JSON files over such a small slice.

The Korean given-name list is generated (not hand-typed one by one) by combining real, commonly-used romanized syllable blocks two at a time — genuinely how the overwhelming majority of Korean given names are formed — then deduplicated. Family names, stage names, group names, and foreign given names are hand-curated.

---

## Open technical questions

- ~~Does the harness `SimEngine` instance survive Unity domain reload?~~ **Answered in Phase 1:** it does not, and it does not need to. The window serialises seed + weeks-elapsed and replays. See *Editor harness* above.
- Attribute storage: named fields vs enum-keyed array. Currently named fields for debuggability; revisit if the count grows past ~20.
- **Girl groups vs boy groups vs co-ed** (DESIGN.md's own open question) — still unresolved. `Person.Gender` and `Group.Gender` exist and `GroupGenerator` keeps a group single-gender, but which gender (or a co-ed mix) is never decided; it's rolled 50/50 per group. Revisit before Phase 4 needs a definite answer for the player's own group.
- Some Phase 2 trainees generate with a flat **0** in an attribute (a raw 15-year-old, tier 0, can roll below the clamp floor often enough to hit it exactly). Mathematically consistent with "mixed quality," but worth a look during balancing — it may read as more of a floor-effect artifact than an intentional "genuinely has nothing going for them yet" case.
- **`ReleaseScheduler`'s seasonality and sibling-avoidance nudges are coarse heuristics** (a fixed few-week push/pull), not a real model of either. Flagged in `ReleaseScheduler`'s own DESIGN comment; revisit if release timing needs to read as more than "roughly clustered."
- **Untuned balance results, Phase 3a → 3b:** Phase 3a's baseline (seed 12345/sweep) passed 5/6 metrics but for the wrong reasons — the chart was never a fifth full, so several "passes" were measuring an absence of scarcity, not health. Phase 3b's structural fixes (proportional fandom growth, a 200-group world with yearly churn, percentile-ranked tiers, multiplicative promo spend) fixed the structural causes but measurably overcorrected: #1 concentration, Persistence, and Fandom spread swung from too-flat to too-extreme (Gini ~0.90, Persistence ~0.87, fandom p90/p10 ~289x against a >20 target); Hit longevity, Top-10 rate, #1 rate, and Tier mobility swung from too-uncontested to way-overcontested (tier mobility ~233/decade against an 8–20 target — roughly 12x over). Every number is internally consistent with the mechanism that produced it — full explanation and the complete sweep output is in `Docs/BALANCE.md`'s Phase 3b entry. This is the actual tuning session's starting point.
- **`FandomGrowthDampingSize` doesn't engage.** Set to 25,000,000 on the assumption fandom sizes would approach it; measured p90 at year 50 is ~4.9M, nowhere close, so the "brake against runaway" fix 1 was supposed to include never actually brakes anything at the scales the sim reaches.
- **Percentile-ranked tiers are sensitive to cohort composition, not just individual performance.** Combined with yearly churn injecting 8–24 new Rookies into the cohort, everyone else's relative percentile shifts every year even when their own score hasn't changed — the likely explanation for tier mobility's ~12x overshoot. Untested hypothesis; would need isolating churn's contribution from the world-size increase's to confirm.
