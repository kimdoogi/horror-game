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
| **F-006** | Matches finish in 7.2 min against §01's 25–35 — moved a long way by the five-storey map, not closed | 🔴 **highest** | §14 Q3; §16-2 (the economy). No longer kills §07's upper tiers — all five are now reached |
| **F-007** | The five-storey map grades 10/10 TooEasy on the 주자 테스트, outside §12's 5–7 band | 🟠 high | §12's only grade on the map; §06's chase as the game's central pressure |
| F-002 | The Listener's HUD contradicts the player's ears through a wall | 🔴 blocking | §04's 청음사 as a role; the CI audio gate is red-by-baseline because of it |
| F-004 | The Runner's sprint-timing dilemma cannot exist at these numbers | 🔴 | a named skill expression in §06; §12 cannot grade a map on sprint timing |
| F-001 | The weight table is a cliff, not a gradient | 🔴 | §16-2; three of §08's four bands mean one thing |
| F-003 | The five-surface alphabet holds in the room, not at range through a wall | 🟡 | §14 Q5's "far" half |
| F-005 | §12 states two loop rules and only one can ever bind | 🟢 | nothing today |

---

## F-006 · Matches finish in 7.2 minutes — read this one first

**Sections:** §01 × §07 × §08. **Source:** 500 simulated matches, seeds 1–500, run on
요양원 지하 5층 — the building the game actually ships.

Reproduced 2026-08-01 03:20, exit 0 — the simulator is deterministic and these are
byte-for-byte the figures in [BALANCE-FINDINGS F-006](../BALANCE-FINDINGS.md#f-006):

```
§01 match length — target 25~35 min
  median                                             7.2 min
  p10 / p90                                          4.2 min / 32.4 min
  inside the window                                  15.8%
  ended with every light dead                        40.6%
  median of the rest                                 17.1 min

§07 threat curve
  mean tier at end (0=초저녁 … 4=동트기 전)                 1.12
  reached 심야 or later                                33.6%
  reached 새벽 or later                                17.4%
  reached 동트기 전                                     13.0%
  chases per match                                   5.52
  chases broken                                      87.7%
  deaths per match                                   0.68

§02 outcome mix
  완전 승리 11.2% · 부분 승리 32.6% · 생존 53.8% · 패배 2.4% · objective recovered 43.8%
```

```bash
dotnet run -c Release --project core/HorrorGame.Sim -- run --matches 500 --seed 1
```

> **Every number on this page was 2.5 min / 0.6% / 1.2% until 2026-08-01, and those
> were measured against a map the game does not have.** `SimMap` built its own
> four-zone ring — 38 places, monster spawning 52 m from the door — while the game
> ships 164 places and 217.5 m. Growing the Unity level could not move the simulator
> at all, and for one pass this document reported that as a *result*. `SimMap` now
> calls `FirstMapSketch.Build`, compiled into the simulator from the same sources
> Unity compiles, so the two cannot drift again. **Check the first five lines of the
> run output — they are the building — before quoting anything below them.**

### What it still costs

§07 declares 「시간이 유일한 통화다」. The five-storey map bought most of the clock back:
**all five of §07's tiers are now reached by real matches** — 심야 by 33.6%, 새벽 by
17.4%, 동트기 전 by 13.0% — so 심야's flashlight −30% and 새벽's 「괴물이 출입구를
안다」 are content now rather than dead text. That was the specific claim this finding
was opened about, and it has been answered.

What has not been answered is §01's word *normal*. 25–35 minutes is asked for as the
usual match; 7.2 minutes is the usual match, and the window is reachable rather than
typical. Two populations sit in the gap:

1. **40.6% end broke, not beaten** — every light dead, wallet too thin for another
   cell, §02 files it as 생존. Excluding them the median is **17.1 min** and 26.6%
   land inside the window, so this one population is most of the remaining gap. It is
   an §08 bootstrap failure and §08 has the knob: `BatteryCells`, or a first-descent
   grubstake.
2. **The clue chain now pins a site 51.2% of the time**, down from 86.4%. Five storeys
   means a misread on the floor mapping costs a descent instead of a corridor.
   That is §03 working as written; whether it is *good* is §14's question, not a bug.

### The economy has started, and every old measurement of it is void

From the same 500 matches: `earned ÷ cost of one of everything 0.70` (was 0.17),
`purchase_upgraded_flashlight 283 of 500` (was 23), `credits after the 1st return
312.71` (was 160.04), `unspent at the end 245.74` (was 24.78). §16-2 **can now be
measured**. The `weight-mul-light` sweep this page used to quote cannot — it was run
on the ring, and its re-run on the real building still shows no trend, for a new
reason: §07 keeps the monster at 4.4 m/s until 심야 and F-001's cliff is about 4.8, so
the experiment worth running is `sweep weight-mul-light --matches 400 --seed 1
--start-minutes 16`.

### The options, and the honest caveat

1. ~~**Make traversal cost real time.**~~ **Done, and it delivered** — 38 places → 164,
   52 m → 217.5 m. §12 leaves little room for more: five surfaces cap the building at
   five zones.
2. **Fix the bootstrap before anything else.** 40.6% of matches never reach §08's
   growth curve. Biggest remaining lever, and it is a price-table question — the same
   question as §16-2.
3. **Compress the threat curve.** Now clearly the wrong move: the tiers are being
   reached, and compressing them throws away what the map growth bought.
4. **Add required dwell time.** Cheaper than it was — 41 dead ends carry 전리품 and
   19.94 pieces are left behind every match.
5. **Accept a shorter match** and rewrite §01 and §07 around ~15 min. Worth
   reconsidering now that the honest number for a funded team is 17.1.

The caveat, unchanged and now cutting both ways: simulated agents do not hesitate,
argue, get lost or freeze — and they also walk past loot a human would grab, which is
part of why 40.6% end broke. **Treat 7.2 minutes as a floor.**

> **Growing the map again:** §12's dimensions come from §06's speeds. The chase tests
> and §12 validation are the guard and both must still pass — and so must the 주자
> 테스트 band, which the last growth broke. See [F-007](#f-007) and
> [Where every number lives §3](03-where-numbers-live.md).

---

## F-007 · The five-storey map grades 10/10 TooEasy

**Sections:** §12 (실전 검증) × §06 (추격). **Source:**
`MapSceneGenerator.ReportQualityMenu` and `horrorsim map`, seed 1204 — the two agree
exactly.

§12 asks for **5–7 of 10** sampled runners to escape aggro. Ten of ten do:

```
§12 주자 테스트 — 요양원 지하 5층: 10/10 (100%), TooEasy
  너무 쉽다 — 시야 차단 지점을 줄인다 (§12).

§12 실전 검증, every place rather than the ten §12 samples:
  164/164 escapable (100%), against §12's 50%~70% band.
  D 하역장 35/35 · A 기록보관소 34/34 · E 기계실 43/43 · C 저탄장 28/28 · B 저수조 24/24

시야 차단 지점 간격 (§12 수치 규칙 15 m~25 m):
  79 corners, nearest-neighbour 2.5 m~10 m, mean 4.1 m, 0 inside the band.
```

The three-storey map graded **7/10, Balanced**. The growth removed exactly the
weakness that was holding the grade: 85 passages → 180 raised loops from 12 to 17,
and more connectivity means more corners. A runner who can always find a third corner
inside 15 m can always break line of sight.

**The last line is the diagnosis.** §12's 수치 규칙 asks corners to sit 15–25 m apart;
not one of the 79 does, and the mean spacing is 4.1 m. Until 2026-08-01 nothing in
the checklist constrained corner *density*, which is why the map passed 16 of 16 while
failing its only grade — [F-005](#f-005)'s "necessary, not sufficient" arriving in
practice for the second time.

**That hole is now closed, and it changed the diagnosis into a build failure.**
`66ce930` added `sight-break-spacing` as the 17th rule. The map fails it, the checklist
verdict is now FAIL, and `MapSceneGenerator` refuses to write a scene the checklist
rejects — so the map can no longer be regenerated ([B-007](../BLOCKERS.md#b-007)).
The rule and the grade are the same defect measured twice; one geometry fix closes
both. **Do not relax `SightBreakPointSpanMax` to clear the blocker.**

The census matters here: 10/10 is a ten-point sample and near the band a sample of ten
is a coin flip, so "unlucky seed" was a live explanation. 164 of 164 rules it out.

**Options:** thin the corners on the named escape routes (`#90`, `#80`, `#135`, `#2`,
`#156`, `#106`, `#21`, `#150`); or add corner spacing as a seventeenth §12 rule and
let the generator enforce it — the one that generalises; or accept a forgiving map and
raise §06 monster pressure instead, which trades a §12 target for a §06 retune and
should not be done without a human playtest.

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
necessary, not sufficient.** Core's own fixture map passes all seventeen validator
rules and still grades 10/10 TooEasy on 실전 검증 — pinned by
`MapTests.SketchMap_PassesTheChecklistAndStillGradesTooEasy`.

That was an abstract point when it was written. It is not any more: the shipped Unity
map has now done the same thing, and it cost the 5–7 band the three-storey building
held. See [F-007](#f-007). Its diagnosis — corner spacing — is now the 17th rule, so
the checklist has caught up with the grade on this one specific quantity. The general
point stands: a passing checklist still cannot promise a good map.

---

## The five questions no test can answer

§14 says questions 1 and 2 decide the project, and that they cannot be settled on
paper — 「직접 만져봐야 나온다」. Current state, from [TESTING.md](../TESTING.md) and
[STATUS.md §6](../STATUS.md):

| # | Question | State |
|:--:|---|---|
| 1 | 추격이 재밌는가? | **askable for the first time**, and unanswered. A machine can say 4.83 m/s; only a person can say whether getting away is a good time |
| 2 | 곁눈질 딜레마가 작동하는가? | needs a human at a mouse. `Horror Game ▸ Player ▸ Feel Harness` shows live speed, the §05 multiplier and the margin |
| 3 | "지금 나갈까?" 갈등이 생기는가? | **still not fairly askable** — F-006. Closer than it was: the economy now runs (0.70 of one of everything earned, 245.74 credits unspent at the end) where before it never started. But 7.2 min against §01's 25–35 is not the loop the question is about, and the in-game §14 overlay says so |
| 4 | "6이었나 9였나" 대화가 나오는가? | confusion pairs implemented and tested; whether they produce the argument is human |
| 5 | 청음사가 방향·거리를 구별하는가? | headphones required; expect it to work close and fail far — F-003 |
