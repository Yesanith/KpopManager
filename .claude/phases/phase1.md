# Phase 1 prompt — paste this into Claude Code

---

Build Phase 1 of KpopManager: the simulation core. No gameplay systems yet — just the machinery that lets time advance deterministically, plus the Editor window I'll be using to drive the game for the next month.

Read `CLAUDE.md` and `Docs/DESIGN.md` first. The architecture rules in CLAUDE.md are hard constraints.

## What to build

### 1. `KpopManager.Core/Core/SimRandom.cs`

A deterministic PRNG. Implement **PCG32 (XSH RR 64/32)** by hand — do not use `System.Random` or `UnityEngine.Random`.

```
State: ulong _state, ulong _inc

NextUInt():
    ulong old = _state;
    _state = old * 6364136223846793005UL + _inc;
    uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
    int rot = (int)(old >> 59);
    return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));

Seeding(ulong seed, ulong seq = 1):
    _state = 0;
    _inc = (seq << 1) | 1;
    NextUInt();
    _state += seed;
    NextUInt();
```

Public API:
- `int NextInt(int minInclusive, int maxExclusive)` — use rejection sampling for an unbiased range, not modulo
- `float NextFloat()` — [0,1)
- `float NextFloat(float min, float max)`
- `float NextGaussian(float mean, float stdDev)` — Marsaglia polar method, cache the spare value
- `bool Chance(float probability)`
- `T Pick<T>(IReadOnlyList<T> items)`
- `void Shuffle<T>(IList<T> items)` — Fisher-Yates

**Critical:** expose `_state`, `_inc`, and the cached Gaussian spare as public properties with setters (or a `SimRandomState` struct with a `GetState()`/`SetState()` pair). Save/load in Phase 9 must restore the generator exactly or determinism breaks across a save.

### 2. `KpopManager.Core/Core/SimDate.cs`

An immutable readonly struct. `int Year`, `int Week` (1–52).

- `SimDate AdvanceWeeks(int n)`
- `static int WeeksBetween(SimDate a, SimDate b)`
- Full comparison operator set, `IComparable<SimDate>`, `IEquatable<SimDate>`
- `ToString()` → `"Y3 W17"`

No `DateTime` anywhere. 52 weeks per year exactly — we're not modelling real calendars.

### 3. `KpopManager.Core/Core/SimLog.cs`

```csharp
public enum LogCategory { System, Chart, Group, Person, Finance, Board, Event, Rival }
public enum LogSeverity { Debug, Info, Notable, Major }

public readonly struct SimLogEntry
{
    public SimDate Date { get; }
    public LogCategory Category { get; }
    public LogSeverity Severity { get; }
    public string Message { get; }
    public int RelatedEntityId { get; }   // -1 when not applicable
}
```

`SimLog` holds a `List<SimLogEntry>`, with `Add(...)`, `IReadOnlyList<SimLogEntry> Entries`, `GetForWeek(SimDate)`, and `Clear()`.

This is the only output channel from the sim. The harness prints it now; the news feed renders it in Phase 7. Never `Debug.Log` inside Core — it wouldn't compile anyway.

### 4. `KpopManager.Core/Core/GameState.cs`

For Phase 1, minimal:

```csharp
public class GameState
{
    public SimDate Date { get; set; }
    public SimRandom Random { get; set; }
    public SimLog Log { get; set; }
    public ulong Seed { get; set; }
    public int NextEntityId { get; set; }   // id allocator for Phase 2 entities
}
```

Plain data. No system references, no delegates, no events.

### 5. `KpopManager.Core/Core/ISimSystem.cs` and `SimEngine.cs`

```csharp
public interface ISimSystem
{
    string Name { get; }
    void Tick(GameState state);
}
```

`SimEngine` holds an ordered `List<ISimSystem>` and a `GameState`.

- `SimEngine(ulong seed)` — builds initial state
- `void AdvanceWeek()` — runs every system in order, then advances `state.Date`
- `void AdvanceWeeks(int n)`
- `void AdvanceYears(int n)`
- `GameState State { get; }`

The system order **is** the design document's twelve-step tick order. Register the steps as named stub systems now, each logging nothing, so the ordering is locked in before any of them have behaviour. Put the ordered list in one clearly commented place — it's the spine of the whole project.

For Phase 1, add one real system: `WeekCounterSystem`, logging an Info entry each week so I can see it working.

### 6. `KpopManager.Editor/HarnessWindow.cs`

An `EditorWindow` at menu path `KpopManager/Harness`. This is my main interface to the game for the next month, so build it properly rather than as a throwaway.

Needs:
- Seed field (ulong) and a **New Game** button that rebuilds the engine
- **Tick Week**, **Tick Year**, **Run N Years** (with an N field)
- Current date and elapsed-weeks readout
- Scrolling log view of `SimLog` entries, newest at the bottom
- Filter toggles per `LogCategory` and a minimum-severity dropdown
- **Clear Log**
- Timing readout: how long the last batch run took in ms

Use UI Toolkit (`CreateGUI` + `rootVisualElement`) rather than IMGUI, so I get practice with the API I'll be using in Phase 7. Keep the log list virtualized with a `ListView` — it'll be holding tens of thousands of entries after a 50-year run.

The engine instance should survive domain reload if that's straightforward; if it isn't, don't fight it, just note the limitation.

### 7. `KpopManager.Tests/`

EditMode tests:

1. **Determinism** — two `SimEngine`s with the same seed, ticked 1000 times, produce identical log output (compare serialized entries, not object references). This is the most important test in the project.
2. **Divergence** — two engines with *different* seeds produce different output. Guards against a broken seeding path that silently ignores the seed.
3. **SimDate** — year rollover at week 52→1, `WeeksBetween` across year boundaries, comparison operators, equality.
4. **SimRandom distribution** — 100,000 draws of `NextInt(0,10)` land within ±2% of uniform per bucket; `NextGaussian(0,1)` has mean within ±0.02 and std dev within ±0.02.
5. **SimRandom state round-trip** — capture state, draw 100 values, restore state, draw 100 again, assert identical.
6. **Performance** — 520 ticks (10 years) completes in under 1000ms.

## Constraints

- Nothing in `Assets/Sim/` may reference `UnityEngine`. If you think something needs to, stop and tell me.
- No UI Toolkit files outside `KpopManager.Editor`.
- Don't build any gameplay systems — no idols, no groups, no charts. That's Phase 2 and 3.
- Flag genuine design choices as `// DESIGN:` comments rather than deciding silently.

## When you're done

1. Update `Docs/ARCHITECTURE.md` with the Core structure and the twelve-step tick order.
2. Tick off Phase 1 in `Docs/PROGRESS.md`.
3. Run the tests and report results.
4. Tell me what to click in the Harness window to verify it works.

Don't commit — I'll review and commit through GitHub Desktop.