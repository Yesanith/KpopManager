# KpopManager — Architecture

Technical reference. **Updated with every structural change** — new system, new entity, changed tick order.

Last updated: *(Phase 1 — sim core and harness)*

---

## Assemblies

| Assembly | Path | Purpose | References |
|---|---|---|---|
| `KpopManager.Core` | `Assets/Sim/` | Pure simulation. **No Engine References ✓** | none |
| `KpopManager.Editor` | `Assets/Editor/` | Harness window, batch sim, CSV export. Editor-only. | Core |
| `KpopManager.Tests` | `Assets/Tests/` | EditMode tests (NUnit). Editor-only. | Core, TestRunner |
| `KpopManager.Unity` | `Assets/Scripts/` | Runtime Unity glue, UI controllers. Empty until Phase 7. | Core |

Dependency direction is strictly one-way: **Editor / Tests / Unity → Core**. Core references nothing.

---

## Core invariants

1. Core cannot reference `UnityEngine` — enforced by the asmdef's No Engine References flag.
2. All randomness flows through the single `SimRandom` instance on `GameState`.
3. Time is `SimDate { Year, Week }` with 52 weeks per year. No `DateTime` anywhere.
4. `GameState` is a plain serializable data graph. Entities reference each other by `int Id`, never by object reference.
5. The sim never calls the UI. The UI polls the sim.

---

## Core structure

*(Fill in as built.)*

```
Assets/Sim/
├── Core/
├── Entities/
├── Systems/
├── Events/
└── Data/
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
| *(pending Phase 2)* | |

### Systems/
| Type | Tick step | Purpose |
|---|---|---|
| `TickOrder` | — | The single place the twelve-step order is written down. `BuildDefault()` returns the list every `SimEngine` is constructed with. Mirrors the table below; change both together. |
| `StubSystem` | 1–12 | A named, registered step with no behaviour. Must never log and never draw from `GameState.Random` — a stub that consumed a draw would shift every later number, invalidating any seed balanced against it. Carries a `DuePhase` for documentation. |
| `WeekCounterSystem` | *(prepended)* | Phase 1 diagnostic. Logs one `Info` line per week so the harness and the determinism test have real data. Delete once Phase 2 entities produce their own output. |

**Graduating a stub:** write the real system in its own file under `Systems/`, then swap one line in `TickOrder.BuildDefault()` — `new StubSystem("Chart simulation", 3)` becomes `new ChartSystem()`.

---

## Tick order

One tick = one week. `SimEngine` runs registered `ISimSystem`s in this exact order, then advances `GameState.Date`.

**This order is the spine of the project.** Changing it after systems depend on each other's side effects is painful. Stubs are registered from Phase 1 so the sequence is locked before behaviour exists.

It also fixes the sequence of draws from `GameState.Random`, which is half of what makes a run reproducible. **Reordering steps changes every future random number**, so an existing save will not replay after a reorder.

Written down in exactly one place in code: `Systems/TickOrder.BuildDefault()`. The strings below are the literal `ISimSystem.Name` values, asserted in order by `DeterminismTests.TickOrder_RegistersAllTwelveSteps`.

| # | System | Phase | Status |
|---|---|---|---|
| 0 | *Week counter* — Phase 1 diagnostic, prepended, deleted in Phase 2 | 1 | **real** |
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
 ├── Date          SimDate       // current week; SimEngine advances it after every system ticks
 ├── Random        SimRandom     // the one generator; every draw in the sim comes from here
 ├── Log           SimLog        // append-only, sorted by date, the sim's only output channel
 ├── Seed          ulong         // the run is reproducible from this alone
 ├── NextEntityId  int           // id allocator; on the state, not a static, so it survives a save
 └── (entities pending Phase 2)
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

Two more, added in Phase 1:

- **A stub that draws.** `StubSystem.Tick` is empty on purpose. If a placeholder ever consumed a random number, every later draw would shift, and replacing it with real behaviour would silently invalidate every seed balanced against it.
- **`Math.Log` in `NextGaussian`.** `Math.Sqrt` is IEEE-754 exact, but .NET does not guarantee `Math.Log` is bit-identical across runtime versions or CPUs. PC-only on one machine, this is fine. If determinism ever has to hold across machines, replace it with a fixed polynomial approximation — flagged as a `// DESIGN:` comment at the call site.

---

## Editor harness

`KpopManager/Harness` opens `HarnessWindow`, the only interface to the game until Phase 7. UI Toolkit (`CreateGUI` + `rootVisualElement`), not IMGUI — the log needs a virtualised `ListView` to stay responsive after a 50-year run, and it is practice for the API Phase 7 uses.

Seed field · New Game · Tick Week · Tick Year · Run N Years · date and elapsed-week readout · per-category filter toggles · minimum-severity dropdown · Clear Log · a collapsed Tick Order list · and a timing readout in ms and ms/week for the last batch.

**Domain reload.** `GameState` is not a Unity-serialisable type, so the engine cannot survive a reload directly. Instead the window keeps `[SerializeField]` copies of the two facts that define a run — the seed and the number of weeks simulated — and rebuilds the engine by replaying from the seed. Determinism makes the replay exact, and a 50-year replay costs about a millisecond, so it is imperceptible. One wrinkle: a cleared log comes back after a reload, because the replay regenerates it.

---

## Open technical questions

- ~~Does the harness `SimEngine` instance survive Unity domain reload?~~ **Answered in Phase 1:** it does not, and it does not need to. The window serialises seed + weeks-elapsed and replays. See *Editor harness* above.
- Attribute storage: named fields vs enum-keyed array. Currently named fields for debuggability; revisit if the count grows past ~20.
