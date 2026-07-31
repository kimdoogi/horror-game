# Open questions — the six balance findings

> These are **contradictions found by running the design document's own numbers
> against each other**. Every one states the sections that disagree, the arithmetic,
> and the options. None of them states a decision, because retuning is the designer's
> call, not ours.
>
> The full write-ups are [`docs/BALANCE-FINDINGS.md`](../BALANCE-FINDINGS.md). This
> page ranks them, says what each one blocks, and says what you may and may not do
> about it.

**Every finding is pinned by a test**, so an edit that changes the answer fails the
build instead of passing silently. If you resolve one, the test fails — that is
intentional, and the write-up moves in the same commit as the change.

---

## The rule for handling a new one

From [ARCHITECTURE.md §6](../ARCHITECTURE.md):

> When you find another: **do not quietly "fix" the design.** Encode what the
> document literally says, write a test that pins the actual consequence, and add an
> entry to `docs/BALANCE-FINDINGS.md` stating the sections that disagree, the
> arithmetic, and the options.

Three steps, in order: **encode literally → pin with a test → write it up.** Choosing
between the options is not your call.

---

## Ranking, and what each blocks

| # | Finding | Priority | Blocks |
|:--:|---|:--:|---|
| **F-006** | Matches finish in 2.5 min, so three of §07's five threat tiers are dead | 🔴 **highest** | §14 Q3 entirely; §16-2 (the economy); makes every chase number a number for a tier nobody reaches |
| F-002 | The Listener's HUD contradicts the player's ears through a wall | 🔴 blocking | §04's 청음사 as a role; the CI audio gate is red-by-baseline because of it |
| F-004 | The Runner's sprint-timing dilemma cannot exist at these numbers | 🔴 | a named skill expression in §06; §12 cannot grade a map on sprint timing |
| F-001 | The weight table is a cliff, not a gradient | 🔴 | §16-2; three of §08's four bands mean one thing |
| F-003 | The five-surface alphabet holds in the room, not at range through a wall | 🟡 | §14 Q5's "far" half |
| F-005 | §12 states two loop rules and only one can ever bind | 🟢 | nothing today |

---

## F-006 · Matches finish in 2.5 minutes — read this one first

**Sections:** §01 × §07 × §08. **Source:** 500 simulated matches, seeds 1–500.

Reproduced 2026-07-31 23:40, exit 0 — the simulator is deterministic and these are
byte-for-byte the figures in the write-up:

```
§01 match length — target 25~35 min
  median                                             2.5 min
  p10 / p90                                          1.3 min / 7.9 min
  inside the window                                  0.6%

§07 threat curve
  mean tier at end (0=초저녁 … 4=동트기 전)                 0.12
  reached 심야 or later                                1.2%
  chases per match                                   5.19
  chases broken                                      59.6%
  deaths per match                                   2.1

§02 outcome mix
  완전 승리 10.6% · 부분 승리 53.2% · 생존 14.6% · 패배 21.6% · objective recovered 63.8%
```

```bash
dotnet run -c Release --project core/HorrorGame.Sim -- run --matches 500 --seed 1
```

### Why it outranks everything

§07 declares 「시간이 유일한 통화다」 and builds the whole pressure system on the clock.
At these numbers the clock barely moves, so:

- 심야's flashlight −30 % never happens;
- 새벽's 「괴물이 출입구를 안다」 never happens;
- the monster's speed never leaves 4.4 — **it never reaches the 4.8 that §06's entire
  speed ladder is built around**;
- three of five tiers are content nobody sees.

And it collides directly with the project's headline result: `MonsterChaseTests` pins
§07 to 심야 to measure against §06's 4.8 m/s, and a real match reaches 심야 1.2 % of the
time. The chase numbers are right for the tier they are measured at. **Fixing F-006 is
what makes them the numbers of the game rather than of a scenario.**

### It blocks the economy, which §16 calls the current bottleneck

From the same 500 matches: `peak weight band reached by anyone 0.82` (band 1 = no
penalty), `earned ÷ cost of one of everything 0.17`,
`purchase_upgraded_flashlight 23 of 500`. Sweeping F-001's `WeightMulLight` from 0.85
to 0.95 over 400 matches per point moves almost nothing, because **teams never
accumulate enough loot to leave weight band 1**, so the cliff is barely tested.

> **The economy cannot be tuned before match length is fixed.** Every economy
> measurement taken now is taken from a game that ends before the economy starts.

### The options, and the honest caveat

1. **Make traversal cost real time** — larger maps, or §12's zone diagonals and floor
   counts scaled so one descent genuinely costs the ~3 minutes §07 assumes. Preserves
   the design as written.
2. **Compress the threat curve** to fit the real match length. Cheapest; trades away
   §07's deliberate gradualness. Reach for it only after a real playtest.
3. **Add required dwell time** — extend §03's sustained-light-and-stillness principle
   to safes, zone lights and objective retrieval, spending time on tension rather than
   on walking.
4. **Accept a shorter match** and rewrite §01 and §07 around it.

The caveat, which is not a reason to dismiss the finding: simulated agents do not
hesitate, argue, get lost or freeze. Some of the missing 25 minutes is human friction.
But **the design currently has no mechanism that consumes 25–35 minutes — only the
hope that players will be slow**, and skilled coordinated players will trend toward
the simulator's number.

> **If you take option 1 and grow the map:** §12's dimensions come from §06's speeds.
> The chase tests and §12 validation are the guard, and both must still pass. See
> [Where every number lives §3](03-where-numbers-live.md).

---

## F-002 · The Listener's HUD contradicts the player's ears

**Sections:** §04 × §12. **Pinned by** `verify_audio.py` section 6 and
`AudioTests.OccludedAudibility_InvertsTheClarityTable_AsF002Reports`.

`GameConstants.ListenerClarity*` says gravel (0.70) gives the monster away more than
concrete (0.50). Measured through a wall, gravel is **32.5 dB quieter** than concrete
(measured 2026-07-31 23:38; the write-up says 32.4 dB from the pre-regeneration
clips). Dry, the ranking holds; occluded, it inverts.

**The audio is not the bug.** 자갈 「부스럭」 is broadband high-frequency rustle and a
wall absorbs high frequencies; 콘크리트 「둔탁」 is a low thud and low frequencies pass
through. The clarity constants were picked from how surfaces sound *in the same room*,
and §04 makes hearing *through walls* the entire point of the role.

Why it is worse than either side being wrong alone: a HUD that disagrees with the
player's ears teaches them to distrust the role, and §04 gives the Listener nothing
else to work with.

Options: make clarity a function of occlusion (option 1 — the only one true at both
ends, and it turns a defect into a mechanic); re-derive the constants at the occlusion
the role actually works through; or push the surfaces apart in the *low* end.

**Option 1's price has already dropped.** The occlusion term now exists and is
measured per emitter every 0.1 s as a 0–1 fraction (`SoundOccluder.Occlusion`), so it
is a plumbing change plus a signature change, not new physics. The audio layer
deliberately does **not** apply a second clarity of its own —
`AudioOcclusion.OccludedLevelChangeDb` records the numbers and drives nothing —
because a parallel clarity in the presentation layer would reproduce this exact
failure one layer further down and harder to see. **The resolution belongs in
`ListenerAbility`, which is Core, which makes it a designer's decision.**

---

## F-004 · The sprint-timing dilemma cannot exist

**Sections:** §06 × §05 × §12. **Pinned by**
`MapTests.RunnerTest_SpendingTheSprintAtOnce_DominatesHoldingIt`.

```
sprint capacity = 5.6 m/s × 12 s = 67.2 m
route reach     = SprintMaxTravelDistance = 60.0 m
```

The bar outlasts the route by 7.2 m, so "spend it immediately" dominates at every
instant. §06 devotes a subsection to the opposite premise —
「처음부터 질주 → 거리는 벌지만 차단 지점 도달 전에 소진 / 아껴두면 → 그 사이에 잡힐 수
있음 / **맵을 알아야 최적화된다**」 — and that paragraph is currently false.

Most seriously: **a map cannot be graded on sprint timing**, so §12's 실전 검증 is blind
to the very thing §06 says makes maps worth learning. `RunnerTest` evaluates
hold-until-corner-*k* strategies that can never win.

Options: shorten the stamina bar below the route reach (10 s → 56 m against 60 m of
route makes the decision real); lengthen the routes (pushes on §12's 15–25 m cover
spacing, which is derived from these same numbers — expect a cascade); or accept it
and delete the dilemma from §06 along with the dead strategy search.

---

## F-001 · The weight table is a cliff, not a gradient

**Sections:** §06 × §08. **Pinned by**
`FoundationTests.WeightBands_AreACliffForTheRunner_NotAGradient`.

| Total weight | Multiplier | Runner sprint | vs monster 4.8 |
|:--:|:--:|:--:|---|
| ≤ 5 | 1.00 | 5.60 | **+0.80** — escapes |
| 6–10 | 0.85 | **4.76** | **−0.04** — caught |
| 11–15 | 0.70 | 3.92 | caught |
| ≥ 16 | sprint disabled | — | caught |

`5.6 × 0.85 = 4.76 < 4.8`. So bands 2, 3 and 4 are identical from the Runner's point
of view — three quarters of the table describe one outcome — and the cliff sits
exactly at weight 5, which is the weight of a single 대형 초상화·궤짝. §08 intends a
dial (「욕심이 곧 속도 저하이고, 속도 저하가 곧 죽음이다」) and the numbers produce a
switch.

Options: own the cliff and redraw the table so it stops implying a gradient; preserve
a slim band-2 escape (needs `WeightMulLight > 0.857`; at 0.90 the Runner makes
+0.24 m/s, which is §05's 측면 margin — real but demanding); or give the Runner a load
exemption, which cuts against §03's 「누가 들 것인가」 being a genuine question.

**Do not tune this before F-006.** The sweep shows why: teams never leave band 1.

---

## F-003 · The alphabet holds in the room, not at range through a wall

**Sections:** §12 × §04. **Pinned by** `verify_audio.py` sections 2 and 5,
`AudioTests.ADoorway_ReadsAsHalfOccluded_NotAsAWall`, and
`AudioSceneTests.AWallBetweenTheMonsterAndTheEars_LowersTheFilterCorner`.

Taking 1.4× spectral-centroid separation as "reliably distinguishable": dry it passes
comfortably (worst pair metal vs tile **2.10×**, measured 2026-07-31 23:38); at 25 m
through a wall it fails (worst pair wood vs metal **1.389×**).

Two updates that changed the price of the answers without closing the finding:

- **The engine's filter corner is clamped**, so the failing case is never reached in
  play: `AudioTuning.ListenerChannelOcclusionFloorHz` is 800 Hz, chosen as the lowest
  corner from which every higher corner also clears 1.4× with 5 % margin under the
  engine's one-biquad 12 dB/oct filter, where the worst pair reads 1.476×. The
  verifier measures through `butter(order=2) + filtfilt` — zero-phase, ~24 dB/oct —
  and is deliberately the conservative figure. **The gap between the two numbers is
  the whole margin**, which is why this stays 🟡.
- **Occlusion is a fraction, and §12's map is mostly the middle case.**
  `SoundOccluder` casts the direct ray plus a ring of four: clear line of sight
  `0.00` → 22 000 Hz; §12's 구역 간 진입점 `0.50` → 4 195 Hz; a solid wall `1.00` →
  800 Hz. §12 connects every zone pair through two or three entry points, so the
  common case has roughly twice the margin the single "through a wall" figure
  suggests.

Option 2 (give each surface a low-frequency signature) is the only one that buys
margin at `1.00` **and** composes with F-002's option 1, resolving both at once.

---

## F-005 · Two loop rules, only one can bind

```
ZoneCountMin 4 × LoopsPerZoneMin 1 = 4   ≥   LoopsTotalMin 3
```

Any map legal under the per-zone rule already has four loops, so §12's map-wide
minimum of three can never fail on its own. Harmless today; it matters if the per-zone
rule is ever relaxed, because nothing would be underneath.

Filed alongside it, and more useful than the finding itself: **§12's checklist is
necessary, not sufficient.** The 첫 맵 스케치 passes all sixteen validator rules and
still grades 10/10 TooEasy on 실전 검증 — pinned by
`MapTests.SketchMap_PassesTheChecklistAndStillGradesTooEasy`.

---

## The five questions no test can answer

§14 says questions 1 and 2 decide the project, and that they cannot be settled on
paper — 「직접 만져봐야 나온다」. Current state, from [TESTING.md](../TESTING.md) and
[STATUS.md §6](../STATUS.md):

| # | Question | State |
|:--:|---|---|
| 1 | 추격이 재밌는가? | **askable for the first time**, and unanswered. A machine can say 4.83 m/s; only a person can say whether getting away is a good time |
| 2 | 곁눈질 딜레마가 작동하는가? | needs a human at a mouse. `Horror Game ▸ Player ▸ Feel Harness` shows live speed, the §05 multiplier and the margin |
| 3 | "지금 나갈까?" 갈등이 생기는가? | **cannot be asked** — F-006 |
| 4 | "6이었나 9였나" 대화가 나오는가? | confusion pairs implemented and tested; whether they produce the argument is human |
| 5 | 청음사가 방향·거리를 구별하는가? | headphones required; expect it to work close and fail far — F-003 |
