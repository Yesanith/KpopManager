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
│   ├── Chart/                — ChartConfig, ChartSystem, TrackGenerator, ReleaseScheduler,
│   │                           TierSystem (Phase 3a); IndustryChurnSystem (Phase 3b).
│   │                           DecayCurve retired in Phase 3b iteration 2 — see below.
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
| `Release` | One comeback's full history: `WeeklyPositions`/`WeeklyPoints` (index = weeks since release, 0 = unranked), `PeakPosition`, `WeeksInTop10`, `WeeksCharted`, `TotalPoints`, `FandomSizeAtRelease` and `GroupTierAtRelease` (fixed snapshots, since the live values keep moving — the tier snapshot was missing in Phase 3a, caught as a real CSV-export bug in Phase 3b). `MusicShowWins` stays 0 until Phase 4. `IsCharting`/`WeeksBelowFloor` (not in the original brief — added so `ChartSystem` can retire a release from active simulation once it's been off-chart for `ChartConfig.ChartRetirementWeeksBelowFloor` consecutive weeks, bounding the per-tick cost of a 50-year run instead of recomputing thousands of long-dead releases forever). **Phase 3b iteration 2:** `FandomPull`, `PublicAppeal`, `CrossoverMultiplier` — the two-curve trajectory model's release-time-fixed inputs (see `ChartSystem` below), stored on the release rather than recomputed weekly so a CSV export can classify what kind of release this was without re-deriving it. |
| `Group` (Phase 3a/3b additions) | `ReleaseCadenceMonths` and `NextReleaseDate` — `ReleaseScheduler`'s memory of this group's comeback rhythm, seeded once by `GroupGenerator` at world-gen time (a stateless `ISimSystem` can't remember it itself). `WeeksSinceLastCharted` — `FandomSystem`'s memory of how long a group has gone quiet, for decay's inactivity grace period. `ConsecutiveRookieYears` (Phase 3b) — `IndustryChurnSystem`'s memory of a failing group, for the same reason. `DisbandDate` (nullable, Phase 3b iteration 2) — set once by `IndustryChurnSystem.Disband`; null while active. Added so `BalanceRunner` can report a real group-lifespan distribution instead of only "still going or not," since a group's age at disband is otherwise lost the instant `IsActive` flips to false. |

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
| `TrackGenerator` | *(pure-ish, no step)* | `Generate(rng, centerTier, composerSkill)` — the literal signature — plus a fuller overload taking `config`/`worldData`. Quality is `NextGaussian` around a tier- and composer-skill-driven mean. Titles: `"{Prefix} {Suffix}"` from `WorldData`'s word bank (2500 combinations from 50×50 words — see Content loading). Falls back to a synthetic `"Untitled ####"` title with no `WorldData`, matching `PersonGenerator`'s no-content path. |
| `ChartSystem` | 4 — Chart simulation | **Phase 3b iteration 2 replaced the single BuzzScore-times-decay formula with two independent trajectory components**, because a Billboard-shaped single curve can't produce Korean-chart behaviour (see `DESIGN.md`'s chart section for the full "why"). `FandomPull` and `PublicAppeal` are computed once at release time (by `ReleaseScheduler`) and stored on `Release`; every week `ChartSystem` decays each by its own independent rate (`FandomDecayK` steep, `PublicDecayK` shallow), sums them, and applies tier/promo boosts, competition, and variance. `conceptFit`/`trendFit` are still flat 50 this phase — Phase 4 owns both. `ComputeFandomPull`/`ComputePublicAppeal`/`RollCrossoverMultiplier`/`ComputeUndecayedStrength`/`ComputeWeeklyPoints`/`BuildCurve`/`CompetitionModifier`/`NormalizeFandom`/`ComputeReentryCutoffPoints`/`RequiresReentryMargin` are `internal`, not `private` — `[InternalsVisibleTo("KpopManager.Tests")]` lets tests exercise the real formula instead of re-deriving it. **`DecayCurve` (the single quality-interpolated exponential) is retired** — its role is now split between the two independent decay rates above.<br><br>**Phase 3b iteration 3** rewrote two pieces the 21-group world's calibration didn't survive scaling, plus added one-directional re-entry hysteresis:<br>— **Fix 1, `CompetitionModifier`:** sum-based → share-based. The original `1/(1+c·sumOfRivalStrength/100)` scales with however many releases happen to be in the window — growing the world (iteration 2) silently divided every release's points by another ~30x on top of iteration 2's own compression, collapsing the whole points scale to a 2.19–10.54 range where a two-week fandom half-life couldn't survive a single week without falling out. Replaced with `1/(1+c·ratio·crowdFactor)`, `ratio = rivalMeanStrength/ownStrength` (population-size-independent), `crowdFactor = clamp(rivalCount/CompetitionExpectedRivals, CompetitionCrowdFactorMin, CompetitionCrowdFactorMax)` — invariant to world size as long as `CompetitionExpectedRivals` scales with it (`ChartSystemTests.CompetitionModifier_IsScaleInvariant...` locks this in: 100 vs. 200 world groups, expected rivals scaled 12→24, measured ratio 1.0084 — within 1%, well inside the required 10%). Measured mean/p5/p95 post-fix: 0.642/0.457/0.938 — comfortably in the "roughly 0.2–1.0" sane range, nowhere near the old formula's measured ~0.01 collapse.<br>— **Fix 2, retirement:** absolute-points-floor-based → chart-presence-based. `ChartFloorPoints` no longer bore any relation to what it actually took to chart once competition and world size moved (iteration 2 measured 1,277 mean `ActiveReleases`/week because releases sat well above the floor for ~200 weeks at `PublicDecayK`'s shallow rate while still failing to rank against everyone else). `ChartRetirementWeeksOffChart` (12 consecutive weeks with no charted position, generous enough that a slow-building crossover release can climb back in) plus a hard `ChartMaxSimulatedWeeks` (350, since Melon's real long-runners top out ~341) replace the old `ChartRetirementWeeksBelowFloor`. Measured: `ActiveReleases` mean dropped 1,277 → 237.70, and sweep runtime dropped ~14s → ~2s per seed, roughly 7x.<br>— **Fix 4, re-entry margin:** a release that was off-chart last week must beat this week's natural rank-`ChartSize` cutoff (computed from the plain, non-margin ranking, via `ComputeReentryCutoffPoints`) by `ChartReentryMarginPct` (15%) to come back — one-directional hysteresis identical in shape to `TierSystem`'s `TierHysteresisPct`, gating re-entry only, never continued residency or a fresh debut (`RequiresReentryMargin` returns false for both). Fixes the boundary-flicker pattern iteration 2 measured directly ("58, 0, 77, 93, 0" — a release oscillating on random variance alone, not doing anything). `Release.WeeklyCompetitionModifiers` (new, index-aligned with `WeeklyPositions`/`WeeklyPoints`) makes the modifier's real distribution directly measurable instead of needing an after-the-fact reconstruction that would require each week's *historical* live group tier — not otherwise recoverable once tier has since changed.<br><br>**Phase 3b iteration 4** restored the fandom-spike population iteration 3 accidentally deleted, and fixed crossover's magnitude and quality-gating:<br>— **Fix 1:** `FandomPullScale` 1.0 → 2.8. At 1.0, `NormalizeFandom`'s 100-point cap meant FandomPull maxed at 100 — below iteration 3's measured ~149-point position-10 cutoff, so no fandom size, however large, could buy a top-10 debut any more (iteration 3's own highest-FandomPull sample peaked at #10 and slid gently for 13 weeks — an ordinary song, not "total attack"). **Do not change `FandomDecayK`** — the 2-week half-life is the mechanism; only the amplitude was wrong.<br>— **Fix 2:** `CrossoverMultiplierMin`/`Max` 2.5–6.0 → 1.8–3.2. The old range, with `PublicDecayK`'s unchanged shallow decay, let a crossover hit sit in the top 5 for ~50 consecutive weeks (iteration 3's "Rainy Daylight") — the goal is a long CHART tail (which `PublicDecayK` alone already produces), not a year owning the top 10.<br>— **Fix 3:** `CrossoverChanceBase`/`CrossoverChancePerQuality` 0.03/0.0006 → 0.008/0.0018. The old coefficients made a Q=100 track (6%) barely more likely to cross over than a Q=50 track (3%) — a near-quality-independent coin flip, which is why `Quality -> Longevity` stuck near 0.09 for three iterations. New coefficients: Q=50 → 0.8%, Q=100 → 9.8%. Measured effect: crossover rate by quality decile now rises monotonically (0.74% at Q1–38.5 to 6.28% at Q77–100).<br>— **Fix 4 (DESIGN):** `ComputePublicAppeal`'s quality term became `pow(quality/100, PublicAppealQualityExponent=1.6) * 100`, not linear quality — a linear term let a Q=20.6 track reach #8 (iteration 3's "Chrome Prologue"). Measured effect: the lowest-quality release ever reaching top 10 stayed at Q≈2 (a fandom spike can still buy a brief appearance — realistic, unaffected by this fix), but the lowest-quality release HOLDING top 10 for 3+ weeks rose from ~Q20.6 (iteration 3) to Q34.7 — confirms the fix separates "can spike" from "can sustain" exactly as intended.<br><br>**Measured overshoot, iteration 4:** all four fixes work correctly in isolation (see above), but in aggregate `FandomPullScale=2.8` let far more of the population spike than the derivation in the fix's own comment assumed — `NormalizeFandom`'s log10 compression means an "ordinary" fandom size and a "giant" one aren't nearly as far apart in normalized (0-100) terms as a linear points estimate suggests. Measured Distinct-top-10-entrants/year: ~456 against an estimated 50-80 target (~6-9x over); Top-10 rate ~72% against an 8-13% target; Longevity skew stayed LOW (~0.12 against >0.25) because the aggregate distribution is now dominated by a much larger one-week-spike population, not because the spike/crossover split itself failed. `FandomPullScale` and `NormalizeFandom` are both frozen this session — reported, not corrected. See `Docs/BALANCE.md`'s iteration 4 entry for the full sweep. |
| `ReleaseScheduler` | 3 — Track quality decay / new releases | Runs `IndustryChurnSystem.Evaluate` first, then drives every **non-player** group whose `NextReleaseDate` is due: generates a `Track` (composer is a real group member ~25% of the time, else an external baseline scaled by center tier), rolls `RollCrossoverMultiplier` from the track's quality, computes `FandomPull`/`PublicAppeal` and stores them on the new `Release` alongside a `CrossoverMultiplier`, snapshots `FandomSizeAtRelease`/`GroupTierAtRelease` — then schedules the next one: cadence in weeks (tier-interpolated, Rookie fastest/Legendary slowest), nudged a few weeks for season and, for the player's own company only, to mildly avoid a sibling center's same-week release. `ComputePromoSpend` is `internal` for the same testability reason as `ChartSystem`'s formula pieces. **Phase 3b fix 2d:** promo spend is multiplicative (`PromoSpendBase * PromoSpendCenterTierMult^centerOrdinal * PromoSpendGroupTierMult^groupOrdinal * jitter`), not additive. The player's own group is never touched here — Phase 4's job. |
| `IndustryChurnSystem` | *(not a tick-order step — invoked from `ReleaseScheduler`)* | **Phase 3b fix 2b**, revised by **iteration 2 fix 3**. Runs once a year (gated on `now.Week == 1`), scoped to **world groups only** (home center not in `Company.CenterIds`): disbands a group after `DisbandFailureYears` consecutive years at Rookie, or rolls `DisbandChanceAtContractEnd` at each `DisbandContractYears`-year contract mark, and records `Group.DisbandDate` (iteration 2 addition, so lifespan is measurable after the fact — see the `Group` entity entry above). Debut volume is **target-seeking, not a fixed random range**: `BaselineNewGroupsPerYear + clamp((TargetActiveWorldGroups - activeWorldGroups) * DebutRateCorrectionGain, 0, MaxNewWorldGroupsPerYear)` — the fixed `NewWorldGroupsPerYearMin`/`Max` roll it replaced let disbanding outpace debuting badly (iteration 1 measured mean `ActiveGroups` 114.6 against a 200 target); the target-seeking version measures 170-188 active world groups across years 1/10/25/50 of seed 12345, much closer to (if still slightly under) the 200 target. Disbanding only ever sets `IsActive = false` — never removed from `GameState.Groups`. Logs to `LogCategory.Rival` (not `Group`/`Notable`, which `TierSystem` owns exclusively). |
| `TierSystem` | *(not a tick-order step — see below)* | Untouched this session (explicitly out of scope for iteration 2 — see `Docs/BALANCE.md`). `Evaluate(state, now)`: computes every active group's composite score (peak/weeks-in-top-10/fandom, reconstructed from `Release.WeeklyPositions` history within a rolling `TierWindowYears`-year window) once per week, up front — then, for each group that's individually *due* (`TierEvaluationIntervalWeeks`-gated on weeks-since-debut), ranks its score as a percentile against that whole cohort and maps the percentile to a tier, with dead-band hysteresis (`TierHysteresisPct`) applied only to single-tier moves (see the Phase 3b iteration 1 bug note this doc used to carry, now folded into `TierSystem.cs`'s own comments). Logs every actual change (`LogCategory.Group`, `LogSeverity.Notable`). |

**Why `TierSystem` isn't its own tick-order step:** DESIGN.md's twelve steps have no slot named for tier mobility, and reordering or extending that fixed spine is exactly the kind of large structural change CLAUDE.md says to report and confirm before making. `TierSystem` is a static, stateless function instead — `FandomSystem` calls `TierSystem.Evaluate` at the very end of its own tick (step 6), which is the point in the week where this week's chart results (step 4, already run) and this week's fandom update are both fresh. `IndustryChurnSystem` follows the identical reasoning, invoked from `ReleaseScheduler` (step 3) instead.

### Systems/Fandom/ — the Phase 3 stand-in

| Type | Tick step | Purpose |
|---|---|---|
| `FandomSystem` | 6 — Fandom update | **Not the real FandomSystem — that's Phase 5.** The minimum needed so charts have something to snowball or decay against: sums each group's `WeeklyPoints` earned *this* calendar week (the last entry in every still-charting release's history, since `ChartSystem` always runs earlier in the same tick) and grows `Fandom.Size`. **Phase 3b fix 1** replaced the growth formula entirely: the original (`growth = FandomGrowthPerPoint * points`, saturating at scale) was purely additive against multiplicative decay, which converges every group toward the same fixed point regardless of history — measured max/min fandom ratio (seed 11111) collapsed from 613x at year 1 to 3.1x by year 50, the opposite of "rewards accumulated success." Growth is now `(Size * FandomGrowthRatePerPoint * points + FandomBootstrapPerPoint * points) * damping` — a proportional term (what makes it compound) plus a small flat bootstrap (so a brand-new group isn't stuck growing by ~nothing), damped only well above the sizes the game currently reaches. A group with no charting release this week increments `WeeksSinceLastCharted`; past `FandomInactivityGraceWeeks`, `Size` decays by `FandomRetentionRateInactive` each week (renamed from `FandomDecayRateInactive` — 0.998 is a retention factor, not a decay rate; the old name read backwards) with no floor, on purpose — a small inactive group shrinking toward nothing is correct, not a bug to guard against. `Sentiment` and `PublicAwareness` stay untouched until Phase 5. Calls `TierSystem.Evaluate` at the end of its own tick — see above. |

**Phase 3b's own overcorrection, for the record:** proportional growth fixed the flat-convergence problem but turned out to be a rich-get-richer mechanic strong enough that `FandomGrowthDampingSize` (25M) never meaningfully engages at the sizes the sim actually reaches — nothing brakes the compounding. Iteration 1 measured p90/p10 fandom ratio at year 50 (seed 11111): 474x against a >20x target. **Iteration 2 made this dramatically worse without touching a single fandom-growth field**, exactly as its own brief warned it might ("overcorrected and will move when the trajectory model changes"): the two-curve model plus the release-volume fix pushed releases/year from ~19.5 to ~621, meaning vastly more `WeeklyPoints` flow into `FandomSystem.ApplyGrowth`'s unchanged proportional term every week. Measured 5-seed mean fandom spread (p90/p10, year 50): **5150x** (range 2802x-12140x) — over 10x worse than iteration 1. Untouched this session per the iteration 2 brief's own constraint; whatever tuning pass finally touches `FandomGrowthRatePerPoint`/`FandomGrowthDampingSize` needs to account for release volume, not just chart points per release. See `Docs/BALANCE.md`.

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
 ├── WeeklyCompetitionModifiers: List<float>   // Phase 3b iteration 3, index-aligned with the above
 └── IsCharting, WeeksOffChart       // ChartSystem's retirement bookkeeping (renamed from
                                     // WeeksBelowFloor in iteration 3 — counts off-chart weeks now)
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

## Balance tooling (Phase 3a, extended 3b)

`KpopManager/Run Balance Simulation` and `KpopManager/Run Balance Sweep` (`BalanceRunner`, `Assets/Editor/`) — headless analysis, no window, output straight to the Console and to `<project root>/SimOutput/` (sibling of `Assets/`, already in `.gitignore`).

Both tick **week-by-week** (not `AdvanceYears(1)` in a loop, as Phase 3a had it) so `chart_occupancy` can sample once per simulated week, and so year-boundary snapshots (`Fandom.Size` + `GroupTier` per group, used by Persistence and the fandom-ratio/tier-distribution diagnostics) land on exactly the right week regardless of how many years are requested.

**Run Balance Simulation:** builds a world from seed 12345, runs 50 years, prints every metric below with a PASS/HIGH/LOW verdict against a target band (diagnostics print with no verdict), plus PromoSpend p5/p50/p95 and an occupancy summary, and writes three CSVs stamped with the seed, `ChartConfig.ComputeConfigHash()`, and a timestamp:
- `releases_s{seed}_cfg{hash}_{timestamp}.csv` — one row per release (`ReleaseId, GroupId, GroupName, GroupTierAtRelease, ReleaseYear, ReleaseWeek, TrackQuality, PromoSpend, FandomSizeAtRelease, FandomPull, PublicAppeal, CrossoverMultiplier, PeakPosition, WeeksCharted, WeeksInTop10, TotalPoints`). The three iteration-2 columns let a release be classified (fandom-driven blip vs. crossover long-runner) directly from the CSV.
- `chart_history_s{seed}_cfg{hash}_{timestamp}.csv` — one row per release per week, for plotting trajectory shapes.
- `chart_occupancy_s{seed}_cfg{hash}_{timestamp}.csv` — one row per simulated week (`Year, Week, ActiveGroups, ActiveReleases, ChartedReleases, DistinctTop10EntrantsThisWeek, PointsAtPos1, PointsAtPos10, PointsAtPos100`). **Iteration 2 fix 2c** renamed `Top10Entries` to `DistinctTop10EntrantsThisWeek` and changed what it counts — the old column always read exactly 10.00 (the top 10 has ten slots by definition, so it measured nothing); the new one counts releases in the top 10 this week that weren't there last week, a real churn signal.

**Run Balance Sweep:** the same run across five fixed seeds (`11111, 22222, 33333, 44444, 55555`), then a summary: mean, min–max range, and how many of the five seeds actually clear each metric's target band.

**Metrics with a target band** (`BalanceMetrics`: dependency-free `Gini`, `PearsonR`, `SpearmanRho`, `Percentile`):

| Metric | Computation | Target |
|---|---|---|
| #1 concentration | Gini over every group's own #1-chart-week count | 0.82–0.94 (iteration 3 fix 3a; was 0.45–0.70) |
| Top-decile #1 share (iteration 3 fix 3b) | Fraction of every #1 week held by the top 10% of groups, ranked by their own #1-week count | 0.55–0.80 |
| Hit longevity median (iteration 2 fix 2a, revised iteration 4) | Median `WeeksInTop10` across releases that reached top 10 | 3–8 (was 2–5) |
| Hit longevity p95 (iteration 2 fix 2a, revised iteration 4) | p95 `WeeksInTop10` across releases that reached top 10 | 30–55 (was 25–60) |
| Hit longevity mean (promoted to a band, iteration 4) | Mean `WeeksInTop10` across releases that reached top 10 — was an unbanded diagnostic through iteration 3 | 6–11 |
| Longevity skew (iteration 4, new) | `(mean − median) / mean` over `WeeksInTop10` — a plain skew check; a long-tailed two-population distribution should read clearly positive | > 0.25 |
| Distinct top-10 entrants / year (iteration 4, new) | Count of releases whose FIRST top-10 week falls in a given year, summed and divided by run length | 50–80 |
| Quality → Peak | Pearson r, `Track.Quality` vs `Release.PeakPosition` | −0.40 to −0.15 |
| Quality → Longevity | Pearson r, `Track.Quality` vs `WeeksInTop10` | > 0.55 |
| Tier mobility per active group (iteration 2 fix 2b) | (`LogCategory.Group`/`LogSeverity.Notable` entries per decade) ÷ mean `ActiveGroups` | 0.4–1.5 |
| Persistence | Spearman rho of every group's `Fandom.Size` at year 5 vs the final year | 0.3–0.7 |
| Top-10 rate (iteration 2 fix 2e, revised iteration 4) | % of all releases that ever reached top 10 | 8–13% (was 4–10%, originally 5–15%) |
| #1 rate | % of all releases that ever reached #1 | 0.5–3% |
| Fandom spread | p90/p10 ratio of active groups' `Fandom.Size` at the run's final year | > 20 |

**Why #1 concentration's band moved (iteration 3 fix 3a):** 0.45–0.70 was calibrated against a 21-group world where most groups had a realistic #1 shot. At ~187 active groups and ~30 distinct #1 songs/year, the overwhelming majority of groups will never see #1 regardless of how healthy the sim is — Gini over that population is forced up near 0.9 by population size alone, which is also what the real industry looks like. Top-decile #1 share exists specifically because Gini can't be trusted to distinguish "healthy but large population" from "the winners' circle is itself a monopoly" — it measures the latter directly.

**Why the longevity/Top-10-rate bands moved again (iteration 4):** iteration 3's 2–5/25–60/4–10% bands were guesses (never validated) and came out wrong in the opposite direction once flicker was fixed — measured median ~27.7, Top-10 rate ~3.2% (implying only ~20 distinct songs/year ever touch the top 10, against a real Melon-scale estimate of 50–80). The iteration 4 bands are a **derivation from that 50–80 estimate run through the 520 slot-weeks identity**, not a fresh measurement — flagged explicitly in `Docs/BALANCE.md` as revisable, and the estimate itself has never been checked against a real chart data source.

**Diagnostics — reported, no target band** (iteration 2 fix 2a/2b/2d, iteration 3 fix 1/4; `MetricValue.IsDiagnostic` skips PASS/HIGH/LOW and the sweep's pass-count column entirely rather than scoring against a meaningless band):

| Diagnostic | Why it's unbanded |
|---|---|
| Tier mobility raw (changes/decade) | Doesn't account for population size; the per-group version above carries the real target. |
| Releases per year | Feeds the 520 slot-weeks identity check (see `Docs/BALANCE.md`); not itself a balance target. |
| Group lifespan mean/p10/p50/p90 (years) | Reads `Group.DisbandDate` (iteration 2 addition) against still-active groups right-censored at the run's final date. Informs `DisbandFailureYears`, not a target itself. |
| Crossover rate (% of releases) | A roll rate (`CrossoverChanceBase`/`CrossoverChancePerQuality`), not a health metric on its own — iteration 4 also reports this broken down by track-quality decile (ad hoc, not a stored metric) to verify the rate actually gates on quality. |
| Weeks charted mean/p95 (any position) | A broader signal than `WeeksInTop10`; useful for spotting whether long-runners are being retired early. |
| Competition modifier mean/p5/p95 (iteration 3 fix 1) | Reads the real per-week values stored in `Release.WeeklyCompetitionModifiers`. Exists to confirm fix 1's scale-invariance rewrite actually lands in a sane range (~0.2–1.0) rather than the old formula's measured ~0.01 collapse — not itself a design target. |
| Chart re-entries (count) (iteration 3 fix 4) | Counts genuine 0→nonzero `WeeklyPositions` transitions after a release had already charted at least once. A raw count, not a rate — exists to confirm the margin is doing something measurable, not to hit a number. |

**This tool does not tune anything** — Phase 3a's job was building the instruments; Phase 3b iteration 1 fixed two structural root causes and extended the instruments to measure contestation directly; iteration 2 replaced the single-curve chart formula with the two-curve model above and fixed three more measurement problems in the instruments themselves (2a/2b/2c); iteration 3 fixed two formulas that had been silently calibrated against the old 21-group world (competition, the Gini band) and stopped chart re-entry flicker; iteration 4 restored the fandom-spike population iteration 3 had accidentally deleted and fixed crossover's magnitude and quality-gating — each fix verified working correctly in isolation, even though the four together overshot the (also-revised, also-derived-not-measured) longevity bands by a wide margin. No iteration touched a number to make a result look good. See `Docs/BALANCE.md` for the full sweep output from every phase, including each iteration's measured overcorrection.

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
- **Untuned balance results, Phase 3a → 3b iteration 1:** Phase 3a's baseline passed 5/6 metrics for the wrong reasons — the chart was never a fifth full. Iteration 1's structural fixes (proportional fandom growth, a 200-group world with yearly churn, percentile-ranked tiers, multiplicative promo spend) fixed the structural causes but measurably overcorrected several metrics from too-flat to too-extreme. Full explanation in `Docs/BALANCE.md`'s iteration 1 entry.
- **Iteration 2's two-curve model measurably changed the picture again, in both directions.** Real gains: Persistence went from HIGH on 0/5 seeds to PASS on 3/5; #1 rate went from HIGH (12–14%) to a clean PASS (1.75–2.0%) now that release volume is realistic (~621 releases/year and ~185 active world groups, vs. iteration 1's ~114.6 active groups). A crossover release was measured charting **270 consecutive weeks** (seed 12345, "Ghost Aftermath") without being retired — direct confirmation the two-curve model can produce a Melon-style multi-year long-runner, which was the whole point of fix 1. But: **fandom spread got roughly 18x worse** (289x mean → 5150x mean) because release volume interacts with `FandomSystem`'s still-untouched proportional growth term (more releases charting simultaneously means more weekly points summed into growth every week) — see the "Phase 3b's own overcorrection" note under Systems/Fandom/ above. **Top-10 rate also went the wrong direction relative to its revised band** — fix 2e lowered the target to 4–10% expecting long-runners to consume slot-weeks with fewer distinct songs, but measured came in at 28–32%, roughly 3x over the *new*, already-lowered band. **The 520 slot-weeks identity holds exactly** (`top10_rate × releases/yr × mean_top10_weeks = 520.00` on every single sweep seed) — confirms the top 10 is continuously full and the accounting is internally consistent, even though the resulting rate reads high against the target.
- **Weeks-in-top-10 is genuinely bimodal, but median-heavy rather than a clean two-hump split.** Seed 12345's deciles: p10 through p80 are *all exactly 1.0 week* (a release that clips the top 10 for a single week is the overwhelming median case), p90 = 2.0, then p95 = 18 and the max = 94 — a long, thin tail of true long-runners on top of a huge base of one-week blips. This is why Hit longevity median (target 2–5) reads LOW at exactly 1.000 on every seed: the *typical* top-10 entrant is even shorter-lived than the new band assumed, even though the long-tail p95 metric (target 25–60, measured 15–19) shows the tail exists, just not long enough yet at these seeds. Both bands may need revisiting once fandom growth (and therefore chart pressure) is retuned — they were calibrated against a mental model of the shape, not measured data, per the iteration 2 brief's own instruction not to tune this session.
- **`FandomGrowthDampingSize` still doesn't engage**, now worse than iteration 1 reported it — see the fandom-spread note above.
- **Percentile-ranked tiers are sensitive to cohort composition, not just individual performance.** Tier mobility per active group (0.4–1.5 target) measured 2.28–2.52/decade in iteration 2, and got slightly *worse* in iteration 3 (2.76–3.06) despite iteration 3 touching nothing in `TierSystem` or its direct inputs — plausibly a knock-on effect of hit longevity's shift changing the peak/weeks-in-top-10 inputs `TierSystem`'s composite score reads. The original iteration-1 hypothesis (yearly churn shifting everyone's relative percentile) is still untested and still the leading suspect for the baseline overshoot.
- **Iteration 3 fixed two world-size-calibration bugs and one boundary-flicker bug, all frozen elsewhere.** Competition went from sum-based (silently divides by ~30x whenever the world grows) to share-based, verified scale-invariant by a real test (100 vs. 200 world groups, ratio 1.0084 — within 1% of the required 10%). Retirement went from an absolute points floor (produced 1,277 mean `ActiveReleases`/week) to chart-presence-based, cutting it to 237.70 and sweep runtime from ~14s to ~2s per seed (~7x). Chart re-entry got one-directional hysteresis, eliminating the "58, 0, 77, 93, 0" flicker iteration 2 measured directly — every one of the iteration 3 sample chart runs is now a clean single arc. None of iteration 3's fixes touched the trajectory model or fandom growth.
- **Fixing the flicker revealed the underlying hit-longevity distribution is long-residency by default, not short — the opposite problem from iteration 2.** Seed 12345 deciles moved from "p10 through p80 all exactly 1.0, then a cliff to p95=18" (iteration 2, an artifact of flicker inflating the count of trivial one-week entries) to a real spread (p10=1, p50=26, p90=49, p95=54, max=72 — iteration 3). Hit longevity median swung from LOW (1.0 against a 2–5 target) to HIGH (~27 against the same target) — the band itself has now been wrong in both directions and was never validated against real data at either point; it needs to be set from this measured distribution, not guessed again.
- **Top-10 rate flipped from HIGH (~29%, iteration 2) to LOW (~3.2%, iteration 3)** as a direct consequence of the longevity swing above: fewer, longer-lived hits monopolize the top 10 instead of many short-lived ones cycling through it (`DistinctTop10EntrantsThisWeek` mean dropped to 1.27/week). Whether 4–10% is the right target for this shape of chart (vs. the "many short-lived hits" shape it was designed around) is genuinely open.
- **Top-decile #1 share (0.847–0.879) sits above its own 0.55–0.80 target even with Gini now passing cleanly (0.895–0.904 against 0.82–0.94)** — the winners' circle is more concentrated than Gini alone shows. Plausibly downstream of fandom spread's still-frozen, still-enormous overcorrection (a handful of groups' fandom sizes are now so far ahead of everyone else's that they mechanically win most #1s); untested hypothesis, and the obvious next thing to check once fandom growth is finally tuned.
- **Iteration 4 restored the fandom-spike population and fixed crossover's magnitude/gating, but overshot by roughly an order of magnitude in aggregate.** All four fixes are confirmed working correctly on their own specific, narrow target: the highest-`FandomPull` sample is now genuinely spike-shaped (peak #1, out of top 10 by week 2); crossover rate rises monotonically by quality decile (0.74%→6.28%); the quality floor for holding top 10 3+ weeks rose from ~Q20.6 to Q34.7. But `NormalizeFandom`'s log10 compression means an "ordinary" fandom size and a "giant" one aren't nearly as far apart in normalized (0-100) terms as the fix's own linear-points derivation assumed — measured Distinct-top-10-entrants/year came in at ~456 against an estimated 50-80 target (~6-9x over), and Longevity skew stayed LOW (~0.12 against >0.25) because the aggregate distribution is now dominated by a much larger one-week-spike population than intended, not because the underlying spike/crossover split failed.
- **`FandomPullScale`/`NormalizeFandom` need to be reasoned about in normalized-fandom-population terms, not raw points, for any future pass.** The failure mode above (a linear points derivation undershooting the actual affected population by an order of magnitude) is specific to log compression: what fraction of the fandom-size distribution clears a given normalized threshold is a very different question from "what raw point value does a maxed-out fandom produce," and only the second one was reasoned about in iteration 4's brief.
- **The 50-80 distinct-top-10-entrants/year estimate behind iteration 4's revised longevity/Top-10-rate bands has never been checked against a real chart data source** (Melon, Circle Chart, or similar) — it's a derivation from a derivation at this point (iteration 3's guess → iteration 4's estimate → the bands). Worth actually looking up before another round of band-guessing.
