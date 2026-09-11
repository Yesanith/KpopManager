# KpopManager — Balance Log

Every tuning change gets a line. What you changed, what happened, whether you kept it.

This exists because balancing is a long sequence of small experiments, and without a log you will make the same change twice, three months apart, having forgotten why you reverted it the first time.

---

## How to use this

1. Change **one** coefficient.
2. Run **KpopManager → Run 50-Year Simulation**.
3. Open `/SimOutput/*.csv`.
4. Record the result below.
5. Repeat.

Always change one thing at a time. Two changes at once and you learn nothing from the result.

---

## Target metrics

The numbers a healthy sim should produce. Check these every balance run.

| Metric | Target | Why |
|---|---|---|
| Distribution of #1 songs | Power law with upsets | One group always winning is broken; uniform random is also broken |
| Average weeks in top 10 (for a hit) | 4–12 | Below 4 and nothing feels earned |
| `TrackQuality` vs `PeakPosition` | Weak correlation | Fandom should dominate week one — that's realistic |
| `TrackQuality` vs `WeeksInTop10` | Strong correlation | Quality must matter *somewhere*, or the player has no lever |
| Top group in year 5 vs year 50 | Some persistence, not lock-in | Total lock-in is a dead game |
| Groups reaching top tier per decade | 2–4 | Mobility has to exist |

---

## Log

Format:

```
### YYYY-MM-DD — [system] short description
**Changed:** coefficient X from A to B
**Expected:** ...
**Observed:** ...
**Verdict:** kept / reverted / partial
```

---

### (no entries yet — Phase 3 starts this)

---

## Reverted changes

Things tried and rejected. Keep them here so you don't retry them in six months.

*(empty)*

---

## Open balance questions

- What should the decay constant `k` range be across the quality spectrum?
- Should `CompetitionModifier` be symmetric, or should the bigger release suppress the smaller one disproportionately?
- Does fandom need an age-decay term, or does concept fatigue alone prevent snowballing?
- How much should promotion spend matter relative to track quality? Too high and money wins; too low and the budget split stops being a decision.
