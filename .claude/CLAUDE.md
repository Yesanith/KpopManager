# KpopManager

A text-first K-pop production-center management sim. Football Manager's structure, K-pop's subject matter. Unity 6, UI Toolkit, PC only.

The player is a Production Center Leader at a mid-tier Korean entertainment company. They run their own roster but answer to a board that sets annual targets and can fire them.

---

## Architecture rules — these are not negotiable

**1. `KpopManager.Core` has "No Engine References" checked.**
No `MonoBehaviour`, no `ScriptableObject`, no `UnityEngine.Random`, no `Debug.Log`, no `DateTime`, no `Application.*`. Unity enforces this at compile time. If a Core file seems to need Unity, the design is wrong — say so rather than working around it.

**2. All randomness goes through `SimRandom`.**
One seeded instance lives on `GameState`. Never `System.Random` (its internals are not guaranteed stable across runtime versions). Never `UnityEngine.Random`. Same seed + same inputs must produce an identical 50-year history — this is what makes balancing possible, and a single stray random call breaks it permanently.

**3. The sim never calls the UI. The UI polls the sim.**
One-way dependency. `KpopManager.Unity` and `KpopManager.Editor` reference `KpopManager.Core`. Never the reverse.

**4. Time is weeks, not dates.**
`SimDate { int Year; int Week; }` where Week is 1–52. Never `DateTime`, never `TimeSpan`.

**5. `GameState` is a plain data graph.**
No references to systems, no delegates, no events, no circular parent pointers. It must serialize cleanly with Newtonsoft in Phase 9. Entities reference each other by `int Id`, not by object reference.

**6. Nothing gets a screen until it's proven fun in the Editor harness.**
Phases 1–6 build zero UXML. Do not create UI Toolkit files unless the phase explicitly asks for them.

---

## Assemblies

| Assembly | Path | Notes |
|---|---|---|
| `KpopManager.Core` | `Assets/Sim/` | **No Engine References ✓**. Pure C#. |
| `KpopManager.Editor` | `Assets/Editor/` | Editor-only. Harness window, menu items. |
| `KpopManager.Tests` | `Assets/Tests/` | Editor-only, EditMode. NUnit. |
| `KpopManager.Unity` | `Assets/Scripts/` | Runtime Unity glue. Empty until Phase 7. |

Namespaces mirror assembly names: `KpopManager.Core.Systems`, `KpopManager.Editor`, etc.

---

## Conventions

- **Flag design decisions, don't make them silently.** When there's a real choice (a curve shape, a weighting, a threshold), write `// DESIGN: chose X over Y because Z. Revisit during balancing.` and keep going.
- **Report before large refactors.** Describe what you'd change and why, wait for confirmation, then act.
- **Update `Docs/ARCHITECTURE.md` with every structural change.** New system, new entity, changed tick order — it goes in the doc in the same session.
- **Update `Docs/PROGRESS.md`** when a phase task completes.
- **Tuning changes go in `Docs/BALANCE.md`** — what you changed, what happened, one line each.
- Commit summaries: `ADD: [description]`. Other prefixes: `FIX:`, `REFACTOR:`, `DOCS:`, `BALANCE:`.
- Prefer explicit named fields over dictionaries for attributes. Easier to debug, and the count is small enough.
- XML doc comments on public types and any method whose behaviour isn't obvious from its name.
- **Don't add a summary at the end of a response.** State results plainly as you go (test output, what changed, what to check) and stop — no closing recap section restating what was just done.

---

## Testing

EditMode tests in `KpopManager.Tests`. The non-negotiable one, present from Phase 1 onward:

```
Two SimEngines with the same seed, ticked 1000 times, produce identical SimLog output.
```

Run it every session. It is what catches a stray `DateTime.Now` or `UnityEngine.Random` sneaking into Core.

---

## Scope discipline

The MVP is **one production center, one group, twelve trainees**. The data model supports multiple groups and multiple centers from day one, but the game ships with one of each. Do not build multi-group management, touring, sub-units, overseas expansion, or military service unless the current phase explicitly calls for it.

If a request seems to require going beyond the current phase, say so instead of expanding scope.

---

## Docs

- `Docs/DESIGN.md` — the game design document. Read it for any question about intended behaviour.
- `Docs/ARCHITECTURE.md` — technical reference. Keep current.
- `Docs/PROGRESS.md` — phase checklist.
- `Docs/BALANCE.md` — tuning log.