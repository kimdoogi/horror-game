# Open questions — the balance findings

> These are **contradictions found by running the design document's own numbers
> against each other**. Every one states the sections that disagree, the arithmetic,
> and the options. None of them states a decision, because retuning is the designer's
> call, not ours.
>
> The full write-ups are [`docs/BALANCE-FINDINGS.md`](../BALANCE-FINDINGS.md). This
> page ranks them, says what each one blocks, and says what you may and may not do
> about it.

**There are thirteen, F-001 … F-013** (counted in BALANCE-FINDINGS.md, 2026-08-12). This
page used to say six, then ranked seven; six more were opened after it was last touched,
and two of the new ones **supersede** entries this page still led with. The ranking below
is re-derived.

**Most findings are pinned by a test**, so an edit that changes the answer fails the
build instead of passing silently. If you resolve one, the test fails — that is
intentional, and the write-up moves in the same commit as the change. **Two are not
pinned by anything any more**, and both are named as such below: a finding whose test was
deleted with its subject is a finding nothing is watching.

> 🔴 **Read this before quoting any number on this page.** Every figure that came from
> `core/HorrorGame.Sim` — match lengths, tier percentages, outcome mixes, credit totals,
> `sweep` results — is **void, not stale.** The simulator was deleted entirely at
> `e8c67ae`, there is no `horrorsim`, and nothing replaced it
> ([TESTING.md §9](../TESTING.md), [B-012](../BLOCKERS.md#b-012)). The audio figures and
> the map figures below were re-measured on 2026-08-12 and are marked with that date.

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
| **F-013** | 100 % escapable is the right answer to the wrong question — the creature is not shruggable, it is **absent** from seven of eight storeys | 🔴 **highest** | §12's only grade. **Supersedes F-007 and F-011**, and closes what F-008's option 2 left open |
| **F-012** | The monster could never chase anybody, because nothing ever made a sound | 🟢 fixed 2026-08-02 | was blocking — the game had no antagonist |
| F-002 | The clarity ladder contradicts the player's ears through a wall — **four** inverted pairs, two of them blocking | 🔴 blocking | the CI audio gate is red-by-baseline because of it |
| F-011 | §12's 실전 검증 has not been measuring the map — it has been measuring one sprint against 12 metres | 🔴 | superseded in turn by F-013, which generalises it |
| F-010 | §07's patrol table counts zones, so it **shrank when the building grew** | 🔴 | §07's pressure at the tier most matches sit in |
| F-008 | §12's escape geometry and §01's match length pull in opposite directions | 🔴 | the two cannot both be satisfied by scale |
| F-004 | The sprint-timing dilemma cannot exist at these numbers | 🔴 | a named skill expression in §06; §12 cannot grade a map on sprint timing |
| F-006 | Match length against §01's target — **and every figure in it came from the deleted simulator** | 🟠 | §14 Q3. Re-open it with a playtest, not a sweep |
| F-003 | The floor alphabet holds in the room, not at range through a wall | 🟡 | §14 Q5's "far" half |
| F-009 | A jump reaches its apex *plus* the free step — §12's climbing constraint is the sum of two numbers | 🟡 | closed by measurement; the *shape* is open |
| F-005 | §12 states two loop rules and only one can ever bind | 🟢 | nothing today |
| F-007 | The five-storey map lost the 주자 테스트 band the three-storey one held | ⚫ superseded | **read F-013 instead** |
| F-001 | The weight table is a cliff, not a gradient | ⚫ resolved by deletion | nothing — §08 and its weight table are gone |

---

## F-013 · The creature is absent from seven of eight storeys — read this one first

**Sections:** §12 (실전 검증) × §01/§02 (경주) × §06 (어그로 해제) × §07 × §12-B③.
**Source:** a harness over `DescentMap.Build(20260802)` + `RunnerTest`, run under `dotnet`
with no Unity, 2026-08-03. **Supersedes [F-007](#f-007) and F-011.**

The map's only grade has been red for four working passes, and **it cannot ever be
green.** That is arithmetic, not an opinion. With cover continuous — which §12's own
시야 차단 지점 간격 guarantees — a release fires once the runner has covered

```
5.6 × max(3 s, 2 m ÷ 0.8 m/s)  =  16.8 m of route past one bend
```

§12 *mandates* an S자 통로 of 10 m × 2 per zone, caps a straight run at 20 m, and requires
three 순환로. **Every §12-legal map hands that out from everywhere**, at every tier of §07:
720/720 places escape at 4.4, 4.6, 4.8, 5.0 and 5.2 m/s alike. *A map cannot obey §12's
construction rules and fail §12's own 실전 검증.*

**So the 5–7/10 band is not mis-set — at §12's parameters it is not a band at all.** It
was written when one player in four had 질주 5.6 and could out-run the creature; §04 is
deleted and DESCENT-PIVOT §5 promotes the sprint to all twenty, so "can you get away" now
has one answer for everybody and §06's own arithmetic already gives it. Grading a race map
on it measures §06, not the map.

> **Do not spend another pass turning the knob.** F-013 shows every value inside
> `Validate()`'s feasible region is on one side of a cliff or the other, and that lowering
> `RunnerTestAggroStartDistance` would *loosen* `SightBreakPointSpanMax`, which is defined
> as `SingleCornerMinDistance` less that same number. **The metric improves and the game
> gets worse.**

**What is actually wrong is one line the audit has printed every run: seven of the eight
storeys have no creature on them.** The replacement instrument is 탈출 대가 — what a chase
*costs* in §07's currency — and measured that way the shipped map is close to right: 680
chases, min 3.4 s, median 7.5 s, max 37.1 s against a 3.4–20 s band, with 20 places over
the ceiling ([STATUS.md §2.1](../STATUS.md)). Those twenty moved the wrong way when the
storeys lengthened, and they are the first sign that growth has a price.

---

## F-006 · Match length — and why not one figure that used to be here survives

**Sections:** §01 × §07. **Status: the evidence is gone, the question is not.**

> 🔴 **This section used to open with 500 simulated matches and twenty figures — median
> 7.2 min, 15.8 % inside the window, 심야 33.6 %, 완전 승리 11.2 %, `credits after the 1st
> return 312.71`. Every one of them is void.** They came from
> `dotnet run --project core/HorrorGame.Sim`, and **`core/HorrorGame.Sim` was deleted
> entirely at `e8c67ae`** — `core/` now holds `HorrorGame.Core`, `HorrorGame.Core.Tests`
> and the solution file, nothing else (checked 2026-08-12). Half of them describe systems
> that no longer exist anyway: there is no economy, no 완전 승리/부분 승리 outcome pair,
> no objective to recover, no battery to run flat.
>
> **The lesson this finding taught is worth more than any of its numbers, and it is why
> the section is kept.** For one whole pass the simulator was measuring a four-zone ring
> `SimMap` built for itself — 38 places, monster spawning 52 m from the door — while the
> game shipped a different building. Growing the Unity level could not move the simulator
> at all, and the document **reported that as a result.** [B-012](../BLOCKERS.md#b-012)
> records it. *A measuring instrument that does not share its subject with the game is a
> generator of confident fiction.*

**The live contradiction underneath it is smaller, sharper, and in the code right now.**

```
game-design §01     한 판의 흐름 (12~20분)
GameConstants       TargetMatchSecondsMin = 25f * 60f
                    TargetMatchSecondsMax = 35f * 60f
```

§01 was rewritten to 12~20분 for the race. **The constants were not**, and they are not
inert: `FoundationTests.ThreatCurve_FitsInsideAMatch` asserts
`TargetMatchSecondsMin / ThreatTierSeconds >= 3`, which passes at 25 min (3.1) and would
fail at 12 (1.5). So §07's five 8-minute tiers are still sized against a match length the
design document no longer asks for. That is a two-line contradiction anybody can check,
and it needs a decision rather than a sweep.

**Nothing measures a real match length today.** §12-D's derivation gives 3.5 min for
somebody who knows the route and 12~20 min for somebody who does not, and explicitly notes
that its own arithmetic predates the current catch rule. The instrument for this is now
§14 — people, in a room, playing it.

### The options that survive the evidence going away

1. **Re-derive `TargetMatchSecondsMin/Max` from §01's 12~20분**, and re-derive
   `ThreatTierSeconds` with them — §07's five tiers at 8 minutes each need 40 minutes to
   run out, which is twice the longest match §01 asks for. This is the concrete one, and
   it is arithmetic rather than taste.
2. **Measure a real match.** §14's prototype note asks for four people at once; nobody has
   ever timed a full eight-storey descent with humans in it.
3. **Accept §01 as written and rewrite §07 around it.** The cheapest of the three, and the
   one that throws away the least.

**And the caveat that outlived the numbers:** simulated agents do not hesitate, argue, get
lost or freeze. That was the reason to treat every simulated match length as a *floor*,
and it applies with more force now that the only remaining derivation
([§12-D](../game-design.md)) is a route length divided by a running speed. A human being
in a dark maze with nineteen competitors is not a path integral.

> **Growing the map again:** §12's dimensions come from §06's speeds. The chase tests and
> §12 validation are the guard and both must still pass. The 주자 테스트 band is **not** a
> guard — [F-013](#f-013) shows it cannot discriminate. See
> [Where every number lives §3](03-where-numbers-live.md).

---

## F-007 · The five-storey map grades 10/10 TooEasy — ⚫ superseded

> ⚫ **Read [F-013](#f-013) instead.** This entry diagnosed the grade as corner *density*
> and pointed at `sight-break-spacing`. The diagnosis was wrong and the fix worked anyway:
> [B-007](../BLOCKERS.md#b-007) closed on 2026-08-10 — 160 시야 차단 지점, deepest 12.5 m
> against a 14.4 m cap, **160/160 inside** §12's 15–25 m band, on all eight roster seeds —
> **and the grade did not move.** F-013 explains why it never could. The entry is kept
> because the mistake is instructive: two of the numbers below are the **raw bend**
> statistic where the rule uses the **지점** statistic, and B-007's closing note says
> quoting the wrong one of two available statistics "sends the next person to fix
> something that was never broken."

**Sections:** §12 (실전 검증) × §06 (추격). **Source, as it then was:**
`MapSceneGenerator.ReportQualityMenu`, seed 1204.

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

The census matters here: 10/10 is a ten-point sample and near the band a sample of ten
is a coin flip, so "unlucky seed" was a live explanation. 164 of 164 ruled it out — and
on the eight-storey building the same census reads **680/680**, 85 places on each of the
eight storeys.

> 🟢 **What happened next, 2026-08-10.** `sight-break-spacing` went in as a rule, the map
> failed it, `MapSceneGenerator` refused to write a scene the checklist rejected, and the
> geometry was then fixed properly — the bands jog outward, and every 시야 차단 지점 is
> now inside the band on all eight roster seeds. `MapValidator` is **14 rules**, not 17;
> three were deleted with the systems they gated. **The grade did not move.** Everything
> below this line is the reasoning that led to the wrong diagnosis, kept because it is a
> good example of a plausible one.

**Options as they were stated:** thin the corners on the named escape routes; or add
corner spacing as a rule and let the generator enforce it — "the one that generalises";
or accept a forgiving map and raise §06 monster pressure instead. The second was done. It
was worth doing on its own merits — 95 m of continuous cover against a 14.4 m cap was a
real defect — and it did not touch the grade, because the grade was never measuring
cover density. [F-013](#f-013).

---

## F-002 · The clarity ladder contradicts the player's ears

**Sections:** §12 (바닥 재질) × §04. **Pinned by** `verify_audio.py` **section [6] HUD vs
EARS** and `AudioTests.OccludedAudibility_InvertsTheClarityTable_AsF002Reports` (both
verified present 2026-08-12).

Re-measured 2026-08-12 by running `tools/audio/verify_audio.py` — exit 1, `RESULT: FAIL`,
**two BLOCKING and four inverted pairs in total**:

```
BLOCKING  gravel (0.70) vs concrete (0.50): gravel is 17.8 dB QUIETER at low-pass 600 Hz
BLOCKING  gravel (0.70) vs earth    (0.40): gravel is 13.8 dB QUIETER
WARNING   tile   (0.85) vs concrete (0.50): tile   is  6.5 dB QUIETER
WARNING   water  (1.00) vs wood     (0.80): water  is  6.2 dB QUIETER
```

Dry, every one of those rankings holds; occluded, all four invert.

> 🔴 **The figure this page carried was 32.5 dB, and it was for one pair on a five-surface
> alphabet.** The alphabet is **eight** surfaces now — `FloorMaterial` adds Water, Earth
> and Carpet — and the gravel-vs-concrete gap is **17.8 dB**. The count of inverted pairs
> grew with the alphabet, which is the honest way to read it: three new surfaces meant
> three new chances for the ladder to disagree with physics, and it took two of them.

**The audio is not the bug.** 자갈 「부스럭」 is broadband high-frequency rustle and a
wall absorbs high frequencies; 콘크리트 「둔탁」 is a low thud and low frequencies pass
through. The clarity constants were picked from how surfaces sound *in the same room*,
and the role that read them worked *through walls*.

> 🔴 **History, and the reason this stayed 🔴 blocking after §04 was deleted.** This
> finding was written about the 청음사 — a HUD that disagrees with the player's ears
> teaches them to distrust the role, and §04 gave the Listener nothing else to work with.
> **There is no Listener. `ListenerAbility` does not exist**, and `GameConstants` records
> its deletion in as many words. What replaced it is *worse for this finding, not better*:
> §04 hands 귀 to all twenty players — 「발소리는 누구에게나 들린다. 바닥 재질에 따라
> 다르게」 — and §01 makes 층마다 하나의 표면 the thing a footstep names. The ladder used
> to mislead one player in four about a teammate; it now misleads everybody about
> everybody, on a floor whose identity *is* its surface. The constants outlived the role
> and kept their job.

Options: make clarity a function of occlusion (option 1 — the only one true at both
ends, and it turns a defect into a mechanic); re-derive the constants at the occlusion
the role actually works through; or push the surfaces apart in the *low* end.

**Option 1's price has already dropped.** The occlusion term now exists and is
measured per emitter every 0.1 s as a 0–1 fraction (`SoundOccluder.Occlusion`), so it
is a plumbing change plus a signature change, not new physics. The audio layer
deliberately does **not** apply a second clarity of its own —
`AudioOcclusion.OccludedLevelChangeDb` records the numbers and drives nothing —
because a parallel clarity in the presentation layer would reproduce this exact
failure one layer further down and harder to see. **The resolution belongs in Core, next
to the `ListenerClarity*` constants themselves, which makes it a designer's decision.**
(The sentence here used to name `ListenerAbility` as the place to put it. That class is
gone; the constants it read are not, and they are still what the game shows a player.)

---

## F-004 · The sprint-timing dilemma cannot exist

**Sections:** §06 × §05 × §12. **Pinned by**
`MapTests.RunnerTest_SpendingTheSprintAtOnce_DominatesHoldingIt` (verified live in
`core/HorrorGame.Core.Tests/MapTests.cs:1121`, 2026-08-12).

**This one got *more* important with the pivot, not less.** §04 promotes 질주 to all
twenty and names the resulting choice as the game's central decision — 「**지금 쓸까,
관문에서 쓸까.** 초반에 태우면 앞서지만 마지막 관문 하나를 스무 명과 함께 통과할 때
아무것도 없다」 — which is the same dilemma this finding says the numbers cannot support,
now asked of everybody instead of one player in four.

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

**§16-5 is where the decision sits**, and it is 🔴 상: 「질주 스태미나 총량과 회복 속도」.
`SprintStaminaSeconds` 12 and `SprintStaminaRecoverySeconds` 20 are both still at their
co-op values (read 2026-08-12).

---

## F-001 · The weight table is a cliff, not a gradient — ⚫ resolved by deletion

> ⚫ **Resolved, and the way it resolved is the point.** §08 deleted the weight table, the
> loot it weighed and the `Inventory` that carried it. Nobody picks anything up in a race,
> so there is no weight, no cliff and no gradient. **This is a finding closed by removing
> its subject rather than by tuning it** — which §15 lists as the reason the whole pivot
> happened.
>
> **Its test is gone too, and that is worth stating plainly.** This page said "Pinned by
> `FoundationTests.WeightBands_AreACliffForTheRunner_NotAGradient`". That method **no
> longer exists** — `FoundationTests.cs` carries a tombstone comment where it was,
> recording what it pinned and why it could go. What the margin now rests on is direction
> and stance alone, asserted in `MovementTests` against
> `SpeedResolver.MarginVersusMonster`. `MovementContext` has exactly two fields,
> `BaseSpeed` and `LoadMultiplier`, and `LoadMultiplier` is now the **stance** multiplier
> rather than carry weight.

The arithmetic, kept because it is the clearest worked example in the repository of a
table that reads as a dial and behaves as a switch:

| Total weight | Multiplier | Runner sprint | vs monster 4.8 |
|:--:|:--:|:--:|---|
| ≤ 5 | 1.00 | 5.60 | **+0.80** — escapes |
| 6–10 | 0.85 | **4.76** | **−0.04** — caught |
| 11–15 | 0.70 | 3.92 | caught |
| ≥ 16 | sprint disabled | — | caught |

`5.6 × 0.85 = 4.76 < 4.8`. Bands 2, 3 and 4 were identical from the Runner's point of
view — three quarters of the table described one outcome, "drop it or die" — and the
cliff sat exactly at weight 5, the weight of a single 대형 초상화·궤짝. §08 intended a
dial (「욕심이 곧 속도 저하이고, 속도 저하가 곧 죽음이다」) and the numbers produced a
switch. **The +0.8 m/s margin those bands ate is still the margin the race runs on**, so
before you add anything that multiplies a player's speed, check it against 4.8 first.

---

## F-003 · The alphabet holds in the room, not at range through a wall

**Sections:** §12 × §01. **Pinned by** `verify_audio.py` **sections [2] §12 CROSS-MATERIAL
SEPARATION and [5] CHANNEL POLICY**, `AudioTests.ADoorway_ReadsAsHalfOccluded_NotAsAWall`,
and `AudioSceneTests.AWallBetweenTheMonsterAndTheEars_LowersTheFilterCorner` (all three
verified present 2026-08-12).

Taking 1.4× spectral-centroid separation as "reliably distinguishable" and re-measuring
2026-08-12: dry it passes, but **only just** — worst pair **water vs gravel 1.44×**
against a 1.40× requirement, and **1.41×** within a single actor. At 25 m through a wall
it fails: worst pair **metal vs gravel 1.137×**.

> 🔴 **The figures this page carried — 2.10× dry, 1.389× occluded — were a five-surface
> alphabet.** `FloorMaterial` is eight now: Wood, Tile, Gravel, Concrete, Metal, **Water,
> Earth, Carpet**. §12's own table named five surfaces and five zones, which capped the
> building at five storeys and was 「the reason every floor read the same」; the three
> additions extend the alphabet rather than the rule, each with its own §04 clarity. **The
> title of this finding said "five-surface" and the count was the whole point of it.**
>
> **Adding surfaces cost margin, and the honest reading is that it cost most of it.** The
> dry worst pair fell from 2.10× to 1.44× — still a pass, with 0.04 of headroom — because
> eight surfaces have to share the same spectral range that five did. §01 wants 여덟 개의
> 표면, 층마다 하나, so a ninth would have to buy its room from somewhere.

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
necessary, not sufficient.** Core's own fixture map passes all **fourteen** validator
rules and still grades 10/10 TooEasy on 실전 검증 — pinned by
`MapTests.SketchMap_PassesTheChecklistAndStillGradesTooEasy`
(`core/HorrorGame.Core.Tests/MapTests.cs:168`, live 2026-08-12).

That was an abstract point when it was written. It is not any more, and the ending is
sharper than "the checklist caught up": the shipped map did the same thing, the specific
quantity everyone blamed — 시야 차단 지점 간격 — became a rule, the geometry was fixed,
the rule now **passes on all eight roster seeds**, and the grade did not move a point.
[F-013](#f-013) explains why: the two were never measuring the same thing. **A passing
checklist cannot promise a good map, and a failing grade cannot tell you which rule to
write.**

---

## The five questions no test can answer

§14 says questions 1 and 2 decide the project — 「1·2번이 안 되면 나머지를 아무리 잘
만들어도 안 된다」 — and that none of them can be settled on paper: 「직접 만져봐야
나온다」. **Three of the five were rewritten by the pivot**, so this table is the current
§14, re-read 2026-08-12. State from [STATUS.md](../STATUS.md) and
[TESTING.md](../TESTING.md).

| # | Question | 실패 시 (§14's own column) | State |
|:--:|---|---|---|
| 1 | **추격이 재밌는가?** (거리를 벌리는 순간이 짜릿한가) | §06 어그로 수치를 고친다 | askable, unanswered. A machine says 4.79 m/s of route; only a person says whether getting away is a good time |
| 2 | **곁눈질 딜레마가 작동하는가?** (뒤를 볼지 고민하게 되는가) | §05 후진 배율(65 %)을 조정 | needs a human at a mouse. `MulBackward` is 0.65 and pinned twice in `GameConstants.Validate()` |
| 3 | **관문에서 붐비는 것이 재밌는가?** (문을 닫고 갈지 고민하는가) | §12-A 관문 수 4/2/1을 조정 | **the question the pivot added, and §14 says it decides whether this game works at twenty.** Needs at least four people at once — 「문 앞에서 붐비는지 보려면 최소 넷은 필요하다」. Never asked |
| 4 | **탈락이 억울하지 않고 납득되는가?** | §06 순찰 반경 · §07 시간 곡선 | unanswered, and [F-013](#f-013) is the reason to expect trouble: seven of eight storeys currently have no creature on them, so nobody has met the thing they would be eliminated by |
| 5 | **발소리의 방향·거리를 구별할 수 있는가?** | FMOD를 붙인다 | headphones required; expect it to work close and fail far — [F-003](#f-003). Note §14's failure branch is a *technology* answer, not a tuning one |

> 🔴 **Three of these are not the questions this page used to list.** Q3 was
> 「"지금 나갈까?" 갈등이 생기는가」 — a 왕복 question about an economy that no longer
> exists — Q4 was 「"6이었나 9였나" 대화가 나오는가」 about the deleted clue chain's
> confusion pairs, and Q5 named the 청음사. The old Q3 entry also said the question was
> "still not fairly askable" until F-006 was resolved. **It is askable today**; what stops
> it is that nobody has put four people in a room, which is a different kind of blocker
> and a much cheaper one.
