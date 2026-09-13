# Untitled K-Pop Management Sim — Design Document v0.2

Text-first management sim. Unity, UI Toolkit. Designed at phone width, ships to PC and mobile.

Football Manager's structure, K-pop's subject matter.

---

# PART ONE — THE GAME

## You get hired

You're the new head of a production center at a mid-tier Korean entertainment company. You're not the boss. There's a board above you that gives you a budget, sets you three targets for the year, and can fire you.

They hand you one existing group that isn't doing great, and a practice room full of trainees.

There are two other production centers in the same building doing the same job. You're all competing for the company's money, its good MV directors, and its promotion slots. And you're all being compared.

> **Why this role:** it's the FM manager exactly — full control of your own roster, zero control of the money. SM Entertainment actually restructured into independent production centers with delegated authority in 2023, so the fantasy is real.

---

## A normal week

You click **next week**.

Usually something small happens. Your trainees got a bit better. Your group did a fan sign. A magazine wants an interview.

Sometimes it isn't small. Your main vocalist tore something in her ankle. A rival center just announced a comeback for the exact week you were planning yours. One of your trainees is quitting.

You read the news feed, handle whatever needs a decision, click next week again.

Most weeks are quiet. That's what makes the loud ones land.

> **Mechanically:** one tick = one week. Twelve ordered steps per tick (training → scheduled activities → releases → chart sim → music shows → fandom → fatigue → random events → rival AI turns → news → cash → board check). Everything you build later has to slot into one of those twelve steps.

---

## Preparing a comeback

Every few months your group comes back with a new release. This is where you actually play the game.

**Your A&R team brings you songs.** Three or four candidates. You don't get to see exactly how good they are — you get a description, a genre, a rough read from your staff. Better staff give you better reads. You pick one.

**You pick a concept.** Cute, girl crush, dark, retro, summer, ballad, experimental. If the concept doesn't fit the song, it lands badly. If you've run the same concept three comebacks in a row, fans are bored of it. If you swing too far from what they know you for, they feel abandoned.

**You split the money.** Production, choreography, MV, styling, promotion. There is never enough for all five. Cheap MV with an expensive song is a different bet than the reverse, and both are valid.

**You pick a week.** The calendar shows what your rivals are doing. Drop against a major group's comeback and you get buried. Wait for a clean window and you lose momentum, and your fandom cools.

> **Mechanically:** tracks have hidden Quality, plus Concept Fit and Trend Fit values. Concept fatigue is a decaying multiplier on repeat use; concept whiplash costs fandom sentiment. Budget is five sliders feeding different terms in the chart formula.

---

## Watching it happen

You click next week and the song is out.

**Week one is mostly your fandom.** If you have loyal fans, you chart well immediately, almost regardless of how good the song is. That's how the real industry works and it's what makes fandom the thing worth building.

**Then the interesting part — does it hold?** A genuinely great song climbs, or parks at #7 for two months. A mediocre song with a big fandom spikes to #2 and is gone by week three. You learn to read that shape, and reading it correctly is most of the skill in the game.

Meanwhile you're running promotions:

- **Music shows** every week. Wins are your trophies.
- **Variety appearances** make your members famous with normal people, not just fans. Costs energy.
- **Fan signs** deepen loyalty. Costs more energy.

You're constantly deciding whether to push harder or let them rest.

Somewhere in here your group might win their first music show. That's the moment the whole game exists for.

> **Mechanically:** fandom size drives week-1 position, song quality drives the decay curve. The most satisfying outcome should be a song still charting at week 10, not one that peaks for three days. Full formula in Part Two.

---

## The other half: trainees

While all of that is happening, you have a dozen kids in the practice rooms.

**You assign what each one works on** every week — vocal, dance, rap, language, media training.

**You don't see their real stats.** You see rough ranges that narrow over time and with better scouting staff. And you never see their ceiling. You're permanently guessing whether the girl who's mediocre at 16 blooms at 19 or just stays mediocre.

**Every month you get a ranked report** and you can cut whoever isn't going anywhere. Cutting is unpleasant and sometimes correct.

**Eventually you decide: these five debut.** Lineup, size, positions, concept. It's the biggest decision in the game and you make it half-blind.

> **Mechanically:** attributes displayed as ranges, Potential never revealed numerically. Intake channels: auditions (cheap/weak), street casting (medium), global auditions (expensive, unlocks foreign members), poaching (very expensive, costs goodwill). Trainees who wait too long lose morale and quit.

---

## The stuff that goes wrong

None of these have correct answers. They have trade-offs.

**Exhaustion.** Push a group through back-to-back comebacks and their performance degrades, then their health, then their morale.

**Popularity imbalance.** One member gets far more popular than the rest and the others resent it. You can rebalance line distribution or give the others solo activities — both of which annoy somebody else.

**Scandals.** Dating rumors, attitude controversies, staff misconduct. Deny, confirm, apologize, or stay silent. Fandom sentiment reacts differently to each and no option is safe.

**The seven-year wall.** Contracts expire at year seven. Whether your members re-sign depends on how you treated them, how much they earned, and what rivals are offering. Losing your main vocalist at peak popularity is a legitimate disaster state and the game should let it happen.

**Military service.** Male idols enlist by around 28. You need a plan for the gap — sub-units, solo releases, or an honest hiatus.

---

## Year end

The board reviews you.

- **Exceeded targets** — bigger budget, more goodwill, maybe a second group
- **Met targets** — nothing changes
- **Missed** — warning, budget frozen
- **Missed twice** — you're fired. Game over.

Board patience is your difficulty setting. A prestige company hands you real money and tolerates nothing. A rookie company has no money and will let you fail for three years straight.

---

## The long arc

Keep succeeding and it grows:

**Junior center** — one group, small budget, board watching closely
**Established** — two groups, you're fighting the other centers for resources
**Flagship** — three groups, and you get first pick of the good staff instead of scraps
**Spin-off label** — semi-independent, your own P&L, you hire your own people

The player opts into complexity by earning it. This is how a solo dev ships a small game that grows into a big one.

---

# PART TWO — MECHANICS

## Idol attributes

**Performance:** Vocal, Rap, Dance, Stage Presence
**Star:** Visual, Charisma, Variety, Fan Connection
**Creative:** Songwriting, Composition, Choreography
**Hidden:** Work Ethic, Mental Resilience, Ambition, Professionalism, Potential
**State:** Age, Morale, Fatigue, Health, In-group Popularity, Nationality, Languages

Nationality is mechanical, not flavor: foreign members open overseas markets faster but need language training and carry more visa and homesickness events.

## Fandom is three numbers

- **Size** — album sales, concert capacity, week-1 chart position
- **Sentiment** (0–100) — loyalty, forgiveness, willingness to spend
- **Public Awareness** — separate from fandom entirely

A group can have a huge fandom and no public awareness: strong sales, weak charts. Or the reverse: a viral hit nobody buys. Balancing the two is a core strategic axis. Collapse these into one popularity number and the game goes flat.

Sentiment drops from visible overwork, jarring concept switches, mishandled scandals, unfair line distribution, and merch overload.

## Chart formula: two curves, not one

Korean digital charts don't behave like the Hot 100, and the formula has to reflect that. Mass
coordinated fandom streaming and buying ("총공"/"total attack") buys a release's opening week
regardless of the song — a large fandom guarantees a strong week-1 position, then contributes
almost nothing afterward, because the fans have already bought. What produces multi-year
longevity (BTS's "Spring Day": 341 charting weeks) is crossover appeal with the general public,
which has nothing to do with fandom size. These are two different populations, not two ends of
one curve: the typical idol comeback spikes and collapses inside a month; the rare crossover hit
climbs for weeks and then charts for years.

So every release has two independent components instead of one BuzzScore:

```
FandomPull   = NormalizeFandom(fandom size at release)      -- buys the opening
PublicAppeal = TrackQuality * 0.70 + ConceptFit * 0.15 + TrendFit * 0.15   -- buys the years
             * CrossoverMultiplier (rare, rolled once at release)

WeeklyPoints = ( FandomPull   * exp(-FandomDecayK * weeksSinceRelease)     -- steep, ~2-week half-life
               + PublicAppeal * BuildCurve(weeksSinceRelease)
                              * exp(-PublicDecayK * weeksSinceRelease) )   -- shallow, ~35-week half-life
             * TierBoost(group tier)         -- an established act's reach buys a bigger opening
             * PromoBoost(promotion spend)   -- promo lifts the whole total, opening and tail alike
             * CompetitionModifier(rival releases this week)
             * RandomVariance(seed, ±12%)
```

Fandom and public appeal are deliberately in **tension**: a release optimized purely for the
fandom (a huge week-one spike, nothing after) and a release optimized purely for crossover appeal
(a slow-building multi-year run) are both valid strategies, and a title track can't usually be
both. That tension is the real strategic axis this formula exists to create — not flavour on top
of a single popularity number.

**Validation:** run 50 simulated years headless, dump chart histories to CSV. If one group always
wins, or the results look like noise, the formula is wrong. Fix it in a spreadsheet, not in Unity.

## What you control vs what you request

**Yours:** trainee intake and training, cuts, debut lineup, group concept, title track, comeback timing, budget split, schedule load, staff hiring inside your center.

**You have to ask HQ for:** promotion budget uplift, the good MV and choreo staff, music show and variety bookings, overseas expansion approval, crisis PR support.

**Never yours:** other centers' rosters, company finances, the board.

Requests cost goodwill and can be refused. Goodwill rises with results and falls with overreach. This request layer is the game's central tension — it's why your budget being finite feels fair instead of arbitrary.

## Data model

```
Company
 ├── Board (targets, patience, goodwill)
 ├── SharedServices (contested pools)
 ├── ProductionCenter[]          // player owns index 0
 │    ├── Budget, Staff[], Trainee[]
 │    └── Group[]
 │         ├── Idol[], Fandom
 │         ├── Discography (Release[])
 │         └── ContractExpiry
 └── World
      ├── Calendar (music shows, awards, festivals)
      ├── ChartState
      └── RivalCenters (same structure, AI-driven)
```

All of this lives in a `Sim` assembly with **zero UnityEngine references**. Deterministic seeded RNG only — never `UnityEngine.Random`. Unity reads state and renders it, never mutates it.

## Screens

All must work at 400px wide. Desktop gets multi-pane with the same data.

**Home** (week, cash, news, deadlines, target status) · **Roster** (group → member → attributes) · **Trainees** (sortable table, assignments) · **Comeback** (the planning flow) · **Charts** (top 100, position history) · **Calendar** (yours + visible rival comebacks) · **Requests** (pending asks, goodwill meter) · **Staff** · **Finances** · **Inbox** (decisions)

---

# PART THREE — SCOPE

## MVP

**In:** one center, one group, 12 trainees, the full comeback cycle, chart sim with 3 AI rivals, trainee training and one debut, board targets and firing, fandom size and sentiment, ~30 random events.

**Out for now:** multiple player groups, overseas expansion, sub-units and solos, side careers, concerts and tours, military service, staff hiring, and any art whatsoever.

## Build order

1. Sim assembly skeleton — entities, seeded RNG, tick loop, no Unity
2. **Chart simulation** — console harness, 50 years to CSV, balanced
3. Comeback cycle resolving headless
4. Trainees and debut, verifiable from logs
5. Board and fail state — full game playable in a terminal
6. **Unity UI layer** — Home, Roster, Comeback, Charts
7. Event system and content writing
8. Save/load (JSON serialization of sim state)
9. Balance pass

Steps 1–5 contain no Unity. If it isn't fun as a terminal application, a UI will not save it.

## Open questions

- Girl groups, boy groups, or both at MVP? Military service only applies to one. Co-ed is rare enough to be a distinctive hook.
- Click-to-advance weeks, or auto-advance you can interrupt?
- Procedurally generated cast, or hand-authored starting characters for flavour?
- Exact numbers, or FM-style star ratings? Star ratings hide the math and extend replay value.
- Monetization, if mobile.
- Real Korean music show wins are multi-factor: digital points, social media views, pre-voting,
  live voting, broadcast points, and a judges' panel — not a readout of chart position. That
  belongs in Phase 4's `MusicShowSystem`, not the chart formula above, and it means a music show
  win is a *different* success axis from the chart, which is worth having on its own rather than
  collapsing into one number. Recorded here for Phase 4; not implemented yet.

## Note on the shared core

The `Sim` assembly is deliberately generic — entities with growing attributes, scheduled competitive events, career timelines, aging, random life events, a pressure layer above the player. The fighter career sim runs on the same skeleton with different content. Build this one properly and that one is a content layer, not a rewrite.
