# KpopManager — Architecture

Technical reference. **Updated with every structural change** — new system, new entity, changed tick order.

Last updated: *(Phase 2 — entities and world generation)*

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
├── Entities/                 — Person, Group, ProductionCenter, Company (Phase 2)
├── Systems/
│   └── Generation/           — PersonGenerator, GroupGenerator, WorldGenerator (Phase 2)
├── Events/                   — (pending Phase 8)
└── Data/                     — WorldData, WorldDataLoader: the shape of loaded content (Phase 2)

Assets/SimData/                — the content JSON itself (not C#, not under Assets/Sim/)
Assets/Editor/Generation/     — ContentLoader: reads Assets/SimData/*.json, hands Core a JSON string
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

### Data/
| Type | Purpose |
|---|---|
| `NameEntry` | `{ Name, Gender }` — one given name. `Gender` carries a custom `GenderJsonConverter` so content JSON can use the compact `"F"`/`"M"` tag instead of spelling out `"Female"`/`"Male"` on ~750 entries. |
| `WeightedName` | `{ Name, Weight }` — one surname with a relative (not normalised) frequency weight. |
| `WorldData` | All loaded content as explicit named lists (not a dictionary — small fixed count, per CLAUDE.md's convention): `KoreanGivenNames`, `KoreanFamilyNames`, `StageNames`, `GroupNames`, `JapaneseGivenNames`, `ChineseGivenNames`, `ThaiGivenNames`. `GetForeignGivenNames(nationality)` returns the right list or null. |
| `WorldDataLoader` | `static WorldData FromJson(string json)` — the only door between raw text and `WorldData`. Takes a string, never a path, so Core stays file-I/O-free. |

### Systems/Generation/
| Type | Purpose |
|---|---|
| `PersonGenerator` | `Generate(rng, birthYear, tier, status, now)` — the literal Phase 2 signature — plus a fuller overload taking an optional `Gender?`, `Position?` and `WorldData` that `GroupGenerator` uses to pin both. Archetype first (one of 8 `PrimaryArchetypes`, excluding Leader/Maknae), then attributes correlated to it via per-archetype bonus/variance tables. Every numeric constant is a `// DESIGN:` comment — none of this is balanced yet. Works with `worldData: null` (falls back to a synthetic name), which is what makes the pure-attribute tests possible without any content file. |
| `GroupGenerator` | Rolls a member count (4–9, weighted 5–7), builds a non-duplicated archetype list (the four unique primaries — MainVocal/MainRapper/MainDancer/Visual — always present once; Lead*/AllRounder fill the rest and may repeat), generates each member, tags exactly one Leader and one Maknae (**always two different people** — see the Phase 2 bugfix below), names the group (retried against every existing group name in the world so two companies never end up with an identical group name), and seeds `Fandom` from a tier-keyed mean. Registers everything it creates directly onto `GameState` as it goes. |
| `WorldGenerator` | `Generate(state, data)` — one deterministic pass: `Company` + 3 centers (player + 2 siblings), the player's one Rookie/Rising group (debuted 1–2 years ago, then deliberately depressed below its tier roll — DESIGN.md's "you get hired" framing), 12 mixed-quality trainees, 2–3 groups per sibling center, and ~15 groups tier-pyramided (6/4/3/1/1) across 5 placeholder outside companies with debuts staggered up to 10 years back. Call once, right after building a fresh `GameState`, before any ticking. |

### Systems/ (tick-order stubs, Phase 1)
| Type | Tick step | Purpose |
|---|---|---|
| `TickOrder` | — | The single place the twelve-step order is written down. `BuildDefault()` returns the list every `SimEngine` is constructed with. Mirrors the table below; change both together. |
| `StubSystem` | 1–12 | A named, registered step with no behaviour. Must never log and never draw from `GameState.Random` — a stub that consumed a draw would shift every later number, invalidating any seed balanced against it. Carries a `DuePhase` for documentation. |
| `WeekCounterSystem` | *(prepended)* | Phase 1 diagnostic. Logs one `Info` line per week so the harness and the determinism test have real data. Still the only tick-order system with real behaviour — Phase 2 built entities and a world, not a tick-order system; delete this once a real one produces output. |

**Graduating a stub:** write the real system in its own file under `Systems/`, then swap one line in `TickOrder.BuildDefault()` — `new StubSystem("Chart simulation", 3)` becomes `new ChartSystem()`.

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
| 3 | Track quality decay / new releases | 3 | stub |
| 4 | **Chart simulation** | 3 | stub |
| 5 | Music show results | 4 | stub |
| 6 | Fandom update | 5 | stub |
| 7 | Fatigue / health / morale | 4 | stub |
| 8 | Random events | 8 | stub |
| 9 | Rival AI turns | 3 | stub |
| 10 | News generation | 8 | stub |
| 11 | Cash settlement | 6 | stub |
| 12 | Board target check | 6 | stub |

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
 ├── People        List<Person>       // authoritative, ordered — iterate this
 ├── Groups        List<Group>        // authoritative, ordered — iterate this
 ├── Centers       List<ProductionCenter>  // authoritative, ordered — iterate this
 ├── [index] _peopleById, _groupsById, _centersById   // Dictionary<int,T>, [JsonIgnore], rebuilt by RebuildIndices()
 └── WorldData     WorldData          // [JsonIgnore] — loaded content, reloaded from disk each run, not saved

Company
 └── CenterIds: List<int>            // this company's centers only — NOT the wider industry
      Board { Patience, Targets: List<BoardTarget> }   // placeholder shape, Phase 6

ProductionCenter (in GameState.Centers, looked up by id)
 ├── GroupIds: List<int>
 └── TraineeIds: List<int>

Group (in GameState.Groups, looked up by id)
 ├── MemberIds: List<int>            // ordered — Positions[0] on each member is their primary archetype
 └── Fandom { Size: long, Sentiment: float, PublicAwareness: float }

Person (in GameState.People, looked up by id)
 ├── GroupId, CenterId: int          // -1 = none
 ├── Positions: List<Position>       // usually 1–2: archetype, optionally + Leader or Maknae
 └── LanguageProficiency: Dictionary<Nationality,int>   // lookup-only, never iterated for a sim outcome
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

American/Other nationalities (~5% of the population combined) aren't covered by a content file — `PersonGenerator` falls back to a small embedded name list for those two rather than asking for two more JSON files over such a small slice.

The Korean given-name list is generated (not hand-typed one by one) by combining real, commonly-used romanized syllable blocks two at a time — genuinely how the overwhelming majority of Korean given names are formed — then deduplicated. Family names, stage names, group names, and foreign given names are hand-curated.

---

## Open technical questions

- ~~Does the harness `SimEngine` instance survive Unity domain reload?~~ **Answered in Phase 1:** it does not, and it does not need to. The window serialises seed + weeks-elapsed and replays. See *Editor harness* above.
- Attribute storage: named fields vs enum-keyed array. Currently named fields for debuggability; revisit if the count grows past ~20.
- **Girl groups vs boy groups vs co-ed** (DESIGN.md's own open question) — still unresolved. `Person.Gender` and `Group.Gender` exist and `GroupGenerator` keeps a group single-gender, but which gender (or a co-ed mix) is never decided; it's rolled 50/50 per group. Revisit before Phase 4 needs a definite answer for the player's own group.
- Some Phase 2 trainees generate with a flat **0** in an attribute (a raw 15-year-old, tier 0, can roll below the clamp floor often enough to hit it exactly). Mathematically consistent with "mixed quality," but worth a look during balancing — it may read as more of a floor-effect artifact than an intentional "genuinely has nothing going for them yet" case.
